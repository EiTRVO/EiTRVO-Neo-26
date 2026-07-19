using System;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Orchestrators;
using EiTRVO.ProEngine.Services;

namespace EiTRVO.ProEngine.ViewModels;

public partial class ResourcePackViewModel : BaseViewModel
{
    private readonly INotificationService _notification;
    private readonly IDialogService _dialogService;
    private readonly IModrinthService _modrinth;

    private string _versionId = "";
    private CancellationTokenSource? _resourceCancelCts;
    private CancellationTokenSource? _shaderCancelCts;
    private readonly object _resourceCancelLock = new();
    private readonly object _shaderCancelLock = new();

    // === Observable Properties ===

    [ObservableProperty]
    private string _instanceName = "";

    [ObservableProperty]
    private string _resourcePacksFolder = "";

    [ObservableProperty]
    private string _shaderPacksFolder = "";

    [ObservableProperty]
    private string _selectedTab = "resource-local";

    [ObservableProperty]
    private bool _isResourceEmpty = true;

    [ObservableProperty]
    private bool _isShaderEmpty = true;

    [ObservableProperty]
    private bool _resourceFolderExists;

    [ObservableProperty]
    private bool _shaderFolderExists;

    // === Download Tab State — Resource Packs ===

    [ObservableProperty]
    private string _resourceSearchQuery = "";

    public ObservableCollection<ModDownloadEntry> ResourceSearchResults { get; } = new();

    [ObservableProperty]
    private bool _isResourceSearching;

    [ObservableProperty]
    private bool _resourceHasNoResults;

    [ObservableProperty]
    private int _resourceTotalResults;

    // === Download Tab State — Shader Packs ===

    [ObservableProperty]
    private string _shaderSearchQuery = "";

    public ObservableCollection<ModDownloadEntry> ShaderSearchResults { get; } = new();

    [ObservableProperty]
    private bool _isShaderSearching;

    [ObservableProperty]
    private bool _shaderHasNoResults;

    [ObservableProperty]
    private int _shaderTotalResults;

    // === Collections ===

    public ObservableCollection<ResourcePackEntry> ResourcePacks { get; } = new();
    public ObservableCollection<ResourcePackEntry> ShaderPacks { get; } = new();

    // === Computed Visibility ===

    public bool IsResourceLocalVisible => SelectedTab == "resource-local";
    public bool IsShaderLocalVisible => SelectedTab == "shader-local";
    public bool IsResourceDownloadVisible => SelectedTab == "resource-download";
    public bool IsShaderDownloadVisible => SelectedTab == "shader-download";

    // === Events ===

    /// <summary>Triggers MainWindow to navigate back to instance detail.</summary>
    public event Action? BackRequested;

    public ResourcePackViewModel(INotificationService notification, IDialogService dialogService,
        IModrinthService modrinth)
    {
        _notification = notification;
        _dialogService = dialogService;
        _modrinth = modrinth;
    }

    partial void OnSelectedTabChanged(string value)
    {
        OnPropertyChanged(nameof(IsResourceLocalVisible));
        OnPropertyChanged(nameof(IsShaderLocalVisible));
        OnPropertyChanged(nameof(IsResourceDownloadVisible));
        OnPropertyChanged(nameof(IsShaderDownloadVisible));
    }

    /// <summary>Load instance context and scan resource/shader pack folders.</summary>
    public void LoadPacks(string instanceName, string resourcePacksFolder, string shaderPacksFolder,
        string versionId)
    {
        InstanceName = instanceName;
        ResourcePacksFolder = resourcePacksFolder;
        ShaderPacksFolder = shaderPacksFolder;
        _versionId = versionId;

        SelectedTab = "resource-local";
        LoadFolder(ResourcePacksFolder, ResourcePacks);
        ResourceFolderExists = Directory.Exists(ResourcePacksFolder);
        LoadFolder(ShaderPacksFolder, ShaderPacks);
        ShaderFolderExists = Directory.Exists(ShaderPacksFolder);
    }

