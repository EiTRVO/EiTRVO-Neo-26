namespace EiTRVO.ProEngine.Models;

/// <summary>
/// 加密存档时的用户选择参数。
/// 由 SaveLockDialog 收集，传递给 SaveLockService.LockSaveAsync()。
/// </summary>
public class SaveLockOptions
{
    /// <summary>用户设置的密码（明文）。此为瞬时 DTO，调用方应在使用完毕后尽快丢弃引用。</summary>
    public string Password { get; set; } = "";

    /// <summary>加密模式：一次性 / 永久</summary>
    public SaveLockMode LockMode { get; set; } = SaveLockMode.OneTime;

    /// <summary>是否导出 .savkey 本地密钥备份文件（DPAPI 加密）</summary>
    public bool SaveLocalBackup { get; set; }

    /// <summary>.savkey 导出路径（用户选择）</summary>
    public string? LocalBackupPath { get; set; }

    /// <summary>是否导出 .savrec 恢复文件（需要微软账户）</summary>
    public bool ExportRecoveryFile { get; set; }

    /// <summary>密码提示（可选，明文存储在 .savenc Metadata 中）</summary>
    public string? PasswordHint { get; set; }

    /// <summary>.savrec 导出路径（用户选择）</summary>
    public string? RecoveryFilePath { get; set; }

    /// <summary>绑定的微软账户邮箱（用于 OneDrive 备份和 .savenc 显示）</summary>
    public string? BoundMsAccount { get; set; }

    /// <summary>绑定的微软账户 UUID（用于计算 .savenc 头部 BoundMsUuidHash，多账户恢复时反查）</summary>
    public string? BoundMsUuid { get; set; }
}
