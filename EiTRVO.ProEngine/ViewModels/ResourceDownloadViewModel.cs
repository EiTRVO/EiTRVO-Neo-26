using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
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

public partial class ResourceDownloadViewModel : BaseViewModel
{
    private readonly IModrinthService _modrinth;
    private readonly INotificationService _notification;
    private readonly IDialogService _dialogService;
    private readonly IGameFolderService _gameFolder;
    private readonly HttpClient _httpClient;

    private List<ManifestVersion> _allManifestVersions = new();

    // === Tab State ===

    [ObservableProperty]
    private string _selectedTab = "mod";

    // === Version Selection ===

    public ObservableCollection<string> MinecraftVersions { get; } = new();

    [ObservableProperty]
    private string _selectedMcVersion = "";

    // === Search State (shared controls, per-tab results) ===

    [ObservableProperty]
    private string _searchQuery = "";

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private bool _hasNoResults;

    [ObservableProperty]
    private int _totalSearchResults;

    // Per-tab search collections (preserved when switching tabs)
    public ObservableCollection<ModDownloadEntry> ModSearchResults { get; } = new();
    public ObservableCollection<ModDownloadEntry> ResourceSearchResults { get; } = new();
    public ObservableCollection<ModDownloadEntry> ShaderSearchResults { get; } = new();

    // === Tab-specific search state ===

    [ObservableProperty]
    private bool _isModSearching;

    [ObservableProperty]
    private bool _modHasNoResults;

    [ObservableProperty]
    private int _modTotalResults;

    [ObservableProperty]
    private bool _isResourceSearching;

    [ObservableProperty]
    private bool _resourceHasNoResults;

    [ObservableProperty]
    private int _resourceTotalResults;

    [ObservableProperty]
    private bool _isShaderSearching;

    [ObservableProperty]
    private bool _shaderHasNoResults;

    [ObservableProperty]
    private int _shaderTotalResults;

    // === Search CTS (race-condition protection for rapid version switches) ===

    private CancellationTokenSource? _searchCts;
    private readonly object _searchLock = new();

    // === Download State (per-tab CancellationTokenSource) ===

    private CancellationTokenSource? _modCancelCts;
    private CancellationTokenSource? _resourceCancelCts;
    private CancellationTokenSource? _shaderCancelCts;
    private readonly object _modCancelLock = new();
    private readonly object _resourceCancelLock = new();
    private readonly object _shaderCancelLock = new();

    // === Default folders (SaveFileDialog initial directory) ===

    private string DefaultModsFolder => Path.Combine(_gameFolder.GameDir, "mods");
    private string DefaultResourcePacksFolder => Path.Combine(_gameFolder.GameDir, "resourcepacks");
    private string DefaultShaderPacksFolder => Path.Combine(_gameFolder.GameDir, "shaderpacks");

    // === Computed Visibility ===

    public bool IsModVisible => SelectedTab == "mod";
    public bool IsResourceVisible => SelectedTab == "resource";
    public bool IsShaderVisible => SelectedTab == "shader";

    /// <summary>Gets the current Modrinth project_type for the active tab.</summary>
    private string ActiveProjectType => SelectedTab switch
    {
        "resource" => "resourcepack",
        "shader" => "shader",
        _ => "mod"
    };

    /// <summary>Gets the current active search results collection.</summary>
    private ObservableCollection<ModDownloadEntry> ActiveSearchResults => SelectedTab switch
    {
        "resource" => ResourceSearchResults,
        "shader" => ShaderSearchResults,
        _ => ModSearchResults
    };

    // === Events ===

    public event Action? BackRequested;

    public ResourceDownloadViewModel(
        IModrinthService modrinth,
        INotificationService notification,
        IDialogService dialogService,
        IGameFolderService gameFolder,
        HttpClient httpClient)
    {
        _modrinth = modrinth;
        _notification = notification;
        _dialogService = dialogService;
        _gameFolder = gameFolder;
        _httpClient = httpClient;
    }

    partial void OnSelectedTabChanged(string value)
    {
        OnPropertyChanged(nameof(IsModVisible));
        OnPropertyChanged(nameof(IsResourceVisible));
        OnPropertyChanged(nameof(IsShaderVisible));

        // Auto-trigger empty search on first switch to a tab with no results
        _ = AutoSearchCurrentTabAsync();
    }

