using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EiTRVO.ProEngine.Helpers;
using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Orchestrators;
using EiTRVO.ProEngine.Services;

namespace EiTRVO.ProEngine.ViewModels;

public partial class ModpackDownloadViewModel : BaseViewModel
{
    private readonly IModrinthService _modrinth;
    private readonly IDownloadService _downloadService;
    private readonly IModLoaderService _modLoaderService;
    private readonly INotificationService _notificationService;
    private readonly IGameFolderService _gameFolder;
    private readonly InstanceManager _instanceManager;
    private readonly HttpClient _httpClient;

    private CancellationTokenSource? _activeDownloadCts;
    private readonly object _downloadLock = new();

    // === Observable Properties ===

    [ObservableProperty]
    private string _searchQuery = "";

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private bool _hasNoResults;

    [ObservableProperty]
    private int _totalSearchResults;

    [ObservableProperty]
    private ObservableCollection<string> _minecraftVersions = new();

    [ObservableProperty]
    private string? _selectedMcVersion;

    /// <summary>完整版本列表（用于在安装时查找版本详情 URL 等）</summary>
    private List<ManifestVersion> _allManifestVersions = new();

    [ObservableProperty]
    private double _downloadProgressValue;

    [ObservableProperty]
    private string _progressText = "";

    [ObservableProperty]
    private bool _isProgressIndeterminate;

    [ObservableProperty]
    private string _currentFileProgress = "";

    // === Events for MainWindow ===

    public event Action? BackRequested;
    public event Action<bool>? DownloadProgressChanged;
    public event Action<DownloadProgress>? FileProgressUpdated;

    // === Collections ===

    public ObservableCollection<ModpackDownloadEntry> SearchResults { get; } = new();

    // === Progress UI state ===

    private string _lastFileProgressName = "";
    private long _lastFileProgressTick;
    private string _lockedProgressFileName = "";
    private long _lockedProgressFileTotalBytes;
    private long _lockedProgressFileLastTick;
    private const long LockProgressTimeoutMs = 5000;

    public ModpackDownloadViewModel(
        IModrinthService modrinth,
        IDownloadService downloadService,
        IModLoaderService modLoaderService,
        INotificationService notificationService,
        IGameFolderService gameFolder,
        InstanceManager instanceManager,
        HttpClient httpClient)
    {
        _modrinth = modrinth;
        _downloadService = downloadService;
        _modLoaderService = modLoaderService;
        _notificationService = notificationService;
        _gameFolder = gameFolder;
        _instanceManager = instanceManager;
        _httpClient = httpClient;
    }

    /// <summary>初始化 MC 版本列表。</summary>
    public async Task InitializeAsync()
    {
        _allManifestVersions = await LoadVersionManifestCachedAsync();
        var ids = _allManifestVersions.Select(v => v.Id).ToList();
        MinecraftVersions = new ObservableCollection<string>(ids);
        SelectedMcVersion = _allManifestVersions
            .FirstOrDefault(v => v.Type == "release")?.Id;
    }

    // === Commands ===

    [RelayCommand]
    private void GoBack() => BackRequested?.Invoke();

