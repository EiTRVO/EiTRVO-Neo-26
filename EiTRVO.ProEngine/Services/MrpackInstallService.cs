using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EiTRVO.ProEngine.Helpers;
using EiTRVO.ProEngine.Models;

namespace EiTRVO.ProEngine.Services;

public class MrpackInstallService : IMrpackInstallService
{
    private readonly IDownloadService _downloadService;
    private readonly IModLoaderService _modLoaderService;
    private readonly IModrinthService _modrinth;
    private readonly HttpClient _httpClient;

    public Func<string, string, Task<string?>>? JavaCompatibilityHandler { get; set; }

    public MrpackInstallService(
        IDownloadService downloadService,
        IModLoaderService modLoaderService,
        IModrinthService modrinth,
        HttpClient httpClient)
    {
        _downloadService = downloadService;
        _modLoaderService = modLoaderService;
        _modrinth = modrinth;
        _httpClient = httpClient;
    }

    // ================================================================
    // ParseMrpackAsync
    // ================================================================

    public Task<MrpackInfo> ParseMrpackAsync(string mrpackPath, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var archive = ZipFile.OpenRead(mrpackPath);
            var indexEntry = archive.GetEntry("modrinth.index.json")
                ?? throw new InvalidDataException("无效的整合包：未找到 modrinth.index.json。");

            using var reader = new StreamReader(indexEntry.Open());
            string json = reader.ReadToEnd();
            var manifest = JsonSerializer.Deserialize<ModpackManifest>(json)
                ?? throw new InvalidDataException("无法解析 modrinth.index.json。");

            ct.ThrowIfCancellationRequested();

            // Validate format
            if (manifest.FormatVersion != 1)
                throw new InvalidDataException($"不支持的整合包格式版本 ({manifest.FormatVersion})。当前仅支持格式版本 1。");

            if (!string.Equals(manifest.Game, "minecraft", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"不支持的 game 类型: {manifest.Game}。仅支持 minecraft。");

            // Determine MC version
            string mcVersion = manifest.Dependencies.TryGetValue("minecraft", out var depMcVersion)
                ? depMcVersion : "";

            // Determine mod loader
            string? loaderType = null;
            string? loaderVersionSpec = null;
            string[] knownLoaders = { "fabric-loader", "forge", "quilt-loader", "neoforge" };
            foreach (var loaderKey in knownLoaders)
            {
                if (manifest.Dependencies.TryGetValue(loaderKey, out var ver))
                {
                    loaderType = loaderKey switch
                    {
                        "fabric-loader" => "Fabric",
                        "forge" => "Forge",
                        "quilt-loader" => "Quilt",
                        "neoforge" => "NeoForge",
                        _ => null
                    };
                    loaderVersionSpec = ver;
                    break;
                }
            }

            return new MrpackInfo
            {
                Manifest = manifest,
                McVersion = mcVersion,
                LoaderType = loaderType,
                LoaderVersionSpec = loaderVersionSpec
            };
        }, ct);
    }

    // ================================================================
    // InstallMrpackAsync
    // ================================================================

    public async Task InstallMrpackAsync(
        string mrpackPath,
        string instanceName,
        string targetDir,
        string gameDir,
        string mcVersion,
        string? loaderType,
        string? loaderVersion,
        string? javaPath,
        LauncherSettings settings,
        IProgress<DownloadProgress> progress,
        Action<string, NotificationType, int> log,
        CancellationToken ct = default)
    {
        // Parse manifest (re-parse to get the full manifest for mod files)
        MrpackInfo info;
        try
        {
            info = await ParseMrpackAsync(mrpackPath, ct);
        }
        catch (Exception ex)
        {
            log($"解析整合包失败：{ex.Message}", NotificationType.Error, 5000);
            throw;
        }

        var manifest = info.Manifest;

        // Use provided values or fall back to manifest values
        if (string.IsNullOrEmpty(mcVersion))
            mcVersion = info.McVersion;
        if (string.IsNullOrEmpty(mcVersion))
            throw new InvalidDataException("无法从清单中确定 Minecraft 版本。");

        loaderType ??= info.LoaderType;
        string? resolvedLoaderVersion = loaderVersion;

        // Resolve loader version if needed
        if (loaderType != null && string.IsNullOrEmpty(resolvedLoaderVersion))
        {
            var loaderVersions = await GetLoaderVersionsForTypeAsync(loaderType, mcVersion, ct);
            resolvedLoaderVersion = ResolveLoaderVersion(info.LoaderVersionSpec, loaderVersions);
            if (string.IsNullOrEmpty(resolvedLoaderVersion))
                throw new InvalidDataException($"无法为 {loaderType} 解析版本 {info.LoaderVersionSpec}。");
        }

        Directory.CreateDirectory(targetDir);

        // === Step 1: Download MC base version + install loader ===
        if (loaderType != null && !string.IsNullOrEmpty(resolvedLoaderVersion))
        {
            // Mod loader path: download vanilla to canonical dir first
            log($"正在下载 Minecraft {mcVersion}...", NotificationType.Info, 3000);

            string versionParentDir = Path.Combine(gameDir, "versions", mcVersion);
            if (!Directory.Exists(versionParentDir) || !File.Exists(Path.Combine(versionParentDir, $"{mcVersion}.jar")))
            {
                var versions = await _downloadService.LoadOnlineVersionsAsync(_httpClient);
                var parentManifest = versions.FirstOrDefault(v => v.Id == mcVersion);
                if (parentManifest == null)
                    throw new InvalidDataException($"未在版本清单中找到 Minecraft {mcVersion}。");

                await _downloadService.DownloadVersionFilesAsync(
                    _httpClient, gameDir, parentManifest.Url,
                    mcVersion, mcVersion, progress, log, ct);
            }

            // Install mod loader
            log($"正在安装 {loaderType} {resolvedLoaderVersion}...", NotificationType.Info, 3000);

            var loaderProgress = new Progress<DownloadProgress>(p =>
            {
                progress.Report(DownloadProgress.FileProgress(
                    p.CurrentFileName ?? $"{loaderType}",
                    p.CurrentFileDownloadedBytes,
                    p.CurrentFileTotalBytes,
                    p.DownloadSpeedBytesPerSecond,
                    p.BytesDownloaded,
                    p.TotalBytes));
            });

            switch (loaderType)
            {
                case "Fabric":
                    await _modLoaderService.InstallFabricAsync(_httpClient, gameDir,
                        mcVersion, resolvedLoaderVersion, instanceName,
                        loaderProgress, log, ct);
                    break;
                case "Forge":
                    await _modLoaderService.InstallForgeAsync(_httpClient, gameDir,
                        mcVersion, resolvedLoaderVersion, instanceName, javaPath!,
                        loaderProgress, log, ct);
                    break;
                case "Quilt":
                    await _modLoaderService.InstallQuiltAsync(_httpClient, gameDir,
                        mcVersion, resolvedLoaderVersion, instanceName,
                        loaderProgress, log, ct);
                    break;
                case "NeoForge":
                    await _modLoaderService.InstallNeoForgeAsync(_httpClient, gameDir,
                        mcVersion, resolvedLoaderVersion, instanceName, javaPath!,
                        loaderProgress, log, ct);
                    break;
            }
        }
        else
        {
            // Vanilla modpack: download directly to instance directory
            log($"正在下载 Minecraft {mcVersion}...", NotificationType.Info, 3000);

            var versions = await _downloadService.LoadOnlineVersionsAsync(_httpClient);
            var parentManifest = versions.FirstOrDefault(v => v.Id == mcVersion);
            if (parentManifest == null)
                throw new InvalidDataException($"未在版本清单中找到 Minecraft {mcVersion}。");

            await _downloadService.DownloadVersionFilesAsync(
                _httpClient, gameDir, parentManifest.Url,
                mcVersion, instanceName, progress, log, ct);
        }

        ct.ThrowIfCancellationRequested();

        // === Step 2: Download all mod files from manifest (parallel) ===
        if (manifest.Files.Count > 0)
        {
            await DownloadModFilesAsync(manifest, targetDir, progress, log, ct);
        }

        // === Step 3: Extract overrides/ ===
        await ExtractOverridesAsync(mrpackPath, targetDir, ct);

        // === Step 4: Write instance.json metadata ===
        bool useIsolatedDir = settings.IsolateNewInstancesByDefault;
        var meta = new InstanceMeta
        {
            UseIsolatedDir = useIsolatedDir,
            InstanceDir = useIsolatedDir ? targetDir : null,
            LoaderType = loaderType,
            LoaderVersion = resolvedLoaderVersion
        };
        await File.WriteAllTextAsync(
            Path.Combine(targetDir, "instance.json"),
            JsonSerializer.Serialize(meta), ct);
    }

    // ================================================================
    // Helpers
    // ================================================================

    private async Task DownloadModFilesAsync(
        ModpackManifest manifest,
        string targetDir,
        IProgress<DownloadProgress> progress,
        Action<string, NotificationType, int> log,
        CancellationToken ct)
    {
        // First pass: collect files to download, skip already-cached
        var downloadList = new List<(ModpackFileEntry Entry, string DestPath, string DownloadUrl)>();
        int preCompleted = 0;

        foreach (var fileEntry in manifest.Files)
        {
            ct.ThrowIfCancellationRequested();

            if (fileEntry.Env?.Client == "unsupported")
                continue;

            string destPath = Path.Combine(targetDir, fileEntry.Path);

            // Path traversal check (skip silently)
            if (!PathSafetyHelper.IsContained(destPath, targetDir))
            {
                log($"整合包文件包含非法路径: {fileEntry.Path}，已跳过。", NotificationType.Warning, 5000);
                continue;
            }

            string? destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir))
                Directory.CreateDirectory(destDir);

            // Check if file already exists with matching hash
            if (File.Exists(destPath) && fileEntry.Hashes?.Sha1 != null)
            {
                try
                {
                    var existingHash = ComputeSha1(destPath);
                    if (string.Equals(existingHash, fileEntry.Hashes.Sha1,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        preCompleted++;
                        continue;
                    }
                }
                catch { /* re-download */ }
            }

            string? downloadUrl = fileEntry.Downloads.FirstOrDefault();
            if (string.IsNullOrEmpty(downloadUrl))
            {
                log($"文件 {fileEntry.Path} 缺少下载地址，已跳过。", NotificationType.Warning, 5000);
                preCompleted++;
                continue;
            }

            // Verify download URL safety
            if (!DownloadSafetyHelper.IsDownloadUrlAllowed(downloadUrl))
            {
                log($"文件 {fileEntry.Path} 的下载地址不在信任域名列表中，已跳过。", NotificationType.Warning, 5000);
                preCompleted++;
                continue;
            }

            downloadList.Add((fileEntry, destPath, downloadUrl));
        }

        int totalFiles = preCompleted + downloadList.Count;
        int completedFiles = preCompleted;
        var errors = new List<string>();
        using var sem = new SemaphoreSlim(16);
        var tasks = new List<Task>();

        log($"下载 Mod ({completedFiles}/{totalFiles})...", NotificationType.Info, 3000);

        foreach (var item in downloadList)
        {
            var fileEntry = item.Entry;
            var destPath = item.DestPath;
            var downloadUrl = item.DownloadUrl;

            tasks.Add(Task.Run(async () =>
            {
                await sem.WaitAsync(ct);
                try
                {
                    string fileName = Path.GetFileName(fileEntry.Path);

                    // Retry up to 3 times
                    bool downloaded = false;
                    for (int retry = 0; retry < 3 && !downloaded; retry++)
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            await _modrinth.DownloadModAsync(downloadUrl, destPath, progress, ct);
                            downloaded = true;
                        }
                        catch (OperationCanceledException) { throw; }
                        catch when (retry < 3)
                        {
                            await Task.Delay(1000 * (retry + 1), ct);
                        }
                    }

                    if (!downloaded)
                    {
                        lock (errors)
                            errors.Add($"下载失败（重试3次后）: {fileEntry.Path}");
                        return;
                    }

                    // Verify hash
                    if (fileEntry.Hashes?.Sha1 != null)
                    {
                        string actualHash = ComputeSha1(destPath);
                        if (!string.Equals(actualHash, fileEntry.Hashes.Sha1,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            try { File.Delete(destPath); } catch { }
                            lock (errors)
                                errors.Add($"哈希校验失败: {fileEntry.Path}");
                            return;
                        }
                    }

                    int done = Interlocked.Increment(ref completedFiles);
                    progress.Report(DownloadProgress.FileProgress(
                        fileName, fileEntry.FileSize, fileEntry.FileSize, 0, done, totalFiles));
                }
                finally
                {
                    sem.Release();
                }
            }, ct));
        }

        await Task.WhenAll(tasks);

        if (errors.Count > 0)
            throw new InvalidDataException(string.Join("\n",
                new[] { $"以下 {errors.Count} 个 Mod 文件处理失败:" }.Concat(errors.Take(15))));
    }

    private static async Task ExtractOverridesAsync(
        string mrpackPath, string targetDir, CancellationToken ct)
    {
        using var archive = ZipFile.OpenRead(mrpackPath);
        foreach (var zipEntry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();

            if (!zipEntry.FullName.StartsWith("overrides/", StringComparison.OrdinalIgnoreCase))
                continue;

            string relativePath = zipEntry.FullName.Substring("overrides/".Length);
            if (string.IsNullOrEmpty(relativePath))
                continue;

            // Normalize path separators for Windows
            relativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);

            string fullDestPath = Path.Combine(targetDir, relativePath);

            // Path traversal check
            PathSafetyHelper.ValidateContained(fullDestPath, targetDir);

            if (string.IsNullOrEmpty(zipEntry.Name))
            {
                Directory.CreateDirectory(fullDestPath);
            }
            else
            {
                string? parentDir = Path.GetDirectoryName(fullDestPath);
                if (!string.IsNullOrEmpty(parentDir))
                    Directory.CreateDirectory(parentDir);
                zipEntry.ExtractToFile(fullDestPath, overwrite: true);
            }
        }
    }

    private async Task<List<ModLoaderVersion>> GetLoaderVersionsForTypeAsync(
        string loaderType, string mcVersion, CancellationToken ct = default)
    {
        return loaderType switch
        {
            "Fabric" => await _modLoaderService.GetFabricLoaderVersionsAsync(_httpClient, mcVersion, ct),
            "Quilt" => await _modLoaderService.GetQuiltLoaderVersionsAsync(_httpClient, mcVersion, ct),
            "Forge" => await _modLoaderService.GetForgeVersionsAsync(_httpClient, mcVersion, ct),
            "NeoForge" => await _modLoaderService.GetNeoForgeVersionsAsync(_httpClient, mcVersion, ct),
            _ => new List<ModLoaderVersion>()
        };
    }

    private static string? ResolveLoaderVersion(string? versionSpec,
        List<ModLoaderVersion> versions)
    {
        if (versions.Count == 0) return null;

        if (string.IsNullOrEmpty(versionSpec))
            return versions[0].LoaderVersion;

        // Strip version operators
        string cleanVersion = versionSpec.TrimStart('=', '>', '<', '~', ' ', '^');

        var match = versions.FirstOrDefault(v =>
            v.LoaderVersion == cleanVersion ||
            v.LoaderVersion.StartsWith(cleanVersion + ".", StringComparison.Ordinal) ||
            v.LoaderVersion.StartsWith(cleanVersion + "-", StringComparison.Ordinal));

        return match?.LoaderVersion ?? versions[0].LoaderVersion;
    }

    private static string ComputeSha1(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        byte[] hash = SHA1.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
