using System;
using System.Collections.Generic;

namespace EiTRVO.ProEngine.Orchestrators;

/// <summary>
/// 平台无关的主题管理服务 — 管理 Catppuccin Mocha 深色/浅色主题切换。
/// 返回颜色字典 (key → hex string)，由平台层负责应用到具体 UI 框架。
/// </summary>
public interface IThemeService
{
    bool IsDark { get; }
    void SetTheme(bool isDark);
    IReadOnlyDictionary<string, string> GetColorScheme();
    event Action<bool>? ThemeChanged;
}
