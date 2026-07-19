using EiTRVO.ProEngine.Services;

namespace EiTRVO.Tests.Fakes;

public class FakeProcessService : IProcessService
{
    public string? LastOpenedUrl { get; private set; }
    public string? LastOpenedFolder { get; private set; }

    public void OpenUrl(string url) => LastOpenedUrl = url;
    public void OpenFolder(string folderPath) => LastOpenedFolder = folderPath;
}
