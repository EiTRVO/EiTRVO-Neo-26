using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EiTRVO.ProEngine.Helpers;
using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Orchestrators;
using EiTRVO.ProEngine.Services;

namespace EiTRVO.ProEngine.ViewModels;

public partial class SettingsViewModel : BaseViewModel
{
    private readonly JavaDetectionService _javaDetection;
    private readonly INotificationService _notificationService;
    private readonly IDialogService _dialogService;
    private readonly IWindowsHelloService _windowsHello;
    private readonly IModrinthService _modrinth;

    private const int SliderMinMB = 2048; // 2 GB

    /// <summary>初始化默认值：对齐到第 2 节点（SliderMinMB + 1 × TickFrequency）。</summary>
    private static int ComputeDefaultNodeMB()
    {
        int range = SystemMemoryInfo.SliderMaxMemoryMB - SliderMinMB;
        int freq = Math.Max(512, (int)Math.Round(range / 7.0 / 512.0) * 512);
        return SliderMinMB + freq;
    }

    [ObservableProperty]
    private int _memory = ComputeDefaultNodeMB();

    [ObservableProperty]
    private string? _selectedResolution;

    [ObservableProperty]
    private JavaInfo? _selectedJava;

    [ObservableProperty]
    private string _memoryText = (ComputeDefaultNodeMB() / 1024.0).ToString("F1");

    // === 动态内存上限（系统内存感知）===

    /// <summary>滑块最大值 = 软上限 16 GB 内 70% RAM。</summary>
    public int MaxMemoryMB => SystemMemoryInfo.SliderMaxMemoryMB;

    /// <summary>滑块最小值（GB 显示用）。</summary>
    public double SliderMinGB => SliderMinMB / 1024.0;

    /// <summary>滑块最大值（GB 显示用）。</summary>
    public double SliderMaxGB => MaxMemoryMB / 1024.0;

    /// <summary>推荐默认内存 = 总内存的 20%（2-8 GB 保底封顶）。</summary>
    public int RecommendedMemoryMB => SystemMemoryInfo.RecommendedDefaultMemoryMB;

    /// <summary>当前内存值（GB），与 Memory (MB) 双向同步。</summary>
    public double MemoryGB
    {
        get => Memory / 1024.0;
        set
        {
            int mb = Math.Clamp((int)Math.Round(value * 1024.0), SliderMinMB, MaxMemoryMB);
            if (mb != Memory)
                Memory = mb;
        }
    }

    /// <summary>第二节点对应的 MB 值（默认按钮目标）。</summary>
    public int DefaultNodeMB => SliderMinMB + CurrentTickFrequency;

    /// <summary>8 等分刻线间距（MB），范围从 2 GB 到软上限。</summary>
    public int CurrentTickFrequency
    {
        get
        {
            int range = MaxMemoryMB - SliderMinMB;
            return Math.Max(512, (int)Math.Round(range / 7.0 / 512.0) * 512);
        }
    }

    /// <summary>内存提示文本（系统信息 + 推荐范围）。</summary>
    public string MemoryHintText
    {
        get
        {
            double totalGB = SystemMemoryInfo.TotalPhysicalMB / 1024.0;
            return $"系统内存 {totalGB:F0} GB  |  推荐 {RecommendedMemoryMB / 1024.0:F1} GB  |  上限 {MaxMemoryMB / 1024.0:F1} GB";
        }
    }

    [ObservableProperty]
    private bool _isCustomResolution;

    [ObservableProperty]
    private string _customWidth = "";

    [ObservableProperty]
    private string _customHeight = "";

    [ObservableProperty]
    private bool _useAutoDetectJava = true;

    [ObservableProperty]
    private string? _manualJavaPath;

    [ObservableProperty]
    private bool _isolateNewInstancesByDefault = true;

    [ObservableProperty]
    private bool _settingsLockEnabled;

