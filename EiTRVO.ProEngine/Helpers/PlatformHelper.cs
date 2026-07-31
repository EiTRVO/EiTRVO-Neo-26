using System.IO;
using System.IO.Compression;

namespace EiTRVO.ProEngine.Helpers;

public static class PlatformHelper
{
    /// <summary>
    /// 从复合版本字符串（如 fabric-loader-0.19.3-1.14.4）中提取实际 Minecraft 版本号。
    /// 取最后一个 "1.x" 或 "1.x.y" 匹配，因为 Fabric/Quilt 的 MC 版本位于字符串末尾。
    /// 若无法匹配则返回空字符串。
    /// </summary>
    private static string ExtractMinecraftVersion(string versionId)
    {
        // 仅匹配作为独立段出现的 "1.x" 或 "1.x.y"
        // — 前面必须是字符串开头或 "-"，避免误匹配 "26.1.0" 中的 "1.0"
        var matches = System.Text.RegularExpressions.Regex.Matches(
            versionId, @"(?:^|-)1\.(\d+)(\.\d+)?");

        if (matches.Count == 0)
            return "";

        // 取最后一个匹配 — Fabric/Quilt 将 MC 版本放在末尾
        // 去掉前导的 "-"（如果有）
        string raw = matches[^1].Value;
        return raw.StartsWith('-') ? raw[1..] : raw;
    }

    public static int GetMinecraftRequiredJavaVersion(string mcVersion)
    {
        // 预处理：从加载器复合版本字符串中提取真实 MC 版本号
        string extracted = ExtractMinecraftVersion(mcVersion);
        if (!string.IsNullOrEmpty(extracted))
            mcVersion = extracted;

        // 新版本号格式（24+, 25+, 26+ 等）
        if (mcVersion.StartsWith("26."))
        {
            int minorEnd = mcVersion.IndexOf('.', 3);
            string minorStr = minorEnd > 3 ? mcVersion.Substring(3, minorEnd - 3) : mcVersion.Substring(3);
            if (int.TryParse(minorStr, out int minor26) && minor26 >= 1)
                return 25;
        }

        if (mcVersion.StartsWith("1."))
        {
            int minorEnd = mcVersion.IndexOf('.', 2);
            string minorStr = minorEnd > 2
                ? mcVersion.Substring(2, minorEnd - 2)
                : mcVersion.Substring(2);

            if (int.TryParse(minorStr, out int minor))
            {
                if (minor >= 21) return 21;
                if (minor >= 18) return 17;
                if (minor == 17) return 16;
                return 8;
            }
        }

        return 17;
    }

    /// <summary>
    /// 返回指定 MC 版本兼容的最高 Java 主版本号。
    /// 1.12.2 及以下使用 LWJGL 2.x，Java 9+ 移除了其依赖的 API，因此硬上限为 8。
    /// </summary>
    public static int GetMaxRecommendedJavaVersion(string mcVersion)
    {
        string extracted = ExtractMinecraftVersion(mcVersion);
        if (!string.IsNullOrEmpty(extracted))
            mcVersion = extracted;

        // 新版本号格式（24+, 25+, 26+）
        if (mcVersion.StartsWith("26.") || mcVersion.StartsWith("25.") || mcVersion.StartsWith("24."))
            return int.MaxValue;

        if (mcVersion.StartsWith("1."))
        {
            int minorEnd = mcVersion.IndexOf('.', 2);
            string minorStr = minorEnd > 2
                ? mcVersion.Substring(2, minorEnd - 2)
                : mcVersion.Substring(2);

            if (int.TryParse(minorStr, out int minor))
            {
                if (minor >= 21) return int.MaxValue;   // 最新版，无上限
                if (minor >= 13) return 21;              // LWJGL 3.x，21 兼容
                return 8;                                // 1.12.2 及以下：LWJGL 2.x，硬上限 Java 8
            }
        }

        return int.MaxValue;
    }

    /// <summary>
    /// Gets the real base directory of the application, accounting for single-file publishing.
    /// In single-file mode, AppContext.BaseDirectory points to the bundle extraction temp directory;
    /// Environment.ProcessPath gives the actual exe location. When running via "dotnet run",
    /// ProcessPath points to dotnet.exe, so we fall back to AppContext.BaseDirectory.
    /// </summary>
    public static string GetBaseDirectory()
    {
        string? processPath = Environment.ProcessPath;
        if (processPath != null &&
            !processPath.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetDirectoryName(processPath)!;
        }
        return AppContext.BaseDirectory;
    }

    public static bool IsLegacyVersion(string versionId)
    {
        if (versionId.StartsWith("a1.") || versionId.StartsWith("b1.")
            || versionId.StartsWith("c0.") || versionId.StartsWith("inf-")
            || versionId.StartsWith("rd-"))
            return true;

        if (versionId.StartsWith("Combat Test")) return true;

        if (!versionId.StartsWith("1.")) return false;

        int minorEnd = versionId.IndexOf('.', 2);
        string minorStr = minorEnd > 2
            ? versionId.Substring(2, minorEnd - 2)
            : versionId.Substring(2);

        if (int.TryParse(minorStr, out int minor))
            return minor <= 5;

        return false;
    }

    public static void EnsureLegacyResources(string versionDir, string versionId, string gameDir)
    {
        string resourcesDir = Path.Combine(gameDir, "resources");
        string langDir = Path.Combine(resourcesDir, "lang");

        string jarPath = Path.Combine(versionDir, $"{versionId}.jar");
        if (!File.Exists(jarPath)) return;

        using var archive = ZipFile.OpenRead(jarPath);
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.StartsWith("assets/minecraft/", System.StringComparison.OrdinalIgnoreCase) &&
                !entry.FullName.EndsWith("/"))
            {
                string relativePath = entry.FullName.Substring("assets/minecraft/".Length);
                string dest = Path.Combine(resourcesDir, relativePath);
                PathSafetyHelper.ValidateContained(dest, resourcesDir);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                if (!File.Exists(dest))
                    entry.ExtractToFile(dest, true);
            }
        }
    }
}
