using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace EiTRVO.ProEngine.Models;

/// <summary>
/// Modrinth MRPack 格式的整合包清单（modrinth.index.json）。
/// 参考：https://docs.modrinth.com/docs/modpacks/format_definition/
/// </summary>
public class ModpackManifest
{
    /// <summary>格式版本，当前仅支持 1</summary>
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; set; }

    /// <summary>游戏类型，必须为 "minecraft"</summary>
    [JsonPropertyName("game")]
    public string Game { get; set; } = "";

    /// <summary>整合包发布的版本号标识</summary>
    [JsonPropertyName("versionId")]
    public string VersionId { get; set; } = "";

    /// <summary>整合包名称（用作实例名称）</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>简介说明</summary>
    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    /// <summary>Mod 文件列表</summary>
    [JsonPropertyName("files")]
    public List<ModpackFileEntry> Files { get; set; } = new();

    /// <summary>
    /// 依赖项映射。
    /// Key: "minecraft", "fabric-loader", "forge", "quilt-loader", "neoforge"
    /// Value: 版本约束，如 "1.20.1", ">=0.16.0"
    /// </summary>
    [JsonPropertyName("dependencies")]
    public Dictionary<string, string> Dependencies { get; set; } = new();
}

/// <summary>MRPack 中单个 Mod 文件的描述</summary>
public class ModpackFileEntry
{
    /// <summary>实例目录内的相对路径，如 "mods/fabric-api.jar"</summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    /// <summary>文件哈希值（SHA1 / SHA512）</summary>
    [JsonPropertyName("hashes")]
    public ModpackHashes Hashes { get; set; } = new();

    /// <summary>下载 URL 列表（按优先级排列）</summary>
    [JsonPropertyName("downloads")]
    public List<string> Downloads { get; set; } = new();

    /// <summary>文件大小（字节）</summary>
    [JsonPropertyName("fileSize")]
    public long FileSize { get; set; }

    /// <summary>客户端/服务端环境限制</summary>
    [JsonPropertyName("env")]
    public ModpackEnv? Env { get; set; }
}

/// <summary>文件哈希</summary>
public class ModpackHashes
{
    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; }

    [JsonPropertyName("sha512")]
    public string? Sha512 { get; set; }
}

/// <summary>客户端/服务端环境支持标记</summary>
public class ModpackEnv
{
    /// <summary>客户端支持："required", "optional", "unsupported"</summary>
    [JsonPropertyName("client")]
    public string? Client { get; set; }

    /// <summary>服务端支持："required", "optional", "unsupported"</summary>
    [JsonPropertyName("server")]
    public string? Server { get; set; }
}
