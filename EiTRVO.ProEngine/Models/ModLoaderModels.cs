using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace EiTRVO.ProEngine.Models;

// ============================================================
// 统一 Mod 加载器版本模型（UI 绑定）
// ============================================================

/// <summary>
/// 统一的 Mod 加载器版本表示，用于 UI 下拉框绑定。
/// </summary>
public class ModLoaderVersion
{
    public string LoaderType { get; set; } = "";
    public string LoaderVersion { get; set; } = "";
    public string MinecraftVersion { get; set; } = "";
    public bool IsRecommended { get; set; }
    public bool IsLatest { get; set; }

    public string DisplayText
    {
        get
        {
            if (IsRecommended && IsLatest) return $"{LoaderVersion} (最新, 推荐)";
            if (IsRecommended) return $"{LoaderVersion} (推荐)";
            if (IsLatest) return $"{LoaderVersion} (最新)";
            return LoaderVersion;
        }
    }

    public override string ToString() => DisplayText;
}

// ============================================================
// Fabric Meta API 模型
// ============================================================

public class FabricLoaderEntry
{
    [JsonPropertyName("loader")] public FabricLoaderInfo? Loader { get; set; }
    [JsonPropertyName("intermediary")] public FabricMappingsInfo? Intermediary { get; set; }
}

public class FabricLoaderInfo
{
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("stable")] public bool Stable { get; set; }
}

public class FabricMappingsInfo
{
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("stable")] public bool Stable { get; set; }
}

// Fabric / Quilt profile JSON 返回的 libraries 结构与原版兼容，
// 直接复用 VersionDetail + Library 模型。

// ============================================================
// Forge Promotions API 模型
// ============================================================

/// <summary>
/// Forge promotions_slim.json 的根对象。
/// promos 字典的 key 格式为 "mcVersion-标签"（如 "1.12.2-latest"），
/// value 为 Forge 版本号（如 "14.23.5.2860"）。
/// </summary>
public class ForgePromotionsResponse
{
    [JsonPropertyName("promos")] public Dictionary<string, string>? Promos { get; set; }
    [JsonPropertyName("homepage")] public string? Homepage { get; set; }
}

public class ForgeVersionEntry
{
    public string Version { get; set; } = "";
    public string McVersion { get; set; } = "";
    public bool IsLatest { get; set; }
    public bool IsRecommended { get; set; }
}

// ============================================================
// Quilt Meta API 模型（与 Fabric 结构兼容）
// ============================================================

public class QuiltLoaderEntry
{
    [JsonPropertyName("loader")] public QuiltLoaderInfo? Loader { get; set; }
    [JsonPropertyName("hashed")] public QuiltMappingsInfo? Hashed { get; set; }
}

public class QuiltLoaderInfo
{
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("stable")] public bool Stable { get; set; }
}

public class QuiltMappingsInfo
{
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("stable")] public bool Stable { get; set; }
}

// ============================================================
// NeoForge 版本列表模型
// ============================================================

public class NeoForgeVersionEntry
{
    public string Version { get; set; } = "";
    public string McVersion { get; set; } = "";
    public bool IsLatest { get; set; }
    public bool IsRecommended { get; set; }
}
