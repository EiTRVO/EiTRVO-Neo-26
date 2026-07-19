using System.Threading;
using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Services;

namespace EiTRVO.Tests.Fakes;

/// <summary>Test-only IModLoaderService. Each method has a configurable
/// exception to throw; set to null (default: NotSupportedException) to make it a no-op.</summary>
public class FakeModLoaderService : IModLoaderService
{
    // Per-method throw configuration — set to null for no-op
    public Exception? InstallFabricThrows { get; set; } = new NotSupportedException();
    public Exception? InstallForgeThrows { get; set; } = new NotSupportedException();
    public Exception? InstallQuiltThrows { get; set; } = new NotSupportedException();
    public Exception? InstallNeoForgeThrows { get; set; } = new NotSupportedException();
    public Exception? InstallOptiFineThrows { get; set; } = new NotSupportedException();

    public VersionDetail MergeVersionJson(VersionDetail child, VersionDetail parent)
        => throw new NotSupportedException();

    public Task<List<ModLoaderVersion>> GetFabricLoaderVersionsAsync(HttpClient http, string mcVersion, CancellationToken ct = default)
        => throw new NotSupportedException();
    public Task<List<ModLoaderVersion>> GetForgeVersionsAsync(HttpClient http, string mcVersion, CancellationToken ct = default)
        => throw new NotSupportedException();
    public Task<List<ModLoaderVersion>> GetQuiltLoaderVersionsAsync(HttpClient http, string mcVersion, CancellationToken ct = default)
        => throw new NotSupportedException();
    public Task<List<ModLoaderVersion>> GetNeoForgeVersionsAsync(HttpClient http, string mcVersion, CancellationToken ct = default)
        => throw new NotSupportedException();
    public Task<List<ModLoaderVersion>> GetOptiFineVersionsAsync(HttpClient http, string mcVersion, CancellationToken ct = default)
        => throw new NotSupportedException();

    public async Task InstallFabricAsync(HttpClient http, string gameDir, string mcVersion,
        string loaderVersion, string instanceName, IProgress<DownloadProgress> progress,
        Action<string, NotificationType, int> showNotification,
        CancellationToken ct = default)
    {
        if (InstallFabricThrows != null) throw InstallFabricThrows;
        await Task.CompletedTask;
    }

    public async Task InstallForgeAsync(HttpClient http, string gameDir, string mcVersion,
        string forgeVersion, string instanceName, string javaPath,
        IProgress<DownloadProgress> progress,
        Action<string, NotificationType, int> showNotification,
        CancellationToken ct = default)
    {
        if (InstallForgeThrows != null) throw InstallForgeThrows;
        await Task.CompletedTask;
    }

    public async Task InstallQuiltAsync(HttpClient http, string gameDir, string mcVersion,
        string loaderVersion, string instanceName, IProgress<DownloadProgress> progress,
        Action<string, NotificationType, int> showNotification,
        CancellationToken ct = default)
    {
        if (InstallQuiltThrows != null) throw InstallQuiltThrows;
        await Task.CompletedTask;
    }

    public async Task InstallNeoForgeAsync(HttpClient http, string gameDir, string mcVersion,
        string neoForgeVersion, string instanceName, string javaPath,
        IProgress<DownloadProgress> progress,
        Action<string, NotificationType, int> showNotification,
        CancellationToken ct = default)
    {
        if (InstallNeoForgeThrows != null) throw InstallNeoForgeThrows;
        await Task.CompletedTask;
    }

    public async Task InstallOptiFineAsync(HttpClient http, string gameDir, string mcVersion,
        string optiFineVersion, string instanceName, string javaPath,
        IProgress<DownloadProgress> progress,
        Action<string, NotificationType, int> showNotification,
        CancellationToken ct = default)
    {
        if (InstallOptiFineThrows != null) throw InstallOptiFineThrows;
        await Task.CompletedTask;
    }
}
