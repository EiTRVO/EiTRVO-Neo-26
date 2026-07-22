using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EiTRVO.ProEngine.Helpers;
using EiTRVO.ProEngine.Models;
using static EiTRVO.ProEngine.Helpers.Endpoints;

namespace EiTRVO.ProEngine.Services.Loaders;

internal static class ForgeInstaller
{
    public static async Task<List<ModLoaderVersion>> GetVersionsAsync(HttpClient http, string mcVersion, CancellationToken ct = default)
    {
        string json;
        try
        {
            json = await http.GetStringAsync(ForgePromoMetadata, ct);
        }
        catch (Exception ex)
        {
            throw new Exception($"无法获取 Forge 版本列表: {ex.Message}");
        }

        var promo = JsonSerializer.Deserialize<ForgePromotionsResponse>(json)
                    ?? new ForgePromotionsResponse();

        var result = new List<ModLoaderVersion>();

        if (promo.Promos == null)
            return result;

        string latestKey = $"{mcVersion}-latest";
        string recommendedKey = $"{mcVersion}-recommended";

        promo.Promos.TryGetValue(latestKey, out var latestVer);
        promo.Promos.TryGetValue(recommendedKey, out var recVer);

        if (string.IsNullOrEmpty(latestVer) && string.IsNullOrEmpty(recVer))
            return result;

        if (!string.IsNullOrEmpty(recVer))
        {
            result.Add(new ModLoaderVersion
            {
                LoaderType = "Forge",
                LoaderVersion = recVer,
                MinecraftVersion = mcVersion,
                IsRecommended = true,
                IsLatest = recVer == latestVer
            });
        }

        if (!string.IsNullOrEmpty(latestVer) && latestVer != recVer)
        {
            result.Add(new ModLoaderVersion
            {
                LoaderType = "Forge",
                LoaderVersion = latestVer,
                MinecraftVersion = mcVersion,
                IsRecommended = false,
                IsLatest = true
            });
        }

        return result;
    }

