using System;
using System.Threading;
using System.Threading.Tasks;
using EiTRVO.ProEngine.Helpers;
using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Services;

namespace EiTRVO.Tests.Fakes;

public class FakeMrpackInstallService : IMrpackInstallService
{
    public MrpackInfo? ParseResult { get; set; }
    public Exception? ParseThrows { get; set; }
    public Exception? InstallThrows { get; set; }
    public bool InstallCalled { get; set; }

    public Func<string, string, Task<string?>>? JavaCompatibilityHandler { get; set; }

    public Task<MrpackInfo> ParseMrpackAsync(string mrpackPath, CancellationToken ct = default)
    {
        if (ParseThrows != null)
            throw ParseThrows;
        return Task.FromResult(ParseResult ?? new MrpackInfo());
    }

    public Task InstallMrpackAsync(
        string mrpackPath,
        string instanceName,
        string targetDir,
        string gameDir,
        string mcVersion,
        string? loaderType,
        string? loaderVersion,
        string? javaPath,
        LauncherSettings settings,
        IProgress<DownloadProgress> progress,
        Action<string, NotificationType, int> log,
        CancellationToken ct = default)
    {
        InstallCalled = true;
        if (InstallThrows != null)
            throw InstallThrows;
        return Task.CompletedTask;
    }
}
