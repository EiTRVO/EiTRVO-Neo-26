namespace EiTRVO.ProEngine.Models;

/// <summary>自动备份间隔。</summary>
public enum BackupInterval
{
    EveryLaunch,   // 每次启动
    Daily,         // 每天
    Weekly,        // 每周
    Monthly        // 每月
}