    /// <summary>Load MC version manifest and populate the version selector (releases only).</summary>
    public async Task InitializeAsync()
    {
        if (MinecraftVersions.Count > 0)
            return; // already initialized

        _allManifestVersions = await LoadVersionManifestCachedAsync();

        var releases = _allManifestVersions
            .Where(v => v.Type == "release")
            .Select(v => v.Id)
            .ToList();

        MinecraftVersions.Clear();
        foreach (var id in releases)
            MinecraftVersions.Add(id);

        // Set default to latest release
        if (MinecraftVersions.Count > 0)
            SelectedMcVersion = MinecraftVersions[0];

        // Auto-load top content for all three tabs (empty query returns popular/recommended)
        _ = SearchModsAsync();
        _ = SearchResourcePacksAsync();
        _ = SearchShaderPacksAsync();
    }

    /// <summary>When the user switches MC version, cancel any in-flight search and
    /// immediately refresh results for the current tab.</summary>
    partial void OnSelectedMcVersionChanged(string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        _ = RefreshCurrentTabAsync();
    }

    private async Task RefreshCurrentTabAsync()
    {
        CancellationTokenSource cts;
        lock (_searchLock)
        {
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = new CancellationTokenSource();
            cts = _searchCts;
        }

        await SearchCurrentTabAsync(cts.Token);
    }

    private async Task SearchCurrentTabAsync(CancellationToken ct)
    {
        switch (SelectedTab)
        {
            case "resource":
                await SearchResourcePacksAsync(ct);
                break;
            case "shader":
                await SearchShaderPacksAsync(ct);
                break;
            default:
                await SearchModsAsync(ct);
                break;
        }
    }

    // === Commands ===

    [RelayCommand]
    private void GoBack() => BackRequested?.Invoke();

    [RelayCommand]
    private void SelectTab(string tab)
    {
        if (SelectedTab != tab)
            SelectedTab = tab;
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        switch (SelectedTab)
        {
            case "resource":
                await SearchResourcePacksAsync();
                break;
            case "shader":
                await SearchShaderPacksAsync();
                break;
            default:
                await SearchModsAsync();
                break;
        }
    }

    // === Mod Search / Download ===

    private async Task SearchModsAsync(CancellationToken ct = default)
    {
        IsModSearching = true;
        ModHasNoResults = false;
        ModTotalResults = 0;
        ModSearchResults.Clear();
        try
        {
            var response = await _modrinth.SearchProjectsAsync(
                SearchQuery.Trim(), SelectedMcVersion, "mod", ct: ct);
            ModTotalResults = response.TotalHits;
            foreach (var hit in response.Hits)
            {
                ModSearchResults.Add(new ModDownloadEntry
                {
                    ProjectId = hit.ProjectId,
                    Title = hit.Title,
                    Description = hit.Description,
                    Author = hit.Author,
                    IconUrl = hit.IconUrl ?? "",
                    Downloads = hit.Downloads,
                    IsCompatible = true
                });
            }
            ModHasNoResults = ModSearchResults.Count == 0;
        }
        catch (OperationCanceledException)
        {
            // Search was superseded by a newer request — silently ignore
        }
        catch (Exception ex)
        {
            _notification.Show($"搜索失败：{ex.Message}", NotificationType.Error);
        }
        finally { IsModSearching = false; }
    }

