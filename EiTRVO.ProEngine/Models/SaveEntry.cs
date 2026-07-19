using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EiTRVO.ProEngine.Models;

public partial class SaveEntry : ObservableObject
{
    /// <summary>存档文件夹名（世界名称）</summary>
    public string Name { get; set; } = "";

    /// <summary>存档文件夹的完整绝对路径</summary>
    public string FullPath { get; set; } = "";

    /// <summary>人类可读的位置</summary>
    public string LocationDisplay { get; set; } = "";

    /// <summary>文件夹最后修改时间</summary>
    public DateTime LastModified { get; set; }

    /// <summary>格式化的大小显示</summary>
    public string SizeDisplay { get; set; } = "";

    /// <summary>位置 · 大小 · 时间</summary>
    public string InfoDisplay { get; set; } = "";

    /// <summary>名称 · 大小 · 时间 标题行</summary>
    public string TitleDisplay { get; set; } = "";

    // ==================== 存档锁相关属性 ====================

    /// <summary>是否为已加密存档（true = .savenc 文件，false = 普通文件夹）</summary>
    public bool IsLocked { get; set; }

    /// <summary>加密模式显示文本："一次性" / "永久"（未加密时为 null）</summary>
    public string? LockModeDisplay { get; set; }

    /// <summary>锁定时间显示文本（未加密时为 null）</summary>
    public string? LockedAtDisplay { get; set; }

    /// <summary>绑定的微软账户邮箱（用于卡片显示，未加密时为 null）</summary>
    public string? BoundMsAccount { get; set; }

    /// <summary>密码提示（明文取自 .savenc Metadata，未加密时为 null）</summary>
    public string? PasswordHint { get; set; }

    /// <summary>.savenc 文件的完整路径（未加密时为 null）</summary>
    public string? SavencPath { get; set; }

    /// <summary>AES 密钥是否已备份到 OneDrive</summary>
    public bool OneDriveBackedUp { get; set; }

    /// <summary>存档类型："folder"（普通文件夹）或 "savenc"（加密文件）</summary>
    public string SaveType { get; set; } = "folder";

    /// <summary>是否显示「未备份」警告标签（已锁定但未备份到 OneDrive）</summary>
    public bool ShowNotBackedUpBadge => IsLocked && !OneDriveBackedUp;
}
