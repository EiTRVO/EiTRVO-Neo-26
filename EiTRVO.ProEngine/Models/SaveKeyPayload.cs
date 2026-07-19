namespace EiTRVO.ProEngine.Models;

/// <summary>
/// 存储在 OneDrive App Folder 中的密钥 JSON 结构。
/// 由 GraphService.UploadSaveKeyAsync / DownloadSaveKeyAsync 序列化/反序列化。
/// </summary>
public class SaveKeyPayload
{
    /// <summary>AES-256 密钥的 Base64 编码</summary>
    public string AesKey { get; set; } = "";

    /// <summary>所属实例名称</summary>
    public string InstanceName { get; set; } = "";

    /// <summary>存档名称</summary>
    public string SaveName { get; set; } = "";

    /// <summary>密钥创建时间（ISO 8601）</summary>
    public string CreatedAt { get; set; } = "";

    /// <summary>格式版本号</summary>
    public int Version { get; set; } = 1;
}
