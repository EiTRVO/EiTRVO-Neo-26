using EiTRVO.ProEngine.Services;

namespace EiTRVO.Tests.Fakes;

public class FakeClipboardService : IClipboardService
{
    public string? LastCopiedText { get; private set; }
    public int CopyCallCount { get; private set; }

    public void Copy(string text)
    {
        LastCopiedText = text;
        CopyCallCount++;
    }
}
