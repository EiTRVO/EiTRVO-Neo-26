using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EiTRVO.ProEngine.Helpers;
using EiTRVO.ProEngine.Models;
using static EiTRVO.ProEngine.Helpers.Endpoints;

namespace EiTRVO.ProEngine.Services;

public class ModrinthService : IModrinthService
{
    private readonly HttpClient _httpClient;

    /// <summary>Files smaller than this are downloaded via single connection to avoid chunking overhead.</summary>
    private const long ChunkedDownloadMinSize = 1 * 1024 * 1024; // 1 MB

    /// <summary>Maximum parallel chunks for multi-threaded download.</summary>
    private static readonly int MaxChunkCount = Math.Min(8, Environment.ProcessorCount * 2);

    /// <summary>当为 true 时，跳过 HTTP Range 分块检测，直接使用单连接下载。</summary>
    public bool ForceSingleConnection { get; set; } = false;

    /// <summary>Choose chunk count based on file size. Larger files benefit from more parallelism.</summary>
    private static int GetChunkCount(long totalBytes) => totalBytes switch
    {
        < 16 * 1024 * 1024 => 4,   //  4-16 MB:  4 chunks (~4 MB each)
        < 50 * 1024 * 1024 => 6,   // 16-50 MB:  6 chunks (~8 MB each)
        _ => MaxChunkCount,  // 50+ MB:    8 chunks (~6+ MB each)
    };

    /// <summary>Represents a single file to download in the dependency chain.</summary>
    private readonly record struct DownloadItem(string ProjectId, string Url, string DestPath, long FileSize, string? Sha1);

    public ModrinthService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    // ==================== Search ====================

    public async Task<ModrinthSearchResponse> SearchAsync(string query, string mcVersion,
        string loader, int limit = 20, int offset = 0, CancellationToken ct = default)
    {
        string modrinthLoader = MapLoaderToModrinth(loader);
        string facets = $"[[\"categories:{modrinthLoader}\"],[\"versions:{mcVersion}\"],[\"project_type:mod\"]]";
        string url = $"{ModrinthApi}/search?query={Uri.EscapeDataString(query)}" +
                     $"&facets={Uri.EscapeDataString(facets)}" +
                     $"&limit={limit}&offset={offset}&index=relevance";

        var json = await _httpClient.GetStringAsync(url, ct);
        return JsonSerializer.Deserialize<ModrinthSearchResponse>(json)
            ?? new ModrinthSearchResponse();
    }

    // ==================== Version Lookup ====================

