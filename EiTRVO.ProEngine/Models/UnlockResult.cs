using System.Collections.Generic;

namespace EiTRVO.ProEngine.Models;

/// <summary>
/// UnlockPanel 返回的结果。由 LaunchOrchestrator 消费，纯数据，不依赖 WPF。
/// </summary>
public class UnlockResult
{
    /// <summary>用户是否成功解锁（点击了「解锁」按钮且密码正确）</summary>
    public bool Success { get; set; }

    /// <summary>
    /// 解密后的密钥字典。Key = 存档名称, Value = (AES 密钥 K, 加密模式)。
    /// LaunchOrchestrator 用 K 解密并管理其生命周期。
    /// </summary>
    public Dictionary<string, (byte[] Key, SaveLockMode Mode)> DecryptedSaves { get; set; } = new();
}
