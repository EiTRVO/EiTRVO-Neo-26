using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Orchestrators;
using EiTRVO.UI.Panels;
using EiTRVO.ProEngine.Services;

namespace EiTRVO.UI.ViewModels;

/// <summary>
/// 协调 UnlockPanel 和 SaveRecoveryDialog 的交互逻辑。
/// 由 UI 层（MainWindow / SaveLockDetailViewModel）实例化。
/// </summary>
public class UnlockViewModel
{
    private readonly SaveLockService _saveLockService;
    private readonly LocalKeyStore _localKeyStore;
    private readonly SaveRecoveryFile _saveRecoveryFile;
    private readonly AccountManager _accountManager;
    private readonly INotificationService _notificationService;
    private readonly IDialogService _dialogService;
    private readonly HttpClient _httpClient;

    public UnlockViewModel(
        SaveLockService saveLockService,
        LocalKeyStore localKeyStore,
        SaveRecoveryFile saveRecoveryFile,
        AccountManager accountManager,
        INotificationService notificationService,
        IDialogService dialogService,
        HttpClient httpClient)
    {
        _saveLockService = saveLockService;
        _localKeyStore = localKeyStore;
        _saveRecoveryFile = saveRecoveryFile;
        _accountManager = accountManager;
        _notificationService = notificationService;
        _dialogService = dialogService;
        _httpClient = httpClient;
    }

    /// <summary>
    /// 显示解锁对话框（模态）。循环处理密码验证、重试、忘记密码恢复。
    /// </summary>
    /// <param name="savencPaths">要解锁的 .savenc 文件路径数组</param>
    /// <returns>UnlockResult — Success=true 时包含 K 字典</returns>
    public async Task<UnlockResult> ShowUnlockDialogAsync(string[] savencPaths)
    {
        if (savencPaths.Length == 0)
            return new UnlockResult { Success = false };

        // 读取所有 .savenc 的 metadata
        var metaList = new List<(string Path, string SaveName, SaveLockMetadata Meta)>();
        foreach (string path in savencPaths)
        {
            try
            {
                var meta = _saveLockService.GetSaveLockMetadata(path);
                string name = string.IsNullOrEmpty(meta.SaveName)
                    ? System.IO.Path.GetFileNameWithoutExtension(path)
                    : meta.SaveName;
                metaList.Add((path, name, meta));
            }
            catch
            {
                _notificationService.Show($"无法读取存档文件: {path}", NotificationType.Warning);
            }
        }

        if (metaList.Count == 0)
            return new UnlockResult { Success = false };

        // 微软账户状态 — 收集所有有 refresh token 的 MS 账户（多账户兼容）
        var msAccounts = _accountManager.Accounts
            .Where(a => a.Type == AccountType.Microsoft && !string.IsNullOrEmpty(a.MicrosoftRefreshToken))
            .ToList();
        bool hasMsAccount = msAccounts.Count > 0;
        string msAccountEmail = msAccounts.FirstOrDefault()?.Username ?? "";

        // 循环：允许密码错误后重试，或走恢复流程
        while (true)
        {
            // 显示 UnlockPanel
            var unlockPanel = new UnlockPanel(metaList, hasMsAccount, msAccountEmail);
            bool? panelResult = unlockPanel.ShowDialog();

            if (unlockPanel.ForgotPasswordClicked)
            {
                // 忘记密码 → 恢复流程
                var recoveredKey = await HandleForgotPasswordAsync(
                    metaList, hasMsAccount, msAccounts);
                if (recoveredKey != null)
                {
                    var result = new UnlockResult { Success = true };
                    foreach (var (path, name, meta) in metaList)
                    {
                        result.DecryptedSaves[name] = (recoveredKey, meta.LockMode);
                    }
                    return result;
                }
                // 恢复失败 → 继续循环（回到密码输入）
                continue;
            }

            if (panelResult != true) // 取消
                return new UnlockResult { Success = false };

            // 密码验证
            string password = unlockPanel.Password;
            if (string.IsNullOrEmpty(password))
                continue;

            bool allValid = true;
            var decryptedSaves = new Dictionary<string, (byte[] Key, SaveLockMode Mode)>();

            foreach (var (path, name, meta) in metaList)
            {
                try
                {
                    if (!_saveLockService.VerifyPassword(path, password))
                    {
                        allValid = false;
                        break;
                    }

                    // 派生 K
                    using var pbkdf2 = new Rfc2898DeriveBytes(
                        password, meta.Salt, 100000, HashAlgorithmName.SHA256);
                    byte[] k = pbkdf2.GetBytes(32);
                    decryptedSaves[name] = (k, meta.LockMode);
                }
                catch
                {
                    allValid = false;
                    break;
                }
            }

            if (!allValid)
            {
                // 密码错误 — 清理已派生的 K
                foreach (var (k, _) in decryptedSaves.Values)
                    CryptographicOperations.ZeroMemory(k);
                decryptedSaves.Clear();

                _notificationService.Show("密码错误，请重新输入。", NotificationType.Warning);
                continue; // 回到密码输入
            }

            // 密码正确
            return new UnlockResult { Success = true, DecryptedSaves = decryptedSaves };
        }
    }

