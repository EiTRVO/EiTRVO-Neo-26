using System;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EiTRVO.ProEngine.Helpers;
using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Orchestrators;
using EiTRVO.ProEngine.Services;

namespace EiTRVO.ProEngine.ViewModels;

public partial class ManageViewModel : BaseViewModel
{
    private readonly InstanceManager _instanceManager;
    private readonly IPackService _packService;
    private readonly INotificationService _notificationService;
    private readonly IGameFolderService _gameFolder;
    private readonly IDownloadService _downloadService;
    private readonly IModLoaderService _modLoaderService;
    private readonly System.Net.Http.HttpClient _httpClient;
    private readonly IDialogService _dialogService;
    private readonly IProcessService _processService;
    private readonly IMrpackInstallService _mrpackInstall;
    private readonly JavaDetectionService _javaDetection;

    private CancellationTokenSource? _activeDownloadCts;
    private readonly object _downloadLock = new();

    public ObservableCollection<GameInstance> Instances => _instanceManager.Instances;

    // === Progress properties (for import/pack operations) ===

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

    /// <summary>Raised when the View should show/hide the download progress overlay.</summary>
    public event Action<bool>? DownloadProgressChanged;

    /// <summary>Raised when an import/export needs settings (Java path, isolation default).</summary>
    public Func<LauncherSettings>? SettingsProvider;

    /// <summary>Java 兼容性检查（下载流程）— 由 MainWindow 注入。</summary>
    public Func<string, string, Task<string?>>? DownloadJavaCompatibilityHandler { get; set; }

    /// <summary>Java 自动检测回调 — 由 MainWindow 注入，将自动检测到的 Java 保存到设置。</summary>
    public Func<JavaInfo, Task>? JavaDetectedCallback { get; set; }

    /// <summary>Raised when the user clicks "管理" on an instance card. Parameter is the instance name.</summary>
    public event Action<string>? NavigateToInstanceDetail;

    public ManageViewModel(
        InstanceManager instanceManager,
        IPackService packService,
        INotificationService notificationService,
        IGameFolderService gameFolder,
        IDownloadService downloadService,
        IModLoaderService modLoaderService,
        System.Net.Http.HttpClient httpClient,
        IDialogService dialogService,
        IProcessService processService,
        IMrpackInstallService mrpackInstall,
        JavaDetectionService javaDetection)
    {
        _instanceManager = instanceManager;
        _packService = packService;
        _notificationService = notificationService;
        _gameFolder = gameFolder;
        _downloadService = downloadService;
        _modLoaderService = modLoaderService;
        _httpClient = httpClient;
        _dialogService = dialogService;
        _processService = processService;
        _mrpackInstall = mrpackInstall;
        _javaDetection = javaDetection;
    }

