using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Orchestrators;
using EiTRVO.ProEngine.Services;
using EiTRVO.ProEngine.ViewModels;

namespace EiTRVO.UI.ViewModels;

/// <summary>
/// 首次启动向导 (OOBE) ViewModel — 步骤导航 + 安全设置 + 账号代理。
/// </summary>
public partial class WizardViewModel : BaseViewModel
{
    private readonly ISettingsService _settingsService;
    private readonly IAuthService _authService;
    private readonly AccountManager _accountManager;
    private readonly HttpClient _httpClient;
    private readonly INotificationService _notificationService;
    private readonly IDialogService _dialogService;
    private readonly IClipboardService _clipboardService;
    private readonly IProcessService _processService;
    private readonly IGameFolderService _gameFolder;

    /// <summary>Guide the View to close the wizard window with a result.</summary>
    public event Action<bool>? RequestClose;

    // ==================== Step Navigation ====================

    [ObservableProperty]
    private int _currentStepIndex;

    [ObservableProperty]
    private bool _isBackVisible;

    [ObservableProperty]
    private bool _isNextVisible;

    [ObservableProperty]
    private bool _isSkipVisible;

    [ObservableProperty]
    private bool _isFinishVisible;

    public ObservableCollection<WizardStepItem> Steps { get; } = new();

    // ==================== Page 2: Security Settings ====================

    [ObservableProperty]
    private bool _firewallEnabled;

    [ObservableProperty]
    private bool _advancedDefenseEnabled;

    [ObservableProperty]
    private bool _backupEnabled;

    [ObservableProperty]
    private BackupInterval _backupInterval = BackupInterval.Weekly;

    [ObservableProperty]
    private string? _backupFolder;

    [ObservableProperty]
    private bool _excludeRedownloadable = true;

    /// <summary>Backup interval options for ComboBox binding.</summary>
    public ObservableCollection<BackupIntervalOption> BackupIntervalOptions { get; } = new()
    {
        new("每次启动", BackupInterval.EveryLaunch),
        new("每天", BackupInterval.Daily),
        new("每周", BackupInterval.Weekly),
        new("每月", BackupInterval.Monthly),
    };

    /// <summary>Advanced defense only available when firewall is on.</summary>
    public bool IsAdvancedDefenseAvailable => FirewallEnabled;

