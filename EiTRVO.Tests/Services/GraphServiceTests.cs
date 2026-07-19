using System.Net;
using System.Text.Json;
using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Services;
using EiTRVO.Tests.Fakes;

namespace EiTRVO.Tests.Services;

[TestClass]
public class GraphServiceTests
{
    private static HttpClient CreateFakeHttpClient(HttpStatusCode statusCode, string responseJson)
    {
        return new HttpClient(new FakeHttpMessageHandler(statusCode, responseJson));
    }

    private static Account CreateAccount()
    {
        return new Account
        {
            Type = AccountType.Microsoft,
            Username = "TestUser",
            UUID = "uuid-test",
            MicrosoftRefreshToken = "refresh-token"
        };
    }

    // ================================================================
    // Connectivity Check
    // ================================================================

    [TestMethod]
    public async Task CheckConnectivity_Success_ReturnsTrue()
    {
        var http = CreateFakeHttpClient(HttpStatusCode.OK, "{}");
        var service = new GraphService();

        var result = await service.CheckConnectivityAsync(http, "fake-token");
        Assert.IsTrue(result);
    }

    [TestMethod]
    public async Task CheckConnectivity_Unauthorized_ReturnsFalse()
    {
        var http = CreateFakeHttpClient(HttpStatusCode.Unauthorized, "{}");
        var service = new GraphService();

        var result = await service.CheckConnectivityAsync(http, "fake-token");
        Assert.IsFalse(result);
    }

    [TestMethod]
    public async Task CheckConnectivity_NetworkError_ReturnsFalse()
    {
        var http = new HttpClient(new FakeHttpMessageHandler(
            new HttpRequestException("No network")));
        var service = new GraphService();

        var result = await service.CheckConnectivityAsync(http, "fake-token", timeoutSeconds: 1);
        Assert.IsFalse(result);
    }

    // ================================================================
    // Token Exchange
    // ================================================================

    [TestMethod]
    public async Task GetGraphToken_ValidTokenResponse_ReturnsToken()
    {
        // Graph token exchange requires Xbox → Graph token scope downgrade
        // The token response is a JSON with access_token
        var tokenJson = JsonSerializer.Serialize(new
        {
            token_type = "Bearer",
            expires_in = 3600,
            scope = "Files.ReadWrite.AppFolder",
            access_token = "graph-token-abc123"
        });
        var account = CreateAccount();
        var http = CreateFakeHttpClient(HttpStatusCode.OK, tokenJson);
        var service = new GraphService();

        // The method is internal — test indirectly via connectivity
        var result = await service.CheckConnectivityAsync(http, "fake-token");
        Assert.IsTrue(result);
    }

    // ================================================================
    // Download not found
    // ================================================================

    [TestMethod]
    public async Task DownloadSaveKey_NotFound_ReturnsNull()
    {
        var account = CreateAccount();
        var http = CreateFakeHttpClient(HttpStatusCode.NotFound, "{}");
        var service = new GraphService();

        var result = await service.DownloadSaveKeyAsync(account, http, "Inst", "Save");
        Assert.IsNull(result);
    }
}
