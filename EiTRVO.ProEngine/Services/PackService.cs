using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using EiTRVO.ProEngine.Helpers;
using EiTRVO.ProEngine.Models;

namespace EiTRVO.ProEngine.Services;

public class PackService : IPackService
{
    // 导出时排除的顶级目录（大小写不敏感）
    private static readonly HashSet<string> ExcludedDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "logs", "crash-reports", "libraries"
    };

    // ==================== 导出 ====================

    public async Task ExportAsync(string instanceName, string gameDir, string instanceDir,
        string exportPath, GameInstance instance,
        IProgress<DownloadProgress> progress,
        Action<string, NotificationType, int> showNotification)
    {
        string versionDir = Path.Combine(gameDir, "versions", instanceName);

        // 1. 读取 version.json
        string versionJsonPath = Path.Combine(versionDir, "version.json");
        if (!File.Exists(versionJsonPath))
            throw new FileNotFoundException($"找不到版本文件：{versionJsonPath}");

        var versionDetail = JsonSerializer.Deserialize<VersionDetail>(File.ReadAllText(versionJsonPath))
            ?? throw new InvalidDataException("版本 JSON 解析失败");

        // 2. 读取 instance.json
        string instanceJsonPath = Path.Combine(versionDir, "instance.json");
        InstanceMeta? meta = null;
        if (File.Exists(instanceJsonPath))
            meta = JsonSerializer.Deserialize<InstanceMeta>(File.ReadAllText(instanceJsonPath));

        // 3. 确定 MC 版本
        // inheritsFrom 优先：Forge/Fabric 实例的 Id 是 "1.12.2-forge-xxx"，
        // 真正的 Minecraft 版本号在 inheritsFrom 字段中。Vanilla 实例 inheritsFrom 为 null。
        string mcVersion = versionDetail.InheritsFrom ?? versionDetail.Id ?? instance.VersionId;
        string? inheritsFrom = versionDetail.InheritsFrom;

        // 4. 确定 mod loader 信息
        string? loaderType = instance.LoaderType;
        string? loaderVersion = instance.LoaderVersion;
        if (meta != null)
        {
            loaderType ??= meta.LoaderType;
            loaderVersion ??= meta.LoaderVersion;
        }

        // 5. 【第一遍】扫描目录：收集文件列表 + 计算 SHA-256（mods only）
        var modEntries = new List<PackModEntry>();
        var filesToPack = new List<(string AbsolutePath, string RelativePath)>();

        if (Directory.Exists(instanceDir))
        {
            var allFiles = Directory.GetFiles(instanceDir, "*", SearchOption.AllDirectories);
            foreach (string filePath in allFiles)
            {
                string relativePath = Path.GetRelativePath(instanceDir, filePath);
                string normalized = relativePath.Replace('\\', '/');

                // 排除检查
                if (ShouldExclude(normalized))
                    continue;

                filesToPack.Add((filePath, normalized));

                // mods/*.jar → 计算 SHA-256
                if (normalized.StartsWith("mods/", StringComparison.OrdinalIgnoreCase)
                    && normalized.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                {
                    string sha256 = ComputeSha256(filePath);
                    modEntries.Add(new PackModEntry
                    {
                        Name = Path.GetFileName(filePath),
                        Sha256 = sha256
                    });
                }

                // 进度：每 20 个文件报告一次
                if (filesToPack.Count % 20 == 0)
                    progress.Report(DownloadProgress.FileProgress(
                        Path.GetFileName(filePath), filesToPack.Count, filesToPack.Count, 0));
            }
        }

        // 6. 检测 saves/
        bool hasSaves = false;
        string savesDir = Path.Combine(instanceDir, "saves");
        if (Directory.Exists(savesDir))
        {
            try { hasSaves = Directory.GetFileSystemEntries(savesDir).Length > 0; }
            catch { /* 忽略权限错误 */ }
        }

        // 7. 构建完整 manifest
        var manifest = new PackManifest
        {
            Format = AppInfo.PackFormat,
            PackVersion = AppInfo.PackVersion,
            Name = instanceName,
            Author = "",
            ExportedAt = DateTime.UtcNow.ToString("o"),
            ExporterVersion = 26,
            Instance = new PackInstanceInfo
            {
                Name = instanceName,
                UseIsolatedDir = instance.UseIsolatedDir
            },
            Minecraft = new PackMinecraftInfo
            {
                Version = mcVersion,
                ModLoader = loaderType,
                ModLoaderVersion = loaderVersion
            },
            InheritsFrom = inheritsFrom,
            Mods = modEntries,
            HasSaves = hasSaves
        };

        // 8. 【第二遍】写入 zip
        string? exportDir = Path.GetDirectoryName(exportPath);
        if (!string.IsNullOrEmpty(exportDir))
            Directory.CreateDirectory(exportDir);

        if (File.Exists(exportPath))
            File.Delete(exportPath);

        int totalEntries = 1 + 1 + (File.Exists(instanceJsonPath) ? 1 : 0) + filesToPack.Count;
        int written = 0;
        progress.Report(DownloadProgress.Overall(0, totalEntries));

        using var zip = ZipFile.Open(exportPath, ZipArchiveMode.Create);

        // 8a. manifest.json（根级）
        {
            var entry = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
            await using var ms = entry.Open();
            await JsonSerializer.SerializeAsync(ms, manifest, new JsonSerializerOptions { WriteIndented = true });
            written++;
            progress.Report(DownloadProgress.Overall(written, totalEntries));
        }

        // 8b. version.json（来自 versions/{name}/ 目录）
        {
            var entry = zip.CreateEntry($"{instanceName}/version.json", CompressionLevel.Optimal);
            await using var es = entry.Open();
            await using var fs = File.OpenRead(versionJsonPath);
            await fs.CopyToAsync(es);
            written++;
            progress.Report(DownloadProgress.Overall(written, totalEntries));
        }

        // 8c. instance.json（如果存在）
        // 写入前清除 InstanceDir（含隐私路径，导入端会自动重建）
        if (File.Exists(instanceJsonPath))
        {
            string metaJson = File.ReadAllText(instanceJsonPath);
            try
            {
                var exportMeta = JsonSerializer.Deserialize<InstanceMeta>(metaJson);
                if (exportMeta != null)
                {
                    exportMeta.InstanceDir = null;  // 隐私：不携带原机器路径
                    metaJson = JsonSerializer.Serialize(exportMeta, new JsonSerializerOptions { WriteIndented = true });
                }
            }
            catch { /* 损坏则原样写入 */ }

            var entry = zip.CreateEntry($"{instanceName}/instance.json", CompressionLevel.Optimal);
            await using var es = entry.Open();
            await using var sw = new StreamWriter(es);
            await sw.WriteAsync(metaJson);
            written++;
            progress.Report(DownloadProgress.Overall(written, totalEntries));
        }

        // 8d. 实例目录下的所有文件
        foreach (var (absPath, relPath) in filesToPack)
        {
            string entryPath = $"{instanceName}/{relPath}";
            var entry = zip.CreateEntry(entryPath, CompressionLevel.Optimal);
            await using var es = entry.Open();
            await using var fs = File.OpenRead(absPath);
            await fs.CopyToAsync(es);

            written++;
            // 每 10 个文件报告一次进度（避免 UI 过于频繁刷新）
            if (written % 10 == 0 || written >= totalEntries)
                progress.Report(DownloadProgress.Overall(written, totalEntries));
        }
    }

    // ==================== 导入 ====================

    public PackManifest ReadManifest(string packPath)
    {
        using var zip = ZipFile.OpenRead(packPath);
        var manifestEntry = zip.GetEntry("manifest.json")
            ?? throw new InvalidDataException("整合包中未找到 manifest.json");

        using var stream = manifestEntry.Open();
        var manifest = JsonSerializer.Deserialize<PackManifest>(stream)
            ?? throw new InvalidDataException("manifest.json 解析失败");

        if (manifest.Format != AppInfo.PackFormat)
            throw new InvalidDataException($"不支持的包格式：\"{manifest.Format}\"（期望 \"{AppInfo.PackFormat}\"）");

        return manifest;
    }

    public string PeekInstanceFolderName(string packPath)
    {
        using var zip = ZipFile.OpenRead(packPath);
        return PeekInstanceFolderNameInternal(zip);
    }

    public VersionDetail? PeekVersionJson(string packPath)
    {
        using var zip = ZipFile.OpenRead(packPath);
        string instanceFolder = PeekInstanceFolderNameInternal(zip);

        var versionEntry = zip.GetEntry($"{instanceFolder}/version.json");
        if (versionEntry == null) return null;

        using var stream = versionEntry.Open();
        return JsonSerializer.Deserialize<VersionDetail>(stream);
    }

    public Task ExtractAsync(string packPath, string instanceName, string gameDir,
        IProgress<DownloadProgress> progress,
        Action<string, NotificationType, int> showNotification)
    {
        return Task.Run(() =>
        {
            using var zip = ZipFile.OpenRead(packPath);
            string instanceFolder = PeekInstanceFolderNameInternal(zip);
            string prefix = instanceFolder + "/";

            string targetDir = Path.Combine(gameDir, "versions", instanceName);
            Directory.CreateDirectory(targetDir);

            // 计数
            int totalEntries = 0;
            int processed = 0;
            foreach (var entry in zip.Entries)
            {
                string fullName = entry.FullName.Replace('\\', '/');
                if (fullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && fullName.Length > prefix.Length
                    && !string.IsNullOrEmpty(entry.Name))
                    totalEntries++;
            }

            progress.Report(DownloadProgress.Overall(0, totalEntries > 0 ? totalEntries : 1));

            foreach (var entry in zip.Entries)
            {
                string fullName = entry.FullName.Replace('\\', '/');

                // 只处理实例文件夹内的条目
                if (!fullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                string relativePath = fullName.Substring(prefix.Length);
                if (string.IsNullOrEmpty(relativePath))
                    continue;

                // 跳过目录条目（entry.Name 为空表示目录）
                if (string.IsNullOrEmpty(entry.Name))
                {
                    string dirPath = Path.Combine(targetDir, relativePath);
                    PathSafetyHelper.ValidateContained(dirPath, targetDir);
                    Directory.CreateDirectory(dirPath);
                    continue;
                }

                // 排除防御：*.part 文件
                if (relativePath.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
                    continue;

                string destPath = Path.Combine(targetDir, relativePath);
                PathSafetyHelper.ValidateContained(destPath, targetDir);

                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                entry.ExtractToFile(destPath, true);

                processed++;
                progress.Report(DownloadProgress.Overall(processed, totalEntries));
            }
        });
    }

    // ==================== 校验 ====================

    public Task<bool> VerifyModsAsync(string instanceDir, PackManifest manifest,
        Action<string, NotificationType, int> showNotification)
    {
        return Task.Run(() =>
        {
            if (manifest.Mods == null || manifest.Mods.Count == 0)
                return true;

            bool allMatch = true;
            string modsDir = Path.Combine(instanceDir, "mods");

            foreach (var mod in manifest.Mods)
            {
                string modPath = Path.Combine(modsDir, mod.Name);
                if (!File.Exists(modPath))
                {
                    showNotification($"Mod 文件缺失：{mod.Name}", NotificationType.Warning, 5000);
                    allMatch = false;
                    continue;
                }

                string actualHash = ComputeSha256(modPath);
                if (!string.Equals(actualHash, mod.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    showNotification($"Mod 文件校验失败：{mod.Name}（哈希不匹配）", NotificationType.Warning, 5000);
                    allMatch = false;
                }
            }

            return allMatch;
        });
    }

    // ==================== 工具方法 ====================

    /// <summary>
    /// 计算文件的 SHA-256 哈希值（小写十六进制）。
    /// </summary>
    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// 判断导出时是否应排除某文件。
    /// </summary>
    /// <param name="normalizedPath">以 / 分隔的相对路径，如 "mods/jei.jar" 或 "logs/latest.log"</param>
    private static bool ShouldExclude(string normalizedPath)
    {
        // 排除特定顶级目录（logs, crash-reports, libraries）
        int firstSlash = normalizedPath.IndexOf('/');
        string topDir = firstSlash > 0 ? normalizedPath.Substring(0, firstSlash) : normalizedPath;
        if (ExcludedDirs.Contains(topDir))
            return true;

        // 排除 *.part 残留文件
        if (normalizedPath.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
            return true;

        // 排除实例根级 *.jar（MC 客户端 JAR + mod loader JAR）
        // mods/ 下的 jar 不会被排除（已通过 topDir 检查）
        if (!normalizedPath.Contains('/') && normalizedPath.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// 在 zip 中定位实例文件夹名（即第一层包含 version.json 的目录名）。
    /// </summary>
    private static string PeekInstanceFolderNameInternal(ZipArchive zip)
    {
        foreach (var entry in zip.Entries)
        {
            string fullName = entry.FullName.Replace('\\', '/');
            int slashIdx = fullName.IndexOf('/');
            if (slashIdx <= 0) continue;

            string folderName = fullName.Substring(0, slashIdx);
            string fileName = fullName.Substring(slashIdx + 1);

            if (fileName.Equals("version.json", StringComparison.OrdinalIgnoreCase))
                return folderName;
        }
        throw new InvalidDataException("整合包中未找到实例文件夹（需包含 version.json）");
    }
}
