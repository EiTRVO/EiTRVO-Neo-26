using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Helpers;
using EiTRVO.ProEngine.Services;
using EiTRVO.ProEngine.Orchestrators;
using EiTRVO.UI.Panels;
using EiTRVO.UI.Platforms;
using EiTRVO.ProEngine.ViewModels;
using EiTRVO.UI.ViewModels;

namespace EiTRVO.UI
{
    public partial class MainWindow : Window
    {
        // ==================== DI 注入依赖 ====================
        private readonly HttpClient _httpClient;
        private readonly IAuthService _authService;
        private readonly IDownloadService _downloadService;
        private readonly IModLoaderService _modLoaderService;
        private readonly IPackService _packService;
        private readonly INotificationService _notificationService;
        private readonly IThemeService _themeService;
        private readonly IDispatcherService _dispatcher;
        private readonly IClipboardService _clipboard;
        private readonly IDialogService _dialogService;
        private readonly JavaDetectionService _javaDetection;
        private readonly AccountManager _accountManager;
        private readonly InstanceManager _instanceManager;
        private readonly LaunchOrchestrator _launchOrchestrator;
        private readonly IGameFolderService _gameFolder;
        private readonly SaveLockService _saveLockService;
        private readonly LocalKeyStore _localKeyStore;
        private readonly SaveRecoveryFile _saveRecoveryFile;
        private readonly IWindowsHelloService _windowsHello;
        private readonly IGameProcessSecurityService _gameSecurity;
        private readonly BackupService _backupService;
        private readonly string _gameDir;

        // ==================== ViewModels (DI 注入) ====================
        private readonly HomeViewModel _homeVm;
        private readonly DownloadViewModel _downloadVm;
        private readonly SettingsViewModel _settingsVm;
        private readonly ManageViewModel _manageVm;
        private readonly AccountViewModel _accountVm;
        private readonly AboutViewModel _aboutVm;
        private readonly InstanceDetailViewModel _instanceDetailVm;
        private readonly ModManagementViewModel _modManagementVm;
        private readonly ResourcePackViewModel _resourcePackVm;
        private readonly SaveLockDetailViewModel _saveLockDetailVm;
        private readonly ModpackDownloadViewModel _modpackDownloadVm;
        private readonly AccountSkinViewModel _accountSkinVm;
        private readonly SchematicManagementViewModel _schematicManagementVm;

        // ==================== UI 状态 ====================
        public ObservableCollection<NotificationItem> Notifications => _notificationService.Notifications;
        public ObservableCollection<LogEntry> LogEntries => _notificationService.LogEntries;
        private readonly Dictionary<string, Button> _sidebarButtons = new();
        private LauncherSettings _settings = new();
        private string _activePanel = "home";
        private bool _settingsReady;
        private bool _isBackupRunning;
        private readonly Dictionary<string, string?> _panelCategories = new();
        private string? _lastVerifiedCategory;
        private DateTime _lastVerifyTime;
        private const int VerifyCacheSeconds = 30;
        private string? _lastLaunchLogPath;

        // ==================== App 信息 ====================
        public string AppVersion => AppInfo.Version;
        public string AppVersionShort => AppInfo.VersionShort;

