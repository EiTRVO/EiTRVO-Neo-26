using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Orchestrators;
using EiTRVO.UI.Panels;
using EiTRVO.ProEngine.Services;
using EiTRVO.ProEngine.ViewModels;

namespace EiTRVO.UI.ViewModels;

public partial class SaveLockDetailViewModel : BaseViewModel
{
    private readonly SaveLockService _saveLockService;
    private readonly LocalKeyStore _localKeyStore;
    private readonly SaveRecoveryFile _saveRecoveryFile;
    private readonly INotificationService _notificationService;
    private readonly IDialogService _dialogService;
    private readonly HttpClient _httpClient;
    private AccountManager? _accountManager;

    // === Observable Properties ===

    [ObservableProperty]
    private string _saveName = "";

    [ObservableProperty]
    private string _instanceName = "";

    [ObservableProperty]
    private bool _isLocked;

    [ObservableProperty]
    private string _lockModeDisplay = "";

    [ObservableProperty]
    private string _lockedAtDisplay = "";

    [ObservableProperty]
    private string _boundMsAccount = "";

    [ObservableProperty]
    private string _passwordHint = "";

    [ObservableProperty]
    private bool _oneDriveBackedUp;

    [ObservableProperty]
    private string _savencPath = "";

    [ObservableProperty]
    private string _saveFolderPath = "";

    [ObservableProperty]
    private string _sizeDisplay = "";

    [ObservableProperty]
    private string _infoDisplay = "";

    [ObservableProperty]
    private bool _showRetryOneDrive;

    [ObservableProperty]
    private bool _hasBoundAccount;

    [ObservableProperty]
    private bool _hasMsAccount;

    [ObservableProperty]
    private string _currentMsAccountEmail = "";

    private string? _currentMsUuid;

    /// <summary>解密存档是否需要密码验证（手动解密始终需要）</summary>
    public bool NeedsPasswordForDecrypt => IsLocked;

    // === Events ===

    public event Action? BackRequested;

    // === Constructor ===

    public SaveLockDetailViewModel(
        SaveLockService saveLockService,
        LocalKeyStore localKeyStore,
        SaveRecoveryFile saveRecoveryFile,
        INotificationService notificationService,
        IDialogService dialogService,
        HttpClient httpClient)
    {
        _saveLockService = saveLockService;
        _localKeyStore = localKeyStore;
        _saveRecoveryFile = saveRecoveryFile;
        _notificationService = notificationService;
        _dialogService = dialogService;
        _httpClient = httpClient;
    }

    /// <summary>
    /// 加载存档详情。由 MainWindow 在导航前调用。
    /// </summary>
    public void LoadSaveDetails(string instanceName, string saveName, string savePath,
        bool isLocked, AccountManager accountManager)
    {
        _accountManager = accountManager;
        InstanceName = instanceName;
        SaveName = saveName;
        IsLocked = isLocked;

        // 微软账户状态
        var msAccount = accountManager.Accounts.FirstOrDefault(
            a => a.Type == AccountType.Microsoft && !string.IsNullOrEmpty(a.MicrosoftRefreshToken));
        HasMsAccount = msAccount != null;
        CurrentMsAccountEmail = msAccount?.Username ?? "";
        _currentMsUuid = msAccount?.UUID;

        if (isLocked)
        {
            SavencPath = savePath;
            SaveFolderPath = "";

            try
            {
                var meta = _saveLockService.GetSaveLockMetadata(savePath);
                LockModeDisplay = meta.LockMode == SaveLockMode.Permanent ? "永久" : "一次性";
                LockedAtDisplay = meta.CreatedAt != DateTimeOffset.MinValue
                    ? meta.CreatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm")
                    : "";
                BoundMsAccount = CurrentMsAccountEmail;
                PasswordHint = meta.PasswordHint ?? "";
                OneDriveBackedUp = meta.OneDriveBackedUp;
                SizeDisplay = FormatByteSize(meta.OriginalSize);

                HasBoundAccount = !string.IsNullOrEmpty(BoundMsAccount);
                ShowRetryOneDrive = IsLocked && !OneDriveBackedUp && HasMsAccount;
                InfoDisplay = $"大小: {SizeDisplay}  ·  文件数: {meta.FileCount}  ·  锁定于 {LockedAtDisplay}";
            }
            catch
            {
                LockModeDisplay = "未知";
                InfoDisplay = "无法读取存档信息";
                ShowRetryOneDrive = false;
            }
        }
        else
        {
            SaveFolderPath = savePath;
            SavencPath = "";
            LockModeDisplay = "";
            LockedAtDisplay = "";
            BoundMsAccount = "";
            PasswordHint = "";
            OneDriveBackedUp = false;
            HasBoundAccount = false;
            ShowRetryOneDrive = false;

            try
            {
                long size = CalculateDirectorySize(savePath);
                SizeDisplay = FormatByteSize(size);
                InfoDisplay = $"大小: {SizeDisplay}  ·  未加密";
            }
            catch
            {
                SizeDisplay = "?";
                InfoDisplay = "未加密";
            }
        }
    }

