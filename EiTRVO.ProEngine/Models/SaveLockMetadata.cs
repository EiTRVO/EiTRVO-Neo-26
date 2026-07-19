using System;

namespace EiTRVO.ProEngine.Models;

/// <summary>
/// .savenc 文件 Header + Metadata JSON 的 POCO。
/// 由 SaveLockService.GetSaveLockMetadata() 填充。
/// </summary>
public class SaveLockMetadata
{
    /// <summary>.savenc 格式版本（当前为 0x01）</summary>
    public byte Version { get; init; }

    /// <summary>加密模式：一次性 / 永久</summary>
    public SaveLockMode LockMode { get; init; }

    /// <summary>PBKDF2 Salt（16 字节）</summary>
    public byte[] Salt { get; init; } = [];

    /// <summary>密码验证值（16 字节）= AES-256-CBC(K, IV=salt, "SAVELOCK_OK")。仅 KeyCheck 复用 salt 作 IV；文件数据加密使用独立随机 ivBase，与 salt 无关。</summary>
    public byte[] KeyCheck { get; init; } = [];

    /// <summary>绑定微软账户 UUID 的 SHA-256 哈希（32 字节），全 0 表示未绑定</summary>
    public string? BoundMsUuidHash { get; init; }

    /// <summary>存档名称（来自 Metadata JSON）</summary>
    public string SaveName { get; init; } = "";

    /// <summary>所属实例名称（来自 Metadata JSON）</summary>
    public string InstanceName { get; init; } = "";

    /// <summary>加密创建时间（ISO 8601，来自 Metadata JSON）</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>加密前原始存档文件夹大小（字节）</summary>
    public long OriginalSize { get; init; }

    /// <summary>加密时存档文件夹内的文件数量</summary>
    public int FileCount { get; init; }

    /// <summary>密码提示（明文存储，可为空）</summary>
    public string? PasswordHint { get; init; }

    /// <summary>AES 密钥是否已备份到 OneDrive</summary>
    public bool OneDriveBackedUp { get; init; }
}

/// <summary>加密模式枚举</summary>
public enum SaveLockMode
{
    /// <summary>一次性 — 解密后保持明文，不自动重加密</summary>
    OneTime = 0,

    /// <summary>永久 — 游戏退出后自动重新加密</summary>
    Permanent = 1
}