    /// <summary>Scan a folder for .zip files, .restemp files, and subdirectories.</summary>
    private static void LoadFolder(string folder, ObservableCollection<ResourcePackEntry> target)
    {
        target.Clear();
        if (!Directory.Exists(folder))
            return;

        try
        {
            // .zip files (enabled packs)
            foreach (var file in Directory.GetFiles(folder, "*.zip"))
                target.Add(ResourcePackEntry.FromPath(file));

            // .restemp files (disabled packs)
            foreach (var file in Directory.GetFiles(folder, "*.restemp"))
                target.Add(ResourcePackEntry.FromPath(file));

            // Folders (unzipped packs — could be normal or disabled)
            foreach (var dir in Directory.GetDirectories(folder))
                target.Add(ResourcePackEntry.FromPath(dir));
        }
        catch { /* skip inaccessible */ }
    }

    // === Commands ===

    [RelayCommand]
    private void GoBack() => BackRequested?.Invoke();

    [RelayCommand]
    private void SelectTab(string tab) => SelectedTab = tab;

    // --- Resource Pack Commands ---

    [RelayCommand]
    private void DeleteResourcePack(ResourcePackEntry? entry)
    {
        if (entry == null) return;
        DeleteEntry(entry);
        ResourcePacks.Remove(entry);
        IsResourceEmpty = ResourcePacks.Count == 0;
    }

    [RelayCommand]
    private void ToggleResourcePack(ResourcePackEntry? entry)
    {
        if (entry == null) return;
        ToggleEntry(entry);
    }

    // --- Shader Pack Commands ---

    [RelayCommand]
    private void DeleteShaderPack(ResourcePackEntry? entry)
    {
        if (entry == null) return;
        DeleteEntry(entry);
        ShaderPacks.Remove(entry);
        IsShaderEmpty = ShaderPacks.Count == 0;
    }

    [RelayCommand]
    private void ToggleShaderPack(ResourcePackEntry? entry)
    {
        if (entry == null) return;
        ToggleEntry(entry);
    }

    // --- Import Commands ---

    [RelayCommand]
    private async Task ImportResourcePackAsync()
    {
        var filePath = await _dialogService.ShowOpenFileDialogAsync(
            "选择资源包文件", "ZIP 压缩包|*.zip");
        if (filePath == null) return;

        try
        {
            string? error = await Task.Run(() => ValidateAndImportPack(filePath, ResourcePacksFolder, "pack"));
            if (error != null)
            {
                _notification.Show(error, NotificationType.Warning);
                return;
            }
            _notification.Show("资源包导入成功！", NotificationType.Success);
            ReloadResourcePacks();
        }
        catch (Exception ex)
        {
            _notification.Show($"导入失败：{ex.Message}", NotificationType.Error);
        }
    }

    [RelayCommand]
    private async Task ImportShaderPackAsync()
    {
        var filePath = await _dialogService.ShowOpenFileDialogAsync(
            "选择光影包文件", "ZIP 压缩包|*.zip");
        if (filePath == null) return;

        try
        {
            string? error = await Task.Run(() => ValidateAndImportPack(filePath, ShaderPacksFolder, "shader"));
            if (error != null)
            {
                _notification.Show(error, NotificationType.Warning);
                return;
            }
            _notification.Show("光影包导入成功！", NotificationType.Success);
            ReloadShaderPacks();
        }
        catch (Exception ex)
        {
            _notification.Show($"导入失败：{ex.Message}", NotificationType.Error);
        }
    }

    /// <summary>
    /// 验证并复制资源包/光影包 zip 到目标文件夹。
    /// packType: "pack" for resource packs, "shader" for shader packs.
    /// 返回 null 表示成功，返回非 null 字符串表示验证失败原因。
    /// </summary>
    private static string? ValidateAndImportPack(string zipPath, string destFolder, string packType)
    {
        using var archive = ZipFile.OpenRead(zipPath);

        if (packType == "pack")
        {
            // 资源包验证：根目录必须有 pack.mcmeta
            var mcmeta = archive.Entries.FirstOrDefault(
                e => e.Name.Equals("pack.mcmeta", StringComparison.OrdinalIgnoreCase)
                    && !e.FullName.EndsWith("/"));
            if (mcmeta == null)
                return "无效的资源包文件：未找到 pack.mcmeta。";

            // 排除整合包（manifest.json）
            if (archive.Entries.Any(e => e.Name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)))
                return "这不是资源包文件，而是一个整合包。";
        }
        else
        {
            // 光影包验证：必须有 shaders/ 目录
            bool hasShadersDir = archive.Entries.Any(
                e => e.FullName.StartsWith("shaders/", StringComparison.OrdinalIgnoreCase));
            if (!hasShadersDir)
                return "无效的光影包文件：未找到 shaders/ 目录。";

            // 排除资源包（pack.mcmeta）
            if (archive.Entries.Any(e => e.Name.Equals("pack.mcmeta", StringComparison.OrdinalIgnoreCase)
                && !e.FullName.EndsWith("/")))
                return "这不是光影包文件，而是一个资源包。";
        }

