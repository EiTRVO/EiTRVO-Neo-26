using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EiTRVO.ProEngine.Helpers;
using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Orchestrators;
using EiTRVO.ProEngine.Services;

namespace EiTRVO.ProEngine.ViewModels;

public partial class ModManagementViewModel : BaseViewModel
{
    private readonly IModrinthService _modrinth;
    private readonly INotificationService _notification;
    private readonly IDialogService _dialogService;
    private readonly IGameFolderService _gameFolder;
    private readonly IDispatcherService _dispatcher;

    private CancellationTokenSource? _activeDownloadCts;
    private readonly object _downloadLock = new();

    public ObservableCollection<ModEntry> Mods { get; } = new();
    public ObservableCollection<ModDownloadEntry> SearchResults { get; } = new();

    [ObservableProperty]
    private string _instanceName = "";

    [ObservableProperty]
    private string _modsFolder = "";

    [ObservableProperty]
    private string _versionId = "";

    [ObservableProperty]
    private string _loaderType = "";

    [ObservableProperty]
    private bool _isResolvingModrinthMetadata;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private string _selectedTab = "local";

    // Search state
    [ObservableProperty]
    private string _searchQuery = "";

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private bool _hasNoResults;

    [ObservableProperty]
    private int _totalSearchResults;

    public bool IsLocalTabVisible => SelectedTab == "local";
    public bool IsDownloadTabVisible => SelectedTab == "download";

    /// <summary>Triggers MainWindow to navigate back.</summary>
    public event Action? BackRequested;

    public ModManagementViewModel(IModrinthService modrinth, INotificationService notification,
        IDialogService dialogService, IGameFolderService gameFolder, IDispatcherService dispatcher)
    {
        _modrinth = modrinth;
        _notification = notification;
        _dialogService = dialogService;
        _gameFolder = gameFolder;
        _dispatcher = dispatcher;
    }

    partial void OnSelectedTabChanged(string value)
    {
        OnPropertyChanged(nameof(IsLocalTabVisible));
        OnPropertyChanged(nameof(IsDownloadTabVisible));

        // Auto-load recommended mods on first switch to download tab
        if (value == "download" && SearchResults.Count == 0 && !IsSearching
            && !string.IsNullOrEmpty(VersionId))
        {
            _ = PerformSearchAsync("");
        }
    }

    /// <summary>Load instance context and scan local mods.</summary>
    public void LoadMods(string instanceName, string modsFolder, string versionId, string loaderType)
    {
        InstanceName = instanceName;
        ModsFolder = modsFolder;
        VersionId = versionId;
        LoaderType = loaderType;
        Mods.Clear();

        if (!Directory.Exists(modsFolder))
        {
            IsEmpty = true;
            return;
        }

        try
        {
            var files = Directory.GetFiles(modsFolder, "*.jar")
                .Concat(Directory.GetFiles(modsFolder, "*.modtemp"))
                .OrderBy(f => Path.GetFileName(f))
                .ToList();

            foreach (var file in files)
                Mods.Add(ModEntry.FromFile(file));

            IsEmpty = Mods.Count == 0;

            // Background: resolve Modrinth metadata for display-friendly names
            _ = ResolveModrinthMetadataAsync();
        }
        catch
        {
            IsEmpty = true;
        }
    }

