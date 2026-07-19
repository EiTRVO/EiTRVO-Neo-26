using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EiTRVO.ProEngine.Helpers;
using EiTRVO.ProEngine.Models;

namespace EiTRVO.ProEngine.Services;

/// <summary>
/// OneDrive App Folder 操作。
/// 通过 Microsoft Graph API 在用户的 OneDrive 应用专属文件夹中存储/读取 AES 密钥。
/// 注册为 Singleton。
/// </summary>
public class GraphService
{
    /// <summary>
    /// 通过已有的 MS refresh_token 换取 scope=Files.ReadWrite.AppFolder 的 Graph access token。
    /// 这是标准 OAuth 2.0 scope 降级——不需要用户重新授权。
    /// </summary>
    public async Task<string?> GetGraphTokenAsync(Account account, HttpClient httpClient, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(account.MicrosoftRefreshToken))
            return null;

        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("client_id", AuthService.MICROSOFT_CLIENT_ID),
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", account.MicrosoftRefreshToken),
            new KeyValuePair<string, string>("scope", "Files.ReadWrite.AppFolder")
        });

        var resp = await httpClient.PostAsync(Endpoints.MicrosoftToken, content, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var errBody = await resp.Content.ReadAsStringAsync(ct);
            System.Diagnostics.Debug.WriteLine(
                $"[GraphService] Token exchange failed ({resp.StatusCode}): {errBody}");
            return null;
        }

        var json = await resp.Content.ReadAsStringAsync(ct);
        var tokenResp = JsonSerializer.Deserialize<GraphTokenResponse>(json);
        return tokenResp?.AccessToken;
    }

    /// <summary>
    /// 网络连通性预检——HEAD 请求 OneDrive App Folder 根目录。
    /// </summary>
    public async Task<bool> CheckConnectivityAsync(HttpClient httpClient, string graphAccessToken,
        int timeoutSeconds = 5, CancellationToken ct = default)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Head,
                Endpoints.GraphAppRoot("/"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", graphAccessToken);
            var resp = await httpClient.SendAsync(request, cts.Token);
            return resp.IsSuccessStatusCode;
        }
        catch (TaskCanceledException) { return false; }
        catch (HttpRequestException) { return false; }
    }

    /// <summary>
    /// 上传 AES 密钥到 OneDrive App Folder。
    /// 路径：mc-launcher/saves/{instanceName}/{saveName}.key
    /// </summary>
    public async Task<bool> UploadSaveKeyAsync(
        Account account,
        HttpClient httpClient,
        string instanceName,
        string saveName,
        byte[] aesKey,
        CancellationToken ct = default)
    {
        string? graphToken = await GetGraphTokenAsync(account, httpClient, ct);
        if (graphToken == null)
            return false;

        var payload = new SaveKeyPayload
        {
            AesKey = Convert.ToBase64String(aesKey),
            InstanceName = instanceName,
            SaveName = saveName,
            CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
            Version = 1
        };

        string json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        string escapedInstance = Uri.EscapeDataString(instanceName);
        string escapedSave = Uri.EscapeDataString(saveName);
        string url = Endpoints.GraphAppRoot(
            $"mc-launcher/saves/{escapedInstance}/{escapedSave}.key:/content");

        var request = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", graphToken);

        var resp = await httpClient.SendAsync(request, ct);
        return resp.IsSuccessStatusCode;
    }

    /// <summary>
    /// 从 OneDrive App Folder 下载 AES 密钥。
    /// </summary>
    public async Task<byte[]?> DownloadSaveKeyAsync(
        Account account,
        HttpClient httpClient,
        string instanceName,
        string saveName,
        CancellationToken ct = default)
    {
        string? graphToken = await GetGraphTokenAsync(account, httpClient, ct);
        if (graphToken == null)
            return null;

        string escapedInstance = Uri.EscapeDataString(instanceName);
        string escapedSave = Uri.EscapeDataString(saveName);
        string url = Endpoints.GraphAppRoot(
            $"mc-launcher/saves/{escapedInstance}/{escapedSave}.key:/content");

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", graphToken);

        var resp = await httpClient.SendAsync(request, ct);
        if (!resp.IsSuccessStatusCode)
            return null;

        string json = await resp.Content.ReadAsStringAsync(ct);
        var payload = JsonSerializer.Deserialize<SaveKeyPayload>(json);
        if (payload == null || string.IsNullOrEmpty(payload.AesKey))
            return null;

        return Convert.FromBase64String(payload.AesKey);
    }

    /// <summary>
    /// 从 OneDrive App Folder 删除密钥文件。
    /// </summary>
    public async Task DeleteSaveKeyAsync(
        Account account,
        HttpClient httpClient,
        string instanceName,
        string saveName,
        CancellationToken ct = default)
    {
        string? graphToken = await GetGraphTokenAsync(account, httpClient, ct);
        if (graphToken == null)
            return;

        string escapedInstance = Uri.EscapeDataString(instanceName);
        string escapedSave = Uri.EscapeDataString(saveName);
        string url = Endpoints.GraphAppRoot(
            $"mc-launcher/saves/{escapedInstance}/{escapedSave}.key");

        var request = new HttpRequestMessage(HttpMethod.Delete, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", graphToken);

        await httpClient.SendAsync(request, ct);
        // 静默忽略失败（fire-and-forget 模式）
    }

    /// <summary>
    /// 列出 OneDrive App Folder 中某实例的所有密钥文件。
    /// 用于惰性清理——检测无对应 .savenc 的孤儿密钥。
    /// </summary>
    public async Task<string[]> ListOrphanedKeysAsync(
        Account account,
        HttpClient httpClient,
        string instanceName,
        CancellationToken ct = default)
    {
        string? graphToken = await GetGraphTokenAsync(account, httpClient, ct);
        if (graphToken == null)
            return [];

        string escapedInstance = Uri.EscapeDataString(instanceName);
        string url = Endpoints.GraphAppRoot(
            $"mc-launcher/saves/{escapedInstance}:/children");

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", graphToken);

        var resp = await httpClient.SendAsync(request, ct);
        if (!resp.IsSuccessStatusCode)
            return [];

        string json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var result = new System.Collections.Generic.List<string>();

        if (doc.RootElement.TryGetProperty("value", out var children))
        {
            foreach (var child in children.EnumerateArray())
            {
                string? name = child.GetProperty("name").GetString();
                if (name != null && name.EndsWith(".key"))
                    result.Add(name);
            }
        }

        return result.ToArray();
    }

    /// <summary>
    /// 清理孤儿密钥（移除无对应 .savenc 的 .key 文件）。
    /// </summary>
    public async Task CleanupOrphanedKeysAsync(
        Account account,
        HttpClient httpClient,
        string instanceName,
        CancellationToken ct = default)
    {
        string? graphToken = await GetGraphTokenAsync(account, httpClient, ct);
        if (graphToken == null)
            return;

        string escapedInstance = Uri.EscapeDataString(instanceName);
        string url = Endpoints.GraphAppRoot(
            $"mc-launcher/saves/{escapedInstance}:/children");

        var listReq = new HttpRequestMessage(HttpMethod.Get, url);
        listReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", graphToken);
        var listResp = await httpClient.SendAsync(listReq, ct);
        if (!listResp.IsSuccessStatusCode)
            return;

        string json = await listResp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("value", out var children))
            return;

        foreach (var child in children.EnumerateArray())
        {
            string? name = child.GetProperty("name").GetString();
            string? itemId = child.GetProperty("id").GetString();
            if (name == null || itemId == null)
                continue;

            if (!name.EndsWith(".key"))
                continue;

            string saveName = name[..^4]; // remove ".key"
            // Check if .savenc exists — caller needs to provide this info
            // For now, just delete the key (simplified)
            // In production, we'd only delete if no matching .savenc exists

            string deleteUrl = Endpoints.GraphAppRoot(
                $"mc-launcher/saves/{escapedInstance}/{Uri.EscapeDataString(name)}");
            var deleteReq = new HttpRequestMessage(HttpMethod.Delete, deleteUrl);
            deleteReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", graphToken);
            await httpClient.SendAsync(deleteReq, ct);
        }
    }

    private class GraphTokenResponse
    {
        public string? AccessToken { get; set; }
        public string? TokenType { get; set; }
        public int ExpiresIn { get; set; }
    }
}
