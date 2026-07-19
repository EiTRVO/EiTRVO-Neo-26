using System.Text.Json.Serialization;

namespace EiTRVO.ProEngine.Models;

public class VersionEntry
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string Url { get; set; } = "";

    [JsonIgnore]
    public string TypeDisplay => Type switch
    {
        "release" => "正式版",
        "snapshot" => "快照版",
        "old_beta" => "旧测试版",
        "old_alpha" => "旧预览版",
        _ => Type
    };

    [JsonIgnore]
    public string TypeLabel => Type switch
    {
        "release" => "Release",
        "snapshot" => "Snapshot",
        "old_beta" => "Beta",
        "old_alpha" => "Alpha",
        _ => Type
    };
}
