using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32.SafeHandles;

namespace EiTRVO.ProEngine.Services;

/// <summary>
/// 游戏进程安全加固：移除关机特权、Job Object 约束、子进程黑名单监控。
/// 接口在 Core 中定义，平台实现（P/Invoke + WMI）在 WPF 项目中。
/// </summary>
public interface IGameProcessSecurityService : IDisposable
{
    /// <summary>对已启动的游戏进程实施 Layer 1（移除 SeShutdownPrivilege）+ Layer 2（Job Object）加固</summary>
    void HardenProcess(Process process);

    /// <summary>
    /// 用 CREATE_SUSPENDED 创建进程 → Layer 0/1/2 加固 → ResumeThread → 返回进程句柄。
    /// 仅 Windows 实现支持；非 Windows 平台返回 null。
    /// </summary>
    HardenedProcessHandle? StartSuspendedAndHarden(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory);

    /// <summary>启动 Layer 3 子进程黑名单监控。回调在检测到黑名单进程时触发，commandLine 为捕获到的完整命令行</summary>
    void StartMonitoring(Process parentProcess, Action<string, int, string?> onThreatDetected);

    /// <summary>
    /// 启动 Layer 4 + Layer 5 高级监控（FileSystemWatcher + DLL 轮询 + TCP 轮询）。
    /// 仅 Windows 实现有效。
    /// </summary>
    void StartAdvancedMonitoring(
        Process gameProcess,
        string gameDir,
        string javaHome,
        Action<string, string> onLayer4Threat,
        Action<string, string> onLayer5Alert);

    /// <summary>停止 Layer 4 + Layer 5 高级监控并释放资源。</summary>
    void StopAdvancedMonitoring();

    /// <summary>停止监控并释放 WMI 资源</summary>
    void StopMonitoring();
}

/// <summary>
/// CREATE_SUSPENDED 创建的进程句柄包装。
/// Dispose 时关闭管道读取端和进程句柄（JobObject KILL_ON_JOB_CLOSE 由 WindowsGameProcessSecurityService 管理）。
/// </summary>
public class HardenedProcessHandle : IDisposable
{
    public Process Process { get; init; } = null!;
    public StreamReader StandardOutput { get; init; } = null!;
    public StreamReader StandardError { get; init; } = null!;

    public SafeFileHandle? StdOutReadHandle { get; set; }
    public SafeFileHandle? StdErrReadHandle { get; set; }

    public void Dispose()
    {
        StandardOutput?.Dispose();
        StandardError?.Dispose();
        StdOutReadHandle?.Dispose();
        StdErrReadHandle?.Dispose();
        Process?.Dispose();
    }
}