    [RelayCommand]
    private async Task DownloadModAsync(ModDownloadEntry entry)
    {
        if (entry == null || !entry.IsCompatible || entry.IsDownloading) return;
        entry.IsDownloading = true;
        entry.DownloadProgress = 0;
        entry.DownloadStatus = "准备下载...";
        entry.CurrentFileName = "";
        entry.CurrentFileDownloadedBytes = 0;
        entry.CurrentFileTotalBytes = 0;
        entry.CurrentFileSpeed = 0;

        CancellationTokenSource cts;
        lock (_modCancelLock)
        {
            _modCancelCts?.Cancel(); _modCancelCts?.Dispose();
            _modCancelCts = new CancellationTokenSource();
            cts = _modCancelCts;
        }

        try
        {
            var version = await _modrinth.GetLatestProjectVersionAsync(
                entry.ProjectId, SelectedMcVersion, cts.Token);
            if (version == null) throw new Exception("未找到兼容版本。");
            var file = version.Files.FirstOrDefault(f => f.Primary)
                       ?? version.Files.FirstOrDefault();
            if (file == null) throw new Exception("未找到兼容的版本文件。");

            // Show SaveFileDialog so user chooses where to save
            Directory.CreateDirectory(DefaultModsFolder);
            string defaultPath = Path.Combine(DefaultModsFolder, file.Filename);
            var savePath = await _dialogService.ShowSaveFileDialogAsync(
                defaultPath, "JAR 文件|*.jar", "选择下载位置");
            if (savePath == null) { cts.Cancel(); return; } // user cancelled

            // CodeQL-compliant path sanitization
            string safeDestPath = SanitizeDownloadPath(savePath);

            IProgress<string> statusProgress = new Progress<string>(s => entry.DownloadStatus = s);
            IProgress<DownloadProgress> fileProgress = new Progress<DownloadProgress>(p =>
            {
                if (!string.IsNullOrEmpty(p.CurrentFileName))
                {
                    entry.CurrentFileName = p.CurrentFileName;
                    entry.CurrentFileDownloadedBytes = p.CurrentFileDownloadedBytes;
                    entry.CurrentFileTotalBytes = p.CurrentFileTotalBytes;
                    entry.CurrentFileSpeed = p.DownloadSpeedBytesPerSecond;
                    if (p.CurrentFileTotalBytes > 0)
                        entry.DownloadProgress = (double)p.CurrentFileDownloadedBytes
                            / p.CurrentFileTotalBytes * 100.0;
                }
            });

            statusProgress.Report($"下载: {Path.GetFileName(safeDestPath)}");
            await _modrinth.DownloadModAsync(file.Url, safeDestPath, fileProgress, cts.Token);

            _notification.Show($"{entry.Title} 下载完成！", NotificationType.Success);
        }
        catch (OperationCanceledException)
        {
            _notification.Show($"{entry.Title} 下载已取消。", NotificationType.Info);
        }
        catch (Exception ex)
        {
            _notification.Show($"下载失败：{ex.Message}", NotificationType.Error);
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
        }
    }

    [RelayCommand]
    private void CancelModDownload(ModDownloadEntry? entry)
    {
        if (entry == null) return;
        lock (_modCancelLock) { _modCancelCts?.Cancel(); }
    }

    // === Resource Pack Search / Download ===

    private async Task SearchResourcePacksAsync(CancellationToken ct = default)
    {
        IsResourceSearching = true;
        ResourceHasNoResults = false;
        ResourceTotalResults = 0;
        ResourceSearchResults.Clear();
        try
        {
            var response = await _modrinth.SearchProjectsAsync(
                SearchQuery.Trim(), SelectedMcVersion, "resourcepack", ct: ct);
            ResourceTotalResults = response.TotalHits;
            foreach (var hit in response.Hits)
            {
                ResourceSearchResults.Add(new ModDownloadEntry
                {
                    ProjectId = hit.ProjectId,
                    Title = hit.Title,
                    Description = hit.Description,
                    Author = hit.Author,
                    IconUrl = hit.IconUrl ?? "",
                    Downloads = hit.Downloads,
                    IsCompatible = true
                });
            }
            ResourceHasNoResults = ResourceSearchResults.Count == 0;
        }
        catch (OperationCanceledException)
        {
            // Search was superseded by a newer request — silently ignore
        }
        catch (Exception ex)
        {
            _notification.Show($"搜索失败：{ex.Message}", NotificationType.Error);
        }
        finally { IsResourceSearching = false; }
    }

