using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using EiTRVO.ProEngine.Models;

namespace EiTRVO.ProEngine.Helpers;

/// <summary>
/// 整合包安全扫描器 —— 对 .zip (eitrvo-pack) 和 .mrpack (Modrinth) 格式
/// 统一执行安全检测，防止恶意整合包导入。
/// </summary>
public static class ModpackSafetyScanner
{
    // ================================================================
    // 危险文件扩展名（在 Minecraft 实例目录中没有合法用途）
    // ================================================================

    /// <summary>整合包中绝对不允许的可执行/脚本文件扩展名</summary>
    public static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".bat", ".cmd", ".ps1", ".vbs", ".vbe", ".wsf", ".wsh",
        ".msi", ".scr", ".pif", ".reg", ".com", ".cpl", ".hta", ".jse", ".sct",
        ".so", ".dylib"
    };

    /// <summary>overrides 目录中不允许的扩展名（比 BlockedExtensions 更严格，排除 .jar）</summary>
    public static readonly HashSet<string> BlockedOverrideExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".ps1", ".vbs", ".vbe", ".wsf", ".wsh",
        ".msi", ".scr", ".pif", ".reg", ".com", ".cpl", ".hta", ".jse", ".sct",
        ".dll", ".so", ".dylib"
    };

    // ================================================================
    // 资源限制
    // ================================================================

    /// <summary>最大压缩包文件大小（500 MB）</summary>
    public const long MaxArchiveSize = 500L * 1024 * 1024;

    /// <summary>最大解压后总大小（2 GB）</summary>
    public const long MaxUncompressedTotalSize = 2L * 1024 * 1024 * 1024;

    /// <summary>最大单文件解压大小（200 MB）</summary>
    public const long MaxSingleFileUncompressedSize = 200L * 1024 * 1024;

    /// <summary>最大文件条目数</summary>
    public const int MaxFileCount = 10_000;

    /// <summary>最大路径嵌套深度</summary>
    public const int MaxNestingDepth = 10;

    // ================================================================
    // 扫描结果
    // ================================================================

    public sealed class SafetyReport
    {
        public bool HasBlockingIssues => BlockingIssues.Count > 0;
        public bool HasWarnings => Warnings.Count > 0;
        public List<string> BlockingIssues { get; } = new();
        public List<string> Warnings { get; } = new();

        public void AddBlock(string issue) => BlockingIssues.Add(issue);
        public void AddWarning(string warning) => Warnings.Add(warning);
    }

    // ================================================================
    // 公共 API
    // ================================================================

    /// <summary>
    /// 扫描 .zip 格式整合包（eitrvo-pack 原生格式）。
    /// </summary>
    public static SafetyReport ScanZipPack(string packPath)
    {
        var report = new SafetyReport();

        // 0. 文件大小检查
        try
        {
            var fileInfo = new FileInfo(packPath);
            if (fileInfo.Exists && fileInfo.Length > MaxArchiveSize)
            {
                report.AddBlock($"整合包文件过大（{FormatSize(fileInfo.Length)}），超过最大限制 {FormatSize(MaxArchiveSize)}。");
                return report; // 不继续解压
            }
        }
        catch (Exception ex)
        {
            report.AddBlock($"无法读取整合包文件信息：{ex.Message}");
            return report;
        }

        try
        {
            using var zip = ZipFile.OpenRead(packPath);
            ScanZipEntries(zip.Entries, report, isMrpack: false);
        }
        catch (InvalidDataException ex)
        {
            report.AddBlock($"整合包文件损坏或格式无效：{ex.Message}");
        }
        catch (Exception ex)
        {
            report.AddBlock($"扫描整合包时出错：{ex.Message}");
        }

        return report;
    }

    /// <summary>
    /// 扫描 .mrpack 格式整合包（Modrinth 格式）。
    /// 先验证清单格式，再检查文件列表和 overrides。
    /// </summary>
    public static SafetyReport ScanMrpack(string mrpackPath)
    {
        var report = new SafetyReport();

        // 0. 文件大小检查
        try
        {
            var fileInfo = new FileInfo(mrpackPath);
            if (fileInfo.Exists && fileInfo.Length > MaxArchiveSize)
            {
                report.AddBlock($"整合包文件过大（{FormatSize(fileInfo.Length)}），超过最大限制 {FormatSize(MaxArchiveSize)}。");
                return report;
            }
        }
        catch (Exception ex)
        {
            report.AddBlock($"无法读取整合包文件信息：{ex.Message}");
            return report;
        }

        try
        {
            using var zip = ZipFile.OpenRead(mrpackPath);

            // 1. 解析 modrinth.index.json
            var indexEntry = zip.GetEntry("modrinth.index.json");
            if (indexEntry == null)
            {
                report.AddBlock("无效的 .mrpack 文件：未找到 modrinth.index.json。");
                return report;
            }

            ModpackManifest? manifest;
            try
            {
                using var reader = new StreamReader(indexEntry.Open());
                manifest = JsonSerializer.Deserialize<ModpackManifest>(reader.ReadToEnd());
            }
            catch (Exception ex)
            {
                report.AddBlock($"无法解析 modrinth.index.json：{ex.Message}");
                return report;
            }

            if (manifest == null)
            {
                report.AddBlock("modrinth.index.json 为空。");
                return report;
            }

            // 2. 格式版本检查
            if (manifest.FormatVersion != 1)
                report.AddBlock($"不支持的整合包格式版本 ({manifest.FormatVersion})。当前仅支持格式版本 1。");

            // 3. game 类型检查
            if (!string.Equals(manifest.Game, "minecraft", StringComparison.OrdinalIgnoreCase))
                report.AddBlock($"不支持的 game 类型: \"{manifest.Game}\"。仅支持 minecraft。");

            if (report.HasBlockingIssues)
                return report;

            // 4. 检查 dependencies 中的未知 key
            var knownDependencyKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "minecraft", "fabric-loader", "forge", "quilt-loader", "neoforge"
            };
            foreach (var key in manifest.Dependencies.Keys)
            {
                if (!knownDependencyKeys.Contains(key))
                    report.AddWarning($"清单 contains 未知依赖项: \"{key}\" = \"{manifest.Dependencies[key]}\"。");
            }

            // 5. 检查下载 URL 白名单
            ScanDownloadUrls(manifest, report);

            // 6. 检查 overrides/ 中的危险文件
            ScanZipEntries(zip.Entries, report, isMrpack: true);

            // 7. 检查 mrpack 特有：overrides/ 外不应有未知文件
            var knownMrpackRootFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "modrinth.index.json"
            };
            foreach (var entry in zip.Entries)
            {
                string fullName = entry.FullName.Replace('\\', '/');
                // 跳过 overrides/ 前缀的条目和目录条目
                if (fullName.StartsWith("overrides/", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.IsNullOrEmpty(entry.Name))
                    continue;

                string rootPath = fullName.Split('/')[0];
                if (!rootPath.Equals("overrides", StringComparison.OrdinalIgnoreCase)
                    && !knownMrpackRootFiles.Contains(fullName))
                {
                    report.AddWarning($"整合包根目录包含非标准文件: {fullName}");
                }
            }
        }
        catch (InvalidDataException ex)
        {
            report.AddBlock($"整合包文件损坏或格式无效：{ex.Message}");
        }
        catch (Exception ex)
        {
            report.AddBlock($"扫描整合包时出错：{ex.Message}");
        }

        return report;
    }

    /// <summary>
    /// 检查 version.json 中的 mainClass 和 JVM 参数安全性。
    /// 可用于 .zip 和 .mrpack 两种格式。
    /// </summary>
    public static void ScanVersionJson(VersionDetail? versionDetail, SafetyReport report)
    {
        if (versionDetail == null) return;

        // mainClass 检查
        string? mainClass = versionDetail.MainClass;
        if (JvmArgHelper.IsMainClassBlocked(mainClass))
        {
            report.AddBlock($"version.json 中的 mainClass（{mainClass}）为 JRE 内部类，存在代码执行风险。");
        }
        else if (!JvmArgHelper.IsMainClassSafe(mainClass))
        {
            if (string.IsNullOrEmpty(mainClass))
                report.AddWarning("version.json 未指定 mainClass，游戏可能无法启动。");
            else
                report.AddWarning($"version.json 中的 mainClass（{mainClass}）非已知安全值，可能存在代码执行风险。");
        }

        // JVM 参数检查
        if (versionDetail.Arguments?.Jvm is { Count: > 0 } jvmArgs)
        {
            foreach (var element in jvmArgs)
            {
                // JVM 参数可以是 string 或 { rules: [...], value: string | string[] }
                if (element.ValueKind == JsonValueKind.String)
                {
                    CheckJvmArgString(element.GetString(), report);
                }
                else if (element.ValueKind == JsonValueKind.Object &&
                         element.TryGetProperty("value", out var valueProp))
                {
                    if (valueProp.ValueKind == JsonValueKind.String)
                        CheckJvmArgString(valueProp.GetString(), report);
                    else if (valueProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in valueProp.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                                CheckJvmArgString(item.GetString(), report);
                        }
                    }
                }
            }
        }
    }

    // ================================================================
    // 内部实现
    // ================================================================

    private static void ScanZipEntries(
        System.Collections.ObjectModel.ReadOnlyCollection<ZipArchiveEntry> entries,
        SafetyReport report,
        bool isMrpack)
    {
        int fileCount = 0;
        long totalUncompressedSize = 0;

        foreach (var entry in entries)
        {
            // 跳过目录
            if (string.IsNullOrEmpty(entry.Name))
            {
                // 但检查深度
                int depth = entry.FullName.Split('/', '\\').Length;
                if (depth > MaxNestingDepth)
                {
                    report.AddBlock($"路径嵌套深度 ({depth}) 超过限制 ({MaxNestingDepth}): {SanitizePath(entry.FullName)}");
                }
                continue;
            }

            fileCount++;
            if (fileCount > MaxFileCount)
            {
                report.AddBlock($"文件条目数 ({fileCount}) 超过限制 ({MaxFileCount})，可能存在 zip bomb 攻击。");
                return;
            }

            totalUncompressedSize += entry.Length;
            if (totalUncompressedSize > MaxUncompressedTotalSize)
            {
                report.AddBlock($"解压后总大小（{FormatSize(totalUncompressedSize)}）超过限制（{FormatSize(MaxUncompressedTotalSize)}），可能存在 zip bomb 攻击。");
                return;
            }

            if (entry.Length > MaxSingleFileUncompressedSize)
            {
                report.AddBlock($"文件 \"{SanitizePath(entry.Name)}\" 解压后大小（{FormatSize(entry.Length)}）超过单文件限制（{FormatSize(MaxSingleFileUncompressedSize)}）。");
            }

            // 路径遍历检查
            string entryPath = entry.FullName.Replace('\\', '/');
            if (entryPath.Contains(".."))
            {
                report.AddBlock($"文件路径包含 \"..\" 遍历序列: {SanitizePath(entry.FullName)}");
                continue;
            }

            // 深度检查
            int entryDepth = entryPath.Split('/').Length;
            if (entryDepth > MaxNestingDepth)
            {
                report.AddBlock($"文件嵌套深度 ({entryDepth}) 超过限制 ({MaxNestingDepth}): {SanitizePath(entry.FullName)}");
                continue;
            }

            // 危险扩展名检查
            string ext = Path.GetExtension(entry.Name);
            if (!string.IsNullOrEmpty(ext))
            {
                bool isInOverrides = isMrpack &&
                    entryPath.StartsWith("overrides/", StringComparison.OrdinalIgnoreCase);

                if (BlockedExtensions.Contains(ext))
                {
                    report.AddBlock($"整合包包含不允许的文件类型 ({ext}): {SanitizePath(entry.FullName)}");
                }
                else if (isInOverrides && BlockedOverrideExtensions.Contains(ext))
                {
                    report.AddBlock($"整合包 overrides 目录包含危险文件类型 ({ext}): {SanitizePath(entry.FullName)}");
                }
            }
        }
    }

    private static void ScanDownloadUrls(ModpackManifest manifest, SafetyReport report)
    {
        int nonWhitelistedCount = 0;
        int nonHttpsCount = 0;

        foreach (var file in manifest.Files)
        {
            foreach (var url in file.Downloads)
            {
                if (string.IsNullOrEmpty(url))
                    continue;

                if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    nonHttpsCount++;
                    break;
                }

                if (!DownloadSafetyHelper.IsDownloadUrlAllowed(url))
                {
                    nonWhitelistedCount++;
                    break;
                }
            }
        }

        if (nonHttpsCount > 0)
            report.AddWarning($"清单中有 {nonHttpsCount} 个文件使用非 HTTPS 下载链接，可能存在中间人攻击风险。");

        if (nonWhitelistedCount > 0)
            report.AddWarning($"清单中有 {nonWhitelistedCount} 个文件的下载链接不在已知信任域名列表中。");
    }

    /// <summary>
    /// 消毒路径用于错误消息 — 仅保留文件名，不泄露完整路径。
    /// </summary>
    private static void CheckJvmArgString(string? arg, SafetyReport report)
    {
        if (string.IsNullOrEmpty(arg)) return;

        if (!JvmArgHelper.IsJvmArgSafe(arg))
        {
            // 对安全消息进行消毒处理：只提取前缀部分，不使用完整参数值
            string safePrefix = arg.Length > 40 ? arg.Substring(0, 40) + "…" : arg;
            report.AddBlock($"version.json 包含危险 JVM 参数: {safePrefix}");
        }
    }

    internal static string SanitizePath(string fullPath)
    {
        string normalized = fullPath.Replace('\\', '/');
        // 只保留最后两级路径
        string[] parts = normalized.Split('/');
        if (parts.Length <= 2)
            return normalized;

        return $"…/{parts[^2]}/{parts[^1]}";
    }

    internal static string FormatSize(long bytes)
    {
        if (bytes < 0) return "?";
        if (bytes < 1024) return $"{bytes} B";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int unitIdx = 0;
        double size = bytes;
        while (size >= 1024.0 && unitIdx < units.Length - 1)
        {
            size /= 1024.0;
            unitIdx++;
        }
        return $"{size:F1} {units[unitIdx]}";
    }
}
