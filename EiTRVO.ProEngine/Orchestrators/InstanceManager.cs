using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using EiTRVO.ProEngine.Models;

namespace EiTRVO.ProEngine.Orchestrators;

/// <summary>
/// 实例管理器 — 扫描 versions/ 目录，自动检测 Mod 加载器类型。
/// 单一 ObservableCollection 作为 HomeViewModel 和 ManageViewModel 的数据来源。
/// </summary>
public class InstanceManager
{
    private readonly IGameFolderService _gameFolder;

    /// <summary>单一来源 — HomeViewModel 和 ManageViewModel 都绑定此集合。</summary>
    public ObservableCollection<GameInstance> Instances { get; } = new();

    public InstanceManager(IGameFolderService gameFolder)
    {
        _gameFolder = gameFolder;
    }

    public void Scan(Action<string, NotificationType> logWarning)
    {
        Instances.Clear();
        string vDir = _gameFolder.VersionsDir;
        if (!Directory.Exists(vDir)) return;

        // First pass: parse all version.json files to collect inheritsFrom targets
        var parsed = new List<(string dir, string folderName, VersionDetail detail)>();
        var inheritsFromTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string dir in Directory.GetDirectories(vDir))
        {
            string jsonFile = Path.Combine(dir, "version.json");
            if (!File.Exists(jsonFile)) continue;

            try
            {
                var detail = JsonSerializer.Deserialize<VersionDetail>(File.ReadAllText(jsonFile));
                if (detail?.Id == null) continue;

                parsed.Add((dir, Path.GetFileName(dir), detail));

                if (!string.IsNullOrEmpty(detail.InheritsFrom))
                    inheritsFromTargets.Add(detail.InheritsFrom);
            }
            catch (Exception ex) { logWarning($"解析版本 {Path.GetFileName(dir)} 失败：{ex.Message}", NotificationType.Warning); }
        }

        // Second pass: add instances, skipping parent-only directories
        foreach (var (dir, folderName, detail) in parsed)
        {
            string versionId = detail.Id!;

            // Skip hidden parent — a directory that exists only as an inheritsFrom target
            // (no instance.json) for another version. Real instances always have instance.json.
            bool isInheritsTarget = inheritsFromTargets.Contains(versionId);
            bool hasInstanceMeta = File.Exists(Path.Combine(dir, "instance.json"));
            if (isInheritsTarget && !hasInstanceMeta)
                continue;

            // For inheritsFrom versions (Forge/NeoForge), the JAR is in the parent's directory
            string jarFile;
            if (!string.IsNullOrEmpty(detail.InheritsFrom))
                jarFile = Path.Combine(vDir, detail.InheritsFrom, $"{detail.InheritsFrom}.jar");
            else
                jarFile = Path.Combine(dir, $"{versionId}.jar");
            if (!File.Exists(jarFile)) continue;

            var instance = new GameInstance { Name = folderName, VersionId = versionId };

            // Read instance isolation metadata
            if (hasInstanceMeta)
            {
                try
                {
                    var meta = JsonSerializer.Deserialize<InstanceMeta>(File.ReadAllText(Path.Combine(dir, "instance.json")));
                    if (meta != null)
                    {
                        instance.UseIsolatedDir = meta.UseIsolatedDir;
                        instance.InstanceDir = meta.InstanceDir;
                        instance.LoaderType = meta.LoaderType;
                        instance.LoaderVersion = meta.LoaderVersion;
                        instance.TotalPlayTimeSeconds = meta.TotalPlayTimeSeconds ?? 0;
                        instance.LastPlayedAt = meta.LastPlayedAt;
                    }
                }
                catch { /* ignore corrupt metadata */ }
            }

            // Fallback: auto-detect loader type from version.json
            if (string.IsNullOrEmpty(instance.LoaderType))
                instance.LoaderType = DetectLoaderType(detail);

            Instances.Add(instance);
        }
    }

    public void Refresh(Action<string, NotificationType> logWarning)
        => Scan(logWarning);

    public GameInstance? FindByName(string name)
        => Instances.FirstOrDefault(i => i.Name == name);

    private static string? DetectLoaderType(VersionDetail detail)
    {
        string mainClass = detail?.MainClass ?? "";
        string tweakClass = detail?.MinecraftArguments ?? "";

        // NeoForge must be checked BEFORE Forge — "neoforged" contains "forge" substring
        if (mainClass.Contains("neoforged") || mainClass.Contains("net.neoforged"))
            return "NeoForge";
        if (mainClass.Contains("forge") || mainClass.Contains("cpw.mods") || mainClass.Contains("net.minecraftforge")
            || (mainClass.Contains("launchwrapper")     // legacy Forge (≤1.12.2)
                && detail?.Libraries != null
                && detail.Libraries.Any(l => (l.Name ?? "").StartsWith("net.minecraftforge:"))))
            return "Forge";
        if (mainClass.Contains("fabricmc"))
            return "Fabric";
        if (mainClass.Contains("quiltmc"))
            return "Quilt";
        if (tweakClass.Contains("optifine.OptiFineTweaker") ||
            tweakClass.Contains("optifine") ||
            (mainClass.Contains("Launch") && detail?.Libraries != null &&
             detail.Libraries.Any(l => (l.Name ?? "").Contains("optifine"))))
            return "OptiFine";

        return null;
    }
}
