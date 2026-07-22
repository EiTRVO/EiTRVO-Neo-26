using System.Net;
using System.Text.Json;
using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Services;
using EiTRVO.Tests.Fakes;

namespace EiTRVO.Tests.Services;

[TestClass]

public class AuthServiceTests
{
    // ================================================================
    // HttpClient Fake Helper
    // ================================================================

    private static HttpClient CreateFakeHttpClient(HttpStatusCode statusCode, string responseJson)
    {
        return new HttpClient(new FakeHttpMessageHandler(statusCode, responseJson));
    }

    // ================================================================
    // OAuth Device Code — initial request
    // ================================================================

    [TestMethod]
    public async Task StartDeviceCodeFlow_InvalidJson_Throws()
    {
        var http = CreateFakeHttpClient(HttpStatusCode.OK, "{invalid json");

        var authService = new AuthService();
        await Assert.ThrowsExceptionAsync<JsonException>(() =>
            authService.StartDeviceCodeFlowAsync(
                http,
                new CancellationTokenSource(),
                (_, _, _) => { },
                _ => { },
                () => { }));
    }

    [TestMethod]
    public async Task StartDeviceCodeFlow_ServerError_Throws()
    {
        var http = CreateFakeHttpClient(HttpStatusCode.InternalServerError, "{}");

        var authService = new AuthService();
        await Assert.ThrowsExceptionAsync<Exception>(() =>
            authService.StartDeviceCodeFlowAsync(
                http,
                new CancellationTokenSource(),
                (_, _, _) => { },
                _ => { },
                () => { }));
    }

    // ================================================================
    // VerificationUri Domain Validation (anti-phishing)
    // ================================================================

    [TestMethod]
    public async Task VerificationUri_ValidMicrosoftDomain_DoesNotThrowInvalidData()
    {
        var deviceCodeJson = """
        {
            "device_code": "test-dc",
            "user_code": "ABC123",
            "verification_uri": "https://login.microsoftonline.com/common/oauth2/deviceauth",
            "expires_in": 15,
            "interval": 5
        }
        """;

        var http = CreateFakeHttpClient(HttpStatusCode.OK, deviceCodeJson);

        var authService = new AuthService();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        cts.Cancel(); // cancel immediately to avoid polling loop

        // Should NOT throw InvalidDataException (domain is valid)
        // May throw OperationCanceledException due to immediate cancellation
        try
        {
            await authService.StartDeviceCodeFlowAsync(
                http, cts, (_, _, _) => { }, _ => { }, () => { });
        }
        catch (InvalidDataException)
        {
            Assert.Fail("Valid Microsoft domain should not be rejected.");
        }
        catch (OperationCanceledException) { /* expected — cancelled before polling */ }
    }

    [TestMethod]
    public async Task VerificationUri_PhishingDomain_ThrowsInvalidData()
    {
        var deviceCodeJson = """
        {
            "device_code": "test-dc",
            "user_code": "ABC123",
            "verification_uri": "https://phishing.example.com/microsoft-login",
            "expires_in": 15,
            "interval": 5
        }
        """;

        var http = CreateFakeHttpClient(HttpStatusCode.OK, deviceCodeJson);

        var authService = new AuthService();
        using var cts = new CancellationTokenSource();

        var ex = await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
            authService.StartDeviceCodeFlowAsync(
                http, cts, (_, _, _) => { }, _ => { }, () => { }));

