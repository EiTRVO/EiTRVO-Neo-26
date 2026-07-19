using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EiTRVO.ProEngine.Helpers;
using EiTRVO.ProEngine.Models;

namespace EiTRVO.ProEngine.Services;

/// <summary>
/// 皮肤服务 — 通过 Mojang/Microsoft 官方 API 获取、上传、重置 Minecraft 皮肤。
/// 零外部依赖，不使用任何第三方皮肤 API。
/// </summary>
public class SkinService
{
    private readonly HttpClient _httpClient;
    private readonly IAuthService _authService;

    public SkinService(HttpClient httpClient, IAuthService authService)
    {
        _httpClient = httpClient;
        _authService = authService;
    }

    /// <summary>
    /// 通过纹理 URL 直接下载皮肤 PNG（不重复请求 sessionserver）。
    /// 返回 null 表示下载失败。
    /// </summary>
    public async Task<byte[]?> DownloadSkinPngAsync(string url, CancellationToken ct = default)
    {
        try
        {
            return await _httpClient.GetByteArrayAsync(url, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 从 Mojang Session Server 获取玩家皮肤的原始 PNG 数据。
    /// 返回 null 表示玩家未设置皮肤或服务器无响应。
    /// </summary>
    public async Task<byte[]?> GetSkinPngAsync(string uuid, CancellationToken ct = default)
    {
        // Step 1: 从 sessionserver 获取纹理 URL
        string textureUrl = await GetTextureUrlAsync(uuid, ct);
        if (textureUrl == null)
            return null;

        // Step 2: 从 textures.minecraft.net 下载 PNG
        try
        {
            return await _httpClient.GetByteArrayAsync(textureUrl, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 获取皮肤纹理元数据（URL + 模型类型 classic/slim）。
    /// </summary>
    public async Task<SkinTextureInfo?> GetSkinTextureInfoAsync(string uuid, CancellationToken ct = default)
    {
        string fullUrl = Endpoints.MojangSessionProfile(uuid);
        try
        {
            var json = await _httpClient.GetStringAsync(fullUrl, ct);
            var profile = JsonSerializer.Deserialize<MojangSessionProfile>(json);
            if (profile == null || profile.Properties.Count == 0)
                return null;

            var texturesProp = profile.Properties.Find(p => p.Name == "textures");
            if (texturesProp == null)
                return null;

            byte[] decoded = Convert.FromBase64String(texturesProp.Value);
            var textureData = JsonSerializer.Deserialize<TextureData>(decoded);
            if (textureData?.Textures?.SKIN == null)
                return null;

            return new SkinTextureInfo
            {
                Url = textureData.Textures.SKIN.Url,
                Model = textureData.Textures.SKIN.Metadata?.Model == "slim" ? "slim" : "classic"
            };
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 获取原始皮肤 PNG URL（不上传时用于取纹理地址）。
    /// </summary>
    private async Task<string?> GetTextureUrlAsync(string uuid, CancellationToken ct)
    {
        var info = await GetSkinTextureInfoAsync(uuid, ct);
        return info?.Url;
    }

    /// <summary>
    /// 上传更换皮肤（multipart/form-data）。需有效的 Microsoft 账户。
    /// </summary>
    public async Task<SkinUploadResult> UploadSkinAsync(
        Account account, byte[] skinPng, string variant,
        Action saveAccountsCallback, CancellationToken ct = default)
    {
        // 获取 Minecraft Bearer Token
        string mcToken;
        try
        {
            (mcToken, _, _, _) = await _authService.RefreshMicrosoftAccessAsync(
                _httpClient, account, saveAccountsCallback);
        }
        catch (Exception ex)
        {
            return SkinUploadResult.Fail($"获取 Minecraft 访问令牌失败：{ex.Message}");
        }

        // 构建 multipart/form-data
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(variant), "variant");
        content.Add(new ByteArrayContent(skinPng)
        {
            Headers = { { "Content-Type", "application/png" } }
        }, "file", "skin.png");

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoints.MinecraftProfileSkins);
        request.Headers.Add("Authorization", $"Bearer {mcToken}");
        request.Content = content;

        try
        {
            using var response = await _httpClient.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var profile = JsonSerializer.Deserialize<AuthenticatedProfile>(json);
                string? activeSkinUrl = profile?.Skins?.Find(s => s.State == "ACTIVE")?.Url;
                return SkinUploadResult.Ok(activeSkinUrl);
            }

            string errorBody = await response.Content.ReadAsStringAsync();
            return response.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized => SkinUploadResult.Fail("认证已过期，请重新登录。"),
                System.Net.HttpStatusCode.BadRequest => SkinUploadResult.Fail($"请求无效：{errorBody}"),
                _ => SkinUploadResult.Fail($"上传失败 ({(int)response.StatusCode})：{errorBody}")
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return SkinUploadResult.Fail($"网络错误：{ex.Message}");
        }
    }

    /// <summary>
    /// 重置皮肤为默认（Steve/Alex）。
    /// </summary>
    public async Task<SkinUploadResult> ResetSkinAsync(
        Account account, Action saveAccountsCallback, CancellationToken ct = default)
    {
        string mcToken;
        try
        {
            (mcToken, _, _, _) = await _authService.RefreshMicrosoftAccessAsync(
                _httpClient, account, saveAccountsCallback);
        }
        catch (Exception ex)
        {
            return SkinUploadResult.Fail($"获取 Minecraft 访问令牌失败：{ex.Message}");
        }

        using var request = new HttpRequestMessage(HttpMethod.Delete, Endpoints.MinecraftProfileSkinsActive);
        request.Headers.Add("Authorization", $"Bearer {mcToken}");

        try
        {
            using var response = await _httpClient.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
                return SkinUploadResult.Ok(null);

            string errorBody = await response.Content.ReadAsStringAsync();
            return response.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized => SkinUploadResult.Fail("认证已过期，请重新登录。"),
                _ => SkinUploadResult.Fail($"重置失败 ({(int)response.StatusCode})：{errorBody}")
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return SkinUploadResult.Fail($"网络错误：{ex.Message}");
        }
    }
}

/// <summary>皮肤纹理元数据</summary>
public class SkinTextureInfo
{
    public string Url { get; init; } = "";
    public string Model { get; init; } = "classic";
}

/// <summary>皮肤上传/重置结果</summary>
public class SkinUploadResult
{
    public bool Success { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? ActiveSkinUrl { get; private set; }

    public static SkinUploadResult Ok(string? activeSkinUrl) => new()
    {
        Success = true,
        ActiveSkinUrl = activeSkinUrl
    };

    public static SkinUploadResult Fail(string error) => new()
    {
        Success = false,
        ErrorMessage = error
    };
}
