namespace EiTRVO.ProEngine.Models;

public class LauncherSettings
{
    public int MemoryMB { get; set; } = 2048;
    public string? Resolution { get; set; }
    public string? JavaPath { get; set; }

    // === Java 运行环境 ===
    /// <summary>true=从自动检测列表中选择Java, false=手动浏览指定</summary>
    public bool UseAutoDetectJava { get; set; } = true;

    /// <summary>手动模式下用户浏览指定的 Java 可执行文件完整路径</summary>
    public string? ManualJavaPath { get; set; }

    // === 实例目录隔离 ===
    /// <summary>创建新实例时默认启用游戏目录隔离</summary>
    public bool IsolateNewInstancesByDefault { get; set; } = true;

    // === 设置锁 ===
    /// <summary>开启后进入设置/管理/账户页面需通过 Windows Hello 或 PIN 验证</summary>
    public bool SettingsLockEnabled { get; set; } = false;

    // === EiTRVO Firewall ===
    /// <summary>启动游戏时启用进程加固 + 子进程黑名单监控</summary>
    public bool FirewallEnabled { get; set; } = false;

    // === 备份 ===
    /// <summary>启用自动备份（启动器启动时检查）</summary>
    public bool BackupEnabled { get; set; } = false;

    /// <summary>自动备份间隔</summary>
    public BackupInterval BackupInterval { get; set; } = BackupInterval.Weekly;

    /// <summary>备份文件输出文件夹</summary>
    public string? BackupFolder { get; set; }

    /// <summary>排除 assets/ 和 libraries/（可从 Mojang 重新下载）</summary>
    public bool ExcludeRedownloadable { get; set; } = true;

    /// <summary>上次备份完成时间（UTC）。用于判断下次备份是否到期。</summary>
    public DateTimeOffset? LastBackupTime { get; set; }
}
