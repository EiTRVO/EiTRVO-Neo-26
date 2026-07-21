using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using EiTRVO.ProEngine.Models;
using static EiTRVO.ProEngine.Helpers.Endpoints;

namespace EiTRVO.ProEngine.Services.Loaders;

internal static class OptiFineInstaller
{
    public static async Task<List<ModLoaderVersion>> GetVersionsAsync(HttpClient http, string mcVersion, CancellationToken ct = default)
    {
        string html;
        try
        {
            html = await http.GetStringAsync(OptiFineDownloadsPage, ct);
        }
        catch (Exception ex)
        {
            throw new Exception($"无法获取 OptiFine 版本列表：{ex.Message}");
        }

        var matches = System.Text.RegularExpressions.Regex.Matches(
            html,
            @"href=""http://optifine\.net/adloadx\?f=(OptiFine_[^""]+\.jar|preview_OptiFine_[^""]+\.jar)""");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ModLoaderVersion>();
        string stablePrefix = $"OptiFine_{mcVersion}_";
        string previewPrefix = $"preview_OptiFine_{mcVersion}_";

        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            string fileName = m.Groups[1].Value;
            string versionId = fileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                ? fileName.Substring(0, fileName.Length - 4)
                : fileName;

            if (seen.Contains(versionId))
                continue;

            bool isStable = fileName.StartsWith(stablePrefix) && !fileName.StartsWith("preview_");
            bool isPreview = fileName.StartsWith(previewPrefix);

            if (!isStable && !isPreview)
                continue;
            if (isPreview)
                continue;

            seen.Add(versionId);

            result.Add(new ModLoaderVersion
            {
                LoaderType = "OptiFine",
                LoaderVersion = versionId,
                MinecraftVersion = mcVersion,
                IsRecommended = isStable && !versionId.Contains("_pre"),
                IsLatest = false
            });
        }

        if (result.Count > 0)
        {
            var firstStable = result.FirstOrDefault(r => r.IsRecommended);
            if (firstStable != null)
                firstStable.IsLatest = true;
            else
                result[0].IsLatest = true;
        }