    [RelayCommand]
    private async Task ImportModAsync()
    {
        var filePath = await _dialogService.ShowOpenFileDialogAsync(
            "选择模组文件", "JAR 文件|*.jar");
        if (filePath == null) return;

        try
        {
            // 计算 SHA-1
            string sha1 = await Task.Run(() => ComputeSha1(filePath));

            // Modrinth 校验
            bool verified = await _modrinth.VerifyFileByHashAsync(sha1);

            if (!verified)
            {
                bool userOk = await _dialogService.ShowConfirmAsync(
                    "该模组可能为非官方模组，请确认来源安全后再导入。\n\n是否仍然导入？",
                    "安全警告");
                if (!userOk) return;
            }

            // 复制到 mods 文件夹
            string fileName = Path.GetFileName(filePath);
            string destPath = Path.Combine(ModsFolder, fileName);

            if (File.Exists(destPath))
            {
                string nameNoExt = Path.GetFileNameWithoutExtension(fileName);
                int counter = 1;
                while (File.Exists(Path.Combine(ModsFolder, $"{nameNoExt}_{counter}.jar")))
                    counter++;
                destPath = Path.Combine(ModsFolder, $"{nameNoExt}_{counter}.jar");
            }

            await Task.Run(() => File.Copy(filePath, destPath));
            _notification.Show("模组导入成功！", NotificationType.Success);
            LoadMods(InstanceName, ModsFolder, VersionId, LoaderType);
        }
        catch (Exception ex)
        {
            _notification.Show($"导入失败：{ex.Message}", NotificationType.Error);
        }
    }

