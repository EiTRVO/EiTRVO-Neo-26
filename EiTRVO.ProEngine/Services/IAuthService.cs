using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using EiTRVO.ProEngine.Models;

namespace EiTRVO.ProEngine.Services;

public interface IAuthService
{
    // ==================== Microsoft OAuth ====================

    Task<Account> StartDeviceCodeFlowAsync(
        HttpClient httpClient,
        CancellationTokenSource cts,
        Action<string, string, string> showDeviceCodeUI,
        Action<string> updateStatus,
        Action hideUI);

    Task<(string mcToken, string playerName, string playerUuid, Account updatedAccount)>
        RefreshMicrosoftAccessAsync(HttpClient httpClient, Account account,
            Action saveAccountsCallback);

    // ==================== Yggdrasil 第三方验证 ====================

    /// <summary>
    /// 用 Yggdrasil 验证服务器登录，返回填充好的 Account。
    /// serverUrl 应去掉尾部斜杠。
    /// </summary>
    Task<Account> AuthenticateYggdrasilAsync(
        HttpClient httpClient, string serverUrl, string email, string password,
        CancellationToken ct = default);

    /// <summary>
    /// 刷新 Yggdrasil access token 并更新账号信息。
    /// </summary>
    Task<Account> RefreshYggdrasilAsync(
        HttpClient httpClient, Account account,
        CancellationToken ct = default);

    /// <summary>
    /// 下载 authlib-injector.jar 到 &lt;gameDir&gt;/authlib-injector/（如果尚不存在）。
    /// </summary>
    Task DownloadAuthlibInjectorAsync(
        HttpClient httpClient, string gameDir,
        CancellationToken ct = default);
}
