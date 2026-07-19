namespace EiTRVO.ProEngine.Helpers;

public static class Endpoints
{
    // Minecraft version manifest
    public const string VersionManifestUrl = "https://launchermeta.mojang.com/mc/game/version_manifest.json";

    // Asset download (hash must be at least 2 characters)
    public static string AssetDownload(string hash)
    {
        ArgumentNullException.ThrowIfNull(hash);
        if (hash.Length < 2)
            throw new ArgumentException("Asset hash must be at least 2 characters.", nameof(hash));
        return $"https://resources.download.minecraft.net/{hash.Substring(0, 2)}/{hash}";
    }

    // Microsoft OAuth 2.0 Device Code Flow
    public const string MicrosoftDeviceCode = "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode";
    public const string MicrosoftToken = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";

    // Xbox Live authentication chain
    public const string XboxLiveAuth = "https://user.auth.xboxlive.com/user/authenticate";
    public const string XstsAuth = "https://xsts.auth.xboxlive.com/xsts/authorize";

    // Minecraft services
    public const string MinecraftAuth = "https://api.minecraftservices.com/authentication/login_with_xbox";
    public const string MinecraftProfile = "https://api.minecraftservices.com/minecraft/profile";
    public const string MinecraftProfileSkins = "https://api.minecraftservices.com/minecraft/profile/skins";
    public const string MinecraftProfileSkinsActive = "https://api.minecraftservices.com/minecraft/profile/skins/active";

    // Mojang Session Server (public — 查询玩家皮肤纹理)
    public static string MojangSessionProfile(string uuid) =>
        $"https://sessionserver.mojang.com/session/minecraft/profile/{uuid.Replace("-", "")}";

    // authlib-injector (Yggdrasil 第三方验证)
    public const string AuthlibInjectorDownloadUrl = "https://authlib-injector.yushi.moe/artifacts/authlib-injector.jar";

    // ==================== Fabric Meta API ====================
    public const string FabricMetaBase = "https://meta.fabricmc.net/v2";
    public static string FabricLoaderVersions(string mcVersion) =>
        $"{FabricMetaBase}/versions/loader/{mcVersion}";
    public static string FabricLoaderProfile(string mcVersion, string loaderVersion) =>
        $"{FabricMetaBase}/versions/loader/{mcVersion}/{loaderVersion}/profile/json";

    // ==================== Quilt Meta API ====================
    public const string QuiltMetaBase = "https://meta.quiltmc.org/v3";
    public static string QuiltLoaderVersions(string mcVersion) =>
        $"{QuiltMetaBase}/versions/loader/{mcVersion}";
    public static string QuiltLoaderProfile(string mcVersion, string loaderVersion) =>
        $"{QuiltMetaBase}/versions/loader/{mcVersion}/{loaderVersion}/profile/json";

    // ==================== Forge Maven ====================
    public const string ForgeMavenBase = "https://maven.minecraftforge.net";
    public const string ForgePromoMetadata =
        "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json";
    public static string ForgeInstaller(string mcVersion, string forgeVersion) =>
        $"{ForgeMavenBase}/net/minecraftforge/forge/{mcVersion}-{forgeVersion}/forge-{mcVersion}-{forgeVersion}-installer.jar";

    // ==================== NeoForge Maven ====================
    public const string NeoForgeMavenBase = "https://maven.neoforged.net";
    public static string NeoForgeInstaller(string version) =>
        $"{NeoForgeMavenBase}/releases/net/neoforged/neoforge/{version}/neoforge-{version}-installer.jar";

    // ==================== OptiFine (optifine.net) ====================
    // OptiFine has no official API. Version lists are scraped from the HTML downloads page
    // and downloads use a two-step flow: adloadx (ad page) → downloadx (actual JAR file).
    public const string OptiFineDownloadsPage = "https://optifine.net/downloads";
    public static string OptiFineAdload(string fileName) =>
        $"https://optifine.net/adloadx?f={fileName}";
    public static string OptiFineDownloadX(string fileName, string hash) =>
        $"https://optifine.net/downloadx?f={fileName}&x={hash}";

    // Modrinth API v2
    public const string ModrinthApi = "https://api.modrinth.com/v2";
    public static string ModrinthVersionFile(string sha1) =>
        $"{ModrinthApi}/version_file/{sha1}?algorithm=sha1";

    // ==================== Microsoft Graph API (OneDrive App Folder) ====================
    public static string GraphAppRoot(string relativePath)
    {
        relativePath = (relativePath ?? "").TrimStart('/');
        return string.IsNullOrEmpty(relativePath)
            ? "https://graph.microsoft.com/v1.0/me/drive/special/approot"
            : $"https://graph.microsoft.com/v1.0/me/drive/special/approot:/{relativePath}";
    }
}
