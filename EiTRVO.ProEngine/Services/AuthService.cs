using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EiTRVO.ProEngine.Models;
using static EiTRVO.ProEngine.Helpers.Endpoints;

namespace EiTRVO.ProEngine.Services;

public class AuthService : IAuthService
{
    public const string MICROSOFT_CLIENT_ID = "5a0b94a6-2810-4a43-a722-ba15271955b4";

    public async Task<Account> StartDeviceCodeFlowAsync(
        HttpClient httpClient,
        CancellationTokenSource cts,
        Action<string, string, string> showDeviceCodeUI,
        Action<string> updateStatus,
        Action hideUI)
    {
        var deviceCodeRequest = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("client_id", MICROSOFT_CLIENT_ID),
            new KeyValuePair<string, string>("scope", "XboxLive.signin offline_access")
        });

        HttpResponseMessage deviceCodeResponse;
        try
        {
            deviceCodeResponse = await httpClient.PostAsync(
                MicrosoftDeviceCode,
                deviceCodeRequest, cts.Token);
        }
        catch (OperationCanceledException) { throw; }

        var deviceCodeJson = await deviceCodeResponse.Content.ReadAsStringAsync(cts.Token);
        if (!deviceCodeResponse.IsSuccessStatusCode)
                throw new Exception($"获取设备代码失败（{deviceCodeResponse.StatusCode}）。");

        var deviceCode = JsonSerializer.Deserialize<DeviceCodeResponse>(deviceCodeJson)
            ?? throw new Exception("无法解析设备代码响应。");

        showDeviceCodeUI(deviceCode.VerificationUri, deviceCode.UserCode, "等待用户在浏览器中完成授权...");

        var tokenRequest = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("client_id", MICROSOFT_CLIENT_ID),
            new KeyValuePair<string, string>("grant_type", "urn:ietf:params:oauth:grant-type:device_code"),
            new KeyValuePair<string, string>("device_code", deviceCode.DeviceCode)
        });

        TokenResponse? token = null;
        int interval = deviceCode.Interval > 0 ? deviceCode.Interval : 5;
        int maxAttempts = (deviceCode.ExpiresIn / interval) + 10;
        int consecutiveFailures = 0;

        for (int i = 0; i < maxAttempts; i++)
        {
            cts.Token.ThrowIfCancellationRequested();

            if (i > 0)
            {
                try { await Task.Delay(interval * 1000, cts.Token); }
                catch (OperationCanceledException) { throw; }
            }

            try
            {
                var tokenResponse = await httpClient.PostAsync(
                    MicrosoftToken,
                    tokenRequest, cts.Token);
                var tokenJson = await tokenResponse.Content.ReadAsStringAsync(cts.Token);

                if (tokenResponse.IsSuccessStatusCode)
                {
                    token = JsonSerializer.Deserialize<TokenResponse>(tokenJson);
                    break;
                }

                var errorElem = JsonSerializer.Deserialize<JsonElement>(tokenJson);
                if (errorElem.TryGetProperty("error", out var err))
                {
                    string errStr = err.GetString() ?? "";
                    if (errStr == "authorization_pending")
                    {
                        consecutiveFailures = 0;
                        updateStatus("等待用户授权中...");
                        continue;
                    }
                    if (errStr == "slow_down")
                    {
                        consecutiveFailures = 0;
                        interval++;
                        updateStatus("请求过于频繁，正在减速...");
                        continue;
                    }
                    if (errStr == "expired_token")
                        throw new Exception("设备代码已过期，请重新添加。");
                    throw new Exception($"认证错误: {errStr}");
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (ex.Message.Contains("设备代码已过期") || ex.Message.Contains("认证错误"))
                    throw;
                consecutiveFailures++;
                if (consecutiveFailures >= 3)
                    throw new Exception($"设备代码轮询连续 {consecutiveFailures} 次网络错误，请检查网络连接。", ex);
                updateStatus($"网络错误，正在重试 ({consecutiveFailures}/3)...");
            }
        }

        if (token == null)
            throw new Exception("设备代码认证超时，请重试。");

        updateStatus("认证成功，正在获取 Xbox Live 令牌...");

        var xblAuthReq = new XblAuthRequest
        {
            Properties = new XblProperties
            {
                RpsTicket = $"d={token.AccessToken}"
            }
        };
        var xblContent = new StringContent(
            JsonSerializer.Serialize(xblAuthReq),
            Encoding.UTF8,
            "application/json");

        XblAuthResponse xblAuth;
        try
        {
            var xblResp = await httpClient.PostAsync(
                XboxLiveAuth,
                xblContent, cts.Token);
            var xblJson = await xblResp.Content.ReadAsStringAsync(cts.Token);
            if (!xblResp.IsSuccessStatusCode)
                throw new Exception($"Xbox Live 认证失败（{xblResp.StatusCode}）。");
            xblAuth = JsonSerializer.Deserialize<XblAuthResponse>(xblJson)
                ?? throw new Exception("无法解析 Xbox Live 响应。");
        }
        catch (OperationCanceledException) { hideUI(); throw; }

        updateStatus("正在获取 XSTS 令牌...");

        var xstsAuthReq = new XstsAuthRequest
        {
            Properties = new XstsProperties
            {
                UserTokens = new List<string> { xblAuth.Token }
            }
        };
        var xstsContent = new StringContent(
            JsonSerializer.Serialize(xstsAuthReq),
            Encoding.UTF8,
            "application/json");

        XblAuthResponse xstsAuth;
        try
        {
            var xstsResp = await httpClient.PostAsync(
                XstsAuth,
                xstsContent, cts.Token);
            var xstsJson = await xstsResp.Content.ReadAsStringAsync(cts.Token);
            if (!xstsResp.IsSuccessStatusCode)
                throw new Exception($"XSTS 认证失败（{xstsResp.StatusCode}）。");
            xstsAuth = JsonSerializer.Deserialize<XblAuthResponse>(xstsJson)
                ?? throw new Exception("无法解析 XSTS 响应。");
        }
        catch (OperationCanceledException) { hideUI(); throw; }

        string uhs = xstsAuth?.DisplayClaims?.Xui?.FirstOrDefault()?.Uhs
            ?? throw new Exception("无法获取用户哈希 (UHS)。");

        updateStatus("正在获取 Minecraft 令牌...");

        var mcAuthReq = new McAuthRequest
        {
            IdentityToken = $"XBL3.0 x={uhs};{xstsAuth.Token}"
        };
        var mcContent = new StringContent(
            JsonSerializer.Serialize(mcAuthReq),
            Encoding.UTF8,
            "application/json");

        string mcAccessToken;
        try
        {
            var mcResp = await httpClient.PostAsync(
                MinecraftAuth,
                mcContent, cts.Token);
            var mcJson = await mcResp.Content.ReadAsStringAsync(cts.Token);
            if (!mcResp.IsSuccessStatusCode)
                throw new Exception($"Minecraft 认证失败（{mcResp.StatusCode}）。");
            var mcAuth = JsonSerializer.Deserialize<McAuthResponse>(mcJson)
                ?? throw new Exception("无法解析 Minecraft 认证响应。");
            mcAccessToken = mcAuth.AccessToken;
        }
        catch (OperationCanceledException) { hideUI(); throw; }

        updateStatus("正在获取 Minecraft 玩家信息...");

        McProfileResponse profile;
        try
        {
            var profileReq = new HttpRequestMessage(HttpMethod.Get,
                MinecraftProfile);
            profileReq.Headers.Add("Authorization", $"Bearer {mcAccessToken}");
            var profileResp = await httpClient.SendAsync(profileReq, cts.Token);
            var profileJson = await profileResp.Content.ReadAsStringAsync(cts.Token);
            if (!profileResp.IsSuccessStatusCode)
                throw new Exception($"获取玩家信息失败（{profileResp.StatusCode}）。");
            profile = JsonSerializer.Deserialize<McProfileResponse>(profileJson)
                ?? throw new Exception("无法解析玩家信息。");
        }
        catch (OperationCanceledException) { hideUI(); throw; }

        hideUI();

        return new Account
        {
            Username = profile.Name,
            UUID = profile.Id,
            MicrosoftRefreshToken = token.RefreshToken,
            LastUsed = DateTime.Now.ToString("O")
        };
    }

    public async Task<(string mcToken, string playerName, string playerUuid, Account updatedAccount)>
        RefreshMicrosoftAccessAsync(HttpClient httpClient, Account account,
            Action saveAccountsCallback)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var refreshRequest = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("client_id", MICROSOFT_CLIENT_ID),
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", account.MicrosoftRefreshToken ?? "")
        });

        var refreshResp = await httpClient.PostAsync(
            MicrosoftToken,
            refreshRequest, cts.Token);
        var refreshJson = await refreshResp.Content.ReadAsStringAsync(cts.Token);
        if (!refreshResp.IsSuccessStatusCode)
            throw new Exception($"刷新令牌失败（{refreshResp.StatusCode}），请重新添加微软账号。");

        var newToken = JsonSerializer.Deserialize<TokenResponse>(refreshJson)
            ?? throw new Exception("无法解析令牌刷新响应。");

        if (!string.IsNullOrWhiteSpace(newToken.RefreshToken))
            account.MicrosoftRefreshToken = newToken.RefreshToken;
        account.LastUsed = DateTime.Now.ToString("O");
        // saveAccountsCallback() deferred to after full XBL→XSTS→Minecraft auth chain succeeds

        var xblReq = new XblAuthRequest
        {
            Properties = new XblProperties
            {
                RpsTicket = $"d={newToken.AccessToken}"
            }
        };
        var xblContent = new StringContent(JsonSerializer.Serialize(xblReq), Encoding.UTF8, "application/json");
        var xblResp = await httpClient.PostAsync(
            XboxLiveAuth, xblContent, cts.Token);
        var xblJson = await xblResp.Content.ReadAsStringAsync(cts.Token);
        if (!xblResp.IsSuccessStatusCode)
            throw new Exception($"Xbox Live 认证失败（{xblResp.StatusCode}）。");
        var xblAuth = JsonSerializer.Deserialize<XblAuthResponse>(xblJson)
            ?? throw new Exception("无法解析 Xbox Live 响应。");

        var xstsReq = new XstsAuthRequest
        {
            Properties = new XstsProperties
            {
                UserTokens = new List<string> { xblAuth.Token }
            }
        };
        var xstsContent = new StringContent(JsonSerializer.Serialize(xstsReq), Encoding.UTF8, "application/json");
        var xstsResp = await httpClient.PostAsync(
            XstsAuth, xstsContent, cts.Token);
        var xstsJson = await xstsResp.Content.ReadAsStringAsync(cts.Token);
        if (!xstsResp.IsSuccessStatusCode)
            throw new Exception($"XSTS 认证失败（{xstsResp.StatusCode}）。");
        var xstsAuth = JsonSerializer.Deserialize<XblAuthResponse>(xstsJson)
            ?? throw new Exception("无法解析 XSTS 响应。");

        string uhs = xstsAuth?.DisplayClaims?.Xui?.FirstOrDefault()?.Uhs
            ?? throw new Exception("无法获取用户哈希 (UHS)。");

        var mcReq = new McAuthRequest
        {
            IdentityToken = $"XBL3.0 x={uhs};{xstsAuth.Token}"
        };
        var mcContent = new StringContent(JsonSerializer.Serialize(mcReq), Encoding.UTF8, "application/json");
        var mcResp = await httpClient.PostAsync(
            MinecraftAuth, mcContent, cts.Token);
        var mcJson = await mcResp.Content.ReadAsStringAsync(cts.Token);
        if (!mcResp.IsSuccessStatusCode)
            throw new Exception($"Minecraft 认证失败（{mcResp.StatusCode}）。");
        var mcAuth = JsonSerializer.Deserialize<McAuthResponse>(mcJson)
            ?? throw new Exception("无法解析 Minecraft 认证响应。");

        var profileReq = new HttpRequestMessage(HttpMethod.Get,
            MinecraftProfile);
        profileReq.Headers.Add("Authorization", $"Bearer {mcAuth.AccessToken}");
        var profileResp = await httpClient.SendAsync(profileReq, cts.Token);
        var profileJson = await profileResp.Content.ReadAsStringAsync(cts.Token);
        if (!profileResp.IsSuccessStatusCode)
            throw new Exception($"获取玩家信息失败（{profileResp.StatusCode}）。");
        var profile = JsonSerializer.Deserialize<McProfileResponse>(profileJson)
            ?? throw new Exception("无法解析玩家信息。");

        account.Username = profile.Name;
        account.UUID = profile.Id;
        saveAccountsCallback();

        return (mcAuth.AccessToken, profile.Name, profile.Id, account);
    }

    // ==================== Yggdrasil 第三方验证 ====================

    public async Task<Account> AuthenticateYggdrasilAsync(
        HttpClient httpClient, string serverUrl, string email, string password,
        CancellationToken ct = default)
    {
        // Normalize server URL (remove trailing slash)
        string baseUrl = serverUrl.TrimEnd('/');

        // Belt-and-suspenders: enforce HTTPS for credential safety
        if (!baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Yggdrasil 服务器 URL 必须使用 HTTPS。");

        // Generate clientToken on first login (stored in returned Account)
        string clientToken = Guid.NewGuid().ToString();

        var authRequest = new YggdrasilAuthRequest
        {
            Username = email,
            Password = password,
            ClientToken = clientToken
        };

        var content = new StringContent(
            JsonSerializer.Serialize(authRequest),
            Encoding.UTF8,
            "application/json");

        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsync(
                $"{baseUrl}/authserver/authenticate", content, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new Exception($"无法连接到验证服务器，请检查网络和服务器地址。\n({ex.Message})");
        }
        catch (TaskCanceledException)
        {
            throw new Exception("连接验证服务器超时，请检查网络。");
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            // Try to parse Yggdrasil error response
            try
            {
                var err = JsonSerializer.Deserialize<YggdrasilErrorResponse>(responseJson);
                if (err != null)
                {
                    string detail = err.ErrorMessage ?? err.Error ?? "";
                    throw new Exception(MapYggdrasilError(err.Error ?? "", detail));
                }
            }
            catch (Exception ex) when (ex is not JsonException) { throw; }
            catch (JsonException) { /* fall through to generic error */ }

            throw new Exception($"验证服务器返回错误（{response.StatusCode}）。");
        }

        var authResponse = JsonSerializer.Deserialize<YggdrasilAuthResponse>(responseJson)
            ?? throw new Exception("无法解析验证服务器响应。");

        if (authResponse.SelectedProfile == null)
            throw new Exception("该账号没有可用的游戏角色。");

        // Encrypt password with DPAPI (scoped to current user)
        string encryptedPassword = EncryptYggdrasilPassword(password);

        return new Account
        {
            Type = AccountType.Yggdrasil,
            Username = authResponse.SelectedProfile.Name,
            UUID = authResponse.SelectedProfile.Id,
            YggdrasilServerUrl = baseUrl,
            YggdrasilEmail = email,
            YggdrasilEncryptedPassword = encryptedPassword,
            YggdrasilAccessToken = authResponse.AccessToken,
            YggdrasilClientToken = authResponse.ClientToken,
            LastUsed = DateTime.Now.ToString("O")
        };
    }

    public async Task<Account> RefreshYggdrasilAsync(
        HttpClient httpClient, Account account,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(account.YggdrasilServerUrl))
            throw new Exception("Yggdrasil 服务器 URL 缺失。");
        if (string.IsNullOrWhiteSpace(account.YggdrasilAccessToken))
            throw new Exception("Yggdrasil access token 缺失，请重新登录。");
        if (string.IsNullOrWhiteSpace(account.YggdrasilClientToken))
            throw new Exception("Yggdrasil client token 缺失，请重新登录。");

        string baseUrl = account.YggdrasilServerUrl.TrimEnd('/');

        // Belt-and-suspenders: enforce HTTPS for token safety
        if (!baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Yggdrasil 服务器 URL 必须使用 HTTPS。");

        var refreshRequest = new YggdrasilRefreshRequest
        {
            AccessToken = account.YggdrasilAccessToken,
            ClientToken = account.YggdrasilClientToken,
            RequestUser = true
        };

        var content = new StringContent(
            JsonSerializer.Serialize(refreshRequest),
            Encoding.UTF8,
            "application/json");

        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsync(
                $"{baseUrl}/authserver/refresh", content, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new Exception($"无法连接到验证服务器，请检查网络。\n({ex.Message})");
        }
        catch (TaskCanceledException)
        {
            throw new Exception("连接验证服务器超时，请检查网络。");
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            try
            {
                var err = JsonSerializer.Deserialize<YggdrasilErrorResponse>(responseJson);
                if (err != null)
                {
                    string detail = err.ErrorMessage ?? err.Error ?? "";
                    throw new Exception(MapYggdrasilError(err.Error ?? "", detail));
                }
            }
            catch (Exception ex) when (ex is not JsonException) { throw; }
            catch (JsonException) { /* fall through */ }

            throw new Exception($"令牌刷新失败（{response.StatusCode}），请重新登录。");
        }

        var refreshResponse = JsonSerializer.Deserialize<YggdrasilAuthResponse>(responseJson)
            ?? throw new Exception("无法解析刷新响应。");

        account.YggdrasilAccessToken = refreshResponse.AccessToken;
        if (!string.IsNullOrWhiteSpace(refreshResponse.ClientToken))
            account.YggdrasilClientToken = refreshResponse.ClientToken;
        if (refreshResponse.SelectedProfile != null)
        {
            account.Username = refreshResponse.SelectedProfile.Name;
            account.UUID = refreshResponse.SelectedProfile.Id;
        }
        account.LastUsed = DateTime.Now.ToString("O");

        return account;
    }

    public async Task DownloadAuthlibInjectorAsync(
        HttpClient httpClient, string gameDir,
        CancellationToken ct = default)
    {
        string dir = Path.Combine(gameDir, "authlib-injector");
        string jarPath = Path.Combine(dir, "authlib-injector.jar");

        if (File.Exists(jarPath))
            return;

        Directory.CreateDirectory(dir);

        // Download with 2 retries
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                string tmpPath = jarPath + ".part";

                using var response = await httpClient.GetAsync(
                    AuthlibInjectorDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                // Reject unreasonably large files (malicious or misconfigured)
                long totalBytes = response.Content.Headers.ContentLength ?? -1;
                const long maxAuthlibSize = 50 * 1024 * 1024; // 50 MB
                if (totalBytes > maxAuthlibSize)
                    throw new InvalidOperationException(
                        $"authlib-injector 大小 ({totalBytes / 1024 / 1024} MB) 异常，已拒绝。");

                await using var fs = new FileStream(tmpPath, FileMode.Create,
                    FileAccess.Write, FileShare.None, 81920, useAsync: true);
                await response.Content.CopyToAsync(fs);

                // Validate downloaded file is a valid JAR/ZIP (magic bytes check)
                if (!IsValidJar(tmpPath))
                {
                    try { File.Delete(tmpPath); } catch { }
                    throw new InvalidDataException("authlib-injector 下载的文件不是有效的 JAR 文件。");
                }

                File.Move(tmpPath, jarPath, overwrite: true);
                return;
            }
            catch
            {
                if (attempt == 2) throw;
                await Task.Delay(800, ct);
            }
        }
    }

    /// <summary>检查文件是否为有效的 ZIP/JAR 格式（魔数校验）。</summary>
    private static bool IsValidJar(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            var info = new FileInfo(path);
            if (info.Length < 4) return false;

            byte[] header = new byte[4];
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Read(header, 0, 4) < 4) return false;

            // ZIP/JAR magic bytes: PK\x03\x04
            return header[0] == 0x50 && header[1] == 0x4B && header[2] == 0x03 && header[3] == 0x04;
        }
        catch { return false; }
    }

    // ==================== Yggdrasil helpers ====================

    /// <summary>将 Yggdrasil 错误码映射为中文提示。</summary>
    private static string MapYggdrasilError(string error, string detail)
    {
        return error switch
        {
            "Method Not Allowed" => "验证服务器不支持该操作，请检查服务器 URL。",
            "Not Found" => "验证服务器未找到认证端点，请检查服务器 URL。",
            "IllegalArgumentException" => $"请求参数错误。\n({detail})",
            "ForbiddenOperationException" =>
                string.IsNullOrWhiteSpace(detail) || detail.Contains("Invalid")
                    ? "用户名或密码错误。"
                    : $"操作被服务器禁止：{detail}",
            "ResourceException" => $"无法连接到验证服务器。\n({detail})",
            _ => string.IsNullOrWhiteSpace(detail)
                ? $"验证服务器错误：{error}"
                : detail
        };
    }

    /// <summary>用 Windows DPAPI 加密 Yggdrasil 密码。</summary>
    private static string EncryptYggdrasilPassword(string password)
    {
        byte[] plaintext = Encoding.UTF8.GetBytes(password);
        byte[] encrypted = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    /// <summary>解密 DPAPI 加密的 Yggdrasil 密码（供启动刷新时使用）。</summary>
    public static string DecryptYggdrasilPassword(string encryptedBase64)
    {
        byte[] encrypted = Convert.FromBase64String(encryptedBase64);
        byte[] plaintext = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plaintext);
    }
}