    public static async Task InstallAsync(HttpClient http, string gameDir, string mcVersion,
        string forgeVersion, string instanceName, string javaPath,
        IProgress<DownloadProgress> progress,
        Action<string, NotificationType, int> showNotification,
        CancellationToken ct = default)
    {
        string primaryUrl = ForgeInstaller(mcVersion, forgeVersion);
        string fallbackUrl = ForgeInstaller(mcVersion, forgeVersion + "-" + mcVersion);

        string installerDir = Path.Combine(gameDir, "installer_cache");
        Directory.CreateDirectory(installerDir);
        string installerPath = Path.Combine(installerDir, $"forge-{mcVersion}-{forgeVersion}-installer.jar");

        showNotification("正在下载 Forge Installer...", NotificationType.Info, 0);

        var urls = new List<(string Url, string Label)>
        {
            (primaryUrl, "Official"),
            (fallbackUrl, "Official (legacy)")
        };

        bool installerDownloaded = false;
        var errors = new List<string>();
        for (int attempt = 0; attempt < 3 && !installerDownloaded; attempt++)
        {
            if (attempt > 0)
            {
                int delayMs = 1500 * attempt;
                showNotification($"Forge Installer 下载失败，正在重试 ({attempt}/2)...", NotificationType.Warning, 0);
                try { await Task.Delay(delayMs, ct); }
                catch (OperationCanceledException) { throw; }
                errors.Clear();
            }

            foreach (var (url, label) in urls)
            {
                try
                {
                    await ModLoaderService.DownloadFileAsync(http, url, installerPath, progress, $"forge-{forgeVersion}-installer.jar", ct: ct);
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
                $"无法下载 Forge {forgeVersion} Installer（已重试 3 次）。\n\n错误详情:\n{detail}\n\n请检查网络连接，或前往 https://files.minecraftforge.net/ 手动下载。");
        }

        bool isLegacy = ModLoaderService.IsLegacyMcVersion(mcVersion);

        Exception? lastInstallEx = null;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                if (isLegacy)
                {
                    await InstallLegacyAsync(http, gameDir, mcVersion, forgeVersion,
                        instanceName, installerPath, progress, showNotification, ct);
                }
                else
                {
                    await InstallModernAsync(gameDir, javaPath, installerPath,
                        instanceName, mcVersion, forgeVersion, progress, showNotification, ct);
                }
                lastInstallEx = null;
                break;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                lastInstallEx = ex;
                if (attempt < 3)
                {
                    if (!isLegacy)
                        await ModLoaderService.PreDownloadMavenLibrariesAsync(http, gameDir, ex.Message, progress, showNotification, ct);
                    int delayMs = 2000 * attempt;
                    showNotification($"Forge 安装失败，正在重试 ({attempt}/3)...", NotificationType.Warning, 0);
                    await Task.Delay(delayMs, ct);
                }
            }
        }
        if (lastInstallEx != null)
            throw lastInstallEx;
    }

    private static async Task InstallLegacyAsync(HttpClient http, string gameDir,
        string mcVersion, string forgeVersion, string instanceName,
        string installerPath, IProgress<DownloadProgress> progress,
        Action<string, NotificationType, int> showNotification,
        CancellationToken ct = default)
    {
        string targetVersionDir = Path.Combine(gameDir, "versions", instanceName);
        Directory.CreateDirectory(targetVersionDir);

        VersionDetail? versionInfo = null;
        string? installerFile = null;

        using (var zip = ZipFile.OpenRead(installerPath))
        {
            var profileEntry = zip.GetEntry("install_profile.json")
                        ?? throw new Exception("install_profile.json 未在 Forge Installer JAR 中找到。");

            using var stream = profileEntry.Open();
            using var doc = await JsonDocument.ParseAsync(stream);

            if (doc.RootElement.TryGetProperty("install", out var installSection) &&
                installSection.TryGetProperty("filePath", out var fp))
            {
                installerFile = fp.GetString();
            }

            if (doc.RootElement.TryGetProperty("versionInfo", out var vi))
            {
                versionInfo = JsonSerializer.Deserialize<VersionDetail>(vi.GetRawText());
            }
            else if (doc.RootElement.TryGetProperty("json", out var jsonRef) &&
                     jsonRef.GetString() is string jsonRefPath)
            {
                var versionEntry = zip.GetEntry(jsonRefPath.TrimStart('/'))
                    ?? throw new Exception($"Forge Installer JAR 中未找到 {jsonRefPath}。");

                using var vs = versionEntry.Open();
                versionInfo = await JsonSerializer.DeserializeAsync<VersionDetail>(vs);
            }
        }

        if (versionInfo == null)
            throw new Exception("无法从 Forge Installer 中提取 versionInfo。");

        var libsToDownload = new List<(string url, string path)>();
        string libDir = Path.Combine(gameDir, "libraries");

        if (versionInfo.Libraries != null)
        {
            foreach (var lib in versionInfo.Libraries)
            {
                string? path;
                string? url;

                var art = lib.Downloads?.Artifact;
                if (art?.Path != null)
                {
                    path = art.Path;
                    url = art.Url;
                }
                else if (!string.IsNullOrEmpty(lib.Name))
                {
                    path = ModLoaderService.MavenNameToPath(lib.Name);
                    url = null;
                }
                else
                {
                    continue;
                }

                if (string.IsNullOrEmpty(path))
                    continue;

                string dest = Path.Combine(libDir, path);
                if (File.Exists(dest))
                    continue;

                if (string.IsNullOrEmpty(url) && path.Contains("forge"))
                {
                    PathSafetyHelper.ValidateContained(dest, libDir);
                    bool extracted = ModLoaderService.ExtractForgeJar(installerPath, path, installerFile, dest);
                    if (extracted)
                        continue;
                }

                if (string.IsNullOrEmpty(url))
                    url = ModLoaderService.ResolveLibraryUrl(lib, path);
                libsToDownload.Add((url, dest));
            }
        }

        showNotification("正在下载 Forge 依赖库...", NotificationType.Info, 0);

        int total = libsToDownload.Count + 1;
        int completed = 0;
        int failedLibs = 0;

        if (libsToDownload.Count > 0)
        {
            using var sem = new SemaphoreSlim(16);
            var tasks = libsToDownload.Select(async item =>
            {
                await sem.WaitAsync();
                try
                {
                    await ModLoaderService.DownloadFileWithFallbackAsync(http, item.url, item.path, progress, Path.GetFileName(item.path), ct);
                    int c = Interlocked.Increment(ref completed);
                    progress.Report(DownloadProgress.Overall(c, total));
                }
                catch (Exception)
                {
                    int f = Interlocked.Increment(ref failedLibs);
                    showNotification($"依赖库下载失败 ({f}): {Path.GetFileName(item.path)}", NotificationType.Warning, 4000);
                }
                finally { sem.Release(); }
            });
            await Task.WhenAll(tasks);
        }

        if (failedLibs > 0)
            showNotification($"警告: {failedLibs} 个依赖库下载失败，Forge 可能无法正常启动。", NotificationType.Warning, 6000);

        string versionJsonPath = Path.Combine(targetVersionDir, "version.json");
        string rawJson = JsonSerializer.Serialize(versionInfo, new JsonSerializerOptions { WriteIndented = true });
        string normalizedJson = ModLoaderService.NormalizeMavenLibraries(rawJson);
        await File.WriteAllTextAsync(versionJsonPath, normalizedJson);

        completed++;
        progress.Report(DownloadProgress.Overall(completed, total));

        string vanillaJar = Path.Combine(gameDir, "versions", mcVersion, $"{mcVersion}.jar");
        string targetJar = Path.Combine(targetVersionDir, $"{versionInfo.Id}.jar");
        if (File.Exists(vanillaJar) && !File.Exists(targetJar))
            File.Copy(vanillaJar, targetJar);

        string vanillaNatives = Path.Combine(gameDir, "natives", mcVersion);
        string forgeNatives = Path.Combine(gameDir, "natives", versionInfo.Id);
        if (Directory.Exists(vanillaNatives) && !Directory.Exists(forgeNatives))
        {
            Directory.CreateDirectory(forgeNatives);
            foreach (var file in Directory.GetFiles(vanillaNatives))
                File.Copy(file, Path.Combine(forgeNatives, Path.GetFileName(file)));
        }

        ModLoaderService.SaveInstanceMeta(targetVersionDir, "Forge", forgeVersion);
    }

    private static async Task InstallModernAsync(string gameDir, string javaPath,
        string installerPath, string instanceName, string mcVersion,
        string forgeVersion, IProgress<DownloadProgress> progress,
        Action<string, NotificationType, int> showNotification,
        CancellationToken ct = default)
    {
        string versionsDir = Path.Combine(gameDir, "versions");
        string targetVersionDir = Path.Combine(versionsDir, instanceName);

        string launcherProfilesPath = Path.Combine(gameDir, "launcher_profiles.json");
        if (!File.Exists(launcherProfilesPath))
            await File.WriteAllTextAsync(launcherProfilesPath, "{\"profiles\":{}}");

        showNotification($"正在执行 Forge {forgeVersion} 安装器（可能需要几分钟）...", NotificationType.Info, 0);
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
                             ?? throw new Exception("无法启动 Forge 安装器进程。");

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
            showNotification($"Forge: {text}", NotificationType.Info, 2000);
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
            throw new Exception($"Forge 安装器执行超时（15 分钟）。请检查网络连接后重试。\n\n--- 安装器输出尾部 ---\n{logTail}");
        }

        if (process.ExitCode != 0)
        {
            string outText = stdout.ToString().Trim();
            string errText = stderr.ToString().Trim();
            string combined = (errText + "\n" + outText).Trim();
            if (combined.Length > 8000)
                combined = combined[^8000..];
            throw new Exception($"Forge 安装器退出码 {process.ExitCode}:\n{combined}");
        }

        string forgeDirName = $"{mcVersion}-forge-{forgeVersion}";
        string forgeDirPath = Path.Combine(versionsDir, forgeDirName);

        if (!Directory.Exists(forgeDirPath))
        {
            string? found = null;
            foreach (string dir in Directory.GetDirectories(versionsDir))
            {
                string dirName = Path.GetFileName(dir);
                if (dirName.Contains("forge") && dirName.Contains(forgeVersion))
                {
                    found = dir;
                    break;
                }
            }

            if (found == null)
                throw new Exception($"无法找到 Forge 安装器生成的版本目录（预期: {forgeDirName}）。");

            forgeDirPath = found;
            forgeDirName = Path.GetFileName(forgeDirPath);
        }

        if (!Directory.Exists(targetVersionDir))
            Directory.CreateDirectory(targetVersionDir);

        string forgeJsonPath = Path.Combine(forgeDirPath, $"{forgeDirName}.json");
        if (!File.Exists(forgeJsonPath))
            forgeJsonPath = Path.Combine(forgeDirPath, "version.json");

        if (File.Exists(forgeJsonPath))
        {
            string destJson = Path.Combine(targetVersionDir, "version.json");
            File.Copy(forgeJsonPath, destJson, overwrite: true);
        }

        foreach (string jar in Directory.GetFiles(forgeDirPath, "*.jar"))
        {
            string destJar = Path.Combine(targetVersionDir, Path.GetFileName(jar));
            File.Copy(jar, destJar, overwrite: true);
        }

        string forgeVersionJsonFile = Path.Combine(targetVersionDir, "version.json");
        if (File.Exists(forgeVersionJsonFile))
        {
            var forgeDetail = JsonSerializer.Deserialize<VersionDetail>(File.ReadAllText(forgeVersionJsonFile));
            if (forgeDetail?.Id != null)
            {
                string expectedJar = Path.Combine(targetVersionDir, $"{forgeDetail.Id}.jar");
                if (!File.Exists(expectedJar))
                {
                    string vanillaFallback = Path.Combine(gameDir, "versions", mcVersion, $"{mcVersion}.jar");
                    if (File.Exists(vanillaFallback))
                        File.Copy(vanillaFallback, expectedJar);
                }
            }
        }

        string forgeLibsDir = Path.Combine(forgeDirPath, "libraries");
        if (Directory.Exists(forgeLibsDir))
        {
            string targetLibsDir = Path.Combine(targetVersionDir, "libraries");
            ModLoaderService.CopyDirectoryRecursive(forgeLibsDir, targetLibsDir);
        }

        try { Directory.Delete(forgeDirPath, true); }
        catch
        {
            // Retry up to 2 more times (file locks may be released shortly)
            for (int retry = 0; retry < 2; retry++)
            {
                try { await Task.Delay(500); Directory.Delete(forgeDirPath, true); break; }
                catch { /* best effort */ }
            }
        }

        progress.Report(DownloadProgress.Overall(1, 1));

        ModLoaderService.SaveInstallerLog(targetVersionDir, stdout.ToString(), stderr.ToString());
        ModLoaderService.SaveInstanceMeta(targetVersionDir, "Forge", forgeVersion);
    }
}