    // === Commands ===

    [RelayCommand]
    private void GoBack() => BackRequested?.Invoke();

    [RelayCommand]
    private async Task LockSaveAsync()
    {
        if (IsLocked || string.IsNullOrEmpty(SaveFolderPath)) return;

        var dialog = new SaveLockDialog(SaveName, CurrentMsAccountEmail, HasMsAccount);
        bool? result = dialog.ShowDialog();
        if (result != true) return;

        if (string.IsNullOrEmpty(dialog.Password) || dialog.Password.Length < 6)
        {
            _notificationService.Show("密码至少需要 6 个字符。", NotificationType.Warning);
            return;
        }
        if (dialog.Password != dialog.ConfirmPassword)
        {
            _notificationService.Show("两次输入的密码不一致。", NotificationType.Warning);
            return;
        }

        var options = new SaveLockOptions
        {
            Password = dialog.Password,
            LockMode = dialog.LockMode,
            SaveLocalBackup = dialog.SaveLocalBackup,
            ExportRecoveryFile = dialog.ExportRecoveryFile,
            PasswordHint = dialog.PasswordHint,
            BoundMsAccount = CurrentMsAccountEmail,
            BoundMsUuid = _currentMsUuid
        };

        string savencOutputPath = SaveFolderPath + ".savenc";

        try
        {
            // 显示进度对话框防止用户在加密过程中关闭启动器
            var progressDialog = new ProgressDialog($"正在加密存档 \"{SaveName}\"...");
            var progress = new Progress<(int, int)>(update =>
                progressDialog.Dispatcher.Invoke(() => progressDialog.Report(update.Item1, update.Item2)));

            var lockTask = _saveLockService.LockSaveAsync(
                SaveFolderPath, savencOutputPath, dialog.Password, options, progress);
            _ = lockTask.ContinueWith(_ =>
                progressDialog.Dispatcher.Invoke(() => progressDialog.Close()),
                TaskScheduler.Default);

            progressDialog.ShowDialog();
            await lockTask; // 传播异常

            // 本地密钥备份 (.savkey)
            if (options.SaveLocalBackup)
            {
                var backupPath = await _dialogService.ShowSaveFileDialogAsync(
                    $"{SaveName}.savkey", "密钥备份文件|*.savkey", "保存密钥备份");
                if (backupPath != null)
                {
                    await ExportKeyBackupAsync(savencOutputPath, dialog.Password, backupPath);
                }
            }

            // 导出 .savrec
            if (options.ExportRecoveryFile && HasMsAccount)
            {
                await ExportRecoveryFileInternalAsync(savencOutputPath, dialog.Password);
            }

            BackRequested?.Invoke();
        }
        catch (Exception ex)
        {
            _notificationService.Show($"加密失败: {ex.Message}", NotificationType.Error);
        }
    }

    [RelayCommand]
    private async Task DecryptSaveAsync()
    {
        if (!IsLocked || string.IsNullOrEmpty(SavencPath)) return;

        // 使用 UnlockPanel（支持忘记密码恢复 + 元数据展示）
        var unlockVm = new UnlockViewModel(
            _saveLockService, _localKeyStore, _saveRecoveryFile,
            _accountManager!, _notificationService, _dialogService, _httpClient);

        var result = await unlockVm.ShowUnlockDialogAsync(new[] { SavencPath });
        if (!result.Success) return;

        string outputFolderPath = Path.ChangeExtension(SavencPath, null)!;

        try
        {
            // UnlockPanel 已验证密码 — 直接用 K 解密
            var firstEntry = result.DecryptedSaves.First();
            byte[] k = firstEntry.Value.Key;
            try
            {
                var progressDialog = new ProgressDialog($"正在解密存档 \"{SaveName}\"...");
                var progress = new Progress<(int, int)>(update =>
                    progressDialog.Dispatcher.Invoke(() => progressDialog.Report(update.Item1, update.Item2)));

                var unlockTask = _saveLockService.UnlockSaveWithKeyAsync(
                    SavencPath, outputFolderPath, k, deleteSavencAfter: true, progress);
                _ = unlockTask.ContinueWith(_ =>
                    progressDialog.Dispatcher.Invoke(() => progressDialog.Close()),
                    TaskScheduler.Default);

                progressDialog.ShowDialog();
                await unlockTask;

                _notificationService.Show("存档已解密。", NotificationType.Success);
                BackRequested?.Invoke();
            }
            finally
            {
                CryptographicOperations.ZeroMemory(k);
            }
        }
        catch (Exception ex)
        {
            _notificationService.Show($"解密失败: {ex.Message}", NotificationType.Error);
        }
    }

