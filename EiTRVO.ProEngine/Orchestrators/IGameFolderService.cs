namespace EiTRVO.ProEngine.Orchestrators;

/// <summary>
/// 游戏目录抽象 — 所有 .minecraft 子路径的单点真实来源。
/// WinUI 3 迁移时只需修改实现，接口不变。
/// </summary>
public interface IGameFolderService
{
    /// <summary>.minecraft 根目录</summary>
    string GameDir { get; }

    /// <summary>.minecraft/versions/</summary>
    string VersionsDir { get; }

    /// <summary>.minecraft/accounts.json</summary>
    string AccountsFile { get; }

    /// <summary>.minecraft/settings.json</summary>
    string SettingsFile { get; }

    /// <summary>.minecraft/libraries/</summary>
    string LibrariesDir { get; }

    /// <summary>.minecraft/assets/</summary>
    string AssetsDir { get; }

    /// <summary>.minecraft/natives/</summary>
    string NativesDir { get; }

    /// <summary>.minecraft/version_manifest.json 缓存文件</summary>
    string VersionManifestCacheFile { get; }

    /// <summary>.minecraft/versions/{versionId}/</summary>
    string GetVersionDir(string versionId);

    /// <summary>.minecraft/versions/{versionId}/version.json</summary>
    string GetVersionJsonPath(string versionId);

    /// <summary>.minecraft/versions/{versionId}/instance.json</summary>
    string GetInstanceMetaPath(string versionId);

    /// <summary>诊断日志目录 (应用根目录/log/)</summary>
    string LogDir { get; }
}
