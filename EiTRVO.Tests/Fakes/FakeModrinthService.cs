using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Services;

namespace EiTRVO.Tests.Fakes;

/// <summary>Test-only IModrinthService with configurable responses.</summary>
public class FakeModrinthService : IModrinthService
{
    public bool ForceSingleConnection { get; set; }

    // Configurable results — set before calling to return specific data
    public ModrinthSearchResponse? SearchResult { get; set; }
    public ModrinthSearchResponse? SearchProjectsResult { get; set; }
    public ModrinthSearchResponse? SearchModpacksResult { get; set; }
    public ModrinthVersion? LatestVersionResult { get; set; }
    public ModrinthVersion? LatestProjectVersionResult { get; set; }
    public ModrinthVersion? ModpackVersionResult { get; set; }

    public Task<ModrinthSearchResponse> SearchAsync(string query, string mcVersion,
        string loader, int limit = 20, int offset = 0,
        CancellationToken ct = default)
        => Task.FromResult(SearchResult ?? throw new NotSupportedException());

    public Task<ModrinthVersion?> GetLatestVersionAsync(string projectId, string mcVersion, string loader,
        CancellationToken ct = default)
        => Task.FromResult(LatestVersionResult)!;

    public Task DownloadModAsync(string url, string destinationPath,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<List<string>> DownloadWithDependenciesAsync(string projectId, string mcVersion,
        string loader, string modsFolder, IProgress<string>? status = null,
        IProgress<DownloadProgress>? fileProgress = null,
        CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<bool> VerifyFileByHashAsync(string sha1, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<ModrinthSearchResponse> SearchProjectsAsync(string query, string mcVersion,
        string projectType, int limit = 20, int offset = 0,
        CancellationToken ct = default)
        => Task.FromResult(SearchProjectsResult ?? throw new NotSupportedException());

    public Task<ModrinthVersion?> GetLatestProjectVersionAsync(string projectId, string mcVersion,
        CancellationToken ct = default)
        => Task.FromResult(LatestProjectVersionResult)!;

    public Task<ModrinthSearchResponse> SearchModpacksAsync(string query, string mcVersion,
        int limit = 20, int offset = 0, CancellationToken ct = default)
        => Task.FromResult(SearchModpacksResult ?? throw new NotSupportedException());

    public Task<ModrinthVersion?> GetModpackLatestVersionAsync(string projectId, string mcVersion,
        CancellationToken ct = default)
        => Task.FromResult(ModpackVersionResult)!;

    public Task<ModrinthVersion?> GetModpackVersionAsync(string projectId, string versionId,
        CancellationToken ct = default)
        => Task.FromResult(ModpackVersionResult)!;
}