    [RelayCommand]
    private void Refresh()
    {
        _instanceManager.Refresh(_notificationService.AppendLog);
        _notificationService.Show("实例列表已刷新。", NotificationType.Info);
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
    private async Task Delete(string instanceName)
    {
        var instance = _instanceManager.FindByName(instanceName);
        if (instance == null) return;

        if (!await _dialogService.ShowConfirmAsync(
            $"确定要删除实例 {instance.Name}({instance.VersionId}) 吗？\n该操作将删除该实例的所有文件且不可恢复。",
            "确认删除"))
            return;

        string instancePath = Path.Combine(_gameFolder.GameDir, "versions", instance.Name);
        try
        {
            if (Directory.Exists(instancePath))
                Directory.Delete(instancePath, true);
            _notificationService.Show($"实例 {instance.Name} 已删除。", NotificationType.Success);
        }
        catch (Exception ex)
        {
            _notificationService.WriteDiagnosticLog("删除实例失败", ex.ToString());
            _notificationService.Show($"删除失败：{ex.Message}", NotificationType.Error);
        }
        finally
        {
            _instanceManager.Refresh(_notificationService.AppendLog);
        }
    }

    [RelayCommand]
    private void ManageInstance(string instanceName)
        => NavigateToInstanceDetail?.Invoke(instanceName);

    [RelayCommand]
    private void OpenFolder(string instanceName)
    {
        string instancePath = Path.Combine(_gameFolder.GameDir, "versions", instanceName);
        if (Directory.Exists(instancePath))
        {
            _processService.OpenFolder(instancePath);
        }
        else
        {
            _notificationService.Show($"实例文件夹不存在：{instancePath}", NotificationType.Warning);
        }
    }

    [RelayCommand]
    private async Task PackAsync(string instanceName)
    {
        var instance = _instanceManager.FindByName(instanceName);
        if (instance == null) return;

        if (!instance.UseIsolatedDir || string.IsNullOrEmpty(instance.InstanceDir))
        {
            _notificationService.Show("导出整合包要求实例已启用游戏目录隔离。请在创建新实例时勾选启用隔离，或重建已有实例。", NotificationType.Warning);
            return;
        }

        var savePath = await _dialogService.ShowSaveFileDialogAsync(
            $"{instance.Name}.zip", "ZIP 压缩包|*.zip", "导出整合包");
        if (savePath == null) return;

        DownloadProgressChanged?.Invoke(true);
        var progress = new Progress<DownloadProgress>(p =>
        {
            if (p.TotalBytes > 0)
            {
                double percent = (double)p.BytesDownloaded / p.TotalBytes * 100.0;
                DownloadProgressValue = percent;
                IsProgressIndeterminate = false;
                ProgressText = $"文件 {p.BytesDownloaded}/{p.TotalBytes}  {percent:F0}%";
            }
            if (!string.IsNullOrEmpty(p.CurrentFileName))
            {
                string sizePart = p.CurrentFileTotalBytes > 0
                    ? $"{FormatByteSize(p.CurrentFileDownloadedBytes)} / {FormatByteSize(p.CurrentFileTotalBytes)}"
                    : FormatByteSize(p.CurrentFileDownloadedBytes);
                string speedPart = p.DownloadSpeedBytesPerSecond > 0
                    ? $"  ·  {FormatByteSize((long)p.DownloadSpeedBytesPerSecond)}/s"
                    : "";
                CurrentFileProgress = $"{p.CurrentFileName}  {sizePart}{speedPart}";
                if (p.CurrentFileTotalBytes > 0)
                {
                    FileProgressValue = (double)p.CurrentFileDownloadedBytes / p.CurrentFileTotalBytes * 100.0;
                    IsFileProgressIndeterminate = false;
                }
                else { IsFileProgressIndeterminate = true; }
            }
        });

        try
        {
            await _packService.ExportAsync(instanceName, _gameFolder.GameDir,
                instance.InstanceDir, savePath, instance,
                progress, _notificationService.Show);
            _notificationService.Show($"整合包已导出至 {savePath}", NotificationType.Success);
        }
        catch (Exception ex)
        {
            _notificationService.WriteDiagnosticLog("导出整合包失败", ex.ToString());
            _notificationService.Show($"导出失败：{ex.Message}", NotificationType.Error);
        }
        finally
        {
            DownloadProgressChanged?.Invoke(false);
        }
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var filePath = await _dialogService.ShowOpenFileDialogAsync("选择整合包文件", "整合包文件|*.zip;*.mrpack");
        if (filePath == null) return;
        await ImportPackAsync(filePath);
    }

    /// <summary>Import a modpack from a file path. Supports .zip (eitrvo-pack) and .mrpack (Modrinth) formats.</summary>
    public async Task ImportPackAsync(string packPath)
    {
        // Detect format by extension
        bool isMrpack = packPath.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase);

        if (isMrpack)
        {
            await ImportMrpackAsync(packPath);
            return;
        }

        await ImportZipPackAsync(packPath);
    }

