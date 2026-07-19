using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace EiTRVO.ProEngine.Models;

public class DeviceCodeResponse
{
    [JsonPropertyName("device_code")] public string DeviceCode { get; set; } = "";
    [JsonPropertyName("user_code")] public string UserCode { get; set; } = "";
    [JsonPropertyName("verification_uri")] public string VerificationUri { get; set; } = "";
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    [JsonPropertyName("interval")] public int Interval { get; set; }
}

public class TokenResponse
{
    [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
    [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; } = "";
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
}

public class XblAuthRequest
{
    [JsonPropertyName("Properties")] public XblProperties Properties { get; set; } = new();
    [JsonPropertyName("RelyingParty")] public string RelyingParty { get; set; } = "http://auth.xboxlive.com";
    [JsonPropertyName("TokenType")] public string TokenType { get; set; } = "JWT";
}

public class XblProperties
{
    [JsonPropertyName("AuthMethod")] public string AuthMethod { get; set; } = "RPS";
    [JsonPropertyName("SiteName")] public string SiteName { get; set; } = "user.auth.xboxlive.com";
    [JsonPropertyName("RpsTicket")] public string RpsTicket { get; set; } = "";
}

public class XblAuthResponse
{
    [JsonPropertyName("Token")] public string Token { get; set; } = "";
    [JsonPropertyName("DisplayClaims")] public XblDisplayClaims DisplayClaims { get; set; } = new();
}

public class XblDisplayClaims
{
    [JsonPropertyName("xui")] public List<XblXui> Xui { get; set; } = new();
}

public class XblXui
{
    [JsonPropertyName("uhs")] public string Uhs { get; set; } = "";
}

public class XstsAuthRequest
{
    [JsonPropertyName("Properties")] public XstsProperties Properties { get; set; } = new();
    [JsonPropertyName("RelyingParty")] public string RelyingParty { get; set; } = "rp://api.minecraftservices.com/";
    [JsonPropertyName("TokenType")] public string TokenType { get; set; } = "JWT";
}

public class XstsProperties
{
    [JsonPropertyName("SandboxId")] public string SandboxId { get; set; } = "RETAIL";
    [JsonPropertyName("UserTokens")] public List<string> UserTokens { get; set; } = new();
}

public class McAuthRequest
{
    [JsonPropertyName("identityToken")] public string IdentityToken { get; set; } = "";
}

public class McAuthResponse
{
    [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
}

public class McProfileResponse
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
}

// ==================== Yggdrasil API 模型 ====================

public class YggdrasilAgent
{
    [JsonPropertyName("name")] public string Name { get; set; } = "Minecraft";
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
}

public class YggdrasilProfile
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
}

public class YggdrasilUserInfo
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("properties")] public List<YggdrasilProperty> Properties { get; set; } = new();
}

public class YggdrasilProperty
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("value")] public string Value { get; set; } = "";
}

/// <summary>POST /authserver/authenticate 请求体</summary>
public class YggdrasilAuthRequest
{
    [JsonPropertyName("agent")] public YggdrasilAgent Agent { get; set; } = new();
    [JsonPropertyName("username")] public string Username { get; set; } = "";
    [JsonPropertyName("password")] public string Password { get; set; } = "";
    [JsonPropertyName("clientToken")] public string? ClientToken { get; set; }
    [JsonPropertyName("requestUser")] public bool RequestUser { get; set; } = true;
}

/// <summary>POST /authserver/authenticate 与 /authserver/refresh 共用响应</summary>
public class YggdrasilAuthResponse
{
    [JsonPropertyName("accessToken")] public string AccessToken { get; set; } = "";
    [JsonPropertyName("clientToken")] public string ClientToken { get; set; } = "";
    [JsonPropertyName("availableProfiles")] public List<YggdrasilProfile> AvailableProfiles { get; set; } = new();
    [JsonPropertyName("selectedProfile")] public YggdrasilProfile? SelectedProfile { get; set; }
    [JsonPropertyName("user")] public YggdrasilUserInfo? User { get; set; }
}

/// <summary>POST /authserver/refresh 请求体</summary>
public class YggdrasilRefreshRequest
{
    [JsonPropertyName("accessToken")] public string AccessToken { get; set; } = "";
    [JsonPropertyName("clientToken")] public string ClientToken { get; set; } = "";
    [JsonPropertyName("requestUser")] public bool RequestUser { get; set; } = true;
}

/// <summary>Yggdrasil 错误响应</summary>
public class YggdrasilErrorResponse
{
    [JsonPropertyName("error")] public string Error { get; set; } = "";
    [JsonPropertyName("errorMessage")] public string ErrorMessage { get; set; } = "";
    [JsonPropertyName("cause")] public string? Cause { get; set; }
}

// ==================== Mojang Session Server (皮肤纹理) ====================

/// <summary>Mojang Session Server 响应的 profile（含 textures 属性）</summary>
public class MojangSessionProfile
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("properties")] public List<MojangSessionProperty> Properties { get; set; } = new();
}

public class MojangSessionProperty
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("value")] public string Value { get; set; } = "";
}

/// <summary>Base64 解码后的 textures JSON 结构</summary>
public class TextureData
{
    [JsonPropertyName("timestamp")] public long Timestamp { get; set; }
    [JsonPropertyName("profileId")] public string ProfileId { get; set; } = "";
    [JsonPropertyName("profileName")] public string ProfileName { get; set; } = "";
    [JsonPropertyName("textures")] public TextureMap Textures { get; set; } = new();
}

public class TextureMap
{
    [JsonPropertyName("SKIN")] public TextureEntry? SKIN { get; set; }
    [JsonPropertyName("CAPE")] public TextureEntry? CAPE { get; set; }
}

public class TextureEntry
{
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("metadata")] public SkinMetadata? Metadata { get; set; }
}

public class SkinMetadata
{
    [JsonPropertyName("model")] public string Model { get; set; } = "classic";
}

// ==================== Minecraft Services Skin API ====================

/// <summary>POST /minecraft/profile/skins 响应（即完整的已验证 profile）</summary>
public class AuthenticatedProfile
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("skins")] public List<ProfileSkin> Skins { get; set; } = new();
    [JsonPropertyName("capes")] public List<ProfileCape> Capes { get; set; } = new();
}

public class ProfileSkin
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("state")] public string State { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("variant")] public string Variant { get; set; } = "";
    [JsonPropertyName("alias")] public string? Alias { get; set; }
}

public class ProfileCape
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("state")] public string State { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("alias")] public string? Alias { get; set; }
}
