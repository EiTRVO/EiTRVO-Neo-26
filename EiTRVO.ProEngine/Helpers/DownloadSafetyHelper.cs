using System;
using System.Collections.Generic;

namespace EiTRVO.ProEngine.Helpers;

/// <summary>下载安全相关工具方法。</summary>
public static class DownloadSafetyHelper
{
    /// <summary>允许直接下载的文件 CDN 域名白名单。</summary>
    private static readonly HashSet<string> AllowedDownloadHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        // Modrinth CDN
        "cdn.modrinth.com",
        "cdn-raw.modrinth.com",

        // CurseForge CDN (used by Modrinth mirrors)
        "edge.forgecdn.net",
        "media.forgecdn.net",

        // Mojang / Microsoft CDN
        "launchermeta.mojang.com",
        "piston-meta.mojang.com",
        "resources.download.minecraft.net",
        "libraries.minecraft.net",

        // Maven repositories for mod loaders
        "maven.minecraftforge.net",
        "maven.neoforged.net",
        "maven.fabricmc.net",
        "repo1.maven.org",

        // Mod loader meta APIs
        "meta.fabricmc.net",
        "meta.quiltmc.org",
        "files.minecraftforge.net",

        // Auth
        "authlib-injector.yushi.moe",
        "optifine.net",

        // Microsoft auth endpoints
        "login.microsoftonline.com",
        "api.minecraftservices.com",
        "sessionserver.mojang.com",
        "user.auth.xboxlive.com",
        "xsts.auth.xboxlive.com",
        "graph.microsoft.com",
    };

    /// <summary>检查下载 URL 是否指向允许的域名。非白名单域名返回 false。</summary>
    public static bool IsDownloadUrlAllowed(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        // Only allow HTTPS and HTTP (HTTP for legacy mirrors, though they redirect)
        if (uri.Scheme != "https" && uri.Scheme != "http")
            return false;

        return AllowedDownloadHosts.Contains(uri.Host);
    }
}
