namespace EiTRVO.ProEngine.Models;

/// <summary>备份恢复模式。</summary>
public enum RestoreMode
{
    /// <summary>覆盖式：逐文件复制到 .minecraft，同名文件跳过不覆盖。</summary>
    Overlay,

    /// <summary>清理式：先删除整个 .minecraft，再从备份解压还原。</summary>
    Clean
}