    private static string ComputeSha1(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        byte[] hash = SHA1.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Background task: compute SHA-1 for each local .jar, then batch-resolve
    /// Modrinth project titles and descriptions. Results are written directly
    /// into <see cref="ModEntry"/> properties — UI updates via binding.
    /// Failures are silent; unmatched files keep their original display names.
    /// </summary>
    private async Task ResolveModrinthMetadataAsync()
    {
        int totalJars = 0;
        int resolvedCount = 0;
        try
        {
            IsResolvingModrinthMetadata = true;

            var jarMods = Mods.Where(m =>
                m.FullPath.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)).ToList();
            if (jarMods.Count == 0) return;
            totalJars = jarMods.Count;

            // 1) Compute SHA-1 in parallel (CPU-bound)
            var sha1Entries = new List<(ModEntry Mod, string Sha1)>(jarMods.Count);
            await Task.Run(() =>
            {
                Parallel.ForEach(jarMods, mod =>
                {
                    try
                    {
                        string sha1 = ComputeSha1(mod.FullPath);
                        lock (sha1Entries)
                            sha1Entries.Add((mod, sha1));
                    }
                    catch { /* skip unreadable files */ }
                });
            });

            if (sha1Entries.Count == 0) return;

            // 2) Check disk cache first
            var cache = new LocalModMetadataCache(_gameFolder.GameDir);
            var cached = cache.GetBatch(sha1Entries.Select(e => e.Sha1));

            // Apply cache hits immediately (on UI thread)
            var uncached = new List<(ModEntry Mod, string Sha1)>();
            var cacheHits = new List<(ModEntry Mod, string Title, string Description)>();
            foreach (var (mod, sha1) in sha1Entries)
            {
                if (cached.TryGetValue(sha1, out var meta))
                    cacheHits.Add((mod, meta.Title, meta.Description));
                else
                    uncached.Add((mod, sha1));
            }

            if (cacheHits.Count > 0)
            {
                _dispatcher.Invoke(() =>
                {
                    foreach (var (mod, title, desc) in cacheHits)
                    {
                        mod.ModrinthTitle = title;
                        mod.ModrinthDescription = desc;
                        mod.NotifyDisplayPropertiesChanged();
                    }
                });
            }
            if (uncached.Count == 0) return;

            // 3) Batch API lookup: SHA-1 → VersionFile (project_id)
            var sha1ToVersionFile = await _modrinth.GetVersionFilesByHashesAsync(
                uncached.Select(e => e.Sha1).ToList());

            if (sha1ToVersionFile.Count == 0) return;

            // 4) Collect unique project IDs, then batch lookup projects
            var sha1ToProjectId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var projectIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (mod, sha1) in uncached)
            {
                if (sha1ToVersionFile.TryGetValue(sha1, out var vf) && !string.IsNullOrEmpty(vf.ProjectId))
                {
                    sha1ToProjectId[sha1] = vf.ProjectId;
                    projectIds.Add(vf.ProjectId);
                }
            }

            if (projectIds.Count == 0) return;

            var projects = await _modrinth.GetProjectsByIdsAsync(projectIds.ToList());
            var projectMap = projects
                .Where(p => !string.IsNullOrEmpty(p.Id))
                .ToDictionary(p => p.Id, p => p, StringComparer.OrdinalIgnoreCase);

            // 5) Apply results to ModEntry (on UI thread) + collect cache entries
            var newCacheEntries = new List<(string Sha1, string Title, string Description)>();
            var resolvedMods = new List<(ModEntry Mod, string Title, string Description)>();
            foreach (var (mod, sha1) in uncached)
            {
                if (sha1ToProjectId.TryGetValue(sha1, out var pid) && projectMap.TryGetValue(pid, out var proj))
                {
                    resolvedMods.Add((mod, proj.Title, proj.Description));
                    newCacheEntries.Add((sha1, proj.Title, proj.Description));
                }
            }

            if (resolvedMods.Count > 0)
            {
                _dispatcher.Invoke(() =>
                {
                    foreach (var (mod, title, desc) in resolvedMods)
                    {
                        mod.ModrinthTitle = title;
                        mod.ModrinthDescription = desc;
                        mod.NotifyDisplayPropertiesChanged();
                    }
                });
            }

            // Persist cache
            if (newCacheEntries.Count > 0)
            {
                cache.PutBatch(newCacheEntries);
                cache.Save();
            }

            // Report completion
            resolvedCount = cacheHits.Count + resolvedMods.Count;
            if (resolvedCount > 0)
            {
                _notification.AppendLog(
                    $"SHA1 识别完成：{resolvedCount}/{totalJars} 个模组已关联 Modrinth 元数据",
                    NotificationType.Info);
            }
        }
        catch (Exception ex)
        {
            _notification.AppendLog(
                $"Modrinth 元数据解析失败（{resolvedCount}/{totalJars}，将显示文件名）：{ex.Message}",
                NotificationType.Warning);
        }
        finally
        {
            IsResolvingModrinthMetadata = false;
        }
    }

    [RelayCommand]
    private void DeleteMod(ModEntry mod)
    {
        if (mod == null) return;

        try
        {
            if (File.Exists(mod.FullPath))
                File.Delete(mod.FullPath);
        }
        catch { }

        Mods.Remove(mod);
        IsEmpty = Mods.Count == 0;
    }

    [RelayCommand]
    private void ToggleMod(ModEntry mod)
    {
        if (mod == null) return;

        try
        {
            if (mod.IsDisabled)
            {
                string newPath = Path.ChangeExtension(mod.FullPath, ".jar");
                if (File.Exists(mod.FullPath))
                {
                    if (File.Exists(newPath)) File.Delete(newPath);
                    File.Move(mod.FullPath, newPath);
                }
                mod.FullPath = newPath;
                mod.FileName = Path.GetFileName(newPath);
                mod.Name = Path.GetFileNameWithoutExtension(newPath);
                mod.IsDisabled = false;
            }
            else
            {
                string newPath = Path.ChangeExtension(mod.FullPath, ".modtemp");
                if (File.Exists(mod.FullPath))
                {
                    if (File.Exists(newPath)) File.Delete(newPath);
                    File.Move(mod.FullPath, newPath);
                }
                mod.FullPath = newPath;
                mod.FileName = Path.GetFileName(newPath);
                mod.IsDisabled = true;
            }
        }
        catch { }
    }

    [RelayCommand]
    private void GoBack() => BackRequested?.Invoke();

    [RelayCommand]
    private void SelectTab(string tab) => SelectedTab = tab;

    // === Modrinth Search ===

    [RelayCommand]
    private async Task SearchAsync()
    {
        await PerformSearchAsync(SearchQuery.Trim());
    }

    /// <summary>Execute a Modrinth search and populate <see cref="SearchResults"/>.</summary>
    /// <param name="query">Search query (empty string returns popular/recommended mods).</param>
    private async Task PerformSearchAsync(string query)
    {
        IsSearching = true;
        HasNoResults = false;
        TotalSearchResults = 0;
        SearchResults.Clear();

        try
        {
            await SearchModrinthAsync(query, null);

            HasNoResults = SearchResults.Count == 0;
        }
        catch (Exception ex)
        {
            _notification.Show($"搜索失败：{ex.Message}", NotificationType.Error);
        }
        finally
        {
            IsSearching = false;
        }
    }

    private async Task SearchModrinthAsync(string term, HashSet<string>? seenIds)
    {
        var response = await _modrinth.SearchAsync(term, VersionId, LoaderType);
        if (seenIds == null)
        {
            // First search — use total hits directly
            TotalSearchResults = response.TotalHits;
            seenIds = new HashSet<string>();
        }

        foreach (var hit in response.Hits)
        {
            if (seenIds.Contains(hit.ProjectId)) continue;
            seenIds.Add(hit.ProjectId);

            // Facets filter by version + loader server-side — all results are compatible
            SearchResults.Add(new ModDownloadEntry
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
    }

    /// <summary>Single-term search — replaces TotalSearchResults and adds all results.</summary>
    private async Task SearchModrinthAsync(string term)
    {
        await SearchModrinthAsync(term, null);
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
        lock (_downloadLock)
        {
            _activeDownloadCts?.Cancel();
            _activeDownloadCts?.Dispose();
            _activeDownloadCts = new CancellationTokenSource();
            cts = _activeDownloadCts;
        }

        try
        {
            var statusProgress = new Progress<string>(s =>
            {
                entry.DownloadStatus = s;
            });

            var fileProgress = new Progress<DownloadProgress>(p =>
            {
                if (!string.IsNullOrEmpty(p.CurrentFileName))
                {
                    entry.CurrentFileName = p.CurrentFileName;
                    entry.CurrentFileDownloadedBytes = p.CurrentFileDownloadedBytes;
                    entry.CurrentFileTotalBytes = p.CurrentFileTotalBytes;
                    entry.CurrentFileSpeed = p.DownloadSpeedBytesPerSecond;

                    // File-count progress from dependency chain (BytesDownloaded/TotalBytes reused)
                    if (p.TotalBytes > 0)
                    {
                        entry.CompletedFiles = (int)p.BytesDownloaded;
                        entry.TotalFiles = (int)p.TotalBytes;
                    }

                    if (p.CurrentFileTotalBytes > 0)
                        entry.DownloadProgress = (double)p.CurrentFileDownloadedBytes / p.CurrentFileTotalBytes * 100.0;
                }
            });

            var allFiles = await _modrinth.DownloadWithDependenciesAsync(
                entry.ProjectId, VersionId, LoaderType, ModsFolder,
                statusProgress, fileProgress, cts.Token);

            _notification.Show(
                allFiles.Count > 1
                    ? $"{entry.Title} 下载完成！(含 {allFiles.Count} 个文件)"
                    : $"{entry.Title} 下载完成！",
                NotificationType.Success);

            LoadMods(InstanceName, ModsFolder, VersionId, LoaderType);
            SelectTab("local");
        }
        catch (OperationCanceledException)
        {
            CleanupPartialDownloads(ModsFolder);
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
            entry.CompletedFiles = 0;
            entry.TotalFiles = 0;
        }
    }

    [RelayCommand]
    private void CancelDownload(ModDownloadEntry entry)
    {
        if (entry == null) return;
        lock (_downloadLock)
        {
            _activeDownloadCts?.Cancel();
        }
    }

    /// <summary>Remove residual .part* files from cancelled/interrupted downloads.</summary>
    private static void CleanupPartialDownloads(string modsFolder)
    {
        try
        {
            if (Directory.Exists(modsFolder))
            {
                foreach (var f in Directory.GetFiles(modsFolder, "*.part*"))
                    try { File.Delete(f); } catch { }
            }
        }
        catch { /* best effort */ }
    }
}