        // ==================== 构造函数 ====================
        public MainWindow(
            HttpClient httpClient,
            IAuthService authService,
            IDownloadService downloadService,
            IModLoaderService modLoaderService,
            IPackService packService,
            IGameFolderService gameFolder,
            INotificationService notificationService,
            IThemeService themeService,
            IDispatcherService dispatcher,
            IClipboardService clipboardService,
            IDialogService dialogService,
            JavaDetectionService javaDetection,
            AccountManager accountManager,
            InstanceManager instanceManager,
            LaunchOrchestrator launchOrchestrator,
            SaveLockService saveLockService,
            LocalKeyStore localKeyStore,
            SaveRecoveryFile saveRecoveryFile,
            IWindowsHelloService windowsHello,
            IGameProcessSecurityService gameSecurity,
            BackupService backupService,
            HomeViewModel homeVm,
            DownloadViewModel downloadVm,
            SettingsViewModel settingsVm,
            ManageViewModel manageVm,
            AccountViewModel accountVm,
            AboutViewModel aboutVm,
            InstanceDetailViewModel instanceDetailVm,
            ModManagementViewModel modManagementVm,
            ResourcePackViewModel resourcePackVm,
            SaveLockDetailViewModel saveLockDetailVm,
            ModpackDownloadViewModel modpackDownloadVm,
            AccountSkinViewModel accountSkinVm,
            SchematicManagementViewModel schematicManagementVm)
        {
            InitializeComponent();

            _httpClient = httpClient;
            _authService = authService;
            _downloadService = downloadService;
            _modLoaderService = modLoaderService;
            _packService = packService;
            _notificationService = notificationService;
            _themeService = themeService;
            _dispatcher = dispatcher;
            _clipboard = clipboardService;
            _dialogService = dialogService;
            _javaDetection = javaDetection;
            _accountManager = accountManager;
            _instanceManager = instanceManager;
            _launchOrchestrator = launchOrchestrator;
            _saveLockService = saveLockService;
            _localKeyStore = localKeyStore;
            _saveRecoveryFile = saveRecoveryFile;
            _windowsHello = windowsHello;
            _gameSecurity = gameSecurity;
            _backupService = backupService;
            _homeVm = homeVm;
            _downloadVm = downloadVm;
            _settingsVm = settingsVm;
            _manageVm = manageVm;
            _accountVm = accountVm;
            _aboutVm = aboutVm;
            _instanceDetailVm = instanceDetailVm;
            _modManagementVm = modManagementVm;
            _resourcePackVm = resourcePackVm;
            _saveLockDetailVm = saveLockDetailVm;
            _modpackDownloadVm = modpackDownloadVm;
            _accountSkinVm = accountSkinVm;
            _schematicManagementVm = schematicManagementVm;

            _themeService.SetTheme(false);
            WpfThemeHelper.ApplyColorScheme(Resources, _themeService.GetColorScheme());
            _themeService.ThemeChanged += OnThemeChanged;
            DataContext = this;
            _gameFolder = gameFolder;
            _gameDir = gameFolder.GameDir;

            // 备份/恢复期间拦截窗口关闭
            Closing += (sender, e) =>
            {
                if (_isBackupRunning)
                {
                    e.Cancel = true;
                    _notificationService.Show("备份/恢复正在进行中，请等待完成或点击进度窗口的关闭按钮取消操作。", NotificationType.Warning);
                }
            };

            // Wire ViewModel DataContexts
            pnlHome.DataContext = _homeVm;
            pnlDownload.DataContext = _downloadVm;
            pnlSettings.DataContext = _settingsVm;
            pnlManage.DataContext = _manageVm;
            pnlInstanceDetail.DataContext = _instanceDetailVm;
            pnlModManagement.DataContext = _modManagementVm;
            pnlResourcePacks.DataContext = _resourcePackVm;
            pnlAccount.DataContext = _accountVm;
            pnlAbout.DataContext = _aboutVm;
            pnlSaveLockDetail.DataContext = _saveLockDetailVm;
            pnlModpackDownload.DataContext = _modpackDownloadVm;
            pnlAccountSkin.DataContext = _accountSkinVm;
            pnlSchematicManagement.DataContext = _schematicManagementVm;

            // Wire ViewModel events
            _homeVm.LaunchFailed += (msg, logPath) => _dispatcher.Invoke(() => ShowFailureDialog(msg, logPath));

            // EiTRVO Firewall: 注入开关状态
            _launchOrchestrator.FirewallEnabledProvider = () => _settings.FirewallEnabled;
            _launchOrchestrator.AdvancedDefenseEnabledProvider = () => _settings.AdvancedDefenseEnabled;

            // Mods 完整性校验：注入未收录 Mod 警告回调
            _launchOrchestrator.ModsWarningHandler = (unknownFiles) =>
            {
                bool result = false;
                _dispatcher.Invoke(() =>
                {
                    var listText = string.Join("\n• ", unknownFiles);
                    var message = $"检测到 {unknownFiles.Count} 个非官方模组文件，请确认这些模组的加入已经过您确认：\n\n• {listText}\n\n继续启动？";
                    result = MessageBox.Show(message, "EiTRVO 安全提醒",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
                });
                return Task.FromResult(result);
            };

            // 存档锁：注入启动时解锁回调
            _homeVm.SaveUnlockHandler = (savencPaths, ct) =>
            {
                var tcs = new TaskCompletionSource<UnlockResult>();
                _dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        var unlockVm = new UnlockViewModel(
                            _saveLockService, _localKeyStore, _saveRecoveryFile,
                            _accountManager, _notificationService, _dialogService, _httpClient);
                        var result = await unlockVm.ShowUnlockDialogAsync(savencPaths);
                        tcs.TrySetResult(result);
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                });
                return tcs.Task;
            };

