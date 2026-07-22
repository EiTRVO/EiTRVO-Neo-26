using System.IO;
using System.IO.Compression;

namespace EiTRVO.ProEngine.Helpers;

public static class PlatformHelper
{
    public static int GetMinecraftRequiredJavaVersion(string mcVersion)
    {
        if (mcVersion.StartsWith("1.17")) return 16;

        if (mcVersion.StartsWith("1."))
        {
            int minorEnd = mcVersion.IndexOf('.', 2);
            string minorStr = minorEnd > 2
                ? mcVersion.Substring(2, minorEnd - 2)
                : mcVersion.Substring(2);

            if (int.TryParse(minorStr, out int minor))
            {
                if (minor >= 18) return 17;
                return 8;
            }
        }

        return 17;
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
