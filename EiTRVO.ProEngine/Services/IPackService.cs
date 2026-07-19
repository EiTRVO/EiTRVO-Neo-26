using System;
using System.Threading.Tasks;
using EiTRVO.ProEngine.Models;

namespace EiTRVO.ProEngine.Services;

public interface IPackService
{
    // === 导出 ===
    Task ExportAsync(string instanceName, string gameDir, string instanceDir,
        string exportPath, GameInstance instance,
        IProgress<DownloadProgress> progress,
        Action<string, NotificationType, int> showNotification);

    // === 导入 ===
    PackManifest ReadManifest(string packPath);
    string PeekInstanceFolderName(string packPath);
    VersionDetail? PeekVersionJson(string packPath);
    Task ExtractAsync(string packPath, string instanceName, string gameDir,
        IProgress<DownloadProgress> progress,
        Action<string, NotificationType, int> showNotification);

    // === 校验 ===
    Task<bool> VerifyModsAsync(string instanceDir, PackManifest manifest,
        Action<string, NotificationType, int> showNotification);
}
