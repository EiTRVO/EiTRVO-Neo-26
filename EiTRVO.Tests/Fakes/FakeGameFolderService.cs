using EiTRVO.ProEngine.Orchestrators;

namespace EiTRVO.Tests.Fakes;

/// <summary>
/// Test-only IGameFolderService. Set GameDir/VersionsDir before use;
/// all other paths derive from GameDir automatically.
/// </summary>
public class FakeGameFolderService : IGameFolderService
{
    public string GameDir { get; set; } = "";
    public string VersionsDir { get; set; } = "";
    public string AccountsFile => Path.Combine(GameDir, "accounts.json");
    public string SettingsFile => Path.Combine(GameDir, "settings.json");
    public string LibrariesDir => Path.Combine(GameDir, "libraries");
    public string AssetsDir => Path.Combine(GameDir, "assets");
    public string NativesDir => Path.Combine(GameDir, "natives");
    public string VersionManifestCacheFile => Path.Combine(GameDir, "version_manifest.json");
    public string LogDir => Path.Combine(GameDir, "log");
    public string GetVersionDir(string versionId) => Path.Combine(VersionsDir, versionId);
    public string GetVersionJsonPath(string versionId) => Path.Combine(GetVersionDir(versionId), $"{versionId}.json");
    public string GetInstanceMetaPath(string versionId) => Path.Combine(GetVersionDir(versionId), "instance.json");
}