        // === 验证通过，复制到目标文件夹 ===

        string fileName = Path.GetFileName(zipPath);
        string destPath = Path.Combine(destFolder, fileName);

        Directory.CreateDirectory(destFolder);

        if (File.Exists(destPath))
        {
            string nameNoExt = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            int counter = 1;
            while (File.Exists(Path.Combine(destFolder, $"{nameNoExt}_{counter}{ext}")))
                counter++;
            destPath = Path.Combine(destFolder, $"{nameNoExt}_{counter}{ext}");
        }

        File.Copy(zipPath, destPath);
        return null; // 成功
    }

    // === Download Commands — Resource Packs ===

    [RelayCommand]
    private async Task SearchResourcePacksAsync()
    {
        if (string.IsNullOrWhiteSpace(ResourceSearchQuery)) return;
        IsResourceSearching = true;
        ResourceHasNoResults = false;
        ResourceTotalResults = 0;
        ResourceSearchResults.Clear();
        try
        {
            var response = await _modrinth.SearchProjectsAsync(
                ResourceSearchQuery.Trim(), _versionId, "resourcepack");
            ResourceTotalResults = response.TotalHits;
            foreach (var hit in response.Hits)
            {
                ResourceSearchResults.Add(new ModDownloadEntry
                {
                    ProjectId = hit.ProjectId, Title = hit.Title,
                    Description = hit.Description, Author = hit.Author,
                    IconUrl = hit.IconUrl ?? "", Downloads = hit.Downloads,
                    IsCompatible = true
                });
            }
            ResourceHasNoResults = ResourceSearchResults.Count == 0;
        }
        catch (Exception ex) { _notification.Show($"搜索失败：{ex.Message}", NotificationType.Error); }
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
                entry.ProjectId, _versionId, cts.Token);
            if (version == null) throw new Exception("未找到兼容版本。");
            var file = version.Files.FirstOrDefault(f => f.Primary)
                       ?? version.Files.FirstOrDefault();
            if (file == null) throw new Exception("未找到兼容的版本文件。");

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

            Directory.CreateDirectory(ResourcePacksFolder);
            string destPath = Path.Combine(ResourcePacksFolder, file.Filename);
            statusProgress.Report($"下载: {file.Filename}");

            await _modrinth.DownloadModAsync(file.Url, destPath, fileProgress, cts.Token);

            _notification.Show($"{entry.Title} 下载完成！", NotificationType.Success);
            ReloadResourcePacks();
        }
        catch (OperationCanceledException)
        {
            CleanupPartialDownloads(ResourcePacksFolder);
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

    // === Download Commands — Shader Packs ===

    [RelayCommand]
    private async Task SearchShaderPacksAsync()
    {
        if (string.IsNullOrWhiteSpace(ShaderSearchQuery)) return;
        IsShaderSearching = true;
        ShaderHasNoResults = false;
        ShaderTotalResults = 0;
        ShaderSearchResults.Clear();
        try
        {
            var response = await _modrinth.SearchProjectsAsync(
                ShaderSearchQuery.Trim(), _versionId, "shader");
            ShaderTotalResults = response.TotalHits;
            foreach (var hit in response.Hits)
            {
                ShaderSearchResults.Add(new ModDownloadEntry
                {
                    ProjectId = hit.ProjectId, Title = hit.Title,
                    Description = hit.Description, Author = hit.Author,
                    IconUrl = hit.IconUrl ?? "", Downloads = hit.Downloads,
                    IsCompatible = true
                });
            }
            ShaderHasNoResults = ShaderSearchResults.Count == 0;
        }
        catch (Exception ex) { _notification.Show($"搜索失败：{ex.Message}", NotificationType.Error); }
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
                entry.ProjectId, _versionId, cts.Token);
            if (version == null) throw new Exception("未找到兼容版本。");
            var file = version.Files.FirstOrDefault(f => f.Primary)
                       ?? version.Files.FirstOrDefault();
            if (file == null) throw new Exception("未找到兼容的版本文件。");

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

            Directory.CreateDirectory(ShaderPacksFolder);
            string destPath = Path.Combine(ShaderPacksFolder, file.Filename);
            statusProgress.Report($"下载: {file.Filename}");

            await _modrinth.DownloadModAsync(file.Url, destPath, fileProgress, cts.Token);

            _notification.Show($"{entry.Title} 下载完成！", NotificationType.Success);
            ReloadShaderPacks();
        }
        catch (OperationCanceledException)
        {
            CleanupPartialDownloads(ShaderPacksFolder);
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

    private static void CleanupPartialDownloads(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                foreach (var f in Directory.GetFiles(folder, "*.part*"))
                    try { File.Delete(f); } catch { }
            }
        }
        catch { /* best effort */ }
    }

    private void ReloadResourcePacks()
    {
        LoadFolder(ResourcePacksFolder, ResourcePacks);
        IsResourceEmpty = ResourcePacks.Count == 0;
    }

    private void ReloadShaderPacks()
    {
        LoadFolder(ShaderPacksFolder, ShaderPacks);
        IsShaderEmpty = ShaderPacks.Count == 0;
    }

    // === Shared helpers ===

    private static void DeleteEntry(ResourcePackEntry entry)
    {
        try
        {
            if (entry.IsFolder)
            {
                if (Directory.Exists(entry.FullPath))
                    Directory.Delete(entry.FullPath, true);
            }
            else
            {
                if (File.Exists(entry.FullPath))
                    File.Delete(entry.FullPath);
            }
        }
        catch { /* best effort */ }
    }

    private static void ToggleEntry(ResourcePackEntry entry)
    {
        try
        {
            if (entry.IsDisabled)
            {
                // Enable: restore from .restemp
                string newName = entry.FileName.EndsWith(".restemp", StringComparison.OrdinalIgnoreCase)
                    ? entry.FileName[..^8] // strip ".restemp"
                    : entry.FileName;
                string? parentDir = Path.GetDirectoryName(entry.FullPath);
                if (parentDir == null) return;
                string newPath = Path.Combine(parentDir, newName);

                if (Directory.Exists(entry.FullPath) || File.Exists(entry.FullPath))
                {
                    if (Directory.Exists(newPath) || File.Exists(newPath))
                    {
                        if (File.Exists(newPath)) File.Delete(newPath);
                        else Directory.Delete(newPath, true);
                    }
                    if (entry.IsFolder)
                        Directory.Move(entry.FullPath, newPath);
                    else
                        File.Move(entry.FullPath, newPath);
                }

                entry.FullPath = newPath;
                entry.FileName = newName;
                entry.IsFolder = Directory.Exists(newPath);
                entry.Name = entry.IsFolder ? newName : Path.GetFileNameWithoutExtension(newName);
                entry.IsDisabled = false;
            }
            else
            {
                // Disable: rename to .restemp
                string newName = entry.FileName + ".restemp";
                string? parentDir = Path.GetDirectoryName(entry.FullPath);
                if (parentDir == null) return;
                string newPath = Path.Combine(parentDir, newName);

                if (Directory.Exists(entry.FullPath) || File.Exists(entry.FullPath))
                {
                    if (Directory.Exists(newPath) || File.Exists(newPath))
                    {
                        if (File.Exists(newPath)) File.Delete(newPath);
                        else Directory.Delete(newPath, true);
                    }
                    if (entry.IsFolder)
                        Directory.Move(entry.FullPath, newPath);
                    else
                        File.Move(entry.FullPath, newPath);
                }

                entry.FullPath = newPath;
                entry.FileName = newName;
                entry.Name = entry.IsFolder
                    ? Path.GetFileNameWithoutExtension(newName)
                    : Path.GetFileNameWithoutExtension(newName);
                entry.IsDisabled = true;
            }
        }
        catch { /* best effort */ }
    }
}