    partial void OnFirewallEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAdvancedDefenseAvailable));
        if (!value)
            AdvancedDefenseEnabled = false;
    }

    // ==================== Page 3: Account (delegation) ====================

    private AccountViewModel? _accountVm;

    /// <summary>Lazily-created AccountViewModel. Creation triggers property forwarding.</summary>
    private AccountViewModel AccountVm
    {
        get
        {
            if (_accountVm == null)
            {
                _accountVm = new AccountViewModel(
                    _authService, _accountManager, _httpClient,
                    _notificationService, _dialogService, _clipboardService, _processService);
                // Forward all AccountViewModel property changes to WizardViewModel
                _accountVm.PropertyChanged += (s, e) => OnPropertyChanged(e.PropertyName);
            }
            return _accountVm;
        }
    }

    // --- Account VM properties (forwarded for XAML bindings) ---
    public bool IsDeviceCodeVisible => _accountVm?.IsDeviceCodeVisible ?? false;
    public string VerificationUri => _accountVm?.VerificationUri ?? "";
    public string UserCode => _accountVm?.UserCode ?? "";
    public string DeviceStatus => _accountVm?.DeviceStatus ?? "";
    public bool IsAddingYggdrasilAccount => _accountVm?.IsAddingYggdrasilAccount ?? false;
    public bool IsOfflineFormVisible => _accountVm?.IsOfflineFormVisible ?? false;
    public bool IsYggdrasilFormVisible => _accountVm?.IsYggdrasilFormVisible ?? false;
    public string YggdrasilStatus => _accountVm?.YggdrasilStatus ?? "";
    public string YggdrasilServerUrl
    {
        get => _accountVm?.YggdrasilServerUrl ?? "";
        set { if (_accountVm != null) _accountVm.YggdrasilServerUrl = value; }
    }
    public string YggdrasilEmail
    {
        get => _accountVm?.YggdrasilEmail ?? "";
        set { if (_accountVm != null) _accountVm.YggdrasilEmail = value; }
    }
    public string OfflinePlayerName
    {
        get => _accountVm?.OfflinePlayerName ?? "";
        set { if (_accountVm != null) _accountVm.OfflinePlayerName = value; }
    }

    /// <summary>Password set from code-behind (PasswordBox can't bind).</summary>
    public void SetYggdrasilPassword(string password)
    {
        if (_accountVm != null)
            _accountVm.YggdrasilPassword = password;
    }

    // --- Account VM commands (forwarded) ---
    public IRelayCommand AddMicrosoftCommand => AccountVm.AddMicrosoftCommand;
    public IRelayCommand ToggleYggdrasilFormCommand => AccountVm.ToggleYggdrasilFormCommand;
    public IRelayCommand ToggleOfflineFormCommand => AccountVm.ToggleOfflineFormCommand;
    public IRelayCommand YggdrasilLoginCommand => AccountVm.YggdrasilLoginCommand;
    public IRelayCommand AddOfflineAccountCommand => AccountVm.AddOfflineAccountCommand;
    public IRelayCommand CancelYggdrasilCommand => AccountVm.CancelYggdrasilCommand;
    public IRelayCommand CancelOfflineCommand => AccountVm.CancelOfflineCommand;
    public IRelayCommand OpenBrowserCommand => AccountVm.OpenBrowserCommand;
    public IRelayCommand CancelAuthCommand => AccountVm.CancelAuthCommand;
    public IRelayCommand CopyCodeCommand => AccountVm.CopyCodeCommand;

    /// <summary>Shared account list (same singleton as main app).</summary>
    public ObservableCollection<Account> Accounts => _accountManager.Accounts;

    // ==================== Page 4: Summary ====================

    /// <summary>Firewall status text for summary page.</summary>
    public string FirewallSummary => FirewallEnabled
        ? $"EiTRVO Firewall 已开启{(AdvancedDefenseEnabled ? "（高级防御）" : "")}"
        : "EiTRVO Firewall 未开启";

    /// <summary>Backup status text for summary page.</summary>
    public string BackupSummary
    {
        get
        {
            if (!BackupEnabled) return "自动备份未开启";
            string intervalLabel = BackupInterval switch
            {
                BackupInterval.EveryLaunch => "每次启动",
                BackupInterval.Daily => "每天",
                BackupInterval.Weekly => "每周",
                BackupInterval.Monthly => "每月",
                _ => ""
            };
            return $"自动备份已开启（{intervalLabel}）";
        }
    }

    /// <summary>Account count text for summary page.</summary>
    public string AccountSummary
    {
        get
        {
            int count = _accountManager.Accounts.Count;
            if (count == 0) return "未添加账号";
            return $"已添加 {count} 个账号";
        }
    }

    // ==================== Constructor ====================

    public WizardViewModel(
        ISettingsService settingsService,
        IAuthService authService,
        AccountManager accountManager,
        HttpClient httpClient,
        INotificationService notificationService,
        IDialogService dialogService,
        IClipboardService clipboardService,
        IProcessService processService,
        IGameFolderService gameFolder)
    {
        _settingsService = settingsService;
        _authService = authService;
        _accountManager = accountManager;
        _httpClient = httpClient;
        _notificationService = notificationService;
        _dialogService = dialogService;
        _clipboardService = clipboardService;
        _processService = processService;
        _gameFolder = gameFolder;

        // Initialize steps
        Steps.Add(new WizardStepItem("1", "欢迎使用", "了解启动器"));
        Steps.Add(new WizardStepItem("2", "安全设置", "防火墙与备份配置"));
        Steps.Add(new WizardStepItem("3", "添加账号", "登录与认证"));
        Steps.Add(new WizardStepItem("4", "准备就绪", "开始使用"));

        UpdateStepState();
    }

    // ==================== Step State Management ====================

    private void UpdateStepState()
    {
        // Update step active states
        for (int i = 0; i < Steps.Count; i++)
            Steps[i].IsActive = i == CurrentStepIndex;

        // Button visibility per step
        IsBackVisible = CurrentStepIndex > 0;
        IsNextVisible = CurrentStepIndex < 3;
        IsSkipVisible = CurrentStepIndex == 0;
        IsFinishVisible = CurrentStepIndex == 3;

        // Refresh summary when reaching step 3
        if (CurrentStepIndex == 3)
        {
            OnPropertyChanged(nameof(FirewallSummary));
            OnPropertyChanged(nameof(BackupSummary));
            OnPropertyChanged(nameof(AccountSummary));
        }
    }

    partial void OnCurrentStepIndexChanged(int value) => UpdateStepState();

    // ==================== Step 3: Buy-Minecraft Reminder ====================

    /// <summary>
    /// Called when the account page becomes visible (step index == 2).
    /// If user adds a non-Microsoft account first, remind them to buy Minecraft.
    /// </summary>
    public void OnAccountPageActivated()
    {
        // Subscribe to account changes to detect first non-Microsoft account addition
        _accountManager.Accounts.CollectionChanged += OnAccountsChanged;
    }

    private void OnAccountsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Add)
            return;

        bool hasMicrosoft = _accountManager.Accounts.Any(a => a.Type == AccountType.Microsoft);
        if (!hasMicrosoft)
        {
            // Show buy-Minecraft reminder on the UI thread
            Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                var message = "检测到您未登录正版Minecraft账号。\n\n" +
                              "如果您喜欢这款游戏，请前往Minecraft官网购买正版账号，\n" +
                              "支持游戏开发者，享受完整的在线服务！\n\n" +
                              "点击「是」前往Minecraft官网。\n" +
                              "若按【否】则正常启动游戏。";

                bool buy = await _dialogService.ShowConfirmAsync(message, "EiTRVO 正版提醒");

                if (buy)
                {
                    try
                    {
                        _processService.OpenUrl("https://www.minecraft.net");
                    }
                    catch { }
                }
            });
        }
    }

    // ==================== Commands ====================

    [RelayCommand]
    private void GoNext()
    {
        // Page 3 → Page 4: refresh summary
        if (CurrentStepIndex == 2)
        {
            OnPropertyChanged(nameof(AccountSummary));
            // Unsubscribe from collection changes when leaving account page
            _accountManager.Accounts.CollectionChanged -= OnAccountsChanged;
        }

        if (CurrentStepIndex < 3)
            CurrentStepIndex++;
    }

    [RelayCommand]
    private void GoBack()
    {
        if (CurrentStepIndex > 0)
            CurrentStepIndex--;
    }

    [RelayCommand]
    private void Skip()
    {
        // Mark wizard as completed via standalone marker (wizard.json)
        // so future settings.json overwrites cannot reset the flag.
        WizardCompletionHelper.MarkCompleted(_gameFolder.GameDir);

        var s = new LauncherSettings { WizardCompleted = true };
        _settingsService.Save(s);
        RequestClose?.Invoke(true);
    }

    [RelayCommand]
    private async Task BrowseBackupFolder()
    {
        var path = await _dialogService.ShowFolderBrowserDialogAsync("选择备份文件夹");
        if (path != null)
            BackupFolder = path;
    }

    [RelayCommand]
    private void Finish()
    {
        // Mark wizard as completed via standalone marker (wizard.json)
        // so future settings.json overwrites cannot reset the flag.
        WizardCompletionHelper.MarkCompleted(_gameFolder.GameDir);

        // Save wizard-configured settings
        var settings = new LauncherSettings
        {
            WizardCompleted = true,
            FirewallEnabled = FirewallEnabled,
            AdvancedDefenseEnabled = AdvancedDefenseEnabled,
            BackupEnabled = BackupEnabled,
            BackupInterval = BackupInterval,
            BackupFolder = BackupFolder,
            ExcludeRedownloadable = ExcludeRedownloadable,
        };
        _settingsService.Save(settings);

        // Cleanup account event subscription
        _accountManager.Accounts.CollectionChanged -= OnAccountsChanged;

        RequestClose?.Invoke(true);
    }

    /// <summary>Update backup-related summary when properties change.</summary>
    partial void OnBackupEnabledChanged(bool value) => RefreshSummary();
    partial void OnBackupIntervalChanged(BackupInterval value) => RefreshSummary();

    private void RefreshSummary()
    {
        if (CurrentStepIndex == 3)
        {
            OnPropertyChanged(nameof(BackupSummary));
            OnPropertyChanged(nameof(FirewallSummary));
        }
    }
}

/// <summary>Step indicator item displayed in the left sidebar of the wizard.</summary>
public class WizardStepItem : ObservableObject
{
    public string Number { get; }
    public string Title { get; }
    public string Description { get; }

    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public WizardStepItem(string number, string title, string description)
    {
        Number = number;
        Title = title;
        Description = description;
    }
}
