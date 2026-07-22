using System;
using System.IO;

namespace EiTRVO.ProEngine.Helpers;

/// <summary>
/// 路径安全工具类 —— 集中式路径穿越防护。
/// 所有需要消毒用户输入或验证路径边界的代码均应使用此类。
/// </summary>
public static class PathSafetyHelper
{
    /// <summary>
    /// 消毒单级路径组件（文件名/目录名），剥离路径分隔符与 <c>..</c> 遍历序列。
    /// 空值/null 返回 <c>"unnamed"</c>。
    /// </summary>
    /// <remarks>
    /// <see cref="Path.GetFileName(string)"/> 会剥离所有目录组件，包括 <c>..</c>、<c>.</c>、
    /// <c>/</c>、<c>\</c>。Windows 和 Unix 均适用——在跨平台项目中行为一致。
    /// </remarks>
    /// <param name="name">待消毒的名称</param>
    /// <returns>仅保留最后一级文件名，非法输入返回 "unnamed"</returns>
    public static string SanitizeNameComponent(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "unnamed";

        string sanitized = Path.GetFileName(name.Trim());

        // Path.GetFileName 在输入为纯分隔符时返回空串
        if (string.IsNullOrEmpty(sanitized))
            return "unnamed";

        return sanitized;
    }

    /// <summary>
    /// 验证目标路径是否安全地落在基准目录内（Zip Slip 防护）。
    /// 使用 <see cref="Path.GetFullPath"/> 解析所有 <c>..</c> 后再比较，
    /// 并附加 <see cref="Path.DirectorySeparatorChar"/> 防止前缀误匹配。
    /// </summary>
    /// <param name="destPath">待验证的目标路径</param>
    /// <param name="baseDir">基准目录</param>
    /// <exception cref="InvalidDataException">路径试图逃逸基准目录</exception>
    public static void ValidateContained(string destPath, string baseDir)
    {
        if (!IsContained(destPath, baseDir))
        {
            string safeDest = SanitizeNameComponent(destPath);
            throw new InvalidDataException($"路径穿越检测: {safeDest}");
        }
    }

    /// <summary>
    /// 检查目标路径是否安全地落在基准目录内（不抛异常版本）。
    /// 用于需要静默跳过而非抛出异常的场景。
    /// </summary>
    /// <param name="destPath">待检查的目标路径</param>
    /// <param name="baseDir">基准目录</param>
    /// <returns>true 表示路径安全</returns>
    public static bool IsContained(string destPath, string baseDir)
    {
        string fullDest = Path.GetFullPath(destPath);
        string fullBase = Path.GetFullPath(baseDir).TrimEnd(Path.DirectorySeparatorChar);

        if (!(fullDest.StartsWith(fullBase + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || string.Equals(fullDest, fullBase, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        // 防御 NTFS 符号链接/Junction 穿越：逐段检查路径中是否存在重解析点
        if (HasReparsePointInPath(fullBase, fullDest))
            return false;

        return true;
    }

    /// <summary>
    /// 检查从 <paramref name="baseDir"/> 到 <paramref name="fullDest"/> 的路径中，
    /// 是否存在 NTFS 重解析点（符号链接 / Junction / 挂载点）。
    /// <see cref="Path.GetFullPath"/> 不会解析重解析点，因此攻击者可在沙箱内
    /// 创建指向外部的 Junction 来绕过前缀检查。
    /// </summary>
    private static bool HasReparsePointInPath(string baseDir, string fullDest)
    {
        string remaining = fullDest.Substring(baseDir.Length)
            .TrimStart(Path.DirectorySeparatorChar);
        if (string.IsNullOrEmpty(remaining)) return false;

        string current = baseDir;
        foreach (string segment in remaining.Split(Path.DirectorySeparatorChar))
        {
            if (string.IsNullOrEmpty(segment)) continue;
            current = Path.Combine(current, segment);

            // 仅检查已存在的路径 — 未创建的路径不构成威胁
            if (!File.Exists(current) && !Directory.Exists(current))
                continue;

            try
            {
                FileAttributes attrs = File.GetAttributes(current);
                if ((attrs & FileAttributes.ReparsePoint) != 0)
                    return true;
            }
            catch
            {
                // 权限不足等场景 — 保守放行（攻击者通常无法在无权限目录中放置 Junction）
            }
        }
        return false;
    }
}
