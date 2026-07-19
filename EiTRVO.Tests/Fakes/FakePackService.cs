using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Services;

namespace EiTRVO.Tests.Fakes;

/// <summary>Test-only IPackService. All methods return configurable results or no-op.</summary>
public class FakePackService : IPackService
{
    public PackManifest? ReadManifestResult { get; set; }
    public Exception? ReadManifestThrows { get; set; }
    public string PeekInstanceFolderNameResult { get; set; } = "";
    public VersionDetail? PeekVersionJsonResult { get; set; }
    public Exception? ExtractAsyncThrows { get; set; }
    public bool VerifyModsResult { get; set; } = true;
    public int ExtractAsyncCallCount { get; private set; }
    public int VerifyModsCallCount { get; private set; }

    public PackManifest ReadManifest(string packPath)
    {
        if (ReadManifestThrows != null) throw ReadManifestThrows;
        return ReadManifestResult ?? new PackManifest { Format = "eitrvo-pack:1", ExporterVersion = 26 };
    }

    public string PeekInstanceFolderName(string packPath) => PeekInstanceFolderNameResult;

    public VersionDetail? PeekVersionJson(string packPath) => PeekVersionJsonResult;

    public Task ExportAsync(string instanceName, string gameDir, string instanceDir,
        string exportPath, GameInstance instance, IProgress<DownloadProgress> progress,
        Action<string, NotificationType, int> showNotification)
        => Task.CompletedTask;

    public async Task ExtractAsync(string packPath, string instanceName, string gameDir,
        IProgress<DownloadProgress> progress,
        Action<string, NotificationType, int> showNotification)
    {
        ExtractAsyncCallCount++;
        if (ExtractAsyncThrows != null) throw ExtractAsyncThrows;
        await Task.CompletedTask;
    }

    public async Task<bool> VerifyModsAsync(string instanceDir, PackManifest manifest,
        Action<string, NotificationType, int> showNotification)
    {
        VerifyModsCallCount++;
        await Task.CompletedTask;
        return VerifyModsResult;
    }
}
