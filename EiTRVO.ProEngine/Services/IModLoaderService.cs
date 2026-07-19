using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using EiTRVO.ProEngine.Models;

namespace EiTRVO.ProEngine.Services;

public interface IModLoaderService
{
    /// <summary>合并子版本 JSON（Forge/Fabric 等）与父版本 JSON（Vanilla）。</summary>
    VersionDetail MergeVersionJson(VersionDetail child, VersionDetail parent);

    // === 版本查询 ===
    Task<List<ModLoaderVersion>> GetFabricLoaderVersionsAsync(HttpClient http, string mcVersion, CancellationToken ct = default);
    Task<List<ModLoaderVersion>> GetForgeVersionsAsync(HttpClient http, string mcVersion, CancellationToken ct = default);
    Task<List<ModLoaderVersion>> GetQuiltLoaderVersionsAsync(HttpClient http, string mcVersion, CancellationToken ct = default);
    Task<List<ModLoaderVersion>> GetNeoForgeVersionsAsync(HttpClient http, string mcVersion, CancellationToken ct = default);
    Task<List<ModLoaderVersion>> GetOptiFineVersionsAsync(HttpClient http, string mcVersion, CancellationToken ct = default);

    // === 安装 ===
    Task InstallFabricAsync(HttpClient http, string gameDir, string mcVersion,
        string loaderVersion, string instanceName, IProgress<DownloadProgress> progress,
        Action<string, NotificationType, int> showNotification,
        CancellationToken ct = default);

    Task InstallForgeAsync(HttpClient http, string gameDir, string mcVersion,
        string forgeVersion, string instanceName, string javaPath,
        IProgress<DownloadProgress> progress,
        Action<string, NotificationType, int> showNotification,
        CancellationToken ct = default);

    Task InstallQuiltAsync(HttpClient http, string gameDir, string mcVersion,
        string loaderVersion, string instanceName, IProgress<DownloadProgress> progress,
        Action<string, NotificationType, int> showNotification,
        CancellationToken ct = default);

    Task InstallNeoForgeAsync(HttpClient http, string gameDir, string mcVersion,
        string neoForgeVersion, string instanceName, string javaPath,
        IProgress<DownloadProgress> progress,
        Action<string, NotificationType, int> showNotification,
        CancellationToken ct = default);

    Task InstallOptiFineAsync(HttpClient http, string gameDir, string mcVersion,
        string optiFineVersion, string instanceName, string javaPath,
        IProgress<DownloadProgress> progress,
        Action<string, NotificationType, int> showNotification,
        CancellationToken ct = default);
}
