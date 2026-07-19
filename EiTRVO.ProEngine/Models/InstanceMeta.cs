namespace EiTRVO.ProEngine.Models;

/// <summary>
/// 存储在 versions/{name}/instance.json 中的实例元数据。
/// 用于在重新扫描时恢复实例的隔离状态和 Mod 加载器配置。
/// </summary>
public class InstanceMeta
{
    public bool UseIsolatedDir { get; set; }
    public string? InstanceDir { get; set; }
    public string? LoaderType { get; set; }
    public string? LoaderVersion { get; set; }

    // === 游戏时长统计 ===
    /// <summary>累计游戏时长（秒）。null 兼容旧数据。</summary>
    public long? TotalPlayTimeSeconds { get; set; }
    /// <summary>最后一次正常退出的时间（UTC）。</summary>
    public DateTimeOffset? LastPlayedAt { get; set; }
}
