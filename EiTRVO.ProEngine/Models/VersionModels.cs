using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EiTRVO.ProEngine.Models;

public class VersionManifest
{
    [JsonPropertyName("latest")] public LatestVersions? Latest { get; set; }
    [JsonPropertyName("versions")] public List<ManifestVersion>? Versions { get; set; }
}

public class LatestVersions
{
    [JsonPropertyName("release")] public string? Release { get; set; }
    [JsonPropertyName("snapshot")] public string? Snapshot { get; set; }
}

public class ManifestVersion
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
}

public class VersionDetail
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("mainClass")] public string? MainClass { get; set; }
    [JsonPropertyName("assets")] public string? Assets { get; set; }
    [JsonPropertyName("assetIndex")] public AssetIndexInfo? AssetIndex { get; set; }
    [JsonPropertyName("libraries")] public List<Library>? Libraries { get; set; }
    [JsonPropertyName("arguments")] public Arguments? Arguments { get; set; }
    [JsonPropertyName("minecraftArguments")] public string? MinecraftArguments { get; set; }
    [JsonPropertyName("downloads")] public Downloads? Downloads { get; set; }
    [JsonPropertyName("logging")] public Logging? Logging { get; set; }
    [JsonPropertyName("inheritsFrom")] public string? InheritsFrom { get; set; }
}

public class AssetIndexInfo
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("sha1")] public string? Sha1 { get; set; }
}

public class Downloads
{
    [JsonPropertyName("client")] public DownloadInfo? Client { get; set; }
}

public class DownloadInfo
{
    [JsonPropertyName("sha1")] public string? Sha1 { get; set; }
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("path")] public string? Path { get; set; }
}

public class Arguments
{
    [JsonPropertyName("game")] public List<JsonElement>? Game { get; set; }
    [JsonPropertyName("jvm")] public List<JsonElement>? Jvm { get; set; }
}

public class Library
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("downloads")] public LibraryDownloads? Downloads { get; set; }
    [JsonPropertyName("natives")] public Dictionary<string, string>? Natives { get; set; }
    [JsonPropertyName("rules")] public List<Rule>? Rules { get; set; }

    /// <summary>
    /// Legacy Forge format (≤1.12.2): Maven repository base URL on the library object itself,
    /// not inside <c>downloads/artifact</c>. Used when <see cref="Downloads"/> is null.
    /// </summary>
    [JsonPropertyName("url")] public string? Url { get; set; }
}

public class LibraryDownloads
{
    [JsonPropertyName("artifact")] public DownloadInfo? Artifact { get; set; }
    [JsonPropertyName("classifiers")] public Dictionary<string, DownloadInfo>? Classifiers { get; set; }
}

public class Rule
{
    [JsonPropertyName("action")] public string? Action { get; set; }
    [JsonPropertyName("os")] public OsRule? Os { get; set; }
}

public class OsRule
{
    [JsonPropertyName("name")] public string? Name { get; set; }
}

public class AssetIndex
{
    [JsonPropertyName("objects")] public Dictionary<string, AssetObject>? Objects { get; set; }
}

public class AssetObject
{
    [JsonPropertyName("hash")] public string? Hash { get; set; }
    [JsonPropertyName("size")] public long Size { get; set; }
}

public class Logging
{
    [JsonPropertyName("client")] public LoggingClient? Client { get; set; }
}

public class LoggingClient
{
    [JsonPropertyName("argument")] public string? Argument { get; set; }
    [JsonPropertyName("file")] public DownloadInfo? File { get; set; }
}