    [ObservableProperty]
    private bool _isWindowsHelloAvailable;

    [ObservableProperty]
    private string _settingsLockHint = "";

    [ObservableProperty]
    private bool _firewallEnabled;

    [ObservableProperty]
    private bool _advancedDefenseEnabled;

    /// <summary>高级防御仅当防火墙启用时可选。</summary>
    public bool IsAdvancedDefenseAvailable => FirewallEnabled;

    // === 备份 ===
    [ObservableProperty]
    private bool _backupEnabled;

    [ObservableProperty]
    private BackupInterval _backupInterval = BackupInterval.Weekly;

    [ObservableProperty]
    private string? _backupFolder;

    [ObservableProperty]
    private bool _excludeRedownloadable = true;

    [ObservableProperty]
    private bool _disableChunkedDownload;

    /// <summary>调试模式：跳过 Java 版本兼容性检查。</summary>
    [ObservableProperty]
    private bool _debugMode;

    /// <summary>上次备份完成时间（UTC），由 MainWindow 同步。</summary>
    [ObservableProperty]
    private DateTimeOffset? _lastBackupTime;

    /// <summary>下次自动备份时间文案（仅在启用备份时非空）。</summary>
    public string NextBackupDisplay =>
        BackupEnabled
            ? BackupService.GetNextBackupDisplay(BackupInterval, LastBackupTime)
            : "";

    // === 恢复 ===
    [ObservableProperty]
    private string? _restoreFilePath;

    [ObservableProperty]
    private RestoreMode _selectedRestoreMode = RestoreMode.Overlay;

    public bool HasRestoreFile => !string.IsNullOrEmpty(RestoreFilePath);

    /// <summary>手动指定模式（非自动检测）。用于 XAML 可见性绑定。</summary>
    public bool IsManualJavaMode => !UseAutoDetectJava;

    public ObservableCollection<JavaInfo> JavaList { get; } = new();

    /// <summary>备份间隔选项（ComboBox ItemSource）。</summary>
    public ObservableCollection<BackupIntervalOption> BackupIntervalOptions { get; } = new()
    {
        new("每次启动", BackupInterval.EveryLaunch),
        new("每天", BackupInterval.Daily),
        new("每周", BackupInterval.Weekly),
        new("每月", BackupInterval.Monthly),
    };

    /// <summary>恢复模式选项（ComboBox ItemSource）。</summary>
    public ObservableCollection<RestoreModeOption> RestoreModeOptions { get; } = new()
    {
        new("覆盖式（跳过同名）", RestoreMode.Overlay),
        new("清理式（删除后还原）", RestoreMode.Clean),
    };

    /// <summary>触发手动备份（由 MainWindow 订阅）。参数为用户选择的备份目标文件夹路径。</summary>
    public event Action<string>? ManualBackupRequested;

    /// <summary>触发恢复（由 MainWindow 订阅）。参数: (备份文件路径, 恢复模式)。</summary>
    public event Action<string, RestoreMode>? RestoreRequested;

    public SettingsViewModel(JavaDetectionService javaDetection, INotificationService notificationService, IDialogService dialogService, IWindowsHelloService windowsHello, IModrinthService modrinth)
    {
        _javaDetection = javaDetection;
        _notificationService = notificationService;
        _dialogService = dialogService;
        _windowsHello = windowsHello;
        _modrinth = modrinth;
    }

    partial void OnDisableChunkedDownloadChanged(bool value)
    {
        _modrinth.ForceSingleConnection = value;
    }

    partial void OnUseAutoDetectJavaChanged(bool value)
    {
        OnPropertyChanged(nameof(IsManualJavaMode));
    }

    partial void OnSelectedResolutionChanged(string? value)
    {
        IsCustomResolution = value == "自定义";
    }

