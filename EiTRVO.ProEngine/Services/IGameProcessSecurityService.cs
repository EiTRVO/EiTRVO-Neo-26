using System;
using System.Diagnostics;

namespace EiTRVO.ProEngine.Services;

/// <summary>
/// 游戏进程安全加固：移除关机特权、Job Object 约束、子进程黑名单监控。
/// 接口在 Core 中定义，平台实现（P/Invoke + WMI）在 WPF 项目中。
/// </summary>
public interface IGameProcessSecurityService : IDisposable
{
    /// <summary>对已启动的游戏进程实施 Layer 1（移除 SeShutdownPrivilege）+ Layer 2（Job Object）加固</summary>
    void HardenProcess(Process process);

    /// <summary>启动 Layer 3 子进程黑名单监控。回调在检测到黑名单进程时触发，commandLine 为捕获到的完整命令行</summary>
    void StartMonitoring(Process parentProcess, Action<string, int, string?> onThreatDetected);

    /// <summary>停止监控并释放 WMI 资源</summary>
    void StopMonitoring();
}