            // 正版提醒：无微软账号且离线/第三方启动时弹窗提醒
            _homeVm.BuyMinecraftReminder = () =>
            {
                var tcs = new TaskCompletionSource();
                _dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        var message = "检测到您未登录正版Minecraft账号。\n\n" +
                                      "如果您喜欢这款游戏，请前往Minecraft官网购买正版账号，\n" +
                                      "支持游戏开发者，享受完整的在线服务！\n\n" +
                                      "点击「是」前往Minecraft官网。";

                        bool buy = MessageBox.Show(message, "EiTRVO 正版提醒",
                            MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes;

                        if (buy)
                        {
                            try
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                                    "https://www.minecraft.net") { UseShellExecute = true });
                            }
                            catch { }
                        }
                    }
                    catch { }
                    tcs.TrySetResult();
                });
                return tcs.Task;
            };

            // 存档锁：注入加解密进度回调
            ProgressDialog? saveLockProgressDialog = null;
            _launchOrchestrator.ShowSaveLockProgress = () => _dispatcher.Invoke(() =>
            {
                saveLockProgressDialog = new ProgressDialog("正在处理加密存档...");
                saveLockProgressDialog.Show();
            });
            _launchOrchestrator.SaveLockProgress = new Progress<(int, int)>(update =>
                _dispatcher.Invoke(() =>
                    saveLockProgressDialog?.Report(update.Item1, update.Item2)));
            _launchOrchestrator.HideSaveLockProgress = () => _dispatcher.Invoke(() =>
            {
                saveLockProgressDialog?.Close();
                saveLockProgressDialog = null;
            });

            // 备份/恢复：注入进度委托
            ProgressDialog? backupProgressDialog = null;
            _backupService.FileProgress = new Progress<(int, int)>(update =>
                _dispatcher.Invoke(() => backupProgressDialog?.Report(update.Item1, update.Item2)));
            _backupService.SetPhase = (phase) => _dispatcher.Invoke(() =>
            {
                if (backupProgressDialog != null) backupProgressDialog.Title = phase;
            });
            _backupService.IsGameRunning = () => _launchOrchestrator.IsGameRunning;

            // 备份/恢复：ViewModel 事件 → MainWindow
            _settingsVm.ManualBackupRequested += async () => await RunBackupAsync(isScheduled: false);
            _settingsVm.RestoreRequested += async (filePath, mode) => await RunRestoreAsync(filePath, mode);

            _settingsVm.PropertyChanged += (s, e) =>
            {
                switch (e.PropertyName)
                {
                    case nameof(SettingsViewModel.SelectedJava):
                        _homeVm.SelectedJava = _settingsVm.SelectedJava;
                        SaveSettingsFromVm();
                        break;
                    case nameof(SettingsViewModel.Memory):
                        _homeVm.Memory = _settingsVm.Memory;
                        SaveSettingsFromVm();
                        break;
                    case nameof(SettingsViewModel.SelectedResolution):
                        _homeVm.SelectedResolution = _settingsVm.SelectedResolution;
                        SaveSettingsFromVm();
                        break;
                    case nameof(SettingsViewModel.CustomWidth):
                        _homeVm.CustomWidth = _settingsVm.CustomWidth;
                        break;
                    case nameof(SettingsViewModel.CustomHeight):
                        _homeVm.CustomHeight = _settingsVm.CustomHeight;
                        break;
                    case nameof(SettingsViewModel.SettingsLockEnabled):
                        _settings.SettingsLockEnabled = _settingsVm.SettingsLockEnabled;
                        SaveSettingsFromVm();
                        break;
                    case nameof(SettingsViewModel.FirewallEnabled):
                        _settings.FirewallEnabled = _settingsVm.FirewallEnabled;
                        SaveSettingsFromVm();
                        break;
                    case nameof(SettingsViewModel.AdvancedDefenseEnabled):
                        _settings.AdvancedDefenseEnabled = _settingsVm.AdvancedDefenseEnabled;
                        SaveSettingsFromVm();
                        break;
                    case nameof(SettingsViewModel.BackupEnabled):
                        _settings.BackupEnabled = _settingsVm.BackupEnabled;
                        SaveSettingsFromVm();
                        break;
                    case nameof(SettingsViewModel.BackupInterval):
                        _settings.BackupInterval = _settingsVm.BackupInterval;
                        SaveSettingsFromVm();
                        break;
                    case nameof(SettingsViewModel.BackupFolder):
                        _settings.BackupFolder = _settingsVm.BackupFolder;
                        SaveSettingsFromVm();
                        break;
                    case nameof(SettingsViewModel.ExcludeRedownloadable):
                        _settings.ExcludeRedownloadable = _settingsVm.ExcludeRedownloadable;
                        SaveSettingsFromVm();
                        break;
                    case nameof(SettingsViewModel.DisableChunkedDownload):
                        _settings.DisableChunkedDownload = _settingsVm.DisableChunkedDownload;
                        SaveSettingsFromVm();
                        break;
                }
            };
            _manageVm.DownloadProgressChanged += show => _dispatcher.Invoke(() =>
            {
                if (show) downloadCard.DataContext = _manageVm;
                downloadCard.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (!show) fileProgress.Visibility = Visibility.Collapsed;
            });
            _manageVm.FileProgressUpdated += p => _dispatcher.Invoke(() =>
            {
                fileProgress.Visibility = Visibility.Visible;
                if (p.CurrentFileTotalBytes > 0)
                {
                    fileProgress.IsIndeterminate = false;
                    fileProgress.Value = (double)p.CurrentFileDownloadedBytes / p.CurrentFileTotalBytes * 100.0;
                }
                else { fileProgress.IsIndeterminate = true; }
            });
            _manageVm.SettingsProvider = () => _settings;
            _downloadVm.DownloadProgressOverlayChanged += show => _dispatcher.Invoke(() =>
            {
                if (show) downloadCard.DataContext = _downloadVm;
                downloadCard.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (!show) { fileProgress.Visibility = Visibility.Collapsed; }
            });
            _downloadVm.FileProgressUpdated += p => _dispatcher.Invoke(() =>
            {
                fileProgress.Visibility = Visibility.Visible;
                if (p.CurrentFileTotalBytes > 0)
                {
                    fileProgress.IsIndeterminate = false;
                    fileProgress.Value = (double)p.CurrentFileDownloadedBytes / p.CurrentFileTotalBytes * 100.0;
                }
                else { fileProgress.IsIndeterminate = true; }
            });
            _downloadVm.SettingsProvider = () => _settings;
            _downloadVm.NavigateToModpackDownload += () => SwitchToPanel("modpack-download");

            // Modpack download navigation
            _modpackDownloadVm.BackRequested += () => SwitchToPanel("download");
            _modpackDownloadVm.DownloadProgressChanged += show => _dispatcher.Invoke(() =>
            {
                if (show) downloadCard.DataContext = _modpackDownloadVm;
                downloadCard.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (!show) fileProgress.Visibility = Visibility.Collapsed;
            });
            _modpackDownloadVm.FileProgressUpdated += p => _dispatcher.Invoke(() =>
            {
                fileProgress.Visibility = Visibility.Visible;
                if (p.CurrentFileTotalBytes > 0)
                {
                    fileProgress.IsIndeterminate = false;
                    fileProgress.Value = (double)p.CurrentFileDownloadedBytes / p.CurrentFileTotalBytes * 100.0;
                }
                else { fileProgress.IsIndeterminate = true; }
            });

            // Instance detail navigation
            _manageVm.NavigateToInstanceDetail += instanceName =>
            {
                _instanceDetailVm.LoadInstance(instanceName);
                SwitchToPanel("instance-detail");
            };
            _instanceDetailVm.BackRequested += () => SwitchToPanel("manage");

            // Mod management navigation
            _instanceDetailVm.NavigateToModManagement += (name, modsFolder, versionId, loaderType) =>
            {
                _modManagementVm.LoadMods(name, modsFolder, versionId, loaderType);
                SwitchToPanel("mod-management");
            };
            _modManagementVm.BackRequested += () => SwitchToPanel("instance-detail");

            // Resource & shader pack management navigation
            _instanceDetailVm.NavigateToResourcePacks += (name, rpFolder, spFolder, versionId) =>
            {
                _resourcePackVm.LoadPacks(name, rpFolder, spFolder, versionId);
                SwitchToPanel("resource-packs");
            };
            _resourcePackVm.BackRequested += () => SwitchToPanel("instance-detail");

            // Save lock detail navigation
            _instanceDetailVm.NavigateToSaveLockDetail += (instanceName, saveName, savePath, isLocked) =>
            {
                _saveLockDetailVm.LoadSaveDetails(instanceName, saveName, savePath, isLocked, _accountManager);
                SwitchToPanel("save-lock-detail");
            };
            _saveLockDetailVm.BackRequested += () => SwitchToPanel("instance-detail");

            // Schematic management navigation
            _instanceDetailVm.NavigateToSchematicManagement += (name, folder) =>
            {
                _schematicManagementVm.LoadSchematics(name, folder);
                SwitchToPanel("schematic-management");
            };
            _schematicManagementVm.BackRequested += () => SwitchToPanel("instance-detail");

            // Account skin navigation
            _accountVm.NavigateToAccountSkin += uuid =>
            {
                var account = _accountManager.Accounts.FirstOrDefault(a => a.UUID == uuid);
                if (account != null)
                {
                    _accountSkinVm.LoadAccount(account);
                    SwitchToPanel("account-skin");
                }
            };
            _accountSkinVm.BackRequested += () => SwitchToPanel("account");

            // Panel category → parent section (for settings-lock verification)
            _panelCategories["settings"] = "settings";
            _panelCategories["manage"] = "manage";
            _panelCategories["instance-detail"] = "manage";
            _panelCategories["mod-management"] = "manage";
            _panelCategories["resource-packs"] = "manage";
            _panelCategories["save-lock-detail"] = "manage";
            _panelCategories["schematic-management"] = "manage";
            _panelCategories["account"] = "account";
            _panelCategories["account-skin"] = "account";

            // Sidebar button map
            _sidebarButtons["home"] = sidebarHome;
            _sidebarButtons["download"] = sidebarDownload;
            _sidebarButtons["settings"] = sidebarSettings;
            _sidebarButtons["manage"] = sidebarManage;
            _sidebarButtons["account"] = sidebarAccount;
            _sidebarButtons["about"] = sidebarAbout;

            Loaded += async (s, e) =>
            {
                try { await InitializeAsync(); }
                catch (Exception ex) { ShowNotification("初始化失败：" + ex.Message, NotificationType.Error); }
            };

            Closed += (s, e) =>
            {
                _themeService.ThemeChanged -= OnThemeChanged;
                _launchOrchestrator.DisposeGameResources();
                _httpClient?.Dispose();
            };
        }

        // ==================== 鍒濆鍖?====================
        private async Task InitializeAsync()
        {
            // === 启动器自完整性校验 ===
            LauncherIntegrity.LogCallback = msg => _notificationService.AppendLog(msg, NotificationType.Warning);
            string? integrityWarning = LauncherIntegrity.Verify(_gameDir);
            if (integrityWarning != null)
            {
                bool trusted = await _dialogService.ShowConfirmAsync(integrityWarning, "安全警告");
                if (!trusted)
                {
                    Application.Current.Shutdown();
                    return;
                }
                LauncherIntegrity.UpdateBaseline(_gameDir);
            }

            LoadAccounts();
            ScanVersions();
            _settings = SettingsService.Load(_gameDir);
            ApplySettings();

            // 清理上次异常中断的备份/恢复残留
            CleanupFailedOperations();

            if (_settings.UseAutoDetectJava)
            {
                await DetectJavaAsync();
                RestoreJavaSelection();
            }
            else
            {
                RestoreJavaSelection();
            }
            _homeVm.SelectedJava = _settingsVm.SelectedJava;
            _homeVm.Memory = _settings.MemoryMB > 0 ? _settings.MemoryMB : 2048;
            _homeVm.SelectedResolution = _settings.Resolution;
            await _downloadVm.InitializeVersionListAsync();
            await _modpackDownloadVm.InitializeAsync();
            await _settingsVm.CheckWindowsHelloAvailabilityAsync();
            _settingsReady = true;

            // === 自动备份调度检查 ===
            if (_settings.BackupEnabled && !string.IsNullOrEmpty(_settings.BackupFolder))
            {
                if (!_launchOrchestrator.IsGameRunning
                    && BackupService.ShouldBackup(_settings.BackupInterval, _settings.LastBackupTime))
                {
                    _ = RunBackupAsync(isScheduled: true);
                }
            }
        }

        // ==================== 璐﹀彿绠＄悊 ====================
        private void LoadAccounts()
        {
            try
            {
                _accountManager.Load();
            }
            catch (Exception ex) { AppendLog("加载账号失败：" + ex.Message, NotificationType.Error); }
        }

        // ==================== 瀹炰緥绠＄悊 ====================
        private void ScanVersions()
        {
            _instanceManager.Scan((msg, type) => AppendLog(msg, type));
        }

        // ==================== 閫氱煡 / 鏃ュ織濮旀墭 ====================
        private void ShowNotification(string message, NotificationType type = NotificationType.Info, int durationMs = 3000)
            => _notificationService.Show(message, type, durationMs);

        private void AppendLog(string message, NotificationType type = NotificationType.Info)
            => _notificationService.AppendLog(message, type);

        private string? WriteDiagnosticLog(string title, string details, bool autoOpen = false)
            => _notificationService.WriteDiagnosticLog(title, details, autoOpen);

        private void CloseNotification_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int id)
                _notificationService.Remove(id);
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
            => _notificationService.ClearLog();

        private void LogEntry_Click(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is FrameworkElement fe && fe.DataContext is LogEntry entry)
            {
                try { _clipboard.Copy($"[{entry.TimeDisplay}] [{entry.TypeLabel}] {entry.Message}"); }
                catch { }
            }
        }

        // ==================== 渚ц竟鏍忓鑸?====================
        private void Sidebar_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string key)
                SwitchToPanel(key);
        }

        private async void SwitchToPanel(string key)
        {
            var category = _panelCategories.GetValueOrDefault(key);

            // 设置锁拦截
            if (category != null && _settings.SettingsLockEnabled)
            {
                bool sameCategory = category == _lastVerifiedCategory
                                 && (DateTime.UtcNow - _lastVerifyTime).TotalSeconds < VerifyCacheSeconds;

                if (!sameCategory)
                {
                    bool passed = await _windowsHello.VerifyAsync("需要验证身份以访问此页面");
                    if (!passed)
                        return; // 验证被拒绝，不跳转
                    _lastVerifiedCategory = category;
                    _lastVerifyTime = DateTime.UtcNow;
                }
            }

            // 导航到非保护页面时清除验证缓存
            if (category == null)
                _lastVerifiedCategory = null;

            _activePanel = key;
            pnlHome.Visibility = key == "home" ? Visibility.Visible : Visibility.Collapsed;
            pnlDownload.Visibility = key == "download" ? Visibility.Visible : Visibility.Collapsed;
            pnlSettings.Visibility = key == "settings" ? Visibility.Visible : Visibility.Collapsed;
            pnlManage.Visibility = key == "manage" ? Visibility.Visible : Visibility.Collapsed;
            pnlInstanceDetail.Visibility = key == "instance-detail" ? Visibility.Visible : Visibility.Collapsed;
            pnlModManagement.Visibility = key == "mod-management" ? Visibility.Visible : Visibility.Collapsed;
            pnlResourcePacks.Visibility = key == "resource-packs" ? Visibility.Visible : Visibility.Collapsed;
            pnlAccount.Visibility = key == "account" ? Visibility.Visible : Visibility.Collapsed;
            pnlAbout.Visibility = key == "about" ? Visibility.Visible : Visibility.Collapsed;
            pnlSaveLockDetail.Visibility = key == "save-lock-detail" ? Visibility.Visible : Visibility.Collapsed;
            pnlModpackDownload.Visibility = key == "modpack-download" ? Visibility.Visible : Visibility.Collapsed;
            pnlAccountSkin.Visibility = key == "account-skin" ? Visibility.Visible : Visibility.Collapsed;
            pnlSchematicManagement.Visibility = key == "schematic-management" ? Visibility.Visible : Visibility.Collapsed;
            HighlightSidebarItem(key is "instance-detail" or "mod-management" or "resource-packs" or "save-lock-detail" or "schematic-management" ? "manage"
                : key is "modpack-download" ? "download"
                : key is "account-skin" ? "account"
                : key);
        }

        private void HighlightSidebarItem(string activeKey)
        {
            foreach (var kv in _sidebarButtons)
            {
                bool isActive = kv.Key == activeKey;
                kv.Value.Background = isActive
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(_themeService.IsDark ? "#363752" : "#CCD2DD"))
                    : new SolidColorBrush(Colors.Transparent);
            }
        }

        // ==================== 涓婚 ====================
        private void ApplyTheme(bool isDark)
        {
            _themeService.SetTheme(isDark);
            WpfThemeHelper.ApplyColorScheme(Resources, _themeService.GetColorScheme());
            HighlightSidebarItem(_activePanel);
            btnThemeToggle.Content = isDark ? "浅色模式" : "深色模式";
        }

        private void OnThemeChanged(bool isDark)
        {
            WpfThemeHelper.ApplyColorScheme(Resources, _themeService.GetColorScheme());
            HighlightSidebarItem(_activePanel);
            btnThemeToggle.Content = isDark ? "浅色模式" : "深色模式";
        }

        private void BtnThemeToggle_Click(object sender, RoutedEventArgs e)
            => ApplyTheme(!_themeService.IsDark);

        // ==================== 澶辫触瀵硅瘽妗?====================
        private void ShowFailureDialog(string briefReason, string? logPath = null)
        {
            _lastLaunchLogPath = logPath ?? "";
            txtFailureReason.Text = briefReason;
            txtFailureLogPath.Text = string.IsNullOrEmpty(_lastLaunchLogPath) ? "" : "详细日志：" + _lastLaunchLogPath;
            pnlFailureDialog.Visibility = Visibility.Visible;
        }

        private void FailureDialogClose_Click(object sender, RoutedEventArgs e)
            => pnlFailureDialog.Visibility = Visibility.Collapsed;

        private void FailureDialogOpenLog_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_lastLaunchLogPath) && File.Exists(_lastLaunchLogPath))
            {
                try { Process.Start(new ProcessStartInfo(_lastLaunchLogPath) { UseShellExecute = true }); }
                catch { }
            }
        }

        // ==================== 璁剧疆 ====================
        private void ApplySettings()
        {
            _settingsVm.ApplySettings(_settings);
            _settingsVm.IsolateNewInstancesByDefault = _settings.IsolateNewInstancesByDefault;
            _homeVm.Memory = _settings.MemoryMB > 0 ? _settings.MemoryMB : 2048;
            _homeVm.SelectedResolution = _settings.Resolution;
            _settingsVm.SelectedResolution = _settings.Resolution;
            _settingsVm.LastBackupTime = _settings.LastBackupTime;
        }

        private void RestoreJavaSelection()
        {
            if (_settings.UseAutoDetectJava)
            {
                // 自动模式：在检测到的列表中按路径匹配
                if (!string.IsNullOrEmpty(_settings.JavaPath) && _settingsVm.JavaList.Count > 0)
                    _settingsVm.SelectedJava = _settingsVm.JavaList
                        .FirstOrDefault(j => j.Path == _settings.JavaPath);
            }
            else
            {
                // 手动模式：恢复手动路径，构建 JavaInfo
                _settingsVm.ManualJavaPath = _settings.ManualJavaPath;
                if (!string.IsNullOrEmpty(_settings.ManualJavaPath))
                {
                    _settingsVm.SelectedJava = new JavaInfo
                    {
                        Path = _settings.ManualJavaPath,
                        Version = "手动指定",
                        ShortVersion = System.IO.Path.GetFileName(_settings.ManualJavaPath),
                        MajorVersion = 0
                    };
                }
            }
        }

        /// <summary>清理上次异常中断的备份/恢复残留。</summary>
        private void CleanupFailedOperations()
        {
            foreach (var markerName in new[] { ".backup_in_progress", ".restore_in_progress" })
            {
                string markerPath = Path.Combine(_gameDir, markerName);
                if (!File.Exists(markerPath)) continue;

                try
                {
                    string? targetDir = File.ReadAllText(markerPath).Trim();
                    // 校验标记文件内容：目录必须在游戏目录内，防止篡改
                    if (string.IsNullOrEmpty(targetDir) || !PathSafetyHelper.IsContained(targetDir, _gameDir))
                    {
                        File.Delete(markerPath);
                        continue;
                    }
                    // 清理残留的 .eibak.tmp
                    if (!string.IsNullOrEmpty(targetDir) && Directory.Exists(targetDir))
                    {
                        var tmpFiles = Directory.GetFiles(targetDir, "*.eibak.tmp");
                        foreach (var tmp in tmpFiles)
                            try { File.Delete(tmp); } catch { }
                    }
                    // 清理可能的临时目录
                    var tempDirs = Directory.GetDirectories(Path.GetTempPath(), "eitrvo_backup_*");
                    foreach (var d in tempDirs)
                        try { Directory.Delete(d, true); } catch { }
                    tempDirs = Directory.GetDirectories(Path.GetTempPath(), "eitrvo_restore_*");
                    foreach (var d in tempDirs)
                        try { Directory.Delete(d, true); } catch { }

                    File.Delete(markerPath);
                    _notificationService.AppendLog($"检测到上次{markerName.Replace(".", "").Replace("_", " ")}操作异常中断，已清理残留文件。", NotificationType.Warning);
                }
                catch { /* best effort */ }
            }
        }

        private void SaveSettingsFromVm()
        {
            if (!_settingsReady) return;
            var s = _settingsVm.ToSettings();
            s.LastBackupTime = _settings.LastBackupTime;  // 保留备份时间（由 MainWindow 管理）
            SettingsService.Save(_gameDir, s);
            _settings = s;
        }

        // ==================== 备份 / 恢复 ====================

        private async Task RunBackupAsync(bool isScheduled = true)
        {
            var folder = _settings.BackupFolder;
            if (string.IsNullOrEmpty(folder)) return;

            _isBackupRunning = true;

            // 写备份标记（异常退出检测）
            string markerPath = Path.Combine(_gameDir, ".backup_in_progress");
            try { File.WriteAllText(markerPath, folder); } catch { }

            var dialog = new ProgressDialog("正在备份...");
            dialog.Owner = this;
            dialog.Show();

            using var cts = new CancellationTokenSource();
            dialog.Closed += (_, _) => cts.Cancel();

            try
            {
                var result = await _backupService.BackupAsync(
                    folder, _settings.ExcludeRedownloadable, cts.Token);

                if (result.Success)
                {
                    _settings.LastBackupTime = DateTimeOffset.UtcNow;
                    _settingsVm.LastBackupTime = _settings.LastBackupTime;
                    SaveSettingsFromVm();
                    _notificationService.Show("备份完成！", NotificationType.Success);
                }
                else if (result.Cancelled)
                {
                    _notificationService.Show("备份已取消。", NotificationType.Info);
                }
                else
                {
                    _notificationService.Show($"备份失败：{result.Error}", NotificationType.Error);
                    _notificationService.AppendLog($"备份失败：{result.Error}", NotificationType.Error);
                }
            }
            catch (Exception ex)
            {
                _notificationService.Show($"备份失败：{ex.Message}", NotificationType.Error);
            }
            finally
            {
                _dispatcher.Invoke(() =>
                {
                    try { dialog.Close(); } catch { }
                });
                try { File.Delete(markerPath); } catch { }
                _isBackupRunning = false;
            }
        }

        private async Task RunRestoreAsync(string filePath, RestoreMode mode)
        {
            if (!File.Exists(filePath))
            {
                _notificationService.Show("备份文件不存在。", NotificationType.Warning);
                return;
            }

            // 游戏运行时禁止恢复
            if (_launchOrchestrator.IsGameRunning)
            {
                _notificationService.Show("游戏正在运行，请先关闭游戏再恢复备份。", NotificationType.Warning);
                return;
            }

            _isBackupRunning = true;

            // 写恢复标记
            string markerPath = Path.Combine(_gameDir, ".restore_in_progress");
            try { File.WriteAllText(markerPath, filePath); } catch { }

            string phaseLabel = mode == RestoreMode.Clean ? "清理式恢复" : "覆盖式恢复";
            var dialog = new ProgressDialog($"正在{phaseLabel}...");
            dialog.Owner = this;
            dialog.Show();

            using var cts = new CancellationTokenSource();
            dialog.Closed += (_, _) => cts.Cancel();

            try
            {
                var result = await _backupService.RestoreAsync(filePath, mode, cts.Token);

                if (result.Success)
                {
                    // === 检查并补全缺失的 assets / libraries ===
                    var missingVersions = FindVersionsMissingAssets();
                    if (missingVersions.Count > 0)
                    {
                        try
                        {
                            _backupService.SetPhase?.Invoke("正在补全版本文件...");
                            var manifest = await _downloadService.LoadOnlineVersionsAsync(_httpClient);
                            var urlMap = manifest.ToDictionary(v => v.Id, v => v.Url);
                            int done = 0;
                            foreach (var (versionId, instanceName) in missingVersions)
                            {
                                if (urlMap.TryGetValue(versionId, out var url))
                                {
                                    await _downloadService.DownloadVersionFilesAsync(
                                        _httpClient, _gameDir, url, versionId, instanceName,
                                        new Progress<DownloadProgress>(),
                                        (msg, type, _) => _notificationService.AppendLog(msg, type));
                                }
                                done++;
                                _backupService.FileProgress?.Report((done, missingVersions.Count));
                            }
                            _notificationService.AppendLog($"已补全 {missingVersions.Count} 个版本的文件。", NotificationType.Success);
                        }
                        catch (Exception ex)
                        {
                            _notificationService.AppendLog($"补全版本文件失败：{ex.Message}", NotificationType.Error);
                        }
                    }
                    _notificationService.Show($"{phaseLabel}完成！建议重启启动器。", NotificationType.Success);
                }
                else if (result.Cancelled)
                {
                    _notificationService.Show("恢复已取消。", NotificationType.Info);
                }
                else
                {
                    _notificationService.Show($"恢复失败：{result.Error}", NotificationType.Error);
                    _notificationService.AppendLog($"恢复失败：{result.Error}", NotificationType.Error);
                }
            }
            catch (Exception ex)
            {
                _notificationService.Show($"恢复失败：{ex.Message}", NotificationType.Error);
            }
            finally
            {
                _dispatcher.Invoke(() =>
                {
                    try { dialog.Close(); } catch { }
                });
                try { File.Delete(markerPath); } catch { }
                _isBackupRunning = false;
            }
        }

        private List<(string versionId, string instanceName)> FindVersionsMissingAssets()
        {
            var result = new List<(string, string)>();
            var seenVersions = new HashSet<string>();
            string versionsDir = Path.Combine(_gameDir, "versions");
            if (!Directory.Exists(versionsDir)) return result;
            foreach (string dir in Directory.GetDirectories(versionsDir))
            {
                string jsonPath = Path.Combine(dir, "version.json");
                if (!File.Exists(jsonPath)) continue;
                try
                {
                    var detail = System.Text.Json.JsonSerializer.Deserialize<VersionDetail>(
                        File.ReadAllText(jsonPath));
                    if (detail == null) continue;
                    string mcVersion = detail.InheritsFrom ?? detail.Id ?? "";
                    if (string.IsNullOrEmpty(mcVersion)) continue;
                    string assetId = detail.AssetIndex?.Id ?? detail.Assets ?? mcVersion;
                    string assetIndexPath = Path.Combine(_gameDir, "assets", "indexes", $"{assetId}.json");
                    if (!File.Exists(assetIndexPath) && seenVersions.Add(mcVersion))
                        result.Add((mcVersion, Path.GetFileName(dir)));
                }
                catch { }
            }
            return result;
        }

        // ==================== Java 妫€娴?====================
        private async Task DetectJavaAsync()
        {
            await _settingsVm.DetectJavaCommand.ExecuteAsync(null);
            if (_settingsVm.JavaList.Count > 0)
                _settingsVm.SelectedJava = _settingsVm.JavaList[0];
        }


    }
}
