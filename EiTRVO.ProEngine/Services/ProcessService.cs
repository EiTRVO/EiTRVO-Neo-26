using System;
using System.Diagnostics;
using System.IO;

namespace EiTRVO.ProEngine.Services;

public class ProcessService : IProcessService
{
    public void OpenUrl(string url)
    {
        // Only allow HTTPS URLs through the shell — prevents file:// and other
        // dangerous schemes from being executed via UseShellExecute.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != "https")
            throw new InvalidOperationException(
                $"无法打开不安全的 URL（仅支持 HTTPS）: {uri?.Host}");
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    public void OpenFolder(string folderPath)
    {
        if (Directory.Exists(folderPath))
            Process.Start(new ProcessStartInfo { FileName = folderPath, UseShellExecute = true });
    }
}