    [RelayCommand]
    private async Task DownloadResourcePackAsync(ModDownloadEntry entry)
    {
        if (entry == null || !entry.IsCompatible || entry.IsDownloading) return;
        entry.IsDownloading = true;
        entry.DownloadProgress = 0;
        entry.DownloadStatus = "准备下载...";
        entry.CurrentFileName = "";
        entry.CurrentFileDownloadedBytes = 0;
        entry.CurrentFileTotalBytes = 0;
        entry.CurrentFileSpeed = 0;

        CancellationTokenSource cts;
        lock (_resourceCancelLock)
        {
            _resourceCancelCts?.Cancel(); _resourceCancelCts?.Dispose();
            _resourceCancelCts = new CancellationTokenSource();
            cts = _resourceCancelCts;
        }

        try
        {
            var version = await _modrinth.GetLatestProjectVersionAsync(
                entry.ProjectId, SelectedMcVersion, cts.Token);
            if (version == null) throw new Exception("未找到兼容版本。");
            var file = version.Files.FirstOrDefault(f => f.Primary)
                       ?? version.Files.FirstOrDefault();
            if (file == null) throw new Exception("未找到兼容的版本文件。");

            // Show SaveFileDialog
            Directory.CreateDirectory(DefaultResourcePacksFolder);
            string defaultPath = Path.Combine(DefaultResourcePacksFolder, file.Filename);
            var savePath = await _dialogService.ShowSaveFileDialogAsync(
                defaultPath, "ZIP 压缩包|*.zip", "选择下载位置");
            if (savePath == null) { cts.Cancel(); return; }

            string safeDestPath = SanitizeDownloadPath(savePath);

            IProgress<string> statusProgress = new Progress<string>(s => entry.DownloadStatus = s);
            IProgress<DownloadProgress> fileProgress = new Progress<DownloadProgress>(p =>
            {
                if (!string.IsNullOrEmpty(p.CurrentFileName))
                {
                    entry.CurrentFileName = p.CurrentFileName;
                    entry.CurrentFileDownloadedBytes = p.CurrentFileDownloadedBytes;
                    entry.CurrentFileTotalBytes = p.CurrentFileTotalBytes;
                    entry.CurrentFileSpeed = p.DownloadSpeedBytesPerSecond;
                    if (p.CurrentFileTotalBytes > 0)
                        entry.DownloadProgress = (double)p.CurrentFileDownloadedBytes
                            / p.CurrentFileTotalBytes * 100.0;
                }
            });

            statusProgress.Report($"下载: {Path.GetFileName(safeDestPath)}");
            await _modrinth.DownloadModAsync(file.Url, safeDestPath, fileProgress, cts.Token);

            _notification.Show($"{entry.Title} 下载完成！", NotificationType.Success);
        }
        catch (OperationCanceledException)
        {
            _notification.Show($"{entry.Title} 下载已取消。", NotificationType.Info);
        }
        catch (Exception ex)
        {
            _notification.Show($"下载失败：{ex.Message}", NotificationType.Error);
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
        }
    }

    [RelayCommand]
    private void CancelResourceDownload(ModDownloadEntry? entry)
    {
        if (entry == null) return;
        lock (_resourceCancelLock) { _resourceCancelCts?.Cancel(); }
    }

    // === Shader Pack Search / Download ===

    private async Task SearchShaderPacksAsync(CancellationToken ct = default)
    {
        IsShaderSearching = true;
        ShaderHasNoResults = false;
        ShaderTotalResults = 0;
        ShaderSearchResults.Clear();
        try
        {
            var response = await _modrinth.SearchProjectsAsync(
                SearchQuery.Trim(), SelectedMcVersion, "shader", ct: ct);
            ShaderTotalResults = response.TotalHits;
            foreach (var hit in response.Hits)
            {
                ShaderSearchResults.Add(new ModDownloadEntry
                {
                    ProjectId = hit.ProjectId,
                    Title = hit.Title,
                    Description = hit.Description,
                    Author = hit.Author,
                    IconUrl = hit.IconUrl ?? "",
                    Downloads = hit.Downloads,
                    IsCompatible = true
                });
            }
            ShaderHasNoResults = ShaderSearchResults.Count == 0;
        }
        catch (OperationCanceledException)
        {
            // Search was superseded by a newer request — silently ignore
        }
        catch (Exception ex)
        {
            _notification.Show($"搜索失败：{ex.Message}", NotificationType.Error);
        }
        finally { IsShaderSearching = false; }
    }