    /// <summary>Import a native .zip modpack (eitrvo-pack format).</summary>
    private async Task ImportZipPackAsync(string packPath)
    {
        CancellationTokenSource cts;
        lock (_downloadLock)
        {
            _activeDownloadCts?.Cancel();
            _activeDownloadCts?.Dispose();
            _activeDownloadCts = new CancellationTokenSource();
            cts = _activeDownloadCts;
        }
        var ct = cts.Token;

        // Phase 1: read manifest
        PackManifest manifest;
        try
        {
            manifest = _packService.ReadManifest(packPath);
        }
        catch (Exception ex)
        {
            _notificationService.Show($"无效的整合包：{ex.Message}", NotificationType.Error);
            return;
        }

        const int currentExporterVersion = 26;
        if (manifest.ExporterVersion != currentExporterVersion)
        {
            if (!await _dialogService.ShowConfirmAsync(
                $"此整合包由版本 {manifest.ExporterVersion} 的启动器导出，与当前版本 ({currentExporterVersion}) 可能不兼容。\n\n是否继续导入？",
                "版本兼容性警告"))
                return;
        }

        string instanceName = PathSafetyHelper.SanitizeNameComponent(
            manifest.Name ?? Path.GetFileNameWithoutExtension(packPath));
        if (string.IsNullOrWhiteSpace(instanceName)) instanceName = "ImportedPack";

        string targetDir = Path.Combine(_gameFolder.GameDir, "versions", instanceName);
        if (Directory.Exists(targetDir))
        {
            if (!await _dialogService.ShowConfirmAsync(
                $"实例 {instanceName} 已存在。是否覆盖并重新导入？",
                "确认覆盖"))
                return;
            try { Directory.Delete(targetDir, true); }
            catch (Exception ex)
            {
                _notificationService.Show($"无法删除已有实例：{ex.Message}", NotificationType.Error);
                return;
            }
        }

        var versionDetail = _packService.PeekVersionJson(packPath);
        string mcVersion = versionDetail?.InheritsFrom ?? manifest.InheritsFrom ?? manifest.Minecraft?.Version ?? "";
        string? inheritsFrom = versionDetail?.InheritsFrom ?? manifest.InheritsFrom;

        // === 安全扫描：有害整合包检测 ===
        var safetyReport = ModpackSafetyScanner.ScanZipPack(packPath);
        if (safetyReport.HasBlockingIssues)
        {
            var msg = "此整合包未通过安全检查，已拒绝导入：\n" +
                      string.Join("\n", safetyReport.BlockingIssues.Take(10));
            _notificationService.Show(msg, NotificationType.Error);
            return;
        }
        if (safetyReport.HasWarnings)
        {
            var warnMsg = "此整合包存在以下安全风险：\n" +
                          string.Join("\n", safetyReport.Warnings.Take(10)) +
                          "\n\n是否继续导入？";
            if (!await _dialogService.ShowConfirmAsync(warnMsg, "安全警告"))
                return;
        }

        // 安全校验：version.json 深度扫描（mainClass + JVM 参数）
        ModpackSafetyScanner.ScanVersionJson(versionDetail, safetyReport);
        if (safetyReport.HasBlockingIssues)
        {
            // Re-check after version.json scan
            var msg = "此整合包未通过安全检查，已拒绝导入：\n" +
                      string.Join("\n", safetyReport.BlockingIssues.Take(10));
            _notificationService.Show(msg, NotificationType.Error);
            return;
        }

        // 安全校验：mainClass 分层检查
        string? mainClass = versionDetail?.MainClass;
        if (JvmArgHelper.IsMainClassBlocked(mainClass))
        {
            _notificationService.Show(
                $"此整合包的 mainClass（{mainClass}）为 JRE 内部类，可能存在恶意代码执行风险，已拒绝导入。",
                NotificationType.Error);
            return;
        }
        if (!JvmArgHelper.IsMainClassSafe(mainClass))
        {
            string warningMsg = string.IsNullOrEmpty(mainClass)
                ? "此整合包的 version.json 未指定 mainClass，可能导致游戏无法启动。\n\n是否继续导入？"
                : $"此整合包的 mainClass（{mainClass}）非已知安全值，可能存在恶意代码执行风险。\n\n是否继续导入？";
            if (!await _dialogService.ShowConfirmAsync(warningMsg, "安全警告"))
                return;
        }

        VersionDisplay = mcVersion;
        LoaderDisplay = !string.IsNullOrEmpty(manifest.Minecraft?.ModLoader) && !string.IsNullOrEmpty(manifest.Minecraft?.ModLoaderVersion)
            ? $"{manifest.Minecraft.ModLoader} {manifest.Minecraft.ModLoaderVersion}"
            : "";

        DownloadProgressChanged?.Invoke(true);
        ProgressText = "准备导入...";
        IsProgressIndeterminate = true;
        DownloadProgressValue = 0;
        var progress = new Progress<DownloadProgress>(p =>
        {
            // Overall progress
            if (p.TotalBytes > 0)
            {
                long completed = p.BytesDownloaded;
                long total = p.TotalBytes;
                double percent = (double)completed / total * 100.0;
                DownloadProgressValue = percent;
                IsProgressIndeterminate = false;
                ProgressText = $"文件 {completed}/{total} (剩余 {total - completed})  {percent:F0}%";
            }
            // Per-file progress
            if (!string.IsNullOrEmpty(p.CurrentFileName))
            {
                string sizePart = p.CurrentFileTotalBytes > 0
                    ? $"{FormatByteSize(p.CurrentFileDownloadedBytes)} / {FormatByteSize(p.CurrentFileTotalBytes)}"
                    : FormatByteSize(p.CurrentFileDownloadedBytes);
                string speedPart = p.DownloadSpeedBytesPerSecond > 0
                    ? $"  ·  {FormatByteSize((long)p.DownloadSpeedBytesPerSecond)}/s"
                    : "";
                CurrentFileProgress = $"{p.CurrentFileName}  {sizePart}{speedPart}";
                if (p.CurrentFileTotalBytes > 0)
                {
                    FileProgressValue = (double)p.CurrentFileDownloadedBytes / p.CurrentFileTotalBytes * 100.0;
                    IsFileProgressIndeterminate = false;
                }
                else { IsFileProgressIndeterminate = true; }
            }
        });

        try
        {
            var settings = SettingsProvider?.Invoke() ?? new LauncherSettings();

            // Step A: download parent version
            if (!string.IsNullOrEmpty(inheritsFrom))
            {
                string safeInheritsFrom = PathSafetyHelper.SanitizeNameComponent(inheritsFrom);
                string parentDir = Path.Combine(_gameFolder.GameDir, "versions", safeInheritsFrom);
                if (!Directory.Exists(parentDir) || !File.Exists(Path.Combine(parentDir, $"{safeInheritsFrom}.jar")))
                {
                    _notificationService.Show($"正在下载父版本 {inheritsFrom}...", NotificationType.Info);
                    var versions = await _downloadService.LoadOnlineVersionsAsync(_httpClient);
                    var parentManifest = versions.FirstOrDefault(v => v.Id == inheritsFrom);
                    if (parentManifest == null)
                        throw new Exception($"无法在 Mojang 版本清单中找到父版本 {inheritsFrom}。");

                    await _downloadService.DownloadVersionFilesAsync(
                        _httpClient, _gameFolder.GameDir, parentManifest.Url,
                        safeInheritsFrom, safeInheritsFrom, progress, _notificationService.Show, ct);
                }
            }

            ct.ThrowIfCancellationRequested();

            // Step B: install mod loader
            string? loaderType = manifest.Minecraft?.ModLoader;
            string? loaderVersion = manifest.Minecraft?.ModLoaderVersion;

            if (!string.IsNullOrEmpty(loaderType) && loaderType != "Vanilla" && !string.IsNullOrEmpty(loaderVersion))
            {
                _notificationService.Show($"正在安装 {loaderType} {loaderVersion}...", NotificationType.Info);
                string? javaPath = null;
                if (loaderType is "Forge" or "NeoForge" or "OptiFine")
                {
                    javaPath = settings.JavaPath;
                    if (string.IsNullOrEmpty(javaPath))
                    {
                        // 按需自动检测 + 版本感知选择
                        var autoDetected = await _javaDetection.ResolveJavaPathAsync(settings, mcVersion);
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
                            throw new Exception(
                                $"{loaderType} 安装需要 Java 环境。未在 PATH 或 JAVA_HOME 中检测到 Java，请安装 Java 或前往设置手动配置。");
                        }
                    }

                    // Java 兼容性检查
                    if (DownloadJavaCompatibilityHandler != null)
                    {
                        var resolved = await DownloadJavaCompatibilityHandler(javaPath, mcVersion);
                        if (resolved == null) throw new OperationCanceledException("用户取消安装。");
                        javaPath = resolved;
                    }
                }

                Directory.CreateDirectory(targetDir);

                switch (loaderType)
                {
                    case "Fabric":
                        await _modLoaderService.InstallFabricAsync(_httpClient, _gameFolder.GameDir,
                            mcVersion, loaderVersion, instanceName, progress, _notificationService.Show, ct);
                        break;
                    case "Forge":
                        await _modLoaderService.InstallForgeAsync(_httpClient, _gameFolder.GameDir,
                            mcVersion, loaderVersion, instanceName, javaPath!, progress, _notificationService.Show, ct);
                        break;
                    case "Quilt":
                        await _modLoaderService.InstallQuiltAsync(_httpClient, _gameFolder.GameDir,
                            mcVersion, loaderVersion, instanceName, progress, _notificationService.Show, ct);
                        break;
                    case "NeoForge":
                        await _modLoaderService.InstallNeoForgeAsync(_httpClient, _gameFolder.GameDir,
                            mcVersion, loaderVersion, instanceName, javaPath!, progress, _notificationService.Show, ct);
                        break;
                    case "OptiFine":
                        await _modLoaderService.InstallOptiFineAsync(_httpClient, _gameFolder.GameDir,
                            mcVersion, loaderVersion, instanceName, javaPath!, progress, _notificationService.Show, ct);
                        break;
                }
            }

            ct.ThrowIfCancellationRequested();

            // Step C: extract pack files
            await _packService.ExtractAsync(packPath, instanceName, _gameFolder.GameDir,
                progress, _notificationService.Show);

            // Fix imported InstanceDir
            FixImportedInstanceDir(targetDir);

            // Fix vanilla imports: ensure the base version JAR and files are in the instance dir.
            // Download directly to the instance directory instead of versions/{baseVersion}/
            // to avoid InstanceManager.Scan() picking up the bare vanilla dir as a spurious instance.
            string versionJsonPath = Path.Combine(targetDir, "version.json");
            if (File.Exists(versionJsonPath) && string.IsNullOrEmpty(inheritsFrom)
                && (string.IsNullOrEmpty(loaderType) || loaderType == "Vanilla"))
            {
                var vDetail = System.Text.Json.JsonSerializer.Deserialize<VersionDetail>(File.ReadAllText(versionJsonPath));
                if (vDetail != null)
                {
                    string baseVersion = vDetail.Id ?? mcVersion;
                    string destJar = Path.Combine(targetDir, $"{baseVersion}.jar");

                    if (!File.Exists(destJar))
                    {
                        _notificationService.Show($"正在下载基础版本 {baseVersion}...", NotificationType.Info);
                        var versions = await _downloadService.LoadOnlineVersionsAsync(_httpClient);
                        var parentManifest = versions.FirstOrDefault(v => v.Id == baseVersion)
                            ?? throw new Exception($"无法在 Mojang 版本清单中找到版本 {baseVersion}。");
                        await _downloadService.DownloadVersionFilesAsync(
                            _httpClient, _gameFolder.GameDir, parentManifest.Url,
                            baseVersion, instanceName, progress, _notificationService.Show, ct);
                    }
                }
            }

            ct.ThrowIfCancellationRequested();

            // Step D: verify mods
            bool allModsMatch = await _packService.VerifyModsAsync(
                Path.Combine(_gameFolder.GameDir, "versions", instanceName),
                manifest, _notificationService.Show);

            if (!allModsMatch)
                _notificationService.Show("部分 Mod 文件哈希不匹配，可能已损坏。", NotificationType.Warning);

            _notificationService.Show($"整合包 {instanceName} 导入完成！", NotificationType.Success);
        }
        catch (OperationCanceledException)
        {
            _notificationService.Show("导入已取消。", NotificationType.Info);
            try { if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true); }
            catch { /* best effort */ }
        }
        catch (Exception ex)
        {
            _notificationService.WriteDiagnosticLog("导入整合包失败", ex.ToString());
            _notificationService.Show($"导入失败：{ex.Message}", NotificationType.Error);
            try { if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true); }
            catch { /* best effort */ }
        }
        finally
        {
            DownloadProgressChanged?.Invoke(false);
            _instanceManager.Refresh(_notificationService.AppendLog);
        }
    }

    /// <summary>Import a .mrpack modpack (Modrinth format).</summary>
    private async Task ImportMrpackAsync(string mrpackPath)
    {
        CancellationTokenSource cts;
        lock (_downloadLock)
        {
            _activeDownloadCts?.Cancel();
            _activeDownloadCts?.Dispose();
            _activeDownloadCts = new CancellationTokenSource();
            cts = _activeDownloadCts;
        }
        var ct = cts.Token;

        // Phase 1: safety scan before doing anything
        var safetyReport = ModpackSafetyScanner.ScanMrpack(mrpackPath);
        if (safetyReport.HasBlockingIssues)
        {
            var msg = "此整合包未通过安全检查，已拒绝导入：\n" +
                      string.Join("\n", safetyReport.BlockingIssues.Take(10));
            _notificationService.Show(msg, NotificationType.Error);
            return;
        }
        if (safetyReport.HasWarnings)
        {
            var warnMsg = "此整合包存在以下安全风险：\n" +
                          string.Join("\n", safetyReport.Warnings.Take(10)) +
                          "\n\n是否继续导入？";
            if (!await _dialogService.ShowConfirmAsync(warnMsg, "安全警告"))
                return;
        }

        // Phase 2: parse manifest
        MrpackInfo info;
        try
        {
            info = await _mrpackInstall.ParseMrpackAsync(mrpackPath, ct);
        }
        catch (Exception ex)
        {
            _notificationService.Show($"无效的整合包：{ex.Message}", NotificationType.Error);
            return;
        }

        string mcVersion = info.McVersion;
        if (string.IsNullOrEmpty(mcVersion))
        {
            _notificationService.Show("整合包清单中未指定 Minecraft 版本，无法导入。", NotificationType.Error);
            return;
        }

        string instanceName = PathSafetyHelper.SanitizeNameComponent(
            info.Manifest.Name ?? Path.GetFileNameWithoutExtension(mrpackPath));
        if (string.IsNullOrWhiteSpace(instanceName)) instanceName = "ImportedMrpack";

        string targetDir = Path.Combine(_gameFolder.GameDir, "versions", instanceName);
        if (Directory.Exists(targetDir))
        {
            int suffix = 1;
            while (Directory.Exists(Path.Combine(_gameFolder.GameDir, "versions", $"{instanceName}_{suffix}")))
                suffix++;
            instanceName = $"{instanceName}_{suffix}";
            targetDir = Path.Combine(_gameFolder.GameDir, "versions", instanceName);
        }

        VersionDisplay = mcVersion;
        LoaderDisplay = !string.IsNullOrEmpty(info.LoaderType) && !string.IsNullOrEmpty(info.LoaderVersionSpec)
            ? $"{info.LoaderType} {info.LoaderVersionSpec}"
            : "";

        DownloadProgressChanged?.Invoke(true);
        ProgressText = "准备导入...";
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
            if (!string.IsNullOrEmpty(p.CurrentFileName))
            {
                string sizePart = p.CurrentFileTotalBytes > 0
                    ? $"{FormatByteSize(p.CurrentFileDownloadedBytes)} / {FormatByteSize(p.CurrentFileTotalBytes)}"
                    : FormatByteSize(p.CurrentFileDownloadedBytes);
                string speedPart = p.DownloadSpeedBytesPerSecond > 0
                    ? $"  ·  {FormatByteSize((long)p.DownloadSpeedBytesPerSecond)}/s"
                    : "";
                CurrentFileProgress = $"{p.CurrentFileName}  {sizePart}{speedPart}";
                if (p.CurrentFileTotalBytes > 0)
                {
                    FileProgressValue = (double)p.CurrentFileDownloadedBytes / p.CurrentFileTotalBytes * 100.0;
                    IsFileProgressIndeterminate = false;
                }
                else { IsFileProgressIndeterminate = true; }
            }
        });

        try
        {
            var settings = SettingsProvider?.Invoke() ?? new LauncherSettings();

            // Determine Java path for Forge/NeoForge
            string? javaPath = null;
            if (info.LoaderType is "Forge" or "NeoForge")
            {
                javaPath = settings.JavaPath;
                if (string.IsNullOrEmpty(javaPath))
                {
                    // 按需自动检测 + 版本感知选择
                    var autoDetected = await _javaDetection.ResolveJavaPathAsync(settings, mcVersion);
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
                        throw new Exception(
                            $"{info.LoaderType} 安装需要 Java 环境。未在 PATH 或 JAVA_HOME 中检测到 Java，请安装 Java 或前往设置手动配置。");
                    }
                }

                if (DownloadJavaCompatibilityHandler != null)
                {
                    var resolved = await DownloadJavaCompatibilityHandler(javaPath, mcVersion);
                    if (resolved == null) throw new OperationCanceledException("用户取消安装。");
                    javaPath = resolved;
                }
            }

            _mrpackInstall.JavaCompatibilityHandler = DownloadJavaCompatibilityHandler;

            await _mrpackInstall.InstallMrpackAsync(
                mrpackPath, instanceName, targetDir, _gameFolder.GameDir,
                mcVersion, info.LoaderType, null, javaPath, settings,
                progress, _notificationService.Show, ct);

            _notificationService.Show($"整合包 {instanceName} 导入完成！", NotificationType.Success);
        }
        catch (OperationCanceledException)
        {
            _notificationService.Show("导入已取消。", NotificationType.Info);
            try { if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true); }
            catch { /* best effort */ }
        }
        catch (Exception ex)
        {
            _notificationService.WriteDiagnosticLog("导入 .mrpack 整合包失败", ex.ToString());
            _notificationService.Show($"导入失败：{ex.Message}", NotificationType.Error);
            try { if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true); }
            catch { /* best effort */ }
        }
        finally
        {
            DownloadProgressChanged?.Invoke(false);
            _instanceManager.Refresh(_notificationService.AppendLog);
        }
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

    private static void FixImportedInstanceDir(string targetDir)
    {
        string metaPath = Path.Combine(targetDir, "instance.json");
        if (!File.Exists(metaPath)) return;

        try
        {
            var meta = System.Text.Json.JsonSerializer.Deserialize<InstanceMeta>(File.ReadAllText(metaPath));
            if (meta == null || !meta.UseIsolatedDir) return;
            meta.InstanceDir = targetDir;
            File.WriteAllText(metaPath,
                System.Text.Json.JsonSerializer.Serialize(meta, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* ignore corrupt metadata */ }
    }
}
