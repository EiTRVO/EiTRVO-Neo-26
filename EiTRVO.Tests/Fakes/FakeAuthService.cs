using System.Net.Http;
using System.Threading;
using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Services;

namespace EiTRVO.Tests.Fakes;

/// <summary>Test-only IAuthService — throws on any real call.</summary>
public class FakeAuthService : IAuthService
{
    public Task<Account> StartDeviceCodeFlowAsync(
        HttpClient httpClient, CancellationTokenSource cts,
        Action<string, string, string> showDeviceCodeUI, Action<string> updateStatus, Action hideUI)
        => throw new NotSupportedException();

    public Task<(string mcToken, string playerName, string playerUuid, Account updatedAccount)>
        RefreshMicrosoftAccessAsync(HttpClient httpClient, Account account, Action saveAccountsCallback)
        => throw new NotSupportedException();

    public Task<Account> AuthenticateYggdrasilAsync(
        HttpClient httpClient, string serverUrl, string email, string password,
        CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<Account> RefreshYggdrasilAsync(HttpClient httpClient, Account account,
        CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task DownloadAuthlibInjectorAsync(HttpClient httpClient, string gameDir,
        CancellationToken ct = default)
        => throw new NotSupportedException();
}