    [RelayCommand]
    private async Task RetryOneDriveBackupAsync()
    {
        if (!IsLocked || OneDriveBackedUp || string.IsNullOrEmpty(SavencPath)) return;

        var dialog = new PasswordPromptDialog("导出密钥备份", SaveName, InstanceName);
        if (dialog.ShowDialog() != true) return;

        var backupPath = await _dialogService.ShowSaveFileDialogAsync(
            $"{SaveName}.savkey", "密钥备份文件|*.savkey", "保存密钥备份");
        if (backupPath == null) return;

        try
        {
            await ExportKeyBackupAsync(SavencPath, dialog.Password, backupPath);
            OneDriveBackedUp = true;
            ShowRetryOneDrive = false;
        }
        catch (Exception ex)
        {
            _notificationService.Show($"导出失败: {ex.Message}", NotificationType.Error);
        }
    }

    [RelayCommand]
    private async Task ExportRecoveryFileAsync()
    {
        if (!IsLocked || string.IsNullOrEmpty(SavencPath)) return;

        var dialog = new PasswordPromptDialog("导出恢复文件", SaveName, InstanceName);
        if (dialog.ShowDialog() != true) return;

        try
        {
            if (!_saveLockService.VerifyPassword(SavencPath, dialog.Password))
            {
                _notificationService.Show("密码错误，请重新输入。", NotificationType.Warning);
                return;
            }

            await ExportRecoveryFileInternalAsync(SavencPath, dialog.Password);
        }
        catch (Exception ex)
        {
            _notificationService.Show($"导出失败: {ex.Message}", NotificationType.Error);
        }
    }

    // === 内部方法 ===

    private async Task ExportKeyBackupAsync(string savencPath, string password, string outputPath)
    {
        if (!_saveLockService.VerifyPassword(savencPath, password))
            throw new CryptographicException("密码错误");

        var meta = _saveLockService.GetSaveLockMetadata(savencPath);
        using var pbkdf2 = new Rfc2898DeriveBytes(
            password, meta.Salt, 100000, HashAlgorithmName.SHA256);
        byte[] k = pbkdf2.GetBytes(32);

        try
        {
            await _localKeyStore.ExportKeyToFileAsync(outputPath, InstanceName, SaveName, k);
            _notificationService.Show($"密钥备份已保存至 {outputPath}", NotificationType.Success);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(k);
        }
    }

    private async Task ExportRecoveryFileInternalAsync(string savencPath, string password)
    {
        var account = _accountManager?.Accounts.FirstOrDefault(
            a => a.Type == AccountType.Microsoft && !string.IsNullOrEmpty(a.MicrosoftRefreshToken));
        if (account == null || string.IsNullOrEmpty(account.UUID))
        {
            _notificationService.Show("需要登录微软账户才能导出恢复文件。", NotificationType.Warning);
            return;
        }

        var recoveryPath = await _dialogService.ShowSaveFileDialogAsync(
            $"{SaveName}.savrec", "恢复文件|*.savrec", "导出恢复文件");
        if (recoveryPath == null) return;

        var meta = _saveLockService.GetSaveLockMetadata(savencPath);
        using var pbkdf2 = new Rfc2898DeriveBytes(
            password, meta.Salt, 100000, HashAlgorithmName.SHA256);
        byte[] k = pbkdf2.GetBytes(32);

        try
        {
            await _saveRecoveryFile.ExportAsync(recoveryPath, InstanceName, SaveName, k, account.UUID);
            _notificationService.Show($"恢复文件已导出至 {recoveryPath}", NotificationType.Success);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(k);
        }
    }

    private static long CalculateDirectorySize(string path)
    {
        try
        {
            return Directory.GetFiles(path, "*", SearchOption.AllDirectories)
                .Sum(f =>
                {
                    try { return new FileInfo(f).Length; }
                    catch { return 0; }
                });
        }
        catch { return 0; }
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
