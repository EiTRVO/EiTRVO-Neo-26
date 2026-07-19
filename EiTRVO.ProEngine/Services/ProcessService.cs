using System.Diagnostics;
using System.IO;

namespace EiTRVO.ProEngine.Services;

public class ProcessService : IProcessService
{
    public void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    public void OpenFolder(string folderPath)
    {
        if (Directory.Exists(folderPath))
            Process.Start(new ProcessStartInfo { FileName = folderPath, UseShellExecute = true });
    }
}
