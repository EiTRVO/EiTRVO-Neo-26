using System.IO;
using EiTRVO.ProEngine.Helpers;

namespace EiTRVO.ProEngine.Orchestrators;

public class GameFolderService : IGameFolderService
{
    public string GameDir { get; }

    public string VersionsDir => Path.Combine(GameDir, "versions");
    public string AccountsFile => Path.Combine(GameDir, "accounts.json");
    public string SettingsFile => Path.Combine(GameDir, "settings.json");
    public string LibrariesDir => Path.Combine(GameDir, "libraries");
    public string AssetsDir => Path.Combine(GameDir, "assets");
    public string NativesDir => Path.Combine(GameDir, "natives");
    public string VersionManifestCacheFile => Path.Combine(GameDir, "version_manifest.json");
    public string LogDir => Path.Combine(PlatformHelper.GetBaseDirectory(), "log");

    public GameFolderService()
    {
        GameDir = Path.Combine(PlatformHelper.GetBaseDirectory(), ".minecraft");
        Directory.CreateDirectory(GameDir);
    }

    public string GetVersionDir(string versionId)
        => Path.Combine(VersionsDir, versionId);

    public string GetVersionJsonPath(string versionId)
        => Path.Combine(VersionsDir, versionId, "version.json");

    public string GetInstanceMetaPath(string versionId)
        => Path.Combine(VersionsDir, versionId, "instance.json");
}