        return result;
    }

    public static async Task InstallAsync(HttpClient http, string gameDir, string mcVersion,
        string optiFineVersion, string instanceName, string javaPath,
        IProgress<DownloadProgress> progress,
        Action<string, NotificationType, int> showNotification,
        CancellationToken ct = default)
    {
        string fileName = $"{optiFineVersion}.jar";

        string installerDir = Path.Combine(gameDir, "installer_cache");
        Directory.CreateDirectory(installerDir);
        string installerPath = Path.Combine(installerDir, fileName);

        showNotification("正在下载 OptiFine Installer...", NotificationType.Info, 0);

        // Step 1a: GET adloadx page
        string adloadUrl = OptiFineAdload(fileName);
        string adloadHtml;
        using (var adReq = new HttpRequestMessage(HttpMethod.Get, adloadUrl))
        {
            adReq.Headers.Referrer = new Uri(OptiFineDownloadsPage);
            using var adResp = await http.SendAsync(adReq, ct);
            adResp.EnsureSuccessStatusCode();
            adloadHtml = await adResp.Content.ReadAsStringAsync();
        }

        // Step 1b: Extract downloadx link
        var dlxMatch = System.Text.RegularExpressions.Regex.Match(
            adloadHtml,
            @"href=['""]downloadx\?f=([^'""&]+)&x=([a-f0-9]+)['""]");

        if (!dlxMatch.Success)
            throw new Exception("OptiFine 下载页面解析失败：未找到 downloadx 链接。");

        string dlxFile = dlxMatch.Groups[1].Value;
        string dlxHash = dlxMatch.Groups[2].Value;
        string downloadUrl = OptiFineDownloadX(dlxFile, dlxHash);

        // Step 1c: Download the actual JAR file
        using (var dlReq = new HttpRequestMessage(HttpMethod.Get, downloadUrl))
        {
            dlReq.Headers.Referrer = new Uri(adloadUrl);
            using var dlResp = await http.SendAsync(dlReq, HttpCompletionOption.ResponseHeadersRead, ct);
            dlResp.EnsureSuccessStatusCode();

            long totalBytes = dlResp.Content.Headers.ContentLength ?? -1;

            // Reject unreasonably large files (malicious or misconfigured)
            const long maxInstallerSize = 50 * 1024 * 1024; // 50 MB
            if (totalBytes > maxInstallerSize)
                throw new InvalidOperationException(
                    $"OptiFine 安装器大小 ({totalBytes / 1024 / 1024} MB) 异常，已拒绝。");
            string tmp = installerPath + ".part";
            var sw = Stopwatch.StartNew();
            long downloadedBytes = 0;
            long lastReportedBytes = 0;
            double lastReportTime = 0;

            try
            {
                await using var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true);
                await using var contentStream = await dlResp.Content.ReadAsStreamAsync(ct);

                byte[] buffer = new byte[8192];
                int bytesRead;
                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                {
                    await fs.WriteAsync(buffer, 0, bytesRead);
                    downloadedBytes += bytesRead;

                    if (downloadedBytes - lastReportedBytes >= 65536 || downloadedBytes == totalBytes)
                    {
                        double now = sw.Elapsed.TotalSeconds;
                        double deltaTime = now - lastReportTime;
                        double speed = deltaTime > 0.05 ? (downloadedBytes - lastReportedBytes) / deltaTime : 0;
                        progress.Report(DownloadProgress.FileProgress(fileName, downloadedBytes, totalBytes, speed));
                        lastReportedBytes = downloadedBytes;
                        lastReportTime = now;
                    }
                }
            }
            catch
            {
                try { File.Delete(tmp); } catch { }
                throw;
            }
            File.Move(tmp, installerPath, overwrite: true);
        }

        // 2. Install OptiFine
        string versionsDir = Path.Combine(gameDir, "versions");
        string targetVersionDir = Path.Combine(versionsDir, instanceName);
        string vanillaVersionDir = Path.Combine(versionsDir, mcVersion);
        string vanillaJar = Path.Combine(vanillaVersionDir, $"{mcVersion}.jar");
        string vanillaJson = File.Exists(Path.Combine(vanillaVersionDir, $"{mcVersion}.json"))
            ? Path.Combine(vanillaVersionDir, $"{mcVersion}.json")
            : Path.Combine(vanillaVersionDir, "version.json");

        if (!File.Exists(vanillaJar))
            throw new Exception($"原版 {mcVersion} 未下载，请先下载原版后再安装 OptiFine。");
        if (!File.Exists(vanillaJson))
            throw new Exception($"原版 {mcVersion} 的 version JSON 未找到，请重新下载原版。");

        if (ModLoaderService.IsLegacyMcVersion(mcVersion))
        {
            await InstallLegacyAsync(gameDir, mcVersion, optiFineVersion, instanceName,
                installerPath, vanillaJar, vanillaJson, targetVersionDir, progress, showNotification);
        }
        else
        {
            await InstallModernAsync(gameDir, mcVersion, optiFineVersion, instanceName, javaPath,
                installerPath, versionsDir, targetVersionDir, showNotification);
        }

        progress.Report(DownloadProgress.Overall(1, 1));

        ModLoaderService.SaveInstanceMeta(targetVersionDir, "OptiFine", optiFineVersion);
    }

    private static async Task InstallLegacyAsync(string gameDir, string mcVersion,
        string optiFineVersion, string instanceName, string installerPath,
        string vanillaJar, string vanillaJson, string targetVersionDir,
        IProgress<DownloadProgress> progress,
        Action<string, NotificationType, int> showNotification)
    {
        showNotification("正在安装 OptiFine (LaunchWrapper 模式)...", NotificationType.Info, 0);

        // Extract launchwrapper-of-*.jar from OptiFine JAR
        string lwVersion;
        using (var zip = ZipFile.OpenRead(installerPath))
        {
            var lwTxtEntry = zip.Entries.FirstOrDefault(e =>
                e.FullName.Equals("launchwrapper-of.txt", StringComparison.OrdinalIgnoreCase));
            if (lwTxtEntry == null)
                throw new Exception("OptiFine JAR 中未找到 launchwrapper-of.txt，无法安装。");
            using var lwReader = new StreamReader(lwTxtEntry.Open());
            lwVersion = (await lwReader.ReadToEndAsync()).Trim();
            if (string.IsNullOrEmpty(lwVersion))
                throw new Exception("launchwrapper-of.txt 为空，无法确定版本。");

            string lwJarName = $"launchwrapper-of-{lwVersion}.jar";
            var lwJarEntry = zip.Entries.FirstOrDefault(e =>
                e.FullName.Equals(lwJarName, StringComparison.OrdinalIgnoreCase));
            if (lwJarEntry == null)
                throw new Exception($"OptiFine JAR 中未找到 {lwJarName}，无法安装。");

            string lwLibDir = Path.Combine(gameDir, "libraries", "optifine", "launchwrapper-of", lwVersion);
            Directory.CreateDirectory(lwLibDir);
            string lwDestPath = Path.Combine(lwLibDir, lwJarName);
            if (!File.Exists(lwDestPath))
                lwJarEntry.ExtractToFile(lwDestPath);
        }

        // Copy OptiFine JAR → libraries
        string ofLibVersion = optiFineVersion.StartsWith("OptiFine_")
            ? optiFineVersion.Substring("OptiFine_".Length)
            : optiFineVersion;
        string ofLibDir = Path.Combine(gameDir, "libraries", "optifine", "OptiFine", ofLibVersion);
        Directory.CreateDirectory(ofLibDir);
        string ofLibDest = Path.Combine(ofLibDir, $"OptiFine-{ofLibVersion}.jar");
        File.Copy(installerPath, ofLibDest, overwrite: true);

        Directory.CreateDirectory(targetVersionDir);
        string instanceJar = Path.Combine(targetVersionDir, $"{instanceName}.jar");
        File.Copy(vanillaJar, instanceJar, overwrite: true);

        var vanillaDetail = JsonSerializer.Deserialize<VersionDetail>(File.ReadAllText(vanillaJson));
        string vanillaArgs = vanillaDetail?.MinecraftArguments ?? "";

        var ofVersionJson = new VersionDetail
        {
            Id = instanceName,
            InheritsFrom = mcVersion,
            Type = "release",
            MainClass = "net.minecraft.launchwrapper.Launch",
            Libraries = new List<Library>
            {
                new Library { Name = $"optifine:OptiFine:{ofLibVersion}" },
                new Library { Name = $"optifine:launchwrapper-of:{lwVersion}" }
            },
            MinecraftArguments = string.IsNullOrEmpty(vanillaArgs)
                ? "--tweakClass optifine.OptiFineTweaker"
                : $"{vanillaArgs} --tweakClass optifine.OptiFineTweaker"
        };

        string versionJsonPath = Path.Combine(targetVersionDir, "version.json");
        await File.WriteAllTextAsync(versionJsonPath,
            JsonSerializer.Serialize(ofVersionJson, new JsonSerializerOptions { WriteIndented = true }),
            System.Text.Encoding.UTF8);

        showNotification("OptiFine 安装完成。", NotificationType.Success, 3000);
    }

    private static async Task InstallModernAsync(string gameDir, string mcVersion,
        string optiFineVersion, string instanceName, string javaPath,
        string installerPath, string versionsDir, string targetVersionDir,
        Action<string, NotificationType, int> showNotification)
    {
        string launcherProfilesPath = Path.Combine(gameDir, "launcher_profiles.json");
        if (!File.Exists(launcherProfilesPath))
            await File.WriteAllTextAsync(launcherProfilesPath, "{\"profiles\":{}}");

        showNotification(
            $"OptiFine 安装器已打开，请在窗口中点击 Install 按钮完成安装。" +
            $"（安装目录已预填：{gameDir}）",
            NotificationType.Info, 0);

        var dirsBefore = new HashSet<string>(
            Directory.GetDirectories(versionsDir).Select(d => Path.GetFileName(d)),
            StringComparer.OrdinalIgnoreCase);

        var psi = new ProcessStartInfo
        {
            FileName = javaPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        psi.ArgumentList.Add("-jar");
        psi.ArgumentList.Add(installerPath);
        psi.ArgumentList.Add("--installClient");
        psi.ArgumentList.Add(gameDir);

        using var process = Process.Start(psi)
                             ?? throw new Exception("无法启动 OptiFine 安装器进程。");

        var stdout = new System.Text.StringBuilder();
        var stderr = new System.Text.StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) stdout.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) stderr.AppendLine(e.Data);
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var waitCts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        try { await process.WaitForExitAsync(waitCts.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(); } catch { }
            string errTail = stderr.ToString().Trim();
            string logTail = errTail.Length > 2000 ? errTail[^2000..] : errTail;
            throw new Exception($"OptiFine 安装器执行超时（10 分钟）。安装器 GUI 可能未响应，请关闭后重试。\n\n--- 安装器输出尾部 ---\n{logTail}");
        }

        if (process.ExitCode != 0)
        {
            string combined = (stderr.ToString() + "\n" + stdout.ToString()).Trim();
            if (combined.Length > 8000) combined = combined[^8000..];
            throw new Exception(
                $"OptiFine 安装器未成功完成（退出码 {process.ExitCode}）。\n{combined}");
        }

        string? optiFineDirPath = null;
        foreach (string dir in Directory.GetDirectories(versionsDir))
        {
            string dirName = Path.GetFileName(dir);
            if (!dirsBefore.Contains(dirName) &&
                (dirName.StartsWith("OptiFine", StringComparison.OrdinalIgnoreCase) ||
                 dirName.Contains("optifine", StringComparison.OrdinalIgnoreCase)))
            {
                optiFineDirPath = dir;
                break;
            }
        }
        if (optiFineDirPath == null)
        {
            foreach (string dir in Directory.GetDirectories(versionsDir))
            {
                string dirName = Path.GetFileName(dir);
                if (dirName.Contains("OptiFine", StringComparison.OrdinalIgnoreCase) ||
                    dirName.Contains("optifine", StringComparison.OrdinalIgnoreCase) ||
                    dirName.Contains(mcVersion + "_HD") ||
                    dirName.Contains(mcVersion + "_U"))
                {
                    optiFineDirPath = dir;
                    break;
                }
            }
        }

        if (optiFineDirPath == null)
            throw new Exception(
                $"OptiFine 安装器运行完毕，但未找到生成的版本目录。\n" +
                $"请确认 OptiFine {optiFineVersion} 是否支持 MC {mcVersion}。");

        string optiFineDirName = Path.GetFileName(optiFineDirPath);
        Directory.CreateDirectory(targetVersionDir);

        string ofJsonPath = Path.Combine(optiFineDirPath, $"{optiFineDirName}.json");
        if (!File.Exists(ofJsonPath))
            ofJsonPath = Path.Combine(optiFineDirPath, "version.json");

        if (File.Exists(ofJsonPath))
        {
            string destJson = Path.Combine(targetVersionDir, "version.json");
            File.Copy(ofJsonPath, destJson, overwrite: true);
            File.Copy(ofJsonPath, Path.Combine(targetVersionDir, $"{instanceName}.json"), overwrite: true);
        }
        else
        {
            throw new Exception("OptiFine 安装器未生成 version.json。");
        }

        foreach (string jar in Directory.GetFiles(optiFineDirPath, "*.jar"))
        {
            string destJar = Path.Combine(targetVersionDir, Path.GetFileName(jar));
            File.Copy(jar, destJar, overwrite: true);
        }

        string ofVersionJsonFile = Path.Combine(targetVersionDir, "version.json");
        if (File.Exists(ofVersionJsonFile))
        {
            var ofDetail = JsonSerializer.Deserialize<VersionDetail>(
                File.ReadAllText(ofVersionJsonFile));
            if (ofDetail?.Id != null)
            {
                string expectedJar = Path.Combine(targetVersionDir, $"{ofDetail.Id}.jar");
                if (!File.Exists(expectedJar))
                {
                    string parentVer = ofDetail.InheritsFrom ?? mcVersion;
                    string fallback = Path.Combine(gameDir, "versions", parentVer, $"{parentVer}.jar");
                    if (File.Exists(fallback))
                        File.Copy(fallback, expectedJar);
                }
            }
        }

        try { Directory.Delete(optiFineDirPath, true); }
        catch { /* best effort */ }

        ModLoaderService.SaveInstallerLog(targetVersionDir, stdout.ToString(), stderr.ToString());
        showNotification("OptiFine 安装完成。", NotificationType.Success, 3000);
    }
}