    public async Task<ModrinthVersion?> GetLatestVersionAsync(string projectId, string mcVersion, string loader,
        CancellationToken ct = default)
    {
        string modrinthLoader = MapLoaderToModrinth(loader);
        string url = $"{ModrinthApi}/project/{Uri.EscapeDataString(projectId)}/version" +
                     $"?loaders={Uri.EscapeDataString($"[\"{modrinthLoader}\"]")}" +
                     $"&game_versions={Uri.EscapeDataString($"[\"{mcVersion}\"]")}";

        var json = await _httpClient.GetStringAsync(url, ct);
        var versions = JsonSerializer.Deserialize<ModrinthVersion[]>(json);

        if (versions == null || versions.Length == 0)
            return null;

        var preferred = versions.FirstOrDefault(v => v.Featured && v.VersionType == "release")
                        ?? versions.FirstOrDefault(v => v.VersionType == "release")
                        ?? versions[0];

        return preferred;
    }

    // ==================== Chunked Multi-Threaded Download ====================

    public async Task DownloadModAsync(string url, string destinationPath,
        IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        string fileName = Path.GetFileName(destinationPath);

        // Validate download URL against allowed domains
        if (!Helpers.DownloadSafetyHelper.IsDownloadUrlAllowed(url))
            throw new InvalidOperationException(
                $"下载 URL 指向非允许域名，已拒绝: {new Uri(url).Host}");

        // Phase 1: HEAD request to detect Range support
        long totalBytes = 0;
        bool supportsRange = false;

        try
        {
            using var headReq = new HttpRequestMessage(HttpMethod.Head, url);
            using var headResp = await _httpClient.SendAsync(headReq, ct);
            headResp.EnsureSuccessStatusCode();
            totalBytes = headResp.Content.Headers.ContentLength ?? 0;
            supportsRange = headResp.Headers.AcceptRanges?.Contains("bytes") == true
                            && totalBytes >= ChunkedDownloadMinSize;
        }
        catch (OperationCanceledException) { throw; }
        catch { /* HEAD failed — fall back to single-connection */ }

        ct.ThrowIfCancellationRequested();

        // Reject unreasonably large files (malicious or misconfigured)
        const long maxFileSize = 1024 * 1024 * 1024; // 1 GB
        if (totalBytes > maxFileSize)
            throw new InvalidOperationException(
                $"文件 {fileName} 大小 ({totalBytes / 1024 / 1024} MB) 超过上限 (1 GB)，已拒绝。");

        // Phase 2: Download
        if (ForceSingleConnection)
        {
            await DownloadSingleConnectionAsync(url, destinationPath, totalBytes, fileName,
                progress, ct);
            return;
        }

        if (!supportsRange)
        {
            await DownloadSingleConnectionAsync(url, destinationPath, totalBytes, fileName,
                progress, ct);
            return;
        }

        CleanupPartFiles(destinationPath);
        bool chunkedOk = false;
        try
        {
            await DownloadChunkedAsync(url, destinationPath, totalBytes, fileName,
                progress, ct);
            chunkedOk = true;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            CleanupPartFiles(destinationPath);
        }

        if (!chunkedOk)
            await DownloadSingleConnectionAsync(url, destinationPath, totalBytes, fileName,
                progress, ct);
    }

    // ==================== Single-Connection Download (Fallback) ====================

    private async Task DownloadSingleConnectionAsync(string url, string destinationPath,
        long totalBytes, string fileName, IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        string tmpPath = destinationPath + ".part";
        try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _httpClient.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        if (totalBytes == 0)
            totalBytes = response.Content.Headers.ContentLength ?? 0;
        long downloaded = 0;

        await using var sourceStream = await response.Content.ReadAsStreamAsync(ct);

        FileStream destStream;
        try
        {
            destStream = new FileStream(tmpPath, FileMode.CreateNew,
                FileAccess.Write, FileShare.Read, 65536, useAsync: true);
        }
        catch (IOException)
        {
            tmpPath = Path.Combine(Path.GetDirectoryName(destinationPath)!,
                $"{Path.GetFileNameWithoutExtension(destinationPath)}_{Guid.NewGuid():N}.part");
            destStream = new FileStream(tmpPath, FileMode.CreateNew,
                FileAccess.Write, FileShare.Read, 65536, useAsync: true);
        }
        await using (destStream)
        {
            byte[] buffer = new byte[65536];
            int bytesRead;
            var speedSw = Stopwatch.StartNew();
            long prevDownloaded = 0;
            double lastSpeedTime = 0;

            while ((bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                await destStream.WriteAsync(buffer, 0, bytesRead, ct);
                downloaded += bytesRead;

                if (speedSw.Elapsed.TotalMilliseconds >= 100)
                {
                    double now = speedSw.Elapsed.TotalSeconds;
                    double dt = now - lastSpeedTime;
                    double speed = dt > 0.05 ? (downloaded - prevDownloaded) / dt : 0;
                    progress?.Report(DownloadProgress.FileProgress(
                        fileName, downloaded, totalBytes, speed));
                    prevDownloaded = downloaded;
                    lastSpeedTime = now;
                    speedSw.Restart();
                }
            }
        }

        progress?.Report(DownloadProgress.FileProgress(fileName, downloaded, totalBytes, 0));
        FinalizeDownload(tmpPath, destinationPath);
    }

    // ==================== Chunked Parallel Download ====================

    private async Task DownloadChunkedAsync(string url, string destinationPath,
        long totalBytes, string fileName, IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        int threadCount = GetChunkCount(totalBytes);
        long chunkSize = totalBytes / threadCount;

        var chunks = new (long Start, long End)[threadCount];
        for (int i = 0; i < threadCount; i++)
        {
            long start = i * chunkSize;
            long end = (i == threadCount - 1) ? totalBytes - 1 : (i + 1) * chunkSize - 1;
            chunks[i] = (start, end);
        }

        string[] partFiles = new string[threadCount];
        long totalDownloaded = 0;
        var sw = Stopwatch.StartNew();
        double lastReportTime = 0;

        await Parallel.ForEachAsync(
            chunks.Select((c, i) => (Range: c, Index: i)),
            new ParallelOptions { MaxDegreeOfParallelism = threadCount, CancellationToken = ct },
            async (item, innerCt) =>
            {
                string partFile = destinationPath + $".part.{item.Index}";
                partFiles[item.Index] = partFile;

                long chunkDownloaded = await DownloadChunkAsync(url,
                    item.Range.Start, item.Range.End, partFile, innerCt);

                long cumulative = Interlocked.Add(ref totalDownloaded, chunkDownloaded);

                double now = sw.Elapsed.TotalSeconds;
                double last = Volatile.Read(ref lastReportTime);
                if (now - last >= 0.1 || cumulative >= totalBytes)
                {
                    if (Interlocked.CompareExchange(ref lastReportTime, now, last) == last)
                    {
                        double speed = now > 0.1 ? cumulative / now : 0;
                        progress?.Report(DownloadProgress.FileProgress(
                            fileName, cumulative, totalBytes, speed));
                    }
                }
            });

        ct.ThrowIfCancellationRequested();

        await using (var destStream = new FileStream(destinationPath, FileMode.Create,
            FileAccess.Write, FileShare.None, 65536, useAsync: true))
        {
            foreach (var partFile in partFiles)
            {
                ct.ThrowIfCancellationRequested();
                await using var partStream = new FileStream(partFile, FileMode.Open,
                    FileAccess.Read, FileShare.Read, 65536, useAsync: true);
                await partStream.CopyToAsync(destStream, 65536, ct);
            }
        }

        foreach (var pf in partFiles)
            try { File.Delete(pf); } catch { }

        progress?.Report(DownloadProgress.FileProgress(fileName, totalBytes, totalBytes, 0));
    }

    private async Task<long> DownloadChunkAsync(string url, long rangeStart, long rangeEnd,
        string partFile, CancellationToken ct)
    {
        for (int attempt = 0; attempt <= 2; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return await DownloadChunkOnceAsync(url, rangeStart, rangeEnd, partFile, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch when (attempt < 2)
            {
                await Task.Delay(300 * (attempt + 1), ct);
            }
        }

        return await DownloadChunkOnceAsync(url, rangeStart, rangeEnd, partFile, ct);
    }

    private async Task<long> DownloadChunkOnceAsync(string url, long rangeStart, long rangeEnd,
        string partFile, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(rangeStart, rangeEnd);

        using var response = await _httpClient.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var sourceStream = await response.Content.ReadAsStreamAsync(ct);
        await using var destStream = new FileStream(partFile, FileMode.Create,
            FileAccess.Write, FileShare.None, 65536, useAsync: true);

        byte[] buffer = new byte[65536];
        long chunkDownloaded = 0;
        int bytesRead;

        while ((bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
        {
            await destStream.WriteAsync(buffer, 0, bytesRead, ct);
            chunkDownloaded += bytesRead;
        }

        return chunkDownloaded;
    }

    // ==================== Two-Phase Dependency Download ====================

    public async Task<List<string>> DownloadWithDependenciesAsync(
        string projectId, string mcVersion, string loader, string modsFolder,
        IProgress<string>? status = null, IProgress<DownloadProgress>? fileProgress = null,
        CancellationToken ct = default)
    {
        // Phase 1: Collect all files to download (resolve deps recursively)
        var queue = new List<DownloadItem>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await CollectDependenciesAsync(projectId, mcVersion, loader, modsFolder,
            queue, visited, isRequired: true, status, ct);

        if (queue.Count == 0)
            return new List<string>();

        // Phase 2: Download each file with count-aware progress
        var downloaded = new List<string>();
        int total = queue.Count;
        int completed = 0;

        for (int i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();

            var item = queue[i];
            string fileName = Path.GetFileName(item.DestPath);

            // SHA-1 dedup: skip if file already exists with matching hash
            if (File.Exists(item.DestPath))
            {
                bool hashMatch = false;
                if (!string.IsNullOrEmpty(item.Sha1))
                {
                    try
                    {
                        string existingHash = ComputeSha1(item.DestPath);
                        hashMatch = string.Equals(existingHash, item.Sha1, StringComparison.OrdinalIgnoreCase);
                    }
                    catch { /* re-download on compute failure */ }
                }

                if (hashMatch)
                {
                    completed++;
                    status?.Report($"跳过({completed}/{total}): {fileName} (已存在)");
                    fileProgress?.Report(DownloadProgress.FileProgress(
                        fileName, item.FileSize, item.FileSize, 0, completed, total));
                    downloaded.Add(fileName);
                    continue;
                }

                // Hash mismatch or no SHA-1 — delete stale file before re-download
                try { File.Delete(item.DestPath); } catch { }
            }

            status?.Report($"下载({completed + 1}/{total}): {fileName}");
            fileProgress?.Report(DownloadProgress.FileProgress(
                fileName, 0, item.FileSize, 0, completed + 1, total));

            await DownloadModAsync(item.Url, item.DestPath, fileProgress, ct);

            // Post-download SHA-1 verification
            if (!string.IsNullOrEmpty(item.Sha1))
            {
                string actualSha1 = ComputeSha1(item.DestPath);
                if (!string.Equals(actualSha1, item.Sha1, StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(item.DestPath); } catch { }
                    throw new InvalidDataException(
                        $"前置模组 SHA-1 校验失败: {fileName}\n期望: {item.Sha1}\n实际: {actualSha1}");
                }
            }

            completed++;
            downloaded.Add(fileName);
        }

        return downloaded;
    }

    private async Task CollectDependenciesAsync(
        string projectId, string mcVersion, string loader, string modsFolder,
        List<DownloadItem> queue, HashSet<string> visited,
        bool isRequired, IProgress<string>? status, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!visited.Add(projectId)) return;

        status?.Report($"解析: {projectId}...");

        var version = await GetLatestVersionAsync(projectId, mcVersion, loader, ct);
        if (version == null)
        {
            if (isRequired)
                throw new Exception($"前置模组 {projectId} 无兼容 {mcVersion}+{loader} 的版本");
            return;
        }

        var file = version.Files.FirstOrDefault(f => f.Primary)
                   ?? version.Files.FirstOrDefault();
        if (file == null) throw new Exception("版本中没有可下载的文件。");
        string safeName = Path.GetFileName(file.Filename);
        string destPath = Path.Combine(modsFolder, safeName);
        PathSafetyHelper.ValidateContained(destPath, modsFolder);
        queue.Add(new DownloadItem(projectId, file.Url, destPath, file.Size, file.Hashes?.Sha1));

        foreach (var dep in version.Dependencies)
        {
            ct.ThrowIfCancellationRequested();
            if (dep.DependencyType == "incompatible") continue;
            if (string.IsNullOrEmpty(dep.ProjectId)) continue;

            bool req = dep.DependencyType == "required" || dep.DependencyType == "embedded";
            await CollectDependenciesAsync(dep.ProjectId, mcVersion, loader, modsFolder,
                queue, visited, req, status, ct);
        }
    }

    // ==================== Helpers ====================

    public async Task<bool> VerifyFileByHashAsync(string sha1, CancellationToken ct = default)
    {
        try
        {
            var result = await GetVersionFilesByHashesAsync(new[] { sha1 }, ct);
            return result.ContainsKey(sha1);
        }
        catch { return false; }
    }

    /// <summary>Bulk SHA-1 → VersionFileResponse via <c>POST /version_files</c>.
    /// Falls back to individual <c>GET /version_file/{hash}</c> lookups if the bulk
    /// endpoint returns an error (e.g. 400 on malformed input).</summary>
    public async Task<Dictionary<string, VersionFileResponse>> GetVersionFilesByHashesAsync(
        IReadOnlyList<string> sha1s, CancellationToken ct = default)
    {
        var result = new Dictionary<string, VersionFileResponse>(StringComparer.OrdinalIgnoreCase);
        if (sha1s.Count == 0) return result;

        // Filter out invalid hashes (must be 40-char hex)
        var valid = sha1s.Where(s => s.Length == 40 && s.All(c =>
            c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F')).ToList();
        if (valid.Count == 0) return result;

        try
        {
            var body = new { hashes = valid, algorithm = "sha1" };
            var json = JsonSerializer.Serialize(body);
            using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, ModrinthVersionFiles) { Content = content };

            using var response = await _httpClient.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                var bulk = await JsonSerializer.DeserializeAsync<VersionFilesBulkResponse>(
                    await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                if (bulk != null)
                {
                    foreach (var kv in bulk)
                        result[kv.Key] = kv.Value;
                }
                return result;
            }

            // If bulk returned non-success, log and fall through to individual lookups
            Trace.TraceWarning(
                $"Modrinth bulk version_files returned {(int)response.StatusCode}, " +
                $"falling back to individual lookups ({valid.Count} hashes)");
        }
        catch (HttpRequestException)
        {
            // Network error — fall through to individual lookups
            Trace.TraceWarning(
                $"Modrinth bulk version_files request failed, " +
                $"falling back to individual lookups ({valid.Count} hashes)");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout — fall through to individual lookups
            Trace.TraceWarning(
                $"Modrinth bulk version_files timed out, " +
                $"falling back to individual lookups ({valid.Count} hashes)");
        }

        // Fallback: individual GET /version_file/{sha1} for each hash
        foreach (var sha1 in valid)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var vfr = await LookupVersionFileByHashAsync(sha1, ct);
                if (vfr != null)
                    result[sha1] = vfr;
            }
            catch
            {
                // Best-effort: skip individual failures
            }
        }

        return result;
    }

    /// <summary>Individual SHA-1 lookup via <c>GET /version_file/{sha1}</c>.</summary>
    private async Task<VersionFileResponse?> LookupVersionFileByHashAsync(
        string sha1, CancellationToken ct)
    {
        try
        {
            string url = $"{ModrinthApi}/version_file/{sha1}?algorithm=sha1";
            var json = await _httpClient.GetStringAsync(url, ct);
            return JsonSerializer.Deserialize<VersionFileResponse>(json);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>Bulk project IDs → ModrinthProject via <c>GET /projects?ids=</c>.</summary>
    public async Task<List<ModrinthProject>> GetProjectsByIdsAsync(
        IReadOnlyList<string> projectIds, CancellationToken ct = default)
    {
        if (projectIds.Count == 0) return new List<ModrinthProject>();

        // Modrinth allows up to ~100 ids in one request (URL length limit ~8000 chars).
        // We deduplicate and batch if needed.
        var unique = new HashSet<string>(projectIds, StringComparer.OrdinalIgnoreCase);
        var allProjects = new List<ModrinthProject>();

        // Process in batches of 100 to stay under URL length limits
        var batch = new List<string>(100);
        foreach (var id in unique)
        {
            batch.Add(id);
            if (batch.Count >= 100)
            {
                var batchResult = await GetProjectsBatchAsync(batch, ct);
                allProjects.AddRange(batchResult);
                batch.Clear();
            }
        }
        if (batch.Count > 0)
        {
            var batchResult = await GetProjectsBatchAsync(batch, ct);
            allProjects.AddRange(batchResult);
        }

        return allProjects;
    }

    private async Task<List<ModrinthProject>> GetProjectsBatchAsync(
        List<string> ids, CancellationToken ct)
    {
        // Build JSON-array query string: ?ids=["id1","id2"]
        // %22 = " (JSON string delimiter), %2C = , (array separator)
        string idsParam = string.Join("%2C", ids.Select(id =>
            $"%22{Uri.EscapeDataString(id)}%22"));
        string url = $"https://api.modrinth.com/v2/projects?ids=[{idsParam}]";

        try
        {
            var json = await _httpClient.GetStringAsync(url, ct);
            return JsonSerializer.Deserialize<List<ModrinthProject>>(json) ?? new List<ModrinthProject>();
        }
        catch (HttpRequestException ex)
        {
            Trace.TraceWarning($"Modrinth projects batch lookup failed ({ids.Count} IDs): {ex.Message}");
            return new List<ModrinthProject>();
        }
    }

    private static void FinalizeDownload(string tmpPath, string destinationPath)
    {
        try
        {
            if (File.Exists(destinationPath))
                File.Delete(destinationPath);
            File.Move(tmpPath, destinationPath);
        }
        catch (IOException)
        {
            string dir = Path.GetDirectoryName(destinationPath)!;
            string name = Path.GetFileNameWithoutExtension(destinationPath);
            string ext = Path.GetExtension(destinationPath);
            int counter = 1;
            string altPath;
            do { altPath = Path.Combine(dir, $"{name} ({counter}){ext}"); counter++; }
            while (File.Exists(altPath));
            File.Move(tmpPath, altPath);
        }
    }

    private static void CleanupPartFiles(string basePath)
    {
        string dir = Path.GetDirectoryName(basePath)!;
        string prefix = Path.GetFileName(basePath) + ".part";
        try
        {
            if (Directory.Exists(dir))
            {
                foreach (var f in Directory.GetFiles(dir, $"{prefix}*"))
                    try { File.Delete(f); } catch { }
            }
        }
        catch { /* best effort */ }
    }

    /// <summary>计算文件的 SHA-1 哈希值（小写十六进制）。</summary>
    private static string ComputeSha1(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        byte[] hash = SHA1.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // ==================== Resource/Shader Pack API ====================

    public async Task<ModrinthSearchResponse> SearchProjectsAsync(string query, string mcVersion,
        string projectType, int limit = 20, int offset = 0, CancellationToken ct = default)
    {
        string facets = $"[[\"versions:{mcVersion}\"],[\"project_type:{projectType}\"]]";
        string url = $"{ModrinthApi}/search?query={Uri.EscapeDataString(query)}" +
                     $"&facets={Uri.EscapeDataString(facets)}" +
                     $"&limit={limit}&offset={offset}&index=relevance";

        var json = await _httpClient.GetStringAsync(url, ct);
        return JsonSerializer.Deserialize<ModrinthSearchResponse>(json)
            ?? new ModrinthSearchResponse();
    }

    public async Task<ModrinthVersion?> GetLatestProjectVersionAsync(string projectId, string mcVersion,
        CancellationToken ct = default)
    {
        string url = $"{ModrinthApi}/project/{Uri.EscapeDataString(projectId)}/version" +
                     $"?game_versions={Uri.EscapeDataString($"[\"{mcVersion}\"]")}";

        var json = await _httpClient.GetStringAsync(url, ct);
        var versions = JsonSerializer.Deserialize<ModrinthVersion[]>(json);

        if (versions == null || versions.Length == 0)
            return null;

        return versions.FirstOrDefault(v => v.Featured && v.VersionType == "release")
            ?? versions.FirstOrDefault(v => v.VersionType == "release")
            ?? versions[0];
    }

    // ==================== Modpack API ====================

    public async Task<ModrinthSearchResponse> SearchModpacksAsync(string query, string mcVersion,
        int limit = 20, int offset = 0, CancellationToken ct = default)
    {
        string facets;
        if (string.IsNullOrEmpty(mcVersion))
        {
            facets = $"[[\"project_type:modpack\"]]";
        }
        else
        {
            facets = $"[[\"versions:{mcVersion}\"],[\"project_type:modpack\"]]";
        }
        string url = $"{ModrinthApi}/search?query={Uri.EscapeDataString(query)}" +
                     $"&facets={Uri.EscapeDataString(facets)}" +
                     $"&limit={limit}&offset={offset}&index=relevance";

        var json = await _httpClient.GetStringAsync(url, ct);
        return JsonSerializer.Deserialize<ModrinthSearchResponse>(json)
            ?? new ModrinthSearchResponse();
    }

    public async Task<ModrinthVersion?> GetModpackLatestVersionAsync(string projectId, string mcVersion,
        CancellationToken ct = default)
    {
        string url = $"{ModrinthApi}/project/{Uri.EscapeDataString(projectId)}/version" +
                     $"?game_versions={Uri.EscapeDataString($"[\"{mcVersion}\"]")}";

        var json = await _httpClient.GetStringAsync(url, ct);
        var versions = JsonSerializer.Deserialize<ModrinthVersion[]>(json);

        if (versions == null || versions.Length == 0)
            return null;

        return versions.FirstOrDefault(v => v.Featured && v.VersionType == "release")
            ?? versions.FirstOrDefault(v => v.VersionType == "release")
            ?? versions[0];
    }

    public async Task<ModrinthVersion?> GetModpackVersionAsync(string projectId, string versionId,
        CancellationToken ct = default)
    {
        string url = $"{ModrinthApi}/project/{Uri.EscapeDataString(projectId)}/version" +
                     $"?ids={Uri.EscapeDataString($"[\"{versionId}\"]")}";

        var json = await _httpClient.GetStringAsync(url, ct);
        var versions = JsonSerializer.Deserialize<ModrinthVersion[]>(json);
        return versions?.FirstOrDefault();
    }

    private static string MapLoaderToModrinth(string loader) => loader.ToLowerInvariant();
}
