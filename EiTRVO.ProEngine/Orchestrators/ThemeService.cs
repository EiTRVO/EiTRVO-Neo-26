using System;
using System.Collections.Generic;

namespace EiTRVO.ProEngine.Orchestrators;

/// <summary>
/// 平台无关的主题服务实现 — 存储颜色方案为 hex 字符串字典。
/// 颜色值由平台层（WPF/WinUI）应用到具体画刷/资源。
/// </summary>
public class ThemeService : IThemeService
{
    public bool IsDark { get; private set; }
    public event Action<bool>? ThemeChanged;

    public void SetTheme(bool isDark)
    {
        IsDark = isDark;
        ThemeChanged?.Invoke(isDark);
    }

    public IReadOnlyDictionary<string, string> GetColorScheme()
    {
        return IsDark ? DarkScheme : LightScheme;
    }

    private static readonly Dictionary<string, string> DarkScheme = new()
    {
        ["WindowBg"] = "#1E1E2E",
        ["SidebarBg"] = "#1A1B26",
        ["SidebarBorder"] = "#2D2E40",
        ["MenuHoverBg"] = "#2A2B3D",
        ["MenuActiveBg"] = "#363752",
        ["PanelBorder"] = "#383A55",
        ["CardBg"] = "#2A2B40",
        ["CardHoverBg"] = "#31335A",
        ["CardSelectedBg"] = "#3B3E6B",
        ["TextPrimary"] = "#CDD6F4",
        ["TextSecondary"] = "#878BA6",
        ["TextMuted"] = "#585B70",
        ["AccentBlue"] = "#2563EB",
        ["AccentRed"] = "#E53935",
        ["AccentOrange"] = "#E67E22",
        ["AccentGreen"] = "#2E7D32",
        ["AccentYellow"] = "#D4A017",
        ["Surface"] = "#313244",
        ["DeepDark"] = "#11111B",
        ["MutedText"] = "#6C7086",
        ["Subtext"] = "#A6ADC8",
        ["BtnBg"] = "#313244",
        ["BtnBorder"] = "#45475A",
        ["BtnHoverBg"] = "#45475A",
        ["InputBorder"] = "#45475A",
        ["PrimaryBtnBg"] = "#2563EB",
        ["PrimaryBtnFg"] = "#1E1E2E",
        ["InfoBrush"] = "#2563EB",
        ["SuccessBrush"] = "#2E7D32",
        ["WarningBrush"] = "#E67E22",
        ["ErrorBrush"] = "#E53935",
        ["LogPanelBg"] = "#1A1B26",
        ["LogItemBg"] = "#24253A",
        ["LogItemBorder"] = "#2D2E40",
    };

    private static readonly Dictionary<string, string> LightScheme = new()
    {
        ["WindowBg"] = "#EEF0F4",
        ["SidebarBg"] = "#E3E5EB",
        ["SidebarBorder"] = "#D0D3DB",
        ["MenuHoverBg"] = "#D6DAE0",
        ["MenuActiveBg"] = "#CCD2DD",
        ["PanelBorder"] = "#D8DCE3",
        ["CardBg"] = "#FFFFFF",
        ["CardHoverBg"] = "#F0F3F8",
        ["CardSelectedBg"] = "#E8EDF8",
        ["TextPrimary"] = "#2C3E50",
        ["TextSecondary"] = "#5A6270",
        ["TextMuted"] = "#8A92A0",
        ["AccentBlue"] = "#66CCFF",
        ["AccentRed"] = "#F38BA8",
        ["AccentOrange"] = "#FAB387",
        ["AccentGreen"] = "#A6E3A1",
        ["AccentYellow"] = "#F9E2AF",
        ["Surface"] = "#FFFFFF",
        ["DeepDark"] = "#F6F7FA",
        ["MutedText"] = "#8A92A0",
        ["Subtext"] = "#5A6270",
        ["BtnBg"] = "#EDEFF4",
        ["BtnBorder"] = "#D0D5DF",
        ["BtnHoverBg"] = "#DEE2EC",
        ["InputBorder"] = "#D0D5DF",
        ["PrimaryBtnBg"] = "#66CCFF",
        ["PrimaryBtnFg"] = "#FFFFFF",
        ["InfoBrush"] = "#3B82F6",
        ["SuccessBrush"] = "#A6E3A1",
        ["WarningBrush"] = "#FAB387",
        ["ErrorBrush"] = "#F38BA8",
        ["LogPanelBg"] = "#E3E5EB",
        ["LogItemBg"] = "#F6F7FA",
        ["LogItemBorder"] = "#D8DCE3",
    };
}
