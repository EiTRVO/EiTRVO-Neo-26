using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Orchestrators;
using EiTRVO.ProEngine.Services;

namespace EiTRVO.ProEngine.ViewModels;

public partial class ModManagementViewModel : BaseViewModel
{
    private readonly IModrinthService _modrinth;
    private readonly INotificationService _notification;
    private readonly IDialogService _dialogService;

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

    public ModManagementViewModel(IModrinthService modrinth, INotificationService notification, IDialogService dialogService)
    {
        _modrinth = modrinth;
        _notification = notification;
        _dialogService = dialogService;
    }

    partial void OnSelectedTabChanged(string value)
    {
        OnPropertyChanged(nameof(IsLocalTabVisible));
        OnPropertyChanged(nameof(IsDownloadTabVisible));
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
        using var sha1 = System.Security.Cryptography.SHA1.Create();
        byte[] hash = sha1.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
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
        if (string.IsNullOrWhiteSpace(SearchQuery)) return;

        IsSearching = true;
        HasNoResults = false;
        TotalSearchResults = 0;
        SearchResults.Clear();

        try
        {
            await SearchModrinthAsync(SearchQuery.Trim(), null);

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
