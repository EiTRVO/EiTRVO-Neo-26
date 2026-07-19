using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using EiTRVO.ProEngine.Models;
using static EiTRVO.ProEngine.Helpers.Endpoints;

namespace EiTRVO.ProEngine.Services.Loaders;

internal static class NeoForgeInstaller
{
    public static async Task<List<ModLoaderVersion>> GetVersionsAsync(HttpClient http, string mcVersion, CancellationToken ct = default)
    {
        string? prefix = null;
        string[] mcParts = mcVersion.Split('.');
        if (mcParts.Length >= 3 && int.TryParse(mcParts[1], out _) && int.TryParse(mcParts[2], out _))
            prefix = $"{mcParts[1]}.{mcParts[2]}";
        else if (mcParts.Length >= 2 && int.TryParse(mcParts[1], out _))
            prefix = $"{mcParts[1]}.0";

        if (prefix == null)
            return new List<ModLoaderVersion>();

        string metadataUrl = $"{NeoForgeMavenBase}/releases/net/neoforged/neoforge/maven-metadata.xml";
        string xml;
        try
        {
            xml = await http.GetStringAsync(metadataUrl, ct);
        }
        catch (Exception ex)
        {
            throw new Exception($"无法获取 NeoForge 版本列表: {ex.Message}");
        }

        var versions = ModLoaderService.ParseMavenMetadataVersions(xml);

        var matching = versions
            .Where(v => v.StartsWith(prefix + "."))
            .OrderByDescending(v => ModLoaderService.ParseNeoForgeBuildNumber(v))
            .Select(v => new ModLoaderVersion
            {
                LoaderType = "NeoForge",
                LoaderVersion = v,
                MinecraftVersion = mcVersion,
                IsRecommended = false,
                IsLatest = false
            })
            .ToList();

        if (matching.Count > 0)
            matching[0].IsLatest = true;

        return matching;
    }

