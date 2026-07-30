using System.Text.Json.Serialization;

namespace EiTRVO.ProEngine.Models;

public class GameInstance
{
    public string Name { get; set; } = "";
    public string VersionId { get; set; } = "";

    /// <summary>version.json 中 inheritsFrom 字段 — 模组加载器实例指向的父级原版版本。</summary>
    [JsonIgnore]
    public string? InheritsFrom { get; set; }

    /// <summary>实例的基础 Minecraft 版本号（不含加载器前缀）。优先取 inheritsFrom，回退到 VersionId。</summary>
    [JsonIgnore]
    public string BaseVersion => InheritsFrom ?? VersionId;

    // === 实例目录隔离 ===
    public bool UseIsolatedDir { get; set; } = false;
    public string? InstanceDir { get; set; }  // 实例游戏数据目录绝对路径，null = 未隔离

    public string DisplayText => $"{Name} ({VersionId})";

    [JsonIgnore]
    public string PathDisplay => UseIsolatedDir && InstanceDir != null
        ? $"游戏数据：{InstanceDir}"
        : $".minecraft\\versions\\{Name}";

    [JsonIgnore]
    public bool ShowIsolatedBadge => UseIsolatedDir;

    // === Mod 加载器 ===
    public string? LoaderType { get; set; }    // null/"Vanilla" | "Forge" | "Fabric" | "Quilt" | "NeoForge"
    public string? LoaderVersion { get; set; } // e.g. "0.16.10" (Fabric) / "50.1.0" (Forge)

    [JsonIgnore]
    public string LoaderBadgeText => LoaderType switch
    {
        "Forge" => "Forge",
        "Fabric" => "Fabric",
        "Quilt" => "Quilt",
        "NeoForge" => "NeoForge",
        "OptiFine" => "OptiFine",
        _ => "Vanilla"
    };

    [JsonIgnore]
    public bool ShowLoaderBadge =>
        !string.IsNullOrEmpty(LoaderType) && LoaderType != "Vanilla";

    // === 游戏时长统计 ===
    /// <summary>累计游戏时长（秒）。</summary>
    public long TotalPlayTimeSeconds { get; set; }
    /// <summary>最后一次正常退出的时间（UTC）。</summary>
    public DateTimeOffset? LastPlayedAt { get; set; }

    public override string ToString() => DisplayText;
}
