using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Helpers;
using static EiTRVO.ProEngine.Helpers.Endpoints;

namespace EiTRVO.ProEngine.Services;

public class DownloadService : IDownloadService
{
    public async Task<List<ManifestVersion>> LoadOnlineVersionsAsync(HttpClient httpClient, CancellationToken ct = default)
    {
        string manifestUrl = VersionManifestUrl;
        string json = await httpClient.GetStringAsync(manifestUrl, ct);
        var manifest = JsonSerializer.Deserialize<VersionManifest>(json);
        return manifest?.Versions ?? new List<ManifestVersion>();
    }

    public async Task DownloadVersionFilesAsync(HttpClient httpClient, string gameDir,
        string versionJsonUrl, string versionId, string instanceName,
        IProgress<DownloadProgress> progress,
        Action<string, NotificationType, int> showNotification,
        CancellationToken ct = default)
    {
        string json = await httpClient.GetStringAsync(versionJsonUrl, ct);
        var detail = JsonSerializer.Deserialize<VersionDetail>(json) ?? throw new Exception("版本信息解析失败");

        string versionDir = Path.Combine(gameDir, "versions", instanceName);
        Directory.CreateDirectory(versionDir);
        File.WriteAllText(Path.Combine(versionDir, "version.json"), json);
        // OptiFine / Mojang convention: also save as {versionId}.json
        File.WriteAllText(Path.Combine(versionDir, $"{versionId}.json"), json);

        // Count unique files to download (dedup by target path).
        // We track "seen" paths during counting so totalFiles matches the actual
        // number of fileCompleted() calls we will make.
        var countedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int totalFiles = 0;

        // Client JAR
        if (detail.Downloads?.Client?.Url != null)
        {
            string p = Path.Combine(Path.Combine(gameDir, "versions", instanceName), $"{versionId}.jar");
            if (countedPaths.Add(p)) totalFiles++;
        }

        // Libraries (dedup by destination path — Mojang JSON may repeat artifacts)
        string libDir = Path.Combine(gameDir, "libraries");
        if (detail.Libraries != null)
        {
            foreach (var lib in detail.Libraries)
            {
                if (!JvmArgHelper.IsRuleAllowed(lib.Rules)) continue;
                if (lib.Downloads?.Artifact?.Path != null && lib.Downloads.Artifact.Url != null)
                {
                    string p = Path.Combine(libDir, lib.Downloads.Artifact.Path);
                    if (countedPaths.Add(p)) totalFiles++;
                }
                if (lib.Natives != null && lib.Natives.TryGetValue("windows", out var nc))
                {
                    var info = lib.Downloads?.Classifiers?.GetValueOrDefault(nc);
                    if (info?.Path != null && info.Url != null)
                    {
                        string p = Path.Combine(libDir, info.Path);
                        if (countedPaths.Add(p)) totalFiles++;
                    }
                }
            }
        }

        // Logging config
        if (detail.Logging?.Client?.File?.Url != null)
        {
            string p = Path.Combine(gameDir, detail.Logging.Client.File.Path ?? "log_configs/temp.xml");
            if (countedPaths.Add(p)) totalFiles++;
        }

        // Asset index JSON
        if (detail.AssetIndex?.Url != null) totalFiles++;

        // Asset objects (preload index to know exact count)
        string assetIdxId = detail.Assets ?? detail.AssetIndex?.Id ?? versionId;
        string idxDir = Path.Combine(gameDir, "assets", "indexes");
        Directory.CreateDirectory(idxDir);
        string idxFile = Path.Combine(idxDir, $"{assetIdxId}.json");

        if (!File.Exists(idxFile) && detail.AssetIndex?.Url != null)
            await DownloadFileWithRetryAsync(httpClient, detail.AssetIndex.Url, idxFile, maxRetries: 2, progress, $"索引 {assetIdxId}.json", ct);

        if (File.Exists(idxFile))
        {
            var assetIndex = JsonSerializer.Deserialize<AssetIndex>(File.ReadAllText(idxFile));
            if (assetIndex?.Objects != null)
                totalFiles += assetIndex.Objects.Values.Count(v => (v.Hash ?? "").Length >= 2);
        }

        // Start progress tracking
        int completed = 0;
        Action fileCompleted = () =>
        {
            int c = Interlocked.Increment(ref completed);
            progress.Report(DownloadProgress.Overall(c, totalFiles));
        };

        // ---------- download phase ----------
        var downloadTasks = new List<Task>();
        using var sem = new SemaphoreSlim(64);
        var scheduledPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Helper: schedule a download if the file is missing, otherwise just tick
        // progress. Deduplicates by destination path so every fileCompleted() call
        // matches exactly one counted file.
        void Schedule(string url, string dest, Action? postDownload = null)
        {
            if (!scheduledPaths.Add(dest)) return;  // already handled — counting deduped too
            if (File.Exists(dest))
            {
                postDownload?.Invoke();
                fileCompleted();
                return;
            }
            string displayName = Path.GetFileName(dest);
            downloadTasks.Add(DownloadWithSemaphoreAsync(httpClient, url, dest, sem, () =>
            {
                postDownload?.Invoke();
                fileCompleted();
            }, progress, displayName, ct));
        }

        // Client JAR
        if (detail.Downloads?.Client?.Url != null)
        {
            string jarPath = Path.Combine(Path.Combine(gameDir, "versions", instanceName), $"{versionId}.jar");
            Schedule(detail.Downloads.Client.Url, jarPath);
        }

        // Libraries
        Directory.CreateDirectory(libDir);
        if (detail.Libraries != null)
        {
            foreach (var lib in detail.Libraries)
            {
                if (!JvmArgHelper.IsRuleAllowed(lib.Rules)) continue;

                if (lib.Downloads?.Artifact?.Url != null && lib.Downloads.Artifact.Path != null)
                {
                    var dest = Path.Combine(libDir, lib.Downloads.Artifact.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    Schedule(lib.Downloads.Artifact.Url, dest);
                }

                if (lib.Natives != null && lib.Natives.TryGetValue("windows", out var nc))
                {
                    var info = lib.Downloads?.Classifiers?.GetValueOrDefault(nc);
                    if (info?.Url != null && info.Path != null)
                    {
                        var jarPath = Path.Combine(libDir, info.Path);
                        Directory.CreateDirectory(Path.GetDirectoryName(jarPath)!);
                        string nativesOut = Path.Combine(gameDir, "natives", versionId);
                        Directory.CreateDirectory(nativesOut);
                        Schedule(info.Url, jarPath, postDownload: () => ExtractNativeJar(jarPath, nativesOut));
                    }
                }
            }
        }

        // Logging config
        if (detail.Logging?.Client?.File != null && detail.Logging.Client.File.Url != null)
        {
            string dest = Path.Combine(gameDir, detail.Logging.Client.File.Path ?? "log_configs/temp.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            Schedule(detail.Logging.Client.File.Url, dest);
        }

        // Asset index (already downloaded above)
        if (detail.AssetIndex?.Url != null)
            fileCompleted();

        // Wait for primary downloads to finish
        await Task.WhenAll(downloadTasks);

        // Download asset objects
        if (File.Exists(idxFile))
        {
            var assetIndex = JsonSerializer.Deserialize<AssetIndex>(File.ReadAllText(idxFile));
            if (assetIndex?.Objects != null && assetIndex.Objects.Count > 0)
            {
                string objDir = Path.Combine(gameDir, "assets", "objects");
                Directory.CreateDirectory(objDir);

                using var assetSem = new SemaphoreSlim(64);
                var assetTasks = new List<Task>();
                int failedCount = 0;
                int successCount = 0;

                foreach (var kv in assetIndex.Objects)
                {
                    string hash = kv.Value.Hash ?? "";
                    if (hash.Length < 2)
                    {
                        fileCompleted();  // counted → must tick
                        continue;
                    }

                    string subDir = hash.Substring(0, 2);
                    string dest = Path.Combine(objDir, subDir, hash);

                    if (File.Exists(dest))
                    {
                        fileCompleted();
                        continue;
                    }

                    string url = AssetDownload(hash);

                    assetTasks.Add(Task.Run(async () =>
                    {
                        await assetSem.WaitAsync();
                        try
                        {
                            Directory.CreateDirectory(Path.Combine(objDir, subDir));

                            bool downloaded = false;
                            for (int retry = 0; retry < 3; retry++)
                            {
                                try
                                {
                                    // Use DownloadFileFastAsync for consistent per-file progress
                                    await DownloadFileFastAsync(httpClient, url, dest, progress, hash, ct, hash);
                                    downloaded = true;
                                    Interlocked.Increment(ref successCount);
                                    break;
                                }
                                catch
                                {
                                    if (retry < 2) await Task.Delay(800 * (retry + 1));
                                }
                            }

                            if (!downloaded)
                                Interlocked.Increment(ref failedCount);
                        }
                        finally
                        {
                            assetSem.Release();
                            fileCompleted();
                        }
                    }));
                }
                await Task.WhenAll(assetTasks);

                if (failedCount > 0)
                    showNotification($"资源文件 {failedCount} 个下载失败，{successCount} 个下载成功。",
                        NotificationType.Warning, 5000);
            }
        }
    }

    public async Task DownloadFileWithRetryAsync(HttpClient httpClient, string url, string path,
        int maxRetries = 2, IProgress<DownloadProgress>? progress = null, string? displayName = null,
        CancellationToken ct = default)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await DownloadFileFastAsync(httpClient, url, path, progress, displayName, ct);
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                if (attempt == maxRetries) throw;
                try { await Task.Delay(800, ct); }
                catch (OperationCanceledException) { throw; }
            }
        }
    }

    private async Task DownloadFileFastAsync(HttpClient httpClient, string url, string path,
        IProgress<DownloadProgress>? progress = null, string? displayName = null,
        CancellationToken ct = default, string? expectedSha1 = null)
    {
        using var resp = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        // Write to temp file first, then atomically rename — prevents
        // "file used by another process" when duplicate paths race.
        long totalBytes = resp.Content.Headers.ContentLength ?? -1;
        string fileName = displayName ?? Path.GetFileName(path);

        // Reject unreasonably large files (malicious or misconfigured)
        const long maxFileSize = 500 * 1024 * 1024; // 500 MB
        if (totalBytes > maxFileSize)
            throw new InvalidOperationException(
                $"文件 {fileName} 大小 ({totalBytes / 1024 / 1024} MB) 超过上限 (500 MB)，已拒绝。");

        string tmp = path + ".part";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long downloadedBytes = 0;
        long lastReportedBytes = 0;
        double lastReportTime = 0;

        try
        {
            await using var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true);
            await using var contentStream = await resp.Content.ReadAsStreamAsync();

            byte[] buffer = new byte[8192];
            int bytesRead;
            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                await fs.WriteAsync(buffer, 0, bytesRead);
                downloadedBytes += bytesRead;

                // Report per-file progress every ~64 KB or when complete
                if (progress != null && (downloadedBytes - lastReportedBytes >= 65536 || downloadedBytes == totalBytes))
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
            try { File.Delete(tmp); } catch { /* best effort */ }
            throw;
        }
        // SHA-1 integrity verification
        if (!string.IsNullOrEmpty(expectedSha1))
        {
            string actualSha1 = ComputeSha1(tmp);
            if (!string.Equals(actualSha1, expectedSha1, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(tmp); } catch { }
                throw new InvalidDataException(
                    $"文件 SHA-1 校验失败: {displayName ?? fileName}\n期望: {expectedSha1}\n实际: {actualSha1}");
            }
        }

        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>计算文件的 SHA-1 哈希值（小写十六进制）。</summary>
    private static string ComputeSha1(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        byte[] hash = SHA1.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task DownloadWithSemaphoreAsync(HttpClient httpClient, string url, string path,
        SemaphoreSlim sem, Action onComplete,
        IProgress<DownloadProgress>? progress = null, string? displayName = null,
        CancellationToken ct = default)
    {
        await sem.WaitAsync(ct);
        try
        {
            await DownloadFileWithRetryAsync(httpClient, url, path, maxRetries: 2, progress, displayName, ct);
            onComplete();
        }
        finally
        {
            sem.Release();
        }
    }

    public void ExtractNativeJar(string jarPath, string destDir)
    {
        using var archive = ZipFile.OpenRead(jarPath);
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.StartsWith("META-INF", StringComparison.OrdinalIgnoreCase)) continue;
            string target = Path.GetFullPath(Path.Combine(destDir, entry.FullName));
            if (!target.StartsWith(Path.GetFullPath(destDir) + Path.DirectorySeparatorChar))
                throw new InvalidDataException("路径穿越检测");
            if (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\"))
                Directory.CreateDirectory(target);
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, true);
            }
        }
    }
}
