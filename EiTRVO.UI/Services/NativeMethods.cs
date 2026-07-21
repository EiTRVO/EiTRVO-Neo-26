using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace EiTRVO.UI.Services;

/// <summary>
/// Win32 P/Invoke 声明 —— 集中管理，仅用于 EiTRVO Firewall 的进程加固。
/// </summary>
internal static class NativeMethods
{
    // ==================== Kernel32 ====================

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern SafeFileHandle CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetInformationJobObject(
        SafeFileHandle hJob,
        JOBOBJECTINFOCLASS JobObjectInfoClass,
        IntPtr lpJobObjectInfo,
        uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool AssignProcessToJobObject(SafeFileHandle hJob, IntPtr hProcess);

    // ==================== Advapi32 ====================

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool OpenProcessToken(
        IntPtr ProcessHandle,
        uint DesiredAccess,
        out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool LookupPrivilegeValue(
        string? lpSystemName,
        string lpName,
        out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool AdjustTokenPrivileges(
        IntPtr TokenHandle,
        bool DisableAllPrivileges,
        ref TOKEN_PRIVILEGES NewState,
        uint BufferLength,
        IntPtr PreviousState,
        IntPtr ReturnLength);

    // ==================== Privileges to remove ====================

    /// <summary>游戏进程不需要持有的所有特权——启动时移除（纵深防御）。</summary>
    public static readonly string[] PrivilegesToRemove =
    {
        "SeShutdownPrivilege",              // 关机/重启
        "SeSystemtimePrivilege",            // 系统时钟回拨
        "SeTimeZonePrivilege",              // 时区变更
        "SeIncreaseWorkingSetPrivilege",    // 内存工作集挤出
        "SeProfileSingleProcessPrivilege",  // 性能计数器侧信道采样
        "SeCreateGlobalPrivilege",          // 全局命名空间对象劫持
        "SeUndockPrivilege",                // 强制从扩展坞移除
        "SeRemoteShutdownPrivilege",        // 远程关机
        "SeDebugPrivilege",                 // 进程注入/内存读取（纵深）
    };

    // ==================== Constants ====================

    public const uint PROCESS_QUERY_INFORMATION = 0x0400;

    public const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    public const uint TOKEN_QUERY = 0x0008;

    public const uint SE_PRIVILEGE_REMOVED = 0x00000004;

    public const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
    public const uint JOB_OBJECT_LIMIT_ACTIVE_PROCESS = 0x00000008;

    public const int JobObjectExtendedLimitInformation = 9;

    // ==================== Structs ====================

    [StructLayout(LayoutKind.Sequential)]
    public struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LUID_AND_ATTRIBUTES
    {
        public LUID Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID_AND_ATTRIBUTES Privileges;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    public enum JOBOBJECTINFOCLASS
    {
        JobObjectBasicLimitInformation = 2,
        JobObjectExtendedLimitInformation = 9,
    }
}