    [RelayCommand]
    private async Task SearchModpacksAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery)) return;

        IsSearching = true;
        HasNoResults = false;
        TotalSearchResults = 0;
        SearchResults.Clear();

        try
        {
            string mcVersion = SelectedMcVersion ?? "";
            var response = await _modrinth.SearchModpacksAsync(
                SearchQuery.Trim(), mcVersion);

            TotalSearchResults = response.TotalHits;

            foreach (var hit in response.Hits)
            {
                // Determine loader from categories
                string loader = hit.Categories
                    .FirstOrDefault(c => c is "fabric" or "forge" or "quilt" or "neoforge") ?? "";

                // MC version from hit versions or filter
                string mcVer = mcVersion;
                if (string.IsNullOrEmpty(mcVer) && hit.Versions.Count > 0)
                    mcVer = hit.Versions[0];

                SearchResults.Add(new ModpackDownloadEntry
                {
                    ProjectId = hit.ProjectId,
                    Title = hit.Title,
                    Description = hit.Description,
                    Author = hit.Author,
                    IconUrl = hit.IconUrl ?? "",
                    Downloads = hit.Downloads,
                    LatestVersionId = hit.LatestVersion ?? "",
                    McVersion = mcVer,
                    Loader = loader,
                });
            }

            HasNoResults = SearchResults.Count == 0;
        }
        catch (Exception ex)
        {
            _notificationService.Show($"搜索失败：{ex.Message}", NotificationType.Error);
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private async Task InstallModpackAsync(ModpackDownloadEntry? entry)
    {
        if (entry == null || entry.IsDownloading) return;

        entry.IsDownloading = true;
        entry.DownloadProgress = 0;
        entry.DownloadStatus = "准备安装...";
        entry.CurrentFileName = "";
        entry.CurrentFileDownloadedBytes = 0;
        entry.CurrentFileTotalBytes = 0;
        entry.CurrentFileSpeed = 0;

        CancellationTokenSource cts;
        lock (_downloadLock)
        {
            _activeDownloadCts?.Cancel();
            _activeDownloadCts?.Dispose();
            _activeDownloadCts = new CancellationTokenSource();
            cts = _activeDownloadCts;
        }

        DownloadProgressChanged?.Invoke(true);
        IsProgressIndeterminate = true;
        ProgressText = "准备安装...";
        DownloadProgressValue = 0;

        var progress = new Progress<DownloadProgress>(p =>
        {
            if (p.TotalBytes > 0)
            {
                double percent = (double)p.BytesDownloaded / p.TotalBytes * 100.0;
                DownloadProgressValue = percent;
                IsProgressIndeterminate = false;
                ProgressText = $"文件 {p.BytesDownloaded}/{p.TotalBytes}  {percent:F0}%";
            }
            UpdateFileProgressUI(p);
            // Also update the card entry progress
            if (!string.IsNullOrEmpty(p.CurrentFileName))
            {
                entry.CurrentFileName = p.CurrentFileName;
                entry.CurrentFileDownloadedBytes = p.CurrentFileDownloadedBytes;
                entry.CurrentFileTotalBytes = p.CurrentFileTotalBytes;
                entry.CurrentFileSpeed = p.DownloadSpeedBytesPerSecond;
                if (p.TotalBytes > 0)
                {
                    entry.CompletedFiles = (int)p.BytesDownloaded;
                    entry.TotalFiles = (int)p.TotalBytes;
                }
                if (p.CurrentFileTotalBytes > 0)
                    entry.DownloadProgress = (double)p.CurrentFileDownloadedBytes / p.CurrentFileTotalBytes * 100.0;
            }
        });

        try
        {
            await InstallModpackInternalAsync(entry, progress, cts.Token);
            _notificationService.Show($"整合包 {entry.Title} 安装完成！", NotificationType.Success);
        }
        catch (OperationCanceledException)
        {
            _notificationService.Show($"{entry.Title} 安装已取消。", NotificationType.Info);
        }
        catch (Exception ex)
        {
            _notificationService.Show($"安装失败：{ex.Message}", NotificationType.Error);
        }
        finally
        {
            entry.IsDownloading = false;
            entry.DownloadProgress = 0;
            entry.DownloadStatus = "";
            entry.CurrentFileName = "";
            entry.CurrentFileTotalBytes = 0;
            entry.CurrentFileDownloadedBytes = 0;
            entry.CurrentFileSpeed = 0;
            entry.CompletedFiles = 0;
            entry.TotalFiles = 0;
            DownloadProgressChanged?.Invoke(false);
            _instanceManager.Refresh(_notificationService.AppendLog);
        }
    }

    [RelayCommand]
    private void CancelDownload()
    {
        lock (_downloadLock)
        {
            _activeDownloadCts?.Cancel();
            _activeDownloadCts?.Dispose();
            _activeDownloadCts = null;
        }
    }

    [RelayCommand]
    private void CancelInstall(ModpackDownloadEntry? entry)
    {
        if (entry == null) return;
        CancelDownload();
    }

    // === Installation Pipeline ===

    private async Task InstallModpackInternalAsync(
        ModpackDownloadEntry entry,
        IProgress<DownloadProgress> progress,
        CancellationToken ct)
    {
        // === Step 1: Get Modrinth version info ===
        IsProgressIndeterminate = true;
        ProgressText = "正在获取版本信息...";

        ModrinthVersion? version;
        if (!string.IsNullOrEmpty(entry.LatestVersionId))
        {
            version = await _modrinth.GetModpackVersionAsync(
                entry.ProjectId, entry.LatestVersionId, ct);
        }
        else
        {
            version = await _modrinth.GetModpackLatestVersionAsync(
                entry.ProjectId, entry.McVersion, ct);
        }

        if (version == null)
            throw new Exception($"未找到整合包 {entry.Title} 的兼容版本。");

        // Find the primary .mrpack file
        var mrpackFile = version.Files.FirstOrDefault(f =>
            f.Filename.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
            ?? version.Files.FirstOrDefault(f => f.Primary)
            ?? version.Files.FirstOrDefault();

        if (mrpackFile == null)
            throw new Exception("整合包版本中没有找到可下载的文件。");

        // === Step 2: Download .mrpack file ===
        string gameDir = _gameFolder.GameDir;
        string packCacheDir = Path.Combine(gameDir, "installer_cache", "modpack_downloads");
        Directory.CreateDirectory(packCacheDir);
        string mrpackPath = Path.Combine(packCacheDir, $"{entry.ProjectId}_{version.VersionNumber}.mrpack");

        if (!File.Exists(mrpackPath))
        {
            ProgressText = "正在下载整合包文件...";
            IsProgressIndeterminate = false;

            progress.Report(DownloadProgress.FileProgress(
                mrpackFile.Filename, 0, mrpackFile.Size, 0, 0, 1));

            await _modrinth.DownloadModAsync(mrpackFile.Url, mrpackPath, progress, ct);
        }
        else
        {
            ct.ThrowIfCancellationRequested();
        }

        // === Step 3: Parse and validate modrinth.index.json ===
        ProgressText = "正在解析整合包信息...";
        IsProgressIndeterminate = true;

        ModpackManifest manifest;
        using (var archive = ZipFile.OpenRead(mrpackPath))
        {
            var indexEntry = archive.GetEntry("modrinth.index.json")
                ?? throw new Exception("无效的整合包：未找到 modrinth.index.json。");

            using var reader = new StreamReader(indexEntry.Open());
            string json = await reader.ReadToEndAsync();
            manifest = JsonSerializer.Deserialize<ModpackManifest>(json)
                ?? throw new Exception("无法解析 modrinth.index.json。");

            ct.ThrowIfCancellationRequested();

            // Validate format
            if (manifest.FormatVersion != 1)
                throw new Exception($"不支持的整合包格式版本 ({manifest.FormatVersion})。当前仅支持格式版本 1。");

            if (manifest.Game != "minecraft")
                throw new Exception($"不支持的 game 类型: {manifest.Game}。仅支持 minecraft。");
        }

        // === Step 4: Determine MC version and loader ===
        string mcVersion = manifest.Dependencies.TryGetValue("minecraft", out var depMcVersion)
            ? depMcVersion : entry.McVersion;

        if (string.IsNullOrEmpty(mcVersion))
            throw new Exception("无法从清单中确定 Minecraft 版本。");

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

        // Resolve loader version
        string? resolvedLoaderVersion = null;
        if (loaderType != null)
        {
            var loaderVersions = await GetLoaderVersionsForTypeAsync(loaderType, mcVersion, ct);
            resolvedLoaderVersion = ResolveLoaderVersion(loaderVersionSpec, loaderVersions);
            if (string.IsNullOrEmpty(resolvedLoaderVersion))
                throw new Exception($"无法为 {loaderType} 解析版本 {loaderVersionSpec}。");
        }

        // Update card display
        entry.McVersion = mcVersion;
        entry.Loader = loaderType ?? "";

        // === Step 5: Create instance name ===
        string instanceName = SanitizeInstanceName(manifest.Name);
        if (string.IsNullOrWhiteSpace(instanceName))
            instanceName = $"{entry.Title}_{mcVersion}";

        string targetDir = Path.Combine(gameDir, "versions", instanceName);
        if (Directory.Exists(targetDir))
        {
            int suffix = 1;
            while (Directory.Exists(Path.Combine(gameDir, "versions", $"{instanceName}_{suffix}")))
                suffix++;
            instanceName = $"{instanceName}_{suffix}";
            targetDir = Path.Combine(gameDir, "versions", instanceName);
        }

        Directory.CreateDirectory(targetDir);

        // === Step 6+7: Download Minecraft version + install mod loader ===
        if (loaderType != null && !string.IsNullOrEmpty(resolvedLoaderVersion))
        {
            // === Mod loader path: download vanilla to canonical dir first ===
            ProgressText = $"正在下载 Minecraft {mcVersion}...";
            IsProgressIndeterminate = true;

            string versionParentDir = Path.Combine(gameDir, "versions", mcVersion);
            if (!Directory.Exists(versionParentDir) || !File.Exists(Path.Combine(versionParentDir, $"{mcVersion}.jar")))
            {
                var versions = await _downloadService.LoadOnlineVersionsAsync(_httpClient);
                var parentManifest = versions.FirstOrDefault(v => v.Id == mcVersion);
                if (parentManifest == null)
                    throw new Exception($"未在版本清单中找到 Minecraft {mcVersion}。");

                await _downloadService.DownloadVersionFilesAsync(
                    _httpClient, gameDir, parentManifest.Url,
                    mcVersion, mcVersion, progress, _notificationService.Show, ct);
            }

            // === Install mod loader ===
            ProgressText = $"正在安装 {loaderType} {resolvedLoaderVersion}...";
            IsProgressIndeterminate = true;

            string? javaPath = null;
            if (loaderType is "Forge" or "NeoForge")
            {
                javaPath = GetJavaPath();
                if (string.IsNullOrEmpty(javaPath))
                    throw new Exception($"{loaderType} 安装需要 Java 环境，请先在设置中配置 Java 路径。");
            }

            var loaderProgress = new Progress<DownloadProgress>(p =>
            {
                if (p.TotalBytes > 1)
                {
                    IsProgressIndeterminate = false;
                    DownloadProgressValue = (double)p.BytesDownloaded / p.TotalBytes * 100.0;
                    ProgressText = $"{loaderType}: {p.BytesDownloaded}/{p.TotalBytes} 文件";
                }
                else if (p.TotalBytes == 1)
                {
                    IsProgressIndeterminate = true;
                    ProgressText = $"{loaderType}: 1/1";
                }
                UpdateFileProgressUI(p);
            });

            switch (loaderType)
            {
                case "Fabric":
                    await _modLoaderService.InstallFabricAsync(_httpClient, gameDir,
                        mcVersion, resolvedLoaderVersion, instanceName,
                        loaderProgress, _notificationService.Show, ct);
                    break;
                case "Forge":
                    await _modLoaderService.InstallForgeAsync(_httpClient, gameDir,
                        mcVersion, resolvedLoaderVersion, instanceName, javaPath!,
                        loaderProgress, _notificationService.Show, ct);
                    break;
                case "Quilt":
                    await _modLoaderService.InstallQuiltAsync(_httpClient, gameDir,
                        mcVersion, resolvedLoaderVersion, instanceName,
                        loaderProgress, _notificationService.Show, ct);
                    break;
                case "NeoForge":
                    await _modLoaderService.InstallNeoForgeAsync(_httpClient, gameDir,
                        mcVersion, resolvedLoaderVersion, instanceName, javaPath!,
                        loaderProgress, _notificationService.Show, ct);
                    break;
            }
        }
        else
        {
            // === Vanilla modpack (no loader): download directly to instance directory ===
            // Bypasses the intermediate versions/{mcVersion}/ directory to avoid
            // InstanceManager.Scan() picking it up as a spurious standalone instance.
            ProgressText = $"正在下载 Minecraft {mcVersion}...";
            IsProgressIndeterminate = true;

            var versions = await _downloadService.LoadOnlineVersionsAsync(_httpClient);
            var parentManifest = versions.FirstOrDefault(v => v.Id == mcVersion);
            if (parentManifest == null)
                throw new Exception($"未在版本清单中找到 Minecraft {mcVersion}。");

            await _downloadService.DownloadVersionFilesAsync(
                _httpClient, gameDir, parentManifest.Url,
                mcVersion, instanceName, progress, _notificationService.Show, ct);
        }

        // === Step 8: Download all mod files from manifest (parallel) ===
        if (manifest.Files.Count > 0)
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

                // Path traversal check (skip, don't throw — malicious files are silently dropped)
                if (!PathSafetyHelper.IsContained(destPath, targetDir))
                {
                    _notificationService.AppendLog(
                        $"整合包文件包含非法路径: {fileEntry.Path}，已跳过。",
                        NotificationType.Warning);
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
                    _notificationService.AppendLog(
                        $"文件 {fileEntry.Path} 缺少下载地址，已跳过。",
                        NotificationType.Warning);
                    preCompleted++;
                    continue;
                }

                downloadList.Add((fileEntry, destPath, downloadUrl));
            }

            int totalFiles = preCompleted + downloadList.Count;
            int completedFiles = preCompleted;
            var errors = new List<string>();
            using var sem = new System.Threading.SemaphoreSlim(16);
            var tasks = new List<Task>();

            IsProgressIndeterminate = false;
            ProgressText = $"下载 Mod (0/{totalFiles})...";

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
                        ProgressText = $"下载 Mod ({done}/{totalFiles})";
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
                throw new Exception(string.Join("\n",
                    new[] { $"以下 {errors.Count} 个 Mod 文件处理失败:" }.Concat(errors.Take(15))));
        }

        // === Step 9: Extract overrides/ ===
        ProgressText = "正在安装覆盖文件...";
        IsProgressIndeterminate = true;

        using (var archive = ZipFile.OpenRead(mrpackPath))
        {
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

        // === Step 10: Write instance.json metadata ===
        bool useIsolatedDir = ReadIsolateNewInstancesSetting();
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

        ProgressText = "安装完成!";
        DownloadProgressValue = 100;
        IsProgressIndeterminate = false;
    }

    // === Helpers ===

    private async Task<List<ManifestVersion>> LoadVersionManifestCachedAsync()
    {
        string cacheFile = Path.Combine(_gameFolder.GameDir, "version_manifest.json");
        if (File.Exists(cacheFile))
        {
            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(cacheFile);
            if (age.TotalMinutes < 5)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(cacheFile);
                    var manifest = JsonSerializer.Deserialize<VersionManifest>(json);
                    if (manifest?.Versions != null && manifest.Versions.Count > 0)
                        return manifest.Versions;
                }
                catch { /* cache corrupt */ }
            }
        }

        var versions = await _downloadService.LoadOnlineVersionsAsync(_httpClient);
        try
        {
            var manifest = new VersionManifest { Versions = versions };
            await File.WriteAllTextAsync(cacheFile, JsonSerializer.Serialize(manifest));
        }
        catch { /* best-effort */ }
        return versions;
    }

    private static string ComputeSha1(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        byte[] hash = SHA1.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private string? GetJavaPath()
    {
        var settingsPath = Path.Combine(_gameFolder.GameDir, "settings.json");
        if (File.Exists(settingsPath))
        {
            try
            {
                var json = File.ReadAllText(settingsPath);
                var settings = JsonSerializer.Deserialize<LauncherSettings>(json);
                return settings?.JavaPath;
            }
            catch { }
        }
        return null;
    }

    private bool ReadIsolateNewInstancesSetting()
    {
        var settingsPath = Path.Combine(_gameFolder.GameDir, "settings.json");
        if (File.Exists(settingsPath))
        {
            try
            {
                var json = File.ReadAllText(settingsPath);
                var settings = JsonSerializer.Deserialize<LauncherSettings>(json);
                return settings?.IsolateNewInstancesByDefault ?? false;
            }
            catch { }
        }
        return false;
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

        // Strip version operators like ">=", "=", ">", "~", "^"
        string cleanVersion = versionSpec.TrimStart('=', '>', '<', '~', ' ', '^');

        // Find best match
        var match = versions.FirstOrDefault(v =>
            v.LoaderVersion == cleanVersion ||
            v.LoaderVersion.StartsWith(cleanVersion + ".", StringComparison.Ordinal) ||
            v.LoaderVersion.StartsWith(cleanVersion + "-", StringComparison.Ordinal));

        return match?.LoaderVersion ?? versions[0].LoaderVersion;
    }

    private static string SanitizeInstanceName(string name)
    {
        return PathSafetyHelper.SanitizeNameComponent(name);
    }

    private void UpdateFileProgressUI(DownloadProgress p)
    {
        if (string.IsNullOrEmpty(p.CurrentFileName) || p.CurrentFileTotalBytes == 0)
            return;

        long now = Environment.TickCount64;

        // Lock: once a file is being shown, stick to it until it completes or times out.
        // This prevents rapid name-jumping when multiple files download in parallel.
        if (_lockedProgressFileName.Length > 0 && p.CurrentFileName != _lockedProgressFileName)
        {
            if (_lockedProgressFileTotalBytes > 0)
            {
                // Known-size locked file: wait for timeout before allowing switch
                if (now - _lockedProgressFileLastTick < LockProgressTimeoutMs)
                    return;
            }
            // Release stale lock
            _lockedProgressFileName = "";
            _lockedProgressFileTotalBytes = 0;
        }

        // Throttle: same file name within 180ms
        bool sameFile = p.CurrentFileName == _lastFileProgressName;
        if (sameFile && now - _lastFileProgressTick < 180)
            return;

        _lastFileProgressName = p.CurrentFileName;
        _lastFileProgressTick = now;

        // Acquire lock for this file
        if (_lockedProgressFileName.Length == 0)
        {
            _lockedProgressFileName = p.CurrentFileName;
            _lockedProgressFileTotalBytes = p.CurrentFileTotalBytes;
        }

        // If locked file completed (or nearing completion), release lock
        if (p.CurrentFileName == _lockedProgressFileName &&
            p.CurrentFileDownloadedBytes >= _lockedProgressFileTotalBytes)
        {
            _lockedProgressFileName = "";
            _lockedProgressFileTotalBytes = 0;
        }

        _lockedProgressFileLastTick = now;

        if (p.CurrentFileTotalBytes > 0)
        {
            CurrentFileProgress = $"{p.CurrentFileName}  " +
                $"{FormatByteSize(p.CurrentFileDownloadedBytes)} / " +
                $"{FormatByteSize(p.CurrentFileTotalBytes)}" +
                (p.DownloadSpeedBytesPerSecond > 0
                    ? $"  ·  {FormatByteSize((long)p.DownloadSpeedBytesPerSecond)}/s"
                    : "");
        }

        FileProgressUpdated?.Invoke(p);
    }

    private static string FormatByteSize(long bytes)
    {
        if (bytes < 0) return "?";
        if (bytes < 1024) return $"{bytes} B";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int unitIdx = 0;
        double size = bytes;
        while (size >= 1024.0 && unitIdx < units.Length - 1)
        {
            size /= 1024.0;
            unitIdx++;
        }
        return $"{size:F1} {units[unitIdx]}";
    }
}