    /// <summary>处理忘记密码恢复流程。返回恢复得到的 K，失败返回 null。</summary>
    private async Task<byte[]?> HandleForgotPasswordAsync(
        List<(string Path, string SaveName, SaveLockMetadata Meta)> metaList,
        bool hasMsAccount, List<Account> msAccounts)
    {
        // 显示恢复对话框
        var recoveryDialog = new SaveRecoveryDialog(msAccounts.FirstOrDefault()?.Username);
        if (recoveryDialog.ShowDialog() != true)
            return null;

        if (recoveryDialog.SelectedMethod == RecoveryMethod.KeyBackup)
        {
            return await RecoverFromKeyBackupAsync(metaList);
        }
        else
        {
            if (!hasMsAccount || msAccounts.Count == 0)
            {
                _notificationService.Show(
                    "需要登录微软账户才能使用 .savrec 恢复文件。",
                    NotificationType.Warning);
                return null;
            }
            return await RecoverFromFileAsync(metaList, msAccounts);
        }
    }

    /// <summary>从 .savkey 密钥备份文件恢复</summary>
    private async Task<byte[]?> RecoverFromKeyBackupAsync(
        List<(string Path, string SaveName, SaveLockMetadata Meta)> metaList)
    {
        var fileDialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择密钥备份文件",
            Filter = "密钥备份文件|*.savkey|所有文件|*.*",
            CheckFileExists = true
        };

        if (fileDialog.ShowDialog() != true)
            return null;

        byte[]? key = await _localKeyStore.ImportKeyFromFileAsync(fileDialog.FileName);
        if (key == null)
        {
            _notificationService.Show(
                "密钥备份文件无效或不属于当前 Windows 用户。",
                NotificationType.Warning);
            return null;
        }

        foreach (var (_, _, meta) in metaList)
        {
            if (!ValidateKeyAgainstMetadata(key, meta))
            {
                _notificationService.Show(
                    "备份文件中的密钥无法解密此存档。",
                    NotificationType.Error);
                return null;
            }
        }

        return key;
    }

    /// <summary>从 .savrec 文件恢复密钥。遍历所有已登录 MS 账户，任意一个 UUID 匹配即成功。</summary>
    private async Task<byte[]?> RecoverFromFileAsync(
        List<(string Path, string SaveName, SaveLockMetadata Meta)> metaList, List<Account> msAccounts)
    {
        // 浏览 .savrec 文件
        var fileDialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择恢复文件",
            Filter = "恢复文件|*.savrec|所有文件|*.*",
            CheckFileExists = true
        };

        if (fileDialog.ShowDialog() != true)
            return null;

        string recoveryPath = fileDialog.FileName;

        // 遍历所有 MS 账户尝试恢复（多账户兼容）
        foreach (var account in msAccounts)
        {
            if (string.IsNullOrEmpty(account.UUID)) continue;

            var importResult = await _saveRecoveryFile.ImportAsync(recoveryPath, account.UUID);
            if (importResult == null) continue;

            var (key, saveName, _) = importResult.Value;

            // 验证 K 能解密 .savenc (KeyCheck)
            foreach (var (path, _, meta) in metaList)
            {
                if (!ValidateKeyAgainstMetadata(key, meta))
                {
                    _notificationService.Show(
                        "恢复文件中的密钥无法解密此存档。",
                        NotificationType.Error);
                    return null;
                }
            }

            return key;
        }

        // 所有账户都未匹配
        _notificationService.Show(
            "恢复文件无效或不属于当前登录的微软账户。",
            NotificationType.Warning);
        return null;
    }

    /// <summary>验证 K 是否匹配 .savenc 元数据中的 KeyCheck</summary>
    private static bool ValidateKeyAgainstMetadata(byte[] key, SaveLockMetadata meta)
    {
        try
        {
            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = key;
            aes.IV = meta.Salt.AsSpan(0, 16).ToArray();

            using var decryptor = aes.CreateDecryptor();
            byte[] decrypted = decryptor.TransformFinalBlock(meta.KeyCheck, 0, meta.KeyCheck.Length);
            string result = System.Text.Encoding.UTF8.GetString(decrypted);
            return result == "SAVELOCK_OK";
        }
        catch
        {
            return false;
        }
    }
}
