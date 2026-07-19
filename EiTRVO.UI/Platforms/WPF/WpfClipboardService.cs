using System.Windows;
using EiTRVO.ProEngine.Services;

namespace EiTRVO.UI.Platforms;

/// <summary>
/// WPF 平台的剪贴板服务实现。
/// </summary>
public class WpfClipboardService : IClipboardService
{
    public void Copy(string text)
    {
        try { Clipboard.SetText(text); } catch { }
    }
}