    partial void OnFirewallEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAdvancedDefenseAvailable));
        if (!value)
            AdvancedDefenseEnabled = false; // 关闭防火墙时自动取消高级防御
    }

    partial void OnMemoryChanged(int value)
    {
        MemoryText = (value / 1024.0).ToString("F1");
        OnPropertyChanged(nameof(MemoryGB));
    }

    /// <summary>手输框解析：兼容 GB 输入（如 "4" 或 "4.0"）。</summary>
    partial void OnMemoryTextChanged(string value)
    {
        if (double.TryParse(value, out double gb))
        {
            int mb = Math.Clamp((int)Math.Round(gb * 1024.0), SliderMinMB, MaxMemoryMB);
            Memory = mb;
        }
    }

    [RelayCommand]
    private void ResetMemoryToDefault()
    {
        Memory = DefaultNodeMB;
    }

    partial void OnBackupEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(NextBackupDisplay));
    }

    partial void OnBackupIntervalChanged(BackupInterval value)
    {
        OnPropertyChanged(nameof(NextBackupDisplay));
    }

    partial void OnLastBackupTimeChanged(DateTimeOffset? value)
    {
        OnPropertyChanged(nameof(NextBackupDisplay));
    }

    partial void OnRestoreFilePathChanged(string? value)
    {
        OnPropertyChanged(nameof(HasRestoreFile));
    }


    /// <summary>Apply stored settings to the ViewModel properties.</summary>
    public void ApplySettings(LauncherSettings settings)
    {
        Memory = settings.MemoryMB > 0 ? settings.MemoryMB : DefaultNodeMB;
        SelectedResolution = settings.Resolution;
        UseAutoDetectJava = settings.UseAutoDetectJava;
        ManualJavaPath = settings.ManualJavaPath;
        SettingsLockEnabled = settings.SettingsLockEnabled;
        FirewallEnabled = settings.FirewallEnabled;
        AdvancedDefenseEnabled = settings.AdvancedDefenseEnabled;
        BackupEnabled = settings.BackupEnabled;
        BackupInterval = settings.BackupInterval;
        BackupFolder = settings.BackupFolder;
        ExcludeRedownloadable = settings.ExcludeRedownloadable;
        DisableChunkedDownload = settings.DisableChunkedDownload;
        DebugMode = settings.DebugMode;
    }

    /// <summary>Snapshot current ViewModel state into a LauncherSettings DTO.</summary>
    public LauncherSettings ToSettings()
    {
        return new LauncherSettings
        {
            MemoryMB = Memory,
            Resolution = SelectedResolution,
            JavaPath = UseAutoDetectJava ? SelectedJava?.Path : ManualJavaPath,
            IsolateNewInstancesByDefault = IsolateNewInstancesByDefault,
            UseAutoDetectJava = UseAutoDetectJava,
            ManualJavaPath = ManualJavaPath,
            SettingsLockEnabled = SettingsLockEnabled,
            FirewallEnabled = FirewallEnabled,
            AdvancedDefenseEnabled = AdvancedDefenseEnabled,
            BackupEnabled = BackupEnabled,
            BackupInterval = BackupInterval,
            BackupFolder = BackupFolder,
            ExcludeRedownloadable = ExcludeRedownloadable,
            DisableChunkedDownload = DisableChunkedDownload,
            DebugMode = DebugMode,
            LastBackupTime = null // LastBackupTime is managed by MainWindow, not synced from VM
        };
    }

    [RelayCommand]
    private async Task DetectJavaAsync()
    {
        try
        {
            var javas = await _javaDetection.DetectAsync();
            JavaList.Clear();
            foreach (var j in javas)
                JavaList.Add(j);
            _notificationService.Show("Java 检测完成。", NotificationType.Success);
        }
        catch (System.Exception ex)
        {
            _notificationService.Show($"Java 检测失败：{ex.Message}", NotificationType.Error);
        }
    }

    [RelayCommand]
    private async Task BrowseJava()
    {
        var path = await _dialogService.ShowOpenFileDialogAsync(
            "选择 Java 可执行文件",
            "Java 可执行文件|java.exe;javaw.exe|所有文件|*.*");
        if (path == null) return;

        // Validate the selected executable by running -version
        var versionInfo = await JavaDetectionService.GetJavaVersionInfoAsync(path);
        if (versionInfo == null)
        {
            _notificationService.Show(
                "选择的文件不是有效的 Java 运行时，请选择 java.exe 或 javaw.exe。",
                NotificationType.Warning);
            return;
        }

        ManualJavaPath = path;
        SelectedJava = new JavaInfo
        {
            Path = path,
            Version = versionInfo.Value.full,
            ShortVersion = versionInfo.Value.shortVer,
            MajorVersion = versionInfo.Value.major
        };
    }

    /// <summary>检查 Windows Hello 可用性，更新 UI 状态。</summary>
    public async Task CheckWindowsHelloAvailabilityAsync()
    {
        var availability = await _windowsHello.CheckAvailabilityAsync();
        IsWindowsHelloAvailable = availability == WindowsHelloAvailability.Available;
        SettingsLockHint = availability switch
        {
            WindowsHelloAvailability.Available
                => "开启后，进入设置、管理、账户页面前需通过 Windows Hello 或 PIN 码验证身份。",
            WindowsHelloAvailability.NotConfigured
                => "⚠ 设备未配置 Windows Hello 或 PIN 码。请在 Windows 设置 → 账户 → 登录选项中配置 PIN 码。",
            WindowsHelloAvailability.DeviceNotPresent
                => "⚠ 当前设备不支持 Windows Hello。",
            WindowsHelloAvailability.DisabledByPolicy
                => "⚠ Windows Hello 已被组策略禁用。",
            _ => ""
        };
    }

    [RelayCommand]
    private async Task BrowseBackupFolder()
    {
        var path = await _dialogService.ShowFolderBrowserDialogAsync("选择备份文件夹");
        if (path != null)
        {
            BackupFolder = path;
        }
    }

    [RelayCommand]
    private async Task BackupNow()
    {
        // 手动备份始终让用户选择目标文件夹，不依赖自动备份配置
        var path = await _dialogService.ShowFolderBrowserDialogAsync("选择备份文件夹");
        if (path == null) return;
        ManualBackupRequested?.Invoke(path);
    }

    [RelayCommand]
    private async Task BrowseRestoreFile()
    {
        var path = await _dialogService.ShowOpenFileDialogAsync(
            "选择备份文件", "EiTRVO 备份文件|*.eibak|所有文件|*.*");
        if (path != null)
        {
            RestoreFilePath = path;
        }
    }

    [RelayCommand]
    private async Task Restore()
    {
        if (string.IsNullOrEmpty(RestoreFilePath)) return;

        string confirmMsg = SelectedRestoreMode == RestoreMode.Clean
            ? "⚠ 将删除当前 .minecraft 文件夹并替换为备份内容。此操作不可撤销！确定继续？"
            : "将使用备份文件补充 .minecraft 中缺失的文件，已存在的文件不会被覆盖。确定继续？";

        if (!await _dialogService.ShowConfirmAsync(confirmMsg, "确认恢复"))
            return;

        RestoreRequested?.Invoke(RestoreFilePath!, SelectedRestoreMode);
    }

    [RelayCommand]
    private void SaveSettings()
    {
        _modrinth.ForceSingleConnection = DisableChunkedDownload;
        _notificationService.Show("设置已保存。", NotificationType.Info);
    }
}

/// <summary>备份间隔选项（ComboBox 绑定）。</summary>
public record BackupIntervalOption(string Label, BackupInterval Value)
{
    public override string ToString() => Label;
}

/// <summary>恢复模式选项（ComboBox 绑定）。</summary>
public record RestoreModeOption(string Label, RestoreMode Value)
{
    public override string ToString() => Label;
}
