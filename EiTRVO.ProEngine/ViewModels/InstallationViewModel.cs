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

public partial class InstallationViewModel : BaseViewModel
{
    private readonly IDownloadService _downloadService;
    private readonly IModLoaderService _modLoaderService;
    private readonly INotificationService _notificationService;
    private readonly IGameFolderService _gameFolder;
    private readonly InstanceManager _instanceManager;
    private readonly HttpClient _httpClient;
    private readonly JavaDetectionService _javaDetection;

    private CancellationTokenSource? _activeDownloadCts;
    private readonly object _downloadLock = new();

    // Prevent race conditions when rapidly switching loaders
    private CancellationTokenSource? _loaderVersionsCts;

    // === Observable Properties ===

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InstanceName))]
    private string _versionId = "";

    [ObservableProperty]
    private string _versionType = "";

    [ObservableProperty]
    private string _versionUrl = "";

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
    private string _versionDisplay = "";

    [ObservableProperty]
    private string _loaderDisplay = "";

    // === Mod loader selection ===

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModLoaderVersionVisible))]
    [NotifyPropertyChangedFor(nameof(IsOptiFineForForgeVisible))]
    [NotifyPropertyChangedFor(nameof(IsOptiFineDropdownVisible))]
    [NotifyPropertyChangedFor(nameof(IsFabricApiVisible))]
    [NotifyPropertyChangedFor(nameof(IsFabricApiVersionVisible))]
    private string? _selectedLoaderType;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModLoaderVersionVisible))]
    private ObservableCollection<ModLoaderVersion> _loaderVersions = new();

    [ObservableProperty]
    private ModLoaderVersion? _selectedLoaderVersion;

    [ObservableProperty]
    private string _modLoaderHint = "";

    // === OptiFine coexistence with Forge ===

    // === Fabric API coexistence ===

    [ObservableProperty]
    private bool _withFabricApi;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFabricApiVersionVisible))]
    private ObservableCollection<FabricApiVersionOption> _fabricApiVersions = new();

    [ObservableProperty]
    private FabricApiVersionOption? _selectedFabricApiVersion;

    /// <summary>Fabric API 版本选择器可见 = 勾选了复选框 + 选中 Fabric + 版本列表非空。</summary>
    public bool IsFabricApiVersionVisible => WithFabricApi && SelectedLoaderType == "Fabric" && FabricApiVersions.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOptiFineForForgeVisible))]
    [NotifyPropertyChangedFor(nameof(IsOptiFineDropdownVisible))]
    private bool _withOptiFine;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOptiFineDropdownVisible))]
    private ObservableCollection<ModLoaderVersion> _optiFineForForgeVersions = new();

    [ObservableProperty]
    private ModLoaderVersion? _selectedOptiFineVersion;

    // === Loader card selection ===

    public ObservableCollection<LoaderCardOption> LoaderOptions { get; } = new()
    {
        new("原版 (Vanilla)", "", "#888888"),
        new("Fabric", "Fabric", "#E6B422"),
        new("Forge", "Forge", "#E67E22"),
        new("OptiFine", "OptiFine", "#C9A02E"),
        new("Quilt", "Quilt", "#3498DB"),
        new("NeoForge", "NeoForge", "#E74C3C"),
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModLoaderVersionVisible))]
    [NotifyPropertyChangedFor(nameof(IsOptiFineForForgeVisible))]
    [NotifyPropertyChangedFor(nameof(IsOptiFineDropdownVisible))]
    private LoaderCardOption? _selectedLoaderOption;

    // === Computed ===
    public bool IsModLoaderVersionVisible => LoaderVersions.Count > 0;
    public bool IsOptiFineForForgeVisible => SelectedLoaderType == "Forge";
    public bool IsOptiFineDropdownVisible => WithOptiFine && SelectedLoaderType == "Forge" && OptiFineForForgeVersions.Count > 0;
    public bool IsFabricApiVisible => SelectedLoaderType == "Fabric";

    // === Events for View interaction ===
    public event Action? BackRequested;
    public event Action<bool>? DownloadProgressChanged;
    public Func<LauncherSettings>? SettingsProvider;

    /// <summary>Java 兼容性检查（下载流程）— 由 MainWindow 注入。</summary>
    public Func<string, string, Task<string?>>? DownloadJavaCompatibilityHandler { get; set; }

    /// <summary>Java 自动检测回调 — 由 MainWindow 注入，将自动检测到的 Java 保存到设置。</summary>
    public Func<JavaInfo, Task>? JavaDetectedCallback { get; set; }

    // Progress UI state
    private string _lastFileProgressName = "";
    private long _lastFileProgressTick;
    private string _lockedFileName = "";
    private long _lockedFileTotalBytes;
    private long _lockedFileLastProgressTick;
    private const long LockTimeoutMs = 5000;

    public InstallationViewModel(
        IDownloadService downloadService,
        IModLoaderService modLoaderService,
        INotificationService notificationService,
        IGameFolderService gameFolder,
        InstanceManager instanceManager,
        HttpClient httpClient,
        JavaDetectionService javaDetection)
    {
        _downloadService = downloadService;
        _modLoaderService = modLoaderService;
        _notificationService = notificationService;
        _gameFolder = gameFolder;
        _instanceManager = instanceManager;
        _httpClient = httpClient;
        _javaDetection = javaDetection;
    }

    /// <summary>由 MainWindow 在导航到安装页面前调用，设置目标版本并重置状态。</summary>
    public void Initialize(VersionEntry entry)
    {
        VersionId = entry.Id;
        VersionType = entry.Type;
        VersionUrl = entry.Url;
        // Reset all state
        InstanceName = "";
        SelectedLoaderType = null;
        SelectedLoaderVersion = null;
        LoaderVersions = new();
        ModLoaderHint = "";
        WithOptiFine = false;
        OptiFineForForgeVersions = new();
        SelectedOptiFineVersion = null;
        WithFabricApi = false;
        FabricApiVersions = new();
        SelectedFabricApiVersion = null;
        SelectedLoaderOption = LoaderOptions[0]; // Default to Vanilla
        IsDownloading = false;
    }

    // === Auto-triggers ===

    partial void OnSelectedLoaderOptionChanged(LoaderCardOption? value)
    {
        SelectedLoaderType = value?.Tag;
    }

    // When SelectedLoaderType changes (from card click or programmatic), load versions + update name
    partial void OnSelectedLoaderTypeChanged(string? value)
    {
        // Cancel previous loader version fetch to prevent race conditions
        _loaderVersionsCts?.Cancel();
        _loaderVersionsCts?.Dispose();
        _loaderVersionsCts = new CancellationTokenSource();
        var ct = _loaderVersionsCts.Token;

        _ = LoadModLoaderVersionsAsync(ct);
        GenerateInstanceName();
    }

    partial void OnVersionIdChanged(string value)
    {
        GenerateInstanceName();
    }

    partial void OnWithOptiFineChanged(bool value)
    {
        _ = LoadOptiFineForForgeAsync();
    }

    partial void OnWithFabricApiChanged(bool value)
    {
        _ = LoadFabricApiVersionsAsync();
    }

    // === Commands ===

    [RelayCommand]
    private void GoBack()
        => BackRequested?.Invoke();

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
    private async Task InstallAsync()
    {
        if (string.IsNullOrEmpty(VersionUrl)) return;

        // 安全消毒：统一使用 PathSafetyHelper，与其他导入路径一致
        string instanceName = PathSafetyHelper.SanitizeNameComponent(InstanceName);
        if (instanceName == "unnamed" || string.IsNullOrWhiteSpace(instanceName))
        {
            _notificationService.Show("请先输入实例名称。", NotificationType.Warning);
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

        var chosen = new VersionEntry { Id = VersionId, Type = VersionType, Url = VersionUrl };
        await DownloadVersionAsync(chosen, instanceName, useIsolatedDir);
    }

    private async Task LoadModLoaderVersionsAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(SelectedLoaderType) || SelectedLoaderType == "Vanilla")
        {
            LoaderVersions = new();
            ModLoaderHint = "";
            WithOptiFine = false;
            return;
        }

        var tag = SelectedLoaderType;
        string mcVersion = VersionId;
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

            ct.ThrowIfCancellationRequested();

            LoaderVersions = new ObservableCollection<ModLoaderVersion>(versions);

            if (LoaderVersions.Count == 0)
            {
                ModLoaderHint = $"该版本暂无可用 {tag} 加载器。";
            }
            else
            {
                SelectedLoaderVersion = LoaderVersions[0];
                ModLoaderHint = "";
            }
        }
        catch (OperationCanceledException)
        {
            // Silently cancelled — user switched loader, the new fetch is already in flight
        }
        catch (Exception ex)
        {
            ModLoaderHint = $"获取版本失败：{ex.Message}";
            LoaderVersions = new();
            _notificationService.AppendLog($"获取 {tag} 版本列表失败：{ex.Message}", NotificationType.Error);
        }
    }

    private async Task LoadOptiFineForForgeAsync()
    {
        if (!WithOptiFine || SelectedLoaderType != "Forge")
        {
            OptiFineForForgeVersions = new();
            return;
        }

        string mcVersion = VersionId;
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

    // === Instance name auto-generation ===

    private void GenerateInstanceName()
    {
        if (string.IsNullOrEmpty(VersionId)) return;

        string prefix = string.IsNullOrEmpty(SelectedLoaderType) || SelectedLoaderType == "Vanilla"
            ? VersionId
            : $"{SelectedLoaderType}-{VersionId}";

        string versionsDir = Path.Combine(_gameFolder.GameDir, "versions");
        int n = 1;
        string candidate;
        do
        {
            candidate = $"{prefix}_{n}";
            n++;
        } while (Directory.Exists(Path.Combine(versionsDir, candidate)));

        InstanceName = candidate;
    }

    // === Download Logic ===

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

        // Set display info for progress page
        VersionDisplay = $"{chosen.Type} {chosen.Id}";
        LoaderDisplay = !string.IsNullOrEmpty(SelectedLoaderType) && SelectedLoaderType != "Vanilla" && SelectedLoaderVersion != null
            ? $"{SelectedLoaderType} {SelectedLoaderVersion.LoaderVersion}"
            : "";

        DownloadProgressChanged?.Invoke(true);
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
                            // 按需自动检测 + 版本感知选择（传入 MC 版本以匹配正确的 Java 版本）
                            var autoDetected = await _javaDetection.ResolveJavaPathAsync(settings, chosen.Id);
                            if (autoDetected != null)
                            {
                                javaPath = autoDetected;
                                _notificationService.Show($"已自动检测到 Java 运行环境：{autoDetected}", NotificationType.Success);
                                if (JavaDetectedCallback != null)
                                {
                                    var javas = await _javaDetection.DetectAsync();
                                    var detectedInfo = javas.FirstOrDefault(j => j.Path == autoDetected);
                                    if (detectedInfo != null) await JavaDetectedCallback(detectedInfo);
                                }
                            }
                            else
                            {
                                _notificationService.Show(
                                    $"{SelectedLoaderType} 安装需要 Java 环境。未在 PATH 或 JAVA_HOME 中检测到 Java，请安装 Java 或前往设置手动配置。",
                                    NotificationType.Error);
                                return;
                            }
                        }

                        // Java 兼容性检查（安全网：处理检测到但版本不兼容的情况）
                        if (DownloadJavaCompatibilityHandler != null)
                        {
                            var resolved = await DownloadJavaCompatibilityHandler(javaPath, chosen.Id);
                            if (resolved == null) return;
                            javaPath = resolved;
                        }
                    }

                    switch (SelectedLoaderType)
                    {
                        case "Fabric":
                            await _modLoaderService.InstallFabricAsync(_httpClient, _gameFolder.GameDir,
                                chosen.Id, loaderVersion, instanceName, loaderProgress, _notificationService.Show, ct);

                            // Fabric API coexistence
                            if (WithFabricApi)
                            {
                                await DownloadFabricApiAsync(chosen.Id, useIsolatedDir, instanceName, ct);
                            }
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
            DownloadProgressChanged?.Invoke(false);
            _instanceManager.Refresh(_notificationService.AppendLog);
        }
    }

    private async Task DownloadOptiFineCoexistAsync(string versionName, CancellationToken ct = default)
    {
        ProgressText = "正在下载 OptiFine (Forge 共存)...";
        IsProgressIndeterminate = true;
        CurrentFileProgress = "";
        IsFileProgressIndeterminate = true;
        FileProgressValue = 0;

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
            if (!DownloadSafetyHelper.IsDownloadUrlAllowed(dlxUrl))
                throw new InvalidOperationException(
                    $"OptiFine 下载 URL 不在白名单中，已拒绝：{dlxUrl}");

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
                    CurrentFileProgress = FormatFileProgressLine(ofFileName, ofDownloaded, ofTotalBytes, speed);
                    if (ofTotalBytes > 0)
                    {
                        FileProgressValue = (double)ofDownloaded / ofTotalBytes * 100.0;
                        IsFileProgressIndeterminate = false;
                    }
                    else { IsFileProgressIndeterminate = true; }
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

    // === Fabric API Version Loading ===

    private async Task LoadFabricApiVersionsAsync()
    {
        if (!WithFabricApi || SelectedLoaderType != "Fabric")
        {
            FabricApiVersions = new();
            return;
        }

        string mcVersion = VersionId;
        try
        {
            string metadataUrl = "https://maven.fabricmc.net/net/fabricmc/fabric-api/fabric-api/maven-metadata.xml";
            string xml;
            using (var resp = await _httpClient.GetAsync(metadataUrl))
            {
                resp.EnsureSuccessStatusCode();
                xml = await resp.Content.ReadAsStringAsync();
            }

            // Extract all <version> entries
            var matches = System.Text.RegularExpressions.Regex.Matches(
                xml, @"<version>([^<]+)</version>");
            var filtered = new List<FabricApiVersionOption>();
            string suffix = $"+{mcVersion}";
            foreach (System.Text.RegularExpressions.Match m in matches)
            {
                string ver = m.Groups[1].Value;
                if (ver.EndsWith(suffix))
                    filtered.Add(new FabricApiVersionOption(ver));
            }

            // Maven XML lists versions oldest-first; reverse so newest appears at top
            // and SelectedFabricApiVersion[0] picks the latest release.
            filtered.Reverse();
            FabricApiVersions = new ObservableCollection<FabricApiVersionOption>(filtered);

            if (FabricApiVersions.Count == 0)
            {
                _notificationService.Show(
                    $"MC {mcVersion} 暂无可用 Fabric API 版本。", NotificationType.Warning);
            }
            else
            {
                SelectedFabricApiVersion = FabricApiVersions[0];
            }
        }
        catch (Exception ex)
        {
            FabricApiVersions = new();
            _notificationService.AppendLog(
                $"获取 Fabric API 版本失败：{ex.Message}", NotificationType.Warning);
        }
    }

    // === Fabric API Download ===

    private async Task DownloadFabricApiAsync(string mcVersion, bool useIsolatedDir, string instanceName, CancellationToken ct)
    {
        var apiVersion = SelectedFabricApiVersion?.DisplayText;
        if (string.IsNullOrEmpty(apiVersion)) return;

        ProgressText = "正在下载 Fabric API...";
        IsProgressIndeterminate = true;

        string jarFileName = $"fabric-api-{apiVersion}.jar";
        string jarUrl = $"https://maven.fabricmc.net/net/fabricmc/fabric-api/fabric-api/{apiVersion}/{jarFileName}";

        string modsDir = useIsolatedDir
            ? Path.Combine(_gameFolder.VersionsDir, instanceName, "mods")
            : Path.Combine(_gameFolder.GameDir, "mods");
        Directory.CreateDirectory(modsDir);
        string destPath = Path.Combine(modsDir, jarFileName);

        // Skip if already exists
        if (File.Exists(destPath))
        {
            _notificationService.Show("Fabric API 已存在，跳过下载。", NotificationType.Info);
            return;
        }

        // Download safety check
        if (!DownloadSafetyHelper.IsDownloadUrlAllowed(jarUrl))
        {
            _notificationService.Show("Fabric API 下载地址校验失败。", NotificationType.Warning);
            return;
        }

        // Clean up orphaned .part files from previous runs (best-effort)
        foreach (string oldPart in Directory.EnumerateFiles(modsDir, $"{jarFileName}.*.part"))
        {
            try { File.Delete(oldPart); } catch { /* best effort */ }
        }

        const int maxRetries = 2;
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            string tmpFile = destPath + ".part";
            try
            {
                // --- HTTP download ---
                using var dlResp = await _httpClient.GetAsync(jarUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                dlResp.EnsureSuccessStatusCode();

                long totalBytes = dlResp.Content.Headers.ContentLength ?? -1;
                long downloaded = 0;
                long lastReported = 0;
                var sw = Stopwatch.StartNew();
                double lastReportTime = 0;

                await using var contentStream = await dlResp.Content.ReadAsStreamAsync(ct);

                // Try exclusive .part first; fall back to GUID-based name if locked (aligns with ModrinthService)
                FileStream destStream;
                try
                {
                    destStream = new FileStream(tmpFile, FileMode.CreateNew,
                        FileAccess.Write, FileShare.Read, 65536, useAsync: true);
                }
                catch (IOException)
                {
                    tmpFile = Path.Combine(modsDir,
                        $"{Path.GetFileNameWithoutExtension(destPath)}_{Guid.NewGuid():N}.part");
                    destStream = new FileStream(tmpFile, FileMode.CreateNew,
                        FileAccess.Write, FileShare.Read, 65536, useAsync: true);
                }

                await using (destStream)
                {
                    byte[] buf = new byte[8192];
                    int read;
                    while ((read = await contentStream.ReadAsync(buf, 0, buf.Length, ct)) > 0)
                    {
                        await destStream.WriteAsync(buf, 0, read, ct);
                        downloaded += read;
                        if (downloaded - lastReported >= 65536 || downloaded == totalBytes)
                        {
                            double now = sw.Elapsed.TotalSeconds;
                            double deltaTime = now - lastReportTime;
                            double speed = deltaTime > 0.05 ? (downloaded - lastReported) / deltaTime : 0;
                            CurrentFileProgress = FormatFileProgressLine(jarFileName, downloaded, totalBytes, speed);
                            if (totalBytes > 0)
                            {
                                FileProgressValue = (double)downloaded / totalBytes * 100.0;
                                IsFileProgressIndeterminate = false;
                            }
                            else { IsFileProgressIndeterminate = true; }
                            lastReported = downloaded;
                            lastReportTime = now;
                        }
                    }
                }
                // destStream disposed here — safe to move

                File.Move(tmpFile, destPath, overwrite: true);
                _notificationService.Show("Fabric API 下载完成。", NotificationType.Success);
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) when (attempt < maxRetries)
            {
                try { File.Delete(tmpFile); } catch { /* best effort */ }
                try { await Task.Delay(800 * (1 << attempt), ct); }
                catch (OperationCanceledException) { throw; }
            }
            catch (Exception ex)
            {
                try { File.Delete(tmpFile); } catch { /* best effort */ }
                _notificationService.Show($"Fabric API 下载失败：{ex.Message}", NotificationType.Warning);
                return;
            }
        }
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

/// <summary>加载器卡片选项（用于 InstallationPanel 的水平卡片选择）。</summary>
public record LoaderCardOption(string Name, string Tag, string ColorHex)
{
    public override string ToString() => Name;
}

public record FabricApiVersionOption(string DisplayText)
{
    public override string ToString() => DisplayText;
}
