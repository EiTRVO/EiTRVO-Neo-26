using System.Threading;
using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Services;

namespace EiTRVO.Tests.Fakes;

/// <summary>Test-only IDownloadService. Returns configurable results or no-op.</summary>
public class FakeDownloadService : IDownloadService
{
    public List<ManifestVersion>? LoadOnlineVersionsResult { get; set; }
    public Exception? LoadOnlineVersionsThrows { get; set; }
    public Exception? DownloadVersionFilesThrows { get; set; }
    public int DownloadVersionFilesCallCount { get; private set; }
    public string? LastDownloadedVersionId { get; private set; }
    public string? LastDownloadedInstanceName { get; private set; }

    public async Task<List<ManifestVersion>> LoadOnlineVersionsAsync(HttpClient httpClient, CancellationToken ct = default)
    {
        if (LoadOnlineVersionsThrows != null) throw LoadOnlineVersionsThrows;
        await Task.CompletedTask;
        return LoadOnlineVersionsResult ?? new List<ManifestVersion>();
    }

    public async Task DownloadVersionFilesAsync(HttpClient httpClient, string gameDir,
        string versionJsonUrl, string versionId, string instanceName,
        IProgress<DownloadProgress> progress,
        Action<string, NotificationType, int> showNotification,
        CancellationToken ct = default)
    {
        DownloadVersionFilesCallCount++;
        LastDownloadedVersionId = versionId;
        LastDownloadedInstanceName = instanceName;
        if (DownloadVersionFilesThrows != null) throw DownloadVersionFilesThrows;
        await Task.CompletedTask;
    }

    public void ExtractNativeJar(string jarPath, string destDir) { }
}