        StringAssert.Contains(ex.Message, "域名不在白名单中");
    }

    [TestMethod]
    public async Task VerificationUri_HttpScheme_ThrowsInvalidData()
    {
        var deviceCodeJson = """
        {
            "device_code": "test-dc",
            "user_code": "ABC123",
            "verification_uri": "http://login.microsoftonline.com/common/oauth2/deviceauth",
            "expires_in": 15,
            "interval": 5
        }
        """;

        var http = CreateFakeHttpClient(HttpStatusCode.OK, deviceCodeJson);

        var authService = new AuthService();
        using var cts = new CancellationTokenSource();

        var ex = await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
            authService.StartDeviceCodeFlowAsync(
                http, cts, (_, _, _) => { }, _ => { }, () => { }));

        StringAssert.Contains(ex.Message, "必须使用 HTTPS");
    }

    [TestMethod]
    public async Task VerificationUri_Empty_ThrowsInvalidData()
    {
        var deviceCodeJson = """
        {
            "device_code": "test-dc",
            "user_code": "ABC123",
            "verification_uri": "",
            "expires_in": 15,
            "interval": 5
        }
        """;

        var http = CreateFakeHttpClient(HttpStatusCode.OK, deviceCodeJson);

        var authService = new AuthService();
        using var cts = new CancellationTokenSource();

        var ex = await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
            authService.StartDeviceCodeFlowAsync(
                http, cts, (_, _, _) => { }, _ => { }, () => { }));

        StringAssert.Contains(ex.Message, "为空");
    }

    // ================================================================
    // OAuth Token Refresh — error cases
    // ================================================================

    [TestMethod]
    public async Task RefreshMicrosoftAccess_BadToken_Throws()
    {
        // First call (token refresh) returns error
        var http = CreateFakeHttpClient(HttpStatusCode.BadRequest, @"{""error"":""invalid_grant""}");

        var authService = new AuthService();
        var account = new Account
        {
            Type = AccountType.Microsoft,
            MicrosoftRefreshToken = "invalid-token"
        };

        var ex = await Assert.ThrowsExceptionAsync<Exception>(() =>
            authService.RefreshMicrosoftAccessAsync(http, account, () => { }));

        StringAssert.Contains(ex.Message, "刷新令牌失败");
    }

    // ================================================================
    // Yggdrasil Authenticate
    // ================================================================

    [TestMethod]
    public async Task AuthenticateYggdrasil_Success()
    {
        var authResponse = new YggdrasilAuthResponse
        {
            AccessToken = "yg-access-token-123",
            ClientToken = "yg-client-token-456",
            SelectedProfile = new YggdrasilProfile { Name = "TestPlayer", Id = "uuid-yg-789" }
        };
        var http = CreateFakeHttpClient(HttpStatusCode.OK, JsonSerializer.Serialize(authResponse));

        var authService = new AuthService();
        var account = await authService.AuthenticateYggdrasilAsync(
            http, "https://littleskin.cn/api/yggdrasil", "test@example.com", "password123");

        Assert.AreEqual(AccountType.Yggdrasil, account.Type);
        Assert.AreEqual("TestPlayer", account.Username);
        Assert.AreEqual("uuid-yg-789", account.UUID);
        Assert.AreEqual("yg-access-token-123", account.YggdrasilAccessToken);
        Assert.AreEqual("test@example.com", account.YggdrasilEmail);
        Assert.AreEqual("https://littleskin.cn/api/yggdrasil", account.YggdrasilServerUrl);
        Assert.IsNotNull(account.YggdrasilEncryptedPassword);
    }

    [TestMethod]
    public async Task AuthenticateYggdrasil_WrongPassword_Throws()
    {
        var errorResponse = new YggdrasilErrorResponse
        {
            Error = "ForbiddenOperationException",
            ErrorMessage = "Invalid credentials. Invalid username or password."
        };
        var http = CreateFakeHttpClient(HttpStatusCode.Forbidden, JsonSerializer.Serialize(errorResponse));

        var authService = new AuthService();
        var ex = await Assert.ThrowsExceptionAsync<Exception>(() =>
            authService.AuthenticateYggdrasilAsync(
                http, "https://littleskin.cn/api/yggdrasil", "test@example.com", "wrong"));

        StringAssert.Contains(ex.Message, "用户名或密码错误");
    }

    [TestMethod]
    public async Task AuthenticateYggdrasil_NoProfile_Throws()
    {
        var authResponse = new YggdrasilAuthResponse
        {
            AccessToken = "token",
            SelectedProfile = null // No game profile
        };
        var http = CreateFakeHttpClient(HttpStatusCode.OK, JsonSerializer.Serialize(authResponse));

        var authService = new AuthService();
        var ex = await Assert.ThrowsExceptionAsync<Exception>(() =>
            authService.AuthenticateYggdrasilAsync(
                http, "https://example.com/yggdrasil", "test@example.com", "pass"));

        StringAssert.Contains(ex.Message, "角色");
    }

    [TestMethod]
    public async Task AuthenticateYggdrasil_NetworkError_Throws()
    {
        var http = new HttpClient(new FakeHttpMessageHandler(
            new HttpRequestException("Connection refused")));

        var authService = new AuthService();
        var ex = await Assert.ThrowsExceptionAsync<Exception>(() =>
            authService.AuthenticateYggdrasilAsync(
                http, "https://example.com/yggdrasil", "test@example.com", "pass"));

        StringAssert.Contains(ex.Message, "网络");
    }

    // ================================================================
    // Yggdrasil Refresh
    // ================================================================

    [TestMethod]
    public async Task RefreshYggdrasil_Success()
    {
        var refreshResponse = new YggdrasilAuthResponse
        {
            AccessToken = "new-access-token",
            ClientToken = "new-client-token",
            SelectedProfile = new YggdrasilProfile { Name = "UpdatedPlayer", Id = "uuid-updated" }
        };
        var http = CreateFakeHttpClient(HttpStatusCode.OK, JsonSerializer.Serialize(refreshResponse));

        var account = new Account
        {
            Type = AccountType.Yggdrasil,
            YggdrasilServerUrl = "https://littleskin.cn/api/yggdrasil",
            YggdrasilAccessToken = "old-access-token",
            YggdrasilClientToken = "old-client-token"
        };

        var authService = new AuthService();
        var updated = await authService.RefreshYggdrasilAsync(http, account);

        Assert.AreEqual("new-access-token", updated.YggdrasilAccessToken);
        Assert.AreEqual("new-client-token", updated.YggdrasilClientToken);
        Assert.AreEqual("UpdatedPlayer", updated.Username);
        Assert.AreEqual("uuid-updated", updated.UUID);
    }

    [TestMethod]
    public async Task RefreshYggdrasil_InvalidToken_Throws()
    {
        var errorResponse = new YggdrasilErrorResponse
        {
            Error = "ForbiddenOperationException",
            ErrorMessage = "Invalid token."
        };
        var http = CreateFakeHttpClient(HttpStatusCode.Forbidden, JsonSerializer.Serialize(errorResponse));

        var authService = new AuthService();
        var ex = await Assert.ThrowsExceptionAsync<Exception>(() =>
            authService.RefreshYggdrasilAsync(http, new Account
            {
                Type = AccountType.Yggdrasil,
                YggdrasilServerUrl = "https://littleskin.cn/api/yggdrasil",
                YggdrasilAccessToken = "old-token",
                YggdrasilClientToken = "old-client"
            }));

        StringAssert.Contains(ex.Message, "用户名或密码错误");
    }

    [TestMethod]
    public async Task RefreshYggdrasil_MissingServerUrl_Throws()
    {
        var http = CreateFakeHttpClient(HttpStatusCode.OK, "{}");
        var authService = new AuthService();

        var ex = await Assert.ThrowsExceptionAsync<Exception>(() =>
            authService.RefreshYggdrasilAsync(http, new Account
            {
                Type = AccountType.Yggdrasil,
                YggdrasilAccessToken = "token",
                YggdrasilClientToken = "client"
            }));

        StringAssert.Contains(ex.Message, "URL");
    }
}