    [RelayCommand]
    private async Task DownloadShaderPackAsync(ModDownloadEntry entry)
    {
        if (entry == null || !entry.IsCompatible || entry.IsDownloading) return;
        entry.IsDownloading = true;
        entry.DownloadProgress = 0;
        entry.DownloadStatus = "准备下载...";
        entry.CurrentFileName = "";
        entry.CurrentFileDownloadedBytes = 0;
        entry.CurrentFileTotalBytes = 0;
        entry.CurrentFileSpeed = 0;

        CancellationTokenSource cts;
        lock (_shaderCancelLock)
        {
            _shaderCancelCts?.Cancel(); _shaderCancelCts?.Dispose();
            _shaderCancelCts = new CancellationTokenSource();
            cts = _shaderCancelCts;
        }

        try
        {
            var version = await _modrinth.GetLatestProjectVersionAsync(
                entry.ProjectId, SelectedMcVersion, cts.Token);
            if (version == null) throw new Exception("未找到兼容版本。");
            var file = version.Files.FirstOrDefault(f => f.Primary)
                       ?? version.Files.FirstOrDefault();
            if (file == null) throw new Exception("未找到兼容的版本文件。");

            // Show SaveFileDialog
            Directory.CreateDirectory(DefaultShaderPacksFolder);
            string defaultPath = Path.Combine(DefaultShaderPacksFolder, file.Filename);
            var savePath = await _dialogService.ShowSaveFileDialogAsync(
                defaultPath, "ZIP 压缩包|*.zip", "选择下载位置");
            if (savePath == null) { cts.Cancel(); return; }

            string safeDestPath = SanitizeDownloadPath(savePath);

            IProgress<string> statusProgress = new Progress<string>(s => entry.DownloadStatus = s);
            IProgress<DownloadProgress> fileProgress = new Progress<DownloadProgress>(p =>
            {
                if (!string.IsNullOrEmpty(p.CurrentFileName))
                {
                    entry.CurrentFileName = p.CurrentFileName;
                    entry.CurrentFileDownloadedBytes = p.CurrentFileDownloadedBytes;
                    entry.CurrentFileTotalBytes = p.CurrentFileTotalBytes;
                    entry.CurrentFileSpeed = p.DownloadSpeedBytesPerSecond;
                    if (p.CurrentFileTotalBytes > 0)
                        entry.DownloadProgress = (double)p.CurrentFileDownloadedBytes
                            / p.CurrentFileTotalBytes * 100.0;
                }
            });

            statusProgress.Report($"下载: {Path.GetFileName(safeDestPath)}");
            await _modrinth.DownloadModAsync(file.Url, safeDestPath, fileProgress, cts.Token);

            _notification.Show($"{entry.Title} 下载完成！", NotificationType.Success);
        }
        catch (OperationCanceledException)
        {
            _notification.Show($"{entry.Title} 下载已取消。", NotificationType.Info);
        }
        catch (Exception ex)
        {
            _notification.Show($"下载失败：{ex.Message}", NotificationType.Error);
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
        }
    }

    [RelayCommand]
    private void CancelShaderDownload(ModDownloadEntry? entry)
    {
        if (entry == null) return;
        lock (_shaderCancelLock) { _shaderCancelCts?.Cancel(); }
    }

    // === Helpers ===

    /// <summary>
    /// Auto-search with empty query on first tab switch (shows popular/recommended content).
    /// </summary>
    private async Task AutoSearchCurrentTabAsync()
    {
        if (ActiveSearchResults.Count > 0) return;
        if (string.IsNullOrEmpty(SelectedMcVersion)) return;

        await SearchAsync();
    }

    /// <summary>
    /// CodeQL-compliant path sanitization for user-selected save paths.
    /// Uses <see cref="Path.GetFullPath"/> for normalization and
    /// <see cref="Path.GetFileName"/> as the CodeQL-recognized sanitizer,
    /// followed by <see cref="PathSafetyHelper.ValidateContained"/> for
    /// NTFS reparse-point defense-in-depth.
    /// </summary>
    private static string SanitizeDownloadPath(string savePath)
    {
        // 1. Normalize — resolves .. / . / redundant separators
        string fullPath = Path.GetFullPath(savePath);

        // 2. Strip directory components — CodeQL-recognized sanitizer
        string safeFileName = Path.GetFileName(fullPath);
        if (string.IsNullOrEmpty(safeFileName))
            safeFileName = "download";

        // 3. Extract the normalized parent directory
        string? parentDir = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(parentDir))
            throw new InvalidOperationException("无效的保存路径");

        // 4. Reconstruct safe path — path traversal no longer possible
        string safeDestPath = Path.Combine(parentDir, safeFileName);

        // 5. Defense-in-depth: NTFS reparse point / junction detection
        PathSafetyHelper.ValidateContained(safeDestPath, parentDir);

        return safeDestPath;
    }

    /// <summary>
    /// Load version manifest from cache (5 min TTL) or fetch from Mojang API.
    /// Reuses the same cache file as <see cref="DownloadViewModel"/>.
    /// </summary>
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
                catch { /* cache corrupt, fall through to network */ }
            }
        }

        // Fetch from Mojang API
        var versionsJson = await _httpClient.GetStringAsync(
            "https://launchermeta.mojang.com/mc/game/version_manifest.json");
        var onlineManifest = JsonSerializer.Deserialize<VersionManifest>(versionsJson);
        var versions = onlineManifest?.Versions ?? new List<ManifestVersion>();

        // Persist cache
        try
        {
            var manifest = new VersionManifest { Versions = versions };
            await File.WriteAllTextAsync(cacheFile, JsonSerializer.Serialize(manifest));
        }
        catch { /* best-effort */ }

        return versions;
    }
}
