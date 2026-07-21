using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Orchestrators;
using EiTRVO.ProEngine.Services;

namespace EiTRVO.ProEngine.ViewModels;

public partial class AccountViewModel : BaseViewModel
{
    private readonly IAuthService _authService;
    private readonly AccountManager _accountManager;
    private readonly HttpClient _httpClient;
    private readonly INotificationService _notificationService;
    private readonly IDialogService _dialogService;
    private readonly IClipboardService _clipboardService;
    private readonly IProcessService _processService;

    private CancellationTokenSource? _deviceCodeCts;
    private readonly object _ctsLock = new();

    // === Observable Properties ===

    [ObservableProperty]
    private bool _isAddingAccount;

    [ObservableProperty]
    private string _verificationUri = "";

    [ObservableProperty]
    private string _userCode = "";

    [ObservableProperty]
    private string _deviceStatus = "";

    [ObservableProperty]
    private bool _isDeviceCodeVisible;

    // === Yggdrasil form ===

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsYggdrasilFormVisible))]
    private bool _isAddingYggdrasilAccount;

    [ObservableProperty]
    private string _yggdrasilServerUrl = "";

    [ObservableProperty]
    private string _yggdrasilEmail = "";

    [ObservableProperty]
    private string _yggdrasilPassword = "";

    [ObservableProperty]
    private string _yggdrasilStatus = "";

    public bool IsYggdrasilFormVisible => IsAddingYggdrasilAccount;

    /// <summary>Raised when the user clicks "管理" on an account card. Parameter is the account UUID.</summary>
    public event Action<string>? NavigateToAccountSkin;

    /// <summary>Shared reference to AccountManager's accounts collection.</summary>
    public ObservableCollection<Account> Accounts => _accountManager.Accounts;

    public AccountViewModel(
        IAuthService authService,
        AccountManager accountManager,
        HttpClient httpClient,
        INotificationService notificationService,
        IDialogService dialogService,
        IClipboardService clipboardService,
        IProcessService processService)
    {
        _authService = authService;
        _accountManager = accountManager;
        _httpClient = httpClient;
        _notificationService = notificationService;
        _dialogService = dialogService;
        _clipboardService = clipboardService;
        _processService = processService;
    }

    // === Microsoft 账号 ===

    [RelayCommand]
    private async Task AddMicrosoftAsync()
    {
        if (IsAddingAccount) return;
        IsAddingAccount = true;

        CancellationTokenSource cts;
        lock (_ctsLock)
        {
            _deviceCodeCts?.Cancel();
            _deviceCodeCts?.Dispose();
            _deviceCodeCts = new CancellationTokenSource();
            cts = _deviceCodeCts;
        }

        try
        {

            var newAccount = await _authService.StartDeviceCodeFlowAsync(
                _httpClient, cts,
                (uri, code, status) =>
                {
                    VerificationUri = uri;
                    UserCode = code;
                    DeviceStatus = status;
                    IsDeviceCodeVisible = true;
                },
                status => DeviceStatus = status,
                () =>
                {
                    IsDeviceCodeVisible = false;
                    UserCode = "";
                    VerificationUri = "";
                    DeviceStatus = "";
                });

            if (newAccount != null)
            {
                var existing = _accountManager.Accounts.FirstOrDefault(a => a.UUID == newAccount.UUID);
                if (existing != null)
                {
                    existing.Username = newAccount.Username;
                    existing.MicrosoftRefreshToken = newAccount.MicrosoftRefreshToken;
                    existing.LastUsed = newAccount.LastUsed;
                }
                else
                {
                    _accountManager.Accounts.Add(newAccount);
                }
                _accountManager.Save();
                _notificationService.Show($"微软账号 {newAccount.Username} 登录成功！", NotificationType.Success);
            }
        }
        catch (Exception ex)
        {
            var detail = new System.Text.StringBuilder();
            var current = ex;
            while (current != null)
            {
                if (detail.Length > 0) detail.Append(" ← ");
                detail.Append($"{current.GetType().Name}: {current.Message}");
                current = current.InnerException;
            }
            _notificationService.WriteDiagnosticLog("认证失败", detail.ToString());
            _notificationService.Show($"认证失败：{ex.Message}", NotificationType.Error);
        }
        finally
        {
            IsAddingAccount = false;
            IsDeviceCodeVisible = false;
        }
    }

    [RelayCommand]
    private void CancelAuth()
    {
        lock (_ctsLock)
        {
            _deviceCodeCts?.Cancel();
        }
        IsDeviceCodeVisible = false;
        UserCode = "";
        VerificationUri = "";
        DeviceStatus = "";
    }

    [RelayCommand]
    private void CopyCode()
    {
        var code = UserCode;
        if (!string.IsNullOrWhiteSpace(code))
        {
            _clipboardService.Copy(code);
            _notificationService.Show("验证码已复制到剪贴板。", NotificationType.Success);
        }
    }

    [RelayCommand]
    private void OpenBrowser()
    {
        var url = VerificationUri;
        if (!string.IsNullOrWhiteSpace(url))
        {
            try
            {
                _processService.OpenUrl(url);
            }
            catch (Exception ex)
            {
                _notificationService.WriteDiagnosticLog("打开浏览器失败", ex.ToString());
                _notificationService.Show($"打开浏览器失败：{ex.Message}", NotificationType.Error);
            }
        }
    }

    // === Yggdrasil 账号 ===

    [RelayCommand]
    private void ToggleYggdrasilForm()
    {
        IsAddingYggdrasilAccount = !IsAddingYggdrasilAccount;
        YggdrasilStatus = "";
    }

    [RelayCommand]
    private void CancelYggdrasil()
    {
        IsAddingYggdrasilAccount = false;
        YggdrasilStatus = "";
    }

    [RelayCommand]
    private async Task YggdrasilLoginAsync()
    {
        string? serverUrl = YggdrasilServerUrl.Trim();
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            _notificationService.Show("请输入验证服务器 URL。", NotificationType.Warning);
            return;
        }

        // 强制使用 HTTPS（安全策略 — 防止凭证明文传输）
        if (!serverUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            _notificationService.Show("验证服务器 URL 必须以 https:// 开头。", NotificationType.Warning);
            return;
        }

        string email = YggdrasilEmail.Trim();
        if (string.IsNullOrWhiteSpace(email))
        {
            _notificationService.Show("请输入邮箱。", NotificationType.Warning);
            return;
        }

        string password = YggdrasilPassword;
        if (string.IsNullOrWhiteSpace(password))
        {
            _notificationService.Show("请输入密码。", NotificationType.Warning);
            return;
        }

        YggdrasilStatus = "正在连接验证服务器...";

        try
        {
            var newAccount = await _authService.AuthenticateYggdrasilAsync(
                _httpClient, serverUrl, email, password);

            if (newAccount != null)
            {
                var existing = _accountManager.Accounts.FirstOrDefault(a => a.UUID == newAccount.UUID);
                if (existing != null)
                {
                    existing.Type = AccountType.Yggdrasil;
                    existing.Username = newAccount.Username;
                    existing.YggdrasilServerUrl = newAccount.YggdrasilServerUrl;
                    existing.YggdrasilEmail = newAccount.YggdrasilEmail;
                    existing.YggdrasilEncryptedPassword = newAccount.YggdrasilEncryptedPassword;
                    existing.YggdrasilAccessToken = newAccount.YggdrasilAccessToken;
                    existing.YggdrasilClientToken = newAccount.YggdrasilClientToken;
                    existing.LastUsed = newAccount.LastUsed;
                }
                else
                {
                    _accountManager.Accounts.Add(newAccount);
                }
                _accountManager.Save();
                _notificationService.Show($"Yggdrasil 账号 {newAccount.Username} 登录成功！", NotificationType.Success);

                IsAddingYggdrasilAccount = false;
                YggdrasilStatus = "";
                YggdrasilPassword = "";
            }
        }
        catch (Exception ex)
        {
            // Sanitized: only log exception chain messages, no stack traces or raw server responses
            var detail = new System.Text.StringBuilder();
            var current = ex;
            while (current != null)
            {
                if (detail.Length > 0) detail.Append(" ← ");
                detail.Append($"{current.GetType().Name}: {current.Message}");
                current = current.InnerException;
            }
            _notificationService.WriteDiagnosticLog("Yggdrasil 认证失败", detail.ToString());
            YggdrasilStatus = ex.Message;
            _notificationService.Show($"Yggdrasil 认证失败：{ex.Message}", NotificationType.Error);
        }
    }

    // === 皮肤管理 ===

    [RelayCommand]
    private void ManageAccount(string uuid)
    {
        NavigateToAccountSkin?.Invoke(uuid);
    }

    /// <summary>手动刷新微软账号 Token（仅 Microsoft 账号）。</summary>
    [RelayCommand]
    private async Task RefreshToken(string uuid)
    {
        var account = _accountManager.Accounts.FirstOrDefault(a => a.UUID == uuid);
        if (account == null || account.Type != AccountType.Microsoft)
        {
            _notificationService.Show("此账号不是微软账号，无需刷新 Token。", NotificationType.Info);
            return;
        }

        try
        {
            _notificationService.Show("正在刷新 Token…", NotificationType.Info, 2000);
            var (_, _, _, _) = await _authService.RefreshMicrosoftAccessAsync(
                _httpClient, account, () => _accountManager.Save());
            _notificationService.Show($"Token 刷新成功（{account.Username}）。", NotificationType.Success);
        }
        catch (Exception ex)
        {
            _notificationService.Show($"Token 刷新失败：{ex.Message}", NotificationType.Error);
        }
    }

    // === 删除账号 ===

    [RelayCommand]
    private async Task RemoveAccount(string uuid)
    {
        var account = _accountManager.Accounts.FirstOrDefault(a => a.UUID == uuid);
        if (account == null) return;

        if (!await _dialogService.ShowConfirmAsync($"确定要删除账号 {account.Username} 吗？\n删除后需重新认证才能恢复。", "确认删除"))
            return;

        _accountManager.Accounts.Remove(account);
        _accountManager.Save();
        _notificationService.Show($"账号 {account.Username} 已删除。", NotificationType.Success);
    }
}
