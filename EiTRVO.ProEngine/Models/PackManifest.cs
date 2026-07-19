using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace EiTRVO.ProEngine.Models;

public class PackManifest
{
    [JsonPropertyName("format")]
    public string Format { get; set; } = AppInfo.PackFormat;

    [JsonPropertyName("packVersion")]
    public int PackVersion { get; set; } = AppInfo.PackVersion;

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("author")]
    public string Author { get; set; } = "";

    [JsonPropertyName("exportedAt")]
    public string ExportedAt { get; set; } = "";

    [JsonPropertyName("exporterVersion")]
    public int ExporterVersion { get; set; }

    [JsonPropertyName("instance")]
    public PackInstanceInfo Instance { get; set; } = new();

    [JsonPropertyName("minecraft")]
    public PackMinecraftInfo Minecraft { get; set; } = new();

    [JsonPropertyName("inheritsFrom")]
    public string? InheritsFrom { get; set; }

    [JsonPropertyName("mods")]
    public List<PackModEntry> Mods { get; set; } = new();

    [JsonPropertyName("hasSaves")]
    public bool HasSaves { get; set; }
}

public class PackInstanceInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("useIsolatedDir")]
    public bool UseIsolatedDir { get; set; }
}

public class PackMinecraftInfo
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("modLoader")]
    public string? ModLoader { get; set; }

    [JsonPropertyName("modLoaderVersion")]
    public string? ModLoaderVersion { get; set; }
}

public class PackModEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = "";
}
