using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EiTRVO.ProEngine.Models;

namespace EiTRVO.ProEngine.Services;

public interface IModrinthService
{
    Task<ModrinthSearchResponse> SearchAsync(string query, string mcVersion,
        string loader, int limit = 20, int offset = 0,
        CancellationToken ct = default);

    Task<ModrinthVersion?> GetLatestVersionAsync(string projectId, string mcVersion, string loader,
        CancellationToken ct = default);

    Task DownloadModAsync(string url, string destinationPath,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>Download a mod and all its required dependencies recursively.</summary>
    Task<List<string>> DownloadWithDependenciesAsync(string projectId, string mcVersion,
        string loader, string modsFolder, IProgress<string>? status = null,
        IProgress<DownloadProgress>? fileProgress = null,
        CancellationToken ct = default);

    /// <summary>Verify whether a file SHA-1 hash matches a known Modrinth mod file.</summary>
    Task<bool> VerifyFileByHashAsync(string sha1, CancellationToken ct = default);

    /// <summary>Search resource packs or shader packs (no loader filter).</summary>
    Task<ModrinthSearchResponse> SearchProjectsAsync(string query, string mcVersion,
        string projectType, int limit = 20, int offset = 0,
        CancellationToken ct = default);

    /// <summary>Get latest version for a project without loader filtering.</summary>
    Task<ModrinthVersion?> GetLatestProjectVersionAsync(string projectId, string mcVersion,
        CancellationToken ct = default);

    // ==================== Modpack API ====================

    /// <summary>Search modpacks on Modrinth (project_type:modpack).</summary>
    Task<ModrinthSearchResponse> SearchModpacksAsync(string query, string mcVersion,
        int limit = 20, int offset = 0, CancellationToken ct = default);

    /// <summary>Get the latest version for a modpack project without loader filter.</summary>
    Task<ModrinthVersion?> GetModpackLatestVersionAsync(string projectId, string mcVersion,
        CancellationToken ct = default);

    /// <summary>Get a specific version of a modpack project by version ID.</summary>
    Task<ModrinthVersion?> GetModpackVersionAsync(string projectId, string versionId,
        CancellationToken ct = default);
}