    public static async Task InstallAsync(HttpClient http, string gameDir, string mcVersion,
        string neoForgeVersion, string instanceName, string javaPath,
        IProgress<DownloadProgress> progress,
        Action<string, NotificationType, int> showNotification,
        CancellationToken ct = default)
    {
        string installerUrl = NeoForgeInstaller(neoForgeVersion);
        string installerDir = Path.Combine(gameDir, "installer_cache");
        Directory.CreateDirectory(installerDir);
        string installerPath = Path.Combine(installerDir, $"neoforge-{neoForgeVersion}-installer.jar");

        showNotification("正在下载 NeoForge Installer...", NotificationType.Info, 0);

        var urls = new[] { (installerUrl, "Official") };

        bool installerDownloaded = false;
        var errors = new List<string>();
        for (int attempt = 0; attempt < 3 && !installerDownloaded; attempt++)
        {
            if (attempt > 0)
            {
                int delayMs = 1500 * attempt;
                showNotification($"NeoForge Installer 下载失败，正在重试 ({attempt}/2)...", NotificationType.Warning, 0);
                await Task.Delay(delayMs, ct);
            }

            foreach (var (url, label) in urls)
            {
                try
                {
                    await ModLoaderService.DownloadFileAsync(http, url, installerPath, progress, $"neoforge-{neoForgeVersion}-installer.jar", ct: ct);
                    installerDownloaded = true;
                    break;
                }
                catch (HttpRequestException ex)
                {
                    errors.Add($"[{label}] {ex.Message}");
                }
            }
        }

        if (!installerDownloaded)
        {
            string detail = errors.Count > 0 ? string.Join("\n", errors) : "(无详细错误)";
            throw new Exception(
                $"无法下载 NeoForge {neoForgeVersion} Installer（已重试 3 次）。\n\n错误详情:\n{detail}\n\n请检查网络连接。");
        }

        Exception? lastInstallEx = null;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await InstallModernAsync(gameDir, javaPath, installerPath,
                    instanceName, neoForgeVersion, progress, showNotification, ct);
                lastInstallEx = null;
                break;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                lastInstallEx = ex;
                if (attempt < 3)
                {
                    await ModLoaderService.PreDownloadMavenLibrariesAsync(http, gameDir, ex.Message, progress, showNotification, ct);
                    int delayMs = 2000 * attempt;
                    showNotification($"NeoForge 安装失败，正在重试 ({attempt}/3)...", NotificationType.Warning, 0);
                    try { await Task.Delay(delayMs, ct); }
                    catch (OperationCanceledException) { throw; }
                }
            }
        }
        if (lastInstallEx != null)
            throw lastInstallEx;
    }

    private static async Task InstallModernAsync(string gameDir, string javaPath,
        string installerPath, string instanceName, string neoForgeVersion,
        IProgress<DownloadProgress> progress,
        Action<string, NotificationType, int> showNotification,
        CancellationToken ct = default)
    {
        string versionsDir = Path.Combine(gameDir, "versions");
        string targetVersionDir = Path.Combine(versionsDir, instanceName);

        string launcherProfilesPath = Path.Combine(gameDir, "launcher_profiles.json");
        if (!File.Exists(launcherProfilesPath))
            await File.WriteAllTextAsync(launcherProfilesPath, "{\"profiles\":{}}");

        showNotification($"正在执行 NeoForge {neoForgeVersion} 安装器（可能需要几分钟）...", NotificationType.Info, 0);
        progress.Report(DownloadProgress.Overall(0, 1));

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
        psi.ArgumentList.Add("-Djava.awt.headless=true");
        psi.ArgumentList.Add("-jar");
        psi.ArgumentList.Add(installerPath);
        psi.ArgumentList.Add("--installClient");
        psi.ArgumentList.Add(gameDir);

        using var process = Process.Start(psi)
                             ?? throw new Exception("无法启动 NeoForge 安装器进程。");

        var stdout = new System.Text.StringBuilder();
        var stderr = new System.Text.StringBuilder();

        var lastNotify = DateTime.MinValue;
        var notifyLock = new object();

        void NotifyThrottled(string text)
        {
            if (!(text.Contains("ownload") || text.Contains("nstall") || text.Contains("rocess") ||
                  text.Contains("ibrary") || text.Contains("ersion") || text.Contains("xtract") ||
                  text.Contains("apping") || text.Contains("epending") || text.Contains("omplete") ||
                  text.Contains("uccess") || text.Contains("err") || text.Contains("rror") ||
                  text.Contains("WARN") || text.Contains("Done")))
                return;

            lock (notifyLock)
            {
                var now = DateTime.Now;
                if ((now - lastNotify).TotalMilliseconds < 2000) return;
                lastNotify = now;
            }
            showNotification($"NeoForge: {text}", NotificationType.Info, 2000);
        }

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                stdout.AppendLine(e.Data);
                NotifyThrottled(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                stderr.AppendLine(e.Data);
                NotifyThrottled(e.Data);
            }
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var waitCts = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        try { await process.WaitForExitAsync(waitCts.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(); } catch { }
            string errText = stderr.ToString().Trim();
            string logTail = errText.Length > 2000 ? errText[^2000..] : errText;
            throw new Exception($"NeoForge 安装器执行超时（15 分钟）。请检查网络连接后重试。\n\n--- 安装器输出尾部 ---\n{logTail}");
        }

        if (process.ExitCode != 0)
        {
            string outText = stdout.ToString().Trim();
            string errText = stderr.ToString().Trim();
            string combined = (errText + "\n" + outText).Trim();
            if (combined.Length > 8000)
                combined = combined[^8000..];
            throw new Exception($"NeoForge 安装器退出码 {process.ExitCode}:\n{combined}");
        }

        string neoforgeDirName = $"neoforge-{neoForgeVersion}";
        string neoforgeDirPath = Path.Combine(versionsDir, neoforgeDirName);

        if (!Directory.Exists(neoforgeDirPath))
        {
            string? found = null;
            foreach (string dir in Directory.GetDirectories(versionsDir))
            {
                string dirName = Path.GetFileName(dir);
                if (dirName.Contains("neoforge", StringComparison.OrdinalIgnoreCase) &&
                    dirName.Contains(neoForgeVersion))
                {
                    found = dir;
                    break;
                }
            }

            if (found == null)
                throw new Exception($"无法找到 NeoForge 安装器生成的版本目录（预期: {neoforgeDirName}）。");

            neoforgeDirPath = found;
            neoforgeDirName = Path.GetFileName(neoforgeDirPath);
        }

        if (!Directory.Exists(targetVersionDir))
            Directory.CreateDirectory(targetVersionDir);

        string nfJsonPath = Path.Combine(neoforgeDirPath, $"{neoforgeDirName}.json");
        if (!File.Exists(nfJsonPath))
            nfJsonPath = Path.Combine(neoforgeDirPath, "version.json");

        if (File.Exists(nfJsonPath))
        {
            string destJson = Path.Combine(targetVersionDir, "version.json");
            File.Copy(nfJsonPath, destJson, overwrite: true);
        }

        foreach (string jar in Directory.GetFiles(neoforgeDirPath, "*.jar"))
        {
            string destJar = Path.Combine(targetVersionDir, Path.GetFileName(jar));
            File.Copy(jar, destJar, overwrite: true);
        }

        string nfVersionJsonFile = Path.Combine(targetVersionDir, "version.json");
        if (File.Exists(nfVersionJsonFile))
        {
            var nfDetail = JsonSerializer.Deserialize<VersionDetail>(File.ReadAllText(nfVersionJsonFile));
            if (nfDetail?.Id != null)
            {
                string expectedJar = Path.Combine(targetVersionDir, $"{nfDetail.Id}.jar");
                if (!File.Exists(expectedJar))
                {
                    string parentMcVersion = nfDetail.InheritsFrom ?? "";
                    if (!string.IsNullOrEmpty(parentMcVersion))
                    {
                        string vanillaFallback = Path.Combine(gameDir, "versions", parentMcVersion, $"{parentMcVersion}.jar");
                        if (File.Exists(vanillaFallback))
                            File.Copy(vanillaFallback, expectedJar);
                    }
                }
            }
        }

        string nfLibsDir = Path.Combine(neoforgeDirPath, "libraries");
        if (Directory.Exists(nfLibsDir))
        {
            string targetLibsDir = Path.Combine(targetVersionDir, "libraries");
            ModLoaderService.CopyDirectoryRecursive(nfLibsDir, targetLibsDir);
        }

        try { Directory.Delete(neoforgeDirPath, true); }
        catch
        {
            // Retry up to 2 more times (file locks may be released shortly)
            for (int retry = 0; retry < 2; retry++)
            {
                try { await Task.Delay(500); Directory.Delete(neoforgeDirPath, true); break; }
                catch { /* best effort */ }
            }
        }

        progress.Report(DownloadProgress.Overall(1, 1));

        ModLoaderService.SaveInstallerLog(targetVersionDir, stdout.ToString(), stderr.ToString());
        ModLoaderService.SaveInstanceMeta(targetVersionDir, "NeoForge", neoForgeVersion);
    }
}
