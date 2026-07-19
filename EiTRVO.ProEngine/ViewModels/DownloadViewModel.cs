using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Diagnostics;
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

public partial class DownloadViewModel : BaseViewModel
{
    private readonly IDownloadService _downloadService;
    private readonly IModLoaderService _modLoaderService;
    private readonly INotificationService _notificationService;
    private readonly IGameFolderService _gameFolder;
    private readonly InstanceManager _instanceManager;
    private readonly HttpClient _httpClient;

    private CancellationTokenSource? _activeDownloadCts;
    private readonly object _downloadLock = new();

    private List<ManifestVersion> _allVersions = new();

    // === Observable Properties ===

    [ObservableProperty]
    private string _instanceName = "";

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private double _downloadProgressValue;

    [ObservableProperty]
    private string _progressText = "";

    [ObservableProperty]
    private bool _isProgressIndeterminate;

    [ObservableProperty]
    private string _currentFileProgress = "";

    [ObservableProperty]
    private double _fileProgressValue;

    [ObservableProperty]
    private bool _isFileProgressIndeterminate;

    [ObservableProperty]
    private VersionEntry? _selectedVersion;

    // === Mod loader selection ===

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModLoaderSelected))]
    [NotifyPropertyChangedFor(nameof(IsModLoaderVersionVisible))]
    [NotifyPropertyChangedFor(nameof(IsOptiFineForForgeVisible))]
    [NotifyPropertyChangedFor(nameof(IsOptiFineDropdownVisible))]
    private string? _selectedLoaderType;  // null/"Vanilla" = none, "Forge"/"Fabric"/"Quilt"/"NeoForge"/"OptiFine"

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModLoaderVersionVisible))]
    private ObservableCollection<ModLoaderVersion> _loaderVersions = new();

    [ObservableProperty]
    private ModLoaderVersion? _selectedLoaderVersion;

    [ObservableProperty]
    private string _modLoaderHint = "";

    // === OptiFine coexistence with Forge ===

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOptiFineForForgeVisible))]
    [NotifyPropertyChangedFor(nameof(IsOptiFineDropdownVisible))]
    private bool _withOptiFine;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOptiFineDropdownVisible))]
    private ObservableCollection<ModLoaderVersion> _optiFineForForgeVersions = new();

    [ObservableProperty]
    private ModLoaderVersion? _selectedOptiFineVersion;

    // === Collections ===
    public ObservableCollection<VersionEntry> FilteredVersions { get; } = new();
    public HashSet<string> ActiveFilters { get; } = new();

    // === Computed ===
    public bool IsModLoaderSelected => !string.IsNullOrEmpty(SelectedLoaderType) && SelectedLoaderType != "Vanilla";
    public bool IsModLoaderVersionVisible => LoaderVersions.Count > 0;
    public bool IsOptiFineForForgeVisible => SelectedLoaderType == "Forge";
    public bool IsOptiFineDropdownVisible => WithOptiFine && SelectedLoaderType == "Forge" && OptiFineForForgeVersions.Count > 0;

    // === Events for View interaction ===
    public event Action<bool>? DownloadProgressOverlayChanged;
    public event Action<DownloadProgress>? FileProgressUpdated;
    public event Action? NavigateToModpackDownload;
    public Func<LauncherSettings>? SettingsProvider;

    // Progress UI state
    private string _lastFileProgressName = "";
    private long _lastFileProgressTick;
    private string _lockedFileName = "";
    private long _lockedFileTotalBytes;
    private long _lockedFileLastProgressTick;
    private const long LockTimeoutMs = 5000;

    public DownloadViewModel(
        IDownloadService downloadService,
        IModLoaderService modLoaderService,
        INotificationService notificationService,
        IGameFolderService gameFolder,
        InstanceManager instanceManager,
        HttpClient httpClient)
    {
        _downloadService = downloadService;
        _modLoaderService = modLoaderService;
        _notificationService = notificationService;
        _gameFolder = gameFolder;
        _instanceManager = instanceManager;
        _httpClient = httpClient;

    }

    public void SetAllVersions(List<ManifestVersion> versions)
    {
        _allVersions = versions;
        RefreshFilteredList();
    }

    /// <summary>Load version manifest from cache/online and populate the filtered list.</summary>
    public async Task InitializeVersionListAsync()
    {
        _allVersions = await LoadVersionManifestCachedAsync();
        RefreshFilteredList();
    }

    public bool HasVersion(string versionId)
        => _allVersions.Any(v => v.Id == versionId);

    public void RefreshFilteredList()
    {
        var filtered = ActiveFilters.Count == 0
            ? _allVersions
            : _allVersions.Where(v => ActiveFilters.Contains(v.Type));

        var items = filtered.Select(v => new VersionEntry
        {
            Id = v.Id,
            Type = v.Type,
            Url = v.Url
        }).ToList();

        FilteredVersions.Clear();
        foreach (var item in items)
            FilteredVersions.Add(item);
    }

    // Auto-trigger mod loader loading when selection changes
    partial void OnSelectedLoaderTypeChanged(string? value)
    {
        _ = LoadModLoaderVersionsCommand.ExecuteAsync(null);
    }

    partial void OnWithOptiFineChanged(bool value)
    {
        _ = LoadOptiFineForForgeCommand.ExecuteAsync(null);
    }

    // === Commands ===

    [RelayCommand]
    private void OpenModpackDownload()
        => NavigateToModpackDownload?.Invoke();

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
    private async Task RefreshVersionsAsync()
    {
        _allVersions = await LoadVersionManifestCachedAsync();
        RefreshFilteredList();
    }

    [RelayCommand]
    private void ToggleFilter(string type)
    {
        if (ActiveFilters.Contains(type))
            ActiveFilters.Remove(type);
        else
            ActiveFilters.Add(type);
        RefreshFilteredList();
    }

    [RelayCommand]
    private async Task LoadModLoaderVersionsAsync()
    {
        if (string.IsNullOrEmpty(SelectedLoaderType) || SelectedLoaderType == "Vanilla")
        {
            LoaderVersions = new();
            ModLoaderHint = "";
            WithOptiFine = false;
            return;
        }

        var tag = SelectedLoaderType;
        var selectedVersion = SelectedVersion;
        if (selectedVersion == null)
        {
            _notificationService.Show("请先在版本列表中选择一个 Minecraft 版本。", NotificationType.Warning);
            SelectedLoaderType = null;
            return;
        }

        string mcVersion = selectedVersion.Id;
        ModLoaderHint = $"正在获取 {tag} 版本列表...";

        try
        {
            var versions = tag switch
            {
                "Fabric" => await _modLoaderService.GetFabricLoaderVersionsAsync(_httpClient, mcVersion),
                "Quilt" => await _modLoaderService.GetQuiltLoaderVersionsAsync(_httpClient, mcVersion),
                "Forge" => await _modLoaderService.GetForgeVersionsAsync(_httpClient, mcVersion),
                "NeoForge" => await _modLoaderService.GetNeoForgeVersionsAsync(_httpClient, mcVersion),
                "OptiFine" => await _modLoaderService.GetOptiFineVersionsAsync(_httpClient, mcVersion),
                _ => new List<ModLoaderVersion>()
            };

            LoaderVersions = new ObservableCollection<ModLoaderVersion>(versions);

            if (LoaderVersions.Count == 0)
            {
                ModLoaderHint = $"该版本暂无可用 {tag} 加载器。";
            }
            else
            {
                SelectedLoaderVersion = LoaderVersions[0];
                ModLoaderHint = "";

                // Auto-fill instance name
                if (string.IsNullOrWhiteSpace(InstanceName))
                    InstanceName = $"{tag} {mcVersion}";
            }
        }
        catch (Exception ex)
        {
            ModLoaderHint = $"获取版本失败：{ex.Message}";
            LoaderVersions = new();
            _notificationService.AppendLog($"获取 {tag} 版本列表失败：{ex.Message}", NotificationType.Error);
        }
    }

    [RelayCommand]
    private async Task LoadOptiFineForForgeAsync()
    {
        if (!WithOptiFine || SelectedLoaderType != "Forge")
        {
            OptiFineForForgeVersions = new();
            return;
        }

        var selectedVersion = SelectedVersion;
        if (selectedVersion == null)
        {
            WithOptiFine = false;
            _notificationService.Show("请先在版本列表中选择一个 Minecraft 版本。", NotificationType.Warning);
            return;
        }

        string mcVersion = selectedVersion.Id;
        try
        {
            var versions = await _modLoaderService.GetOptiFineVersionsAsync(_httpClient, mcVersion);
            OptiFineForForgeVersions = new ObservableCollection<ModLoaderVersion>(versions);

            if (OptiFineForForgeVersions.Count == 0)
            {
                _notificationService.Show($"MC {mcVersion} 暂无可用 OptiFine 版本。", NotificationType.Warning);
            }
            else
            {
                SelectedOptiFineVersion = OptiFineForForgeVersions[0];
            }
        }
        catch (Exception ex)
        {
            OptiFineForForgeVersions = new();
            _notificationService.AppendLog($"获取 OptiFine 版本失败：{ex.Message}", NotificationType.Warning);
            _notificationService.Show("OptiFine 服务器暂时不可用，Forge 仍可正常安装。", NotificationType.Warning);
        }
    }

    [RelayCommand]
    private async Task DownloadAsync(string versionId)
    {
        var manifestVer = _allVersions.FirstOrDefault(v => v.Id == versionId);
        if (manifestVer == null) return;

        var chosen = new VersionEntry { Id = manifestVer.Id, Type = manifestVer.Type, Url = manifestVer.Url };

        string instanceName = InstanceName.Trim();
        if (string.IsNullOrWhiteSpace(instanceName))
        {
            _notificationService.Show("请先输入实例名称。", NotificationType.Warning);
            return;
        }
        if (instanceName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            _notificationService.Show("实例名称包含非法字符。", NotificationType.Warning);
            return;
        }
        string targetDir = Path.Combine(_gameFolder.GameDir, "versions", instanceName);
        if (Directory.Exists(targetDir))
        {
            _notificationService.Show($"实例 {instanceName} 已存在，请更换名称。", NotificationType.Warning);
            return;
        }

        var settings = SettingsProvider?.Invoke() ?? new LauncherSettings();
        bool useIsolatedDir = settings.IsolateNewInstancesByDefault;

        await DownloadVersionAsync(chosen, instanceName, useIsolatedDir);
    }

    // === Download Logic (extracted from MainWindow) ===

    private async Task DownloadVersionAsync(VersionEntry chosen, string instanceName, bool useIsolatedDir)
    {
        string jsonUrl = chosen.Url;
        if (string.IsNullOrEmpty(jsonUrl)) return;

        CancellationTokenSource cts;
        lock (_downloadLock)
        {
            _activeDownloadCts?.Cancel();
            _activeDownloadCts?.Dispose();
            _activeDownloadCts = new CancellationTokenSource();
            cts = _activeDownloadCts;
        }
        var ct = cts.Token;

        // Reset progress state
        _lastFileProgressName = "";
        _lockedFileName = "";

        DownloadProgressOverlayChanged?.Invoke(true);
        ProgressText = "准备下载...";
        IsProgressIndeterminate = true;
        DownloadProgressValue = 0;

        var progress = new Progress<DownloadProgress>(p =>
        {
            if (p.TotalBytes > 0)
            {
                long completed = p.BytesDownloaded;
                long total = p.TotalBytes;
                double percent = (double)completed / total * 100.0;
                DownloadProgressValue = percent;
                IsProgressIndeterminate = false;
                ProgressText = $"文件 {completed}/{total} (剩余 {total - completed})  {percent:F0}%";
            }
            UpdateFileProgressUI(p);
        });

        try
        {
            string? instanceGameDir = useIsolatedDir
                ? Path.Combine(_gameFolder.VersionsDir, instanceName)
                : null;
            if (instanceGameDir != null)
                Directory.CreateDirectory(instanceGameDir);

            // Phase 1: download vanilla
            await _downloadService.DownloadVersionFilesAsync(_httpClient, _gameFolder.GameDir,
                jsonUrl, chosen.Id, instanceName, progress, _notificationService.Show, ct);

            // Write instance metadata
            string versionDir = Path.Combine(_gameFolder.GameDir, "versions", instanceName);
            var meta = new InstanceMeta
            {
                UseIsolatedDir = useIsolatedDir,
                InstanceDir = instanceGameDir
            };
            File.WriteAllText(Path.Combine(versionDir, "instance.json"),
                JsonSerializer.Serialize(meta));

            ct.ThrowIfCancellationRequested();

            // Phase 2: install mod loader
            if (!string.IsNullOrEmpty(SelectedLoaderType) && SelectedLoaderType != "Vanilla")
            {
                // Mirror vanilla JSON + JAR to canonical {versionId} directory
                // so mod loaders that use inheritsFrom can resolve the parent version.
                if (instanceName != chosen.Id)
                {
                    string canonicalDir = Path.Combine(_gameFolder.GameDir, "versions", chosen.Id);
                    Directory.CreateDirectory(canonicalDir);
                    string sourceDir = Path.Combine(_gameFolder.GameDir, "versions", instanceName);
                    string sourceJson = Path.Combine(sourceDir, "version.json");
                    string sourceJar = Path.Combine(sourceDir, $"{chosen.Id}.jar");
                    if (File.Exists(sourceJson))
                    {
                        File.Copy(sourceJson, Path.Combine(canonicalDir, "version.json"), overwrite: true);
                        File.Copy(sourceJson, Path.Combine(canonicalDir, $"{chosen.Id}.json"), overwrite: true);
                    }
                    if (File.Exists(sourceJar) && !File.Exists(Path.Combine(canonicalDir, $"{chosen.Id}.jar")))
                        File.Copy(sourceJar, Path.Combine(canonicalDir, $"{chosen.Id}.jar"));
                }

                string? loaderVersion = SelectedLoaderVersion?.LoaderVersion;
                if (!string.IsNullOrEmpty(loaderVersion))
                {
                    ProgressText = $"正在安装 {SelectedLoaderType} {loaderVersion}...";
                    IsProgressIndeterminate = true;
                    _lastFileProgressName = "";
                    _lockedFileName = "";

                    var loaderProgress = new Progress<DownloadProgress>(p =>
                    {
                        long t = p.TotalBytes;
                        if (t > 1)
                        {
                            long c = p.BytesDownloaded;
                            double pct = (double)c / t * 100.0;
                            IsProgressIndeterminate = false;
                            DownloadProgressValue = pct;
                            ProgressText = $"{SelectedLoaderType}: {c}/{t} 文件  {pct:F0}%";
                        }
                        else if (t == 1)
                        {
                            IsProgressIndeterminate = true;
                            ProgressText = $"{SelectedLoaderType}: {p.BytesDownloaded}/{t}";
                        }
                        UpdateFileProgressUI(p);
                    });

                    var settings = SettingsProvider?.Invoke() ?? new LauncherSettings();
                    string? javaPath = null;
                    if (SelectedLoaderType is "Forge" or "NeoForge" or "OptiFine")
                    {
                        javaPath = settings.JavaPath;
                        if (string.IsNullOrEmpty(javaPath))
                        {
                            _notificationService.Show($"{SelectedLoaderType} 安装需要 Java 环境，请先在设置中配置 Java 路径。", NotificationType.Error);
                            return;
                        }
                    }

                    switch (SelectedLoaderType)
                    {
                        case "Fabric":
                            await _modLoaderService.InstallFabricAsync(_httpClient, _gameFolder.GameDir,
                                chosen.Id, loaderVersion, instanceName, loaderProgress, _notificationService.Show, ct);
                            break;
                        case "Quilt":
                            await _modLoaderService.InstallQuiltAsync(_httpClient, _gameFolder.GameDir,
                                chosen.Id, loaderVersion, instanceName, loaderProgress, _notificationService.Show, ct);
                            break;
                        case "OptiFine":
                            await _modLoaderService.InstallOptiFineAsync(_httpClient, _gameFolder.GameDir,
                                chosen.Id, loaderVersion, instanceName, javaPath!, loaderProgress, _notificationService.Show, ct);
                            break;
                        case "Forge":
                            await _modLoaderService.InstallForgeAsync(_httpClient, _gameFolder.GameDir,
                                chosen.Id, loaderVersion, instanceName, javaPath!, loaderProgress, _notificationService.Show, ct);

                            // OptiFine coexistence
                            if (WithOptiFine && SelectedOptiFineVersion != null)
                            {
                                await DownloadOptiFineCoexistAsync(SelectedOptiFineVersion.LoaderVersion, ct);
                            }
                            break;
                        case "NeoForge":
                            await _modLoaderService.InstallNeoForgeAsync(_httpClient, _gameFolder.GameDir,
                                chosen.Id, loaderVersion, instanceName, javaPath!, loaderProgress, _notificationService.Show, ct);
                            break;
                    }
                }
            }

            _notificationService.Show($"版本 {chosen.Id} (实例: {instanceName}) 下载完成。", NotificationType.Success);
        }
        catch (OperationCanceledException)
        {
            _notificationService.Show("下载已取消。", NotificationType.Info);
        }
        catch (Exception ex)
        {
            _notificationService.WriteDiagnosticLog("下载失败", ex.ToString(), autoOpen: true);
            _notificationService.Show($"下载失败：{ex.Message}", NotificationType.Error);
        }
        finally
        {
            DownloadProgressOverlayChanged?.Invoke(false);
            _instanceManager.Refresh(_notificationService.AppendLog);
        }
    }

    private async Task DownloadOptiFineCoexistAsync(string versionName, CancellationToken ct = default)
    {
        ProgressText = "正在下载 OptiFine (Forge 共存)...";
        IsProgressIndeterminate = true;
        FileProgressUpdated?.Invoke(new DownloadProgress
        {
            CurrentFileName = "",
            CurrentFileDownloadedBytes = 0,
            CurrentFileTotalBytes = 0
        });

        string ofFileName = $"{versionName}.jar";
        string installerCacheDir = Path.Combine(_gameFolder.GameDir, "installer_cache");
        Directory.CreateDirectory(installerCacheDir);
        string ofJarPath = Path.Combine(installerCacheDir, $"optifine_coexist_{ofFileName}");

        try
        {
            // Step 1: GET adloadx page
            string adloadUrl = Endpoints.OptiFineAdload(ofFileName);
            string adloadHtml;
            using (var adReq = new HttpRequestMessage(HttpMethod.Get, adloadUrl))
            {
                adReq.Headers.Referrer = new Uri(Endpoints.OptiFineDownloadsPage);
                using var adResp = await _httpClient.SendAsync(adReq, ct);
                adResp.EnsureSuccessStatusCode();
                adloadHtml = await adResp.Content.ReadAsStringAsync();
            }

            // Step 2: Extract downloadx URL and download
            var dlxMatch = System.Text.RegularExpressions.Regex.Match(
                adloadHtml, @"href=['""]downloadx\?f=([^'""&]+)&x=([a-f0-9]+)['""]");
            if (!dlxMatch.Success)
                throw new Exception("未找到 OptiFine downloadx 链接。");

            string dlxUrl = Endpoints.OptiFineDownloadX(dlxMatch.Groups[1].Value, dlxMatch.Groups[2].Value);
            using var dlReq = new HttpRequestMessage(HttpMethod.Get, dlxUrl);
            dlReq.Headers.Referrer = new Uri(adloadUrl);
            using var dlResp = await _httpClient.SendAsync(dlReq, HttpCompletionOption.ResponseHeadersRead, ct);
            dlResp.EnsureSuccessStatusCode();

            long ofTotalBytes = dlResp.Content.Headers.ContentLength ?? -1;
            string tmpFile = ofJarPath + ".part";
            var sw = Stopwatch.StartNew();
            long ofDownloaded = 0;
            long ofLastReported = 0;
            double ofLastReportTime = 0;

            await using var contentStream = await dlResp.Content.ReadAsStreamAsync(ct);
            await using var ofs = new FileStream(tmpFile, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true);
            byte[] buf = new byte[8192];
            int read;
            while ((read = await contentStream.ReadAsync(buf, 0, buf.Length, ct)) > 0)
            {
                await ofs.WriteAsync(buf, 0, read, ct);
                ofDownloaded += read;
                if (ofDownloaded - ofLastReported >= 65536 || ofDownloaded == ofTotalBytes)
                {
                    double now = sw.Elapsed.TotalSeconds;
                    double deltaTime = now - ofLastReportTime;
                    double speed = deltaTime > 0.05 ? (ofDownloaded - ofLastReported) / deltaTime : 0;
                    FileProgressUpdated?.Invoke(DownloadProgress.FileProgress(
                        ofFileName, ofDownloaded, ofTotalBytes, speed));
                    ofLastReported = ofDownloaded;
                    ofLastReportTime = now;
                }
            }
            File.Move(tmpFile, ofJarPath, overwrite: true);
        }
        catch (Exception ex)
        {
            _notificationService.Show($"OptiFine 共存安装失败：{ex.Message}", NotificationType.Warning);
        }

        // Copy OptiFine JAR to mods/
        if (File.Exists(ofJarPath))
        {
            string modsDir = Path.Combine(_gameFolder.GameDir, "mods");
            Directory.CreateDirectory(modsDir);
            string destOfJar = Path.Combine(modsDir, ofFileName);
            if (!File.Exists(destOfJar))
                File.Copy(ofJarPath, destOfJar);
        }
    }

    // === Version Manifest Cache ===

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

    // === File Progress UI Logic ===

    private void UpdateFileProgressUI(DownloadProgress p)
    {
        if (string.IsNullOrEmpty(p.CurrentFileName) || p.CurrentFileTotalBytes == 0)
            return;

        long now = Environment.TickCount64;

        if (_lockedFileName.Length > 0 && p.CurrentFileName != _lockedFileName)
        {
            if (_lockedFileTotalBytes > 0)
            {
                if (now - _lockedFileLastProgressTick < LockTimeoutMs)
                    return;
                _lockedFileName = "";
                _lockedFileTotalBytes = 0;
            }
            else
            {
                _lockedFileName = "";
            }
        }

        bool sameFile = p.CurrentFileName == _lastFileProgressName;
        if (sameFile && now - _lastFileProgressTick < 180)
            return;

        _lastFileProgressName = p.CurrentFileName;
        _lastFileProgressTick = now;

        if (_lockedFileName.Length == 0)
        {
            _lockedFileName = p.CurrentFileName;
            _lockedFileTotalBytes = p.CurrentFileTotalBytes;
        }

        if (p.CurrentFileName == _lockedFileName)
        {
            _lockedFileLastProgressTick = now;
            if (p.CurrentFileDownloadedBytes >= _lockedFileTotalBytes)
            {
                _lockedFileName = "";
                _lockedFileTotalBytes = 0;
            }
        }

        if (p.CurrentFileTotalBytes > 0)
        {
            IsFileProgressIndeterminate = false;
            FileProgressValue = (double)p.CurrentFileDownloadedBytes / p.CurrentFileTotalBytes * 100.0;
        }
        else { IsFileProgressIndeterminate = true; }

        CurrentFileProgress = FormatFileProgressLine(
            p.CurrentFileName, p.CurrentFileDownloadedBytes,
            p.CurrentFileTotalBytes, p.DownloadSpeedBytesPerSecond);

        FileProgressUpdated?.Invoke(p);
    }

    private static string FormatFileProgressLine(string fileName, long downloaded, long total, double speedBps)
    {
        string sizePart = total > 0
            ? $"{FormatByteSize(downloaded)} / {FormatByteSize(total)}"
            : FormatByteSize(downloaded);
        string speedPart = speedBps > 0
            ? $"  ·  {FormatByteSize((long)speedBps)}/s"
            : "";
        return $"{fileName}  {sizePart}{speedPart}";
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

