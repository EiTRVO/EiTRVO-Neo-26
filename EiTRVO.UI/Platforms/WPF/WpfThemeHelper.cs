using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace EiTRVO.UI.Platforms;

/// <summary>
/// WPF 平台的主题辅助 — 将颜色 hex 字典应用到 ResourceDictionary 画刷。
/// </summary>
public static class WpfThemeHelper
{
    public static void ApplyColorScheme(ResourceDictionary resources, IReadOnlyDictionary<string, string> colors)
    {
        foreach (var kv in colors)
        {
            var color = ColorConverter.ConvertFromString(kv.Value);
            if (color is Color c)
                resources[kv.Key] = new SolidColorBrush(c);
        }
    }
}
