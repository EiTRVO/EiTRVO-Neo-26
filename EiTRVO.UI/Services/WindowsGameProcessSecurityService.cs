using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using EiTRVO.ProEngine.Services;

namespace EiTRVO.UI.Services;

/// <summary>
/// 游戏进程安全加固的 Windows 实现。
/// Layer 1: AdjustTokenPrivileges 移除 SeShutdownPrivilege
/// Layer 2: Job Object (KILL_ON_JOB_CLOSE + 50 进程上限)
/// Layer 3: WMI Win32_ProcessStartTrace 子进程黑名单监控
/// </summary>
public class WindowsGameProcessSecurityService : IGameProcessSecurityService
{
    private static readonly HashSet<string> Blocklist = new(StringComparer.OrdinalIgnoreCase)
    {
        // 代码执行
        "cmd.exe", "powershell.exe", "pwsh.exe",
        "msbuild.exe", "cscript.exe", "wscript.exe",
        "mshta.exe", "rundll32.exe",
        // 注册表操作
        "reg.exe", "regedit.exe", "regsvr32.exe",
        // 网络与传输
        "curl.exe", "wget.exe", "certutil.exe",
        "bitsadmin.exe", "ftp.exe", "netsh.exe",
        // 系统配置
        "net.exe", "net1.exe", "sc.exe", "schtasks.exe",
        // 系统破坏
        "shutdown.exe", "bcdedit.exe", "diskpart.exe",
        // 痕迹清理
        "wevtutil.exe", "vssadmin.exe", "wmic.exe",
        // 权限操作
        "icacls.exe", "takeown.exe", "attrib.exe",
        // 任务管理
        "taskkill.exe",
    };

    private SafeFileHandle? _jobHandle;
    private ManagementEventWatcher? _watcher;
    private volatile int _parentPid;
    private volatile Action<string, int, string?>? _onThreat;

    // ==================== Layer 1 + 2 ====================

    public void HardenProcess(Process process)
    {
        IntPtr hProcess = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_QUERY_INFORMATION, false, process.Id);

        if (hProcess != IntPtr.Zero)
        {
            try
            {
                // ── Layer 1: 移除所有非必需特权 ──
                if (NativeMethods.OpenProcessToken(hProcess,
                    NativeMethods.TOKEN_ADJUST_PRIVILEGES | NativeMethods.TOKEN_QUERY,
                    out IntPtr hToken))
                {
                    try
                    {
                        foreach (var priv in NativeMethods.PrivilegesToRemove)
                        {
                            if (NativeMethods.LookupPrivilegeValue(
                                null, priv, out var luid))
                            {
                                var tp = new NativeMethods.TOKEN_PRIVILEGES
                                {
                                    PrivilegeCount = 1,
                                    Privileges = new NativeMethods.LUID_AND_ATTRIBUTES
                                    {
                                        Luid = luid,
                                        Attributes = NativeMethods.SE_PRIVILEGE_REMOVED
                                    }
                                };
                                NativeMethods.AdjustTokenPrivileges(
                                    hToken, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
                            }
                        }
                    }
                    finally { NativeMethods.CloseHandle(hToken); }
                }

                // ── Layer 2: Job Object ──
                _jobHandle = NativeMethods.CreateJobObject(IntPtr.Zero, null);
                if (!_jobHandle.IsInvalid)
                {
                    var limits = new NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                    limits.BasicLimitInformation.LimitFlags =
                        NativeMethods.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE |
                        NativeMethods.JOB_OBJECT_LIMIT_ACTIVE_PROCESS;
                    limits.BasicLimitInformation.ActiveProcessLimit = 50;

                    int size = Marshal.SizeOf<NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
                    IntPtr ptr = Marshal.AllocHGlobal(size);
                    try
                    {
                        Marshal.StructureToPtr(limits, ptr, false);
                        NativeMethods.SetInformationJobObject(
                            _jobHandle,
                            NativeMethods.JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation,
                            ptr, (uint)size);
                    }
                    finally { Marshal.FreeHGlobal(ptr); }

                    NativeMethods.AssignProcessToJobObject(_jobHandle, hProcess);
                }
            }
            finally { NativeMethods.CloseHandle(hProcess); }
        }
    }

    // ==================== Layer 3 ====================

    public void StartMonitoring(Process parentProcess, Action<string, int, string?> onThreatDetected)
    {
        _parentPid = parentProcess.Id;
        _onThreat = onThreatDetected;

        try
        {
            var query = new WqlEventQuery(
                "__InstanceCreationEvent",
                TimeSpan.FromSeconds(1),
                "TargetInstance ISA 'Win32_Process'");

            _watcher = new ManagementEventWatcher(query);
            _watcher.EventArrived += OnProcessStarted;
            _watcher.Start();
        }
        catch
        {
            // WMI 不可用时静默失败 —— Layer 1 + 2 仍然生效
        }
    }

    private void OnProcessStarted(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var target = (ManagementBaseObject)e.NewEvent["TargetInstance"];
            string name = (string)target["Name"];
            uint parentPid = (uint)target["ParentProcessId"];
            uint pid = (uint)target["ProcessId"];

            if (parentPid == _parentPid && Blocklist.Contains(name))
            {
                // 抓取命令行（事后 WMI 查询，同用户进程无需管理员权限）
                string? commandLine = null;
                try
                {
                    using var searcher = new ManagementObjectSearcher(
                        $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
                    using var results = searcher.Get();
                    foreach (var obj in results)
                    {
                        commandLine = obj["CommandLine"]?.ToString();
                        break;
                    }
                }
                catch { /* WMI 查询失败不影响熔断 */ }

                // 立即杀子进程
                try { Process.GetProcessById((int)pid)?.Kill(); } catch { }

                _onThreat?.Invoke(name, (int)pid, commandLine);
            }
        }
        catch { }
    }

    public void StopMonitoring()
    {
        try
        {
            _watcher?.Stop();
            _watcher?.Dispose();
        }
        catch { }
        _watcher = null;
    }

    // ==================== Dispose ====================

    public void Dispose()
    {
        StopMonitoring();
        _jobHandle?.Dispose();
        _jobHandle = null;
    }
}
