using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using EiTRVO.ProEngine.Models;

namespace EiTRVO.ProEngine.Services;

public interface IDownloadService
{
    Task<List<ManifestVersion>> LoadOnlineVersionsAsync(HttpClient httpClient, CancellationToken ct = default);

    Task DownloadVersionFilesAsync(HttpClient httpClient, string gameDir,
        string versionJsonUrl, string versionId, string instanceName,
        IProgress<DownloadProgress> progress,
        Action<string, NotificationType, int> showNotification,
        CancellationToken ct = default);

    void ExtractNativeJar(string jarPath, string destDir);
}
