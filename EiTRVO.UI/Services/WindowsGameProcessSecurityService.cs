using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using EiTRVO.ProEngine.Orchestrators;
using EiTRVO.ProEngine.Services;

namespace EiTRVO.UI.Services;

/// <summary>
/// 游戏进程安全加固的 Windows 实现。
/// Layer 0: 扩展点禁用策略（EXTENSION_POINT_DISABLE_ALWAYS_ON）
/// Layer 1+2 合并为统一 Job Object:
///   - KILL_ON_JOB_CLOSE + 进程数上限 50
///   - UI 限制（剪贴板/句柄/桌面/退出 Windows）
///   - JOB_OBJECT_SECURITY_NO_ADMIN
///   - 子进程自动继承全部限制
/// Layer 3: IOCP 子进程黑名单监控（内核同步推送，微秒级响应）
/// Layer 4/5a/5b: FileSystemWatcher + DLL 轮询 + TCP 轮询（高级防御）
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

    // ── 反改名：黑名单文件 System32/SysWOW64 路径 → 支持 32/64 位双副本 ──
    private static readonly Dictionary<string, string[]> BlocklistPathMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cmd.exe"] = new[] { @"C:\Windows\System32\cmd.exe", @"C:\Windows\SysWOW64\cmd.exe" },
        ["powershell.exe"] = new[] { @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", @"C:\Windows\SysWOW64\WindowsPowerShell\v1.0\powershell.exe" },
        ["pwsh.exe"] = new[] { @"C:\Program Files\PowerShell\7\pwsh.exe" },
        ["msbuild.exe"] = new[] { @"C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe", @"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe" },
        ["cscript.exe"] = new[] { @"C:\Windows\System32\cscript.exe", @"C:\Windows\SysWOW64\cscript.exe" },
        ["wscript.exe"] = new[] { @"C:\Windows\System32\wscript.exe", @"C:\Windows\SysWOW64\wscript.exe" },
        ["mshta.exe"] = new[] { @"C:\Windows\System32\mshta.exe", @"C:\Windows\SysWOW64\mshta.exe" },
        ["rundll32.exe"] = new[] { @"C:\Windows\System32\rundll32.exe", @"C:\Windows\SysWOW64\rundll32.exe" },
    };
    // 未在上表中出现的 blocklist 条目默认走 System32/ 查找
    private static readonly string SystemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);

    // ── 反改名: SHA-256 哈希缓存 ──
    // 文件名（小写） → 文件 SHA-256 hex
    private readonly ConcurrentDictionary<string, string> _hashCache = new(StringComparer.OrdinalIgnoreCase);
    // 持久化缓存文件路径
    private static string CacheFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EiTRVO", "blacklist_hash_cache.dat");

    private SafeFileHandle? _jobHandle;

    // === IOCP 子进程拦截（替代原 WMI + Toolhelp32 双通道） ===
    private IntPtr _iocpHandle = IntPtr.Zero;
    private Thread? _iocpThread;
    private CancellationTokenSource? _iocpCts;
    private volatile Action<string, int, string?>? _onThreat;

    // === Layer 4: FileSystemWatcher ===
    private FileSystemWatcher? _gameDirWatcher;
    private FileSystemWatcher? _tempDirWatcher;
    private FileSystemWatcher? _startupDirWatcher;
    private Action<string, string>? _onLayer4Threat;
    private Action<string, string>? _onLayer5Alert;

    // === Layer 5a/5b: polling ===
    private System.Threading.Timer? _dllPollTimer;
    private System.Threading.Timer? _tcpPollTimer;
    private string? _gameJavaHome;
    private int _gameProcessId;
    private readonly HashSet<string> _seenAlerts = new(StringComparer.OrdinalIgnoreCase);

    private readonly INotificationService? _notification;

    public WindowsGameProcessSecurityService(INotificationService notificationService)
    {
        _notification = notificationService;
    }

    // ==================== Job Object 统一加固 + IOCP ====================

    /// <summary>创建并配置 Job Object（包含 UI/安全限制），返回 false 表示 Job 不可用。</summary>
    private bool CreateJobWithLimits(out SafeFileHandle jobHandle, IntPtr iocpHandle)
    {
        jobHandle = NativeMethods.CreateJobObject(IntPtr.Zero, null);
        if (jobHandle.IsInvalid)
        {
            _notification?.AppendLog(
                "EiTRVO Firewall: CreateJobObject 失败",
                EiTRVO.ProEngine.Models.NotificationType.Warning);
            jobHandle = null!;
            return false;
        }

        try
        {
            // a. 扩展限制：KILL_ON_JOB_CLOSE + 进程数上限
            var limits = new NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            limits.BasicLimitInformation.LimitFlags =
                NativeMethods.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE |
                NativeMethods.JOB_OBJECT_LIMIT_ACTIVE_PROCESS;
            limits.BasicLimitInformation.ActiveProcessLimit = 50;

            int limSize = Marshal.SizeOf<NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
            IntPtr limPtr = Marshal.AllocHGlobal(limSize);
            try
            {
                Marshal.StructureToPtr(limits, limPtr, false);
                if (!NativeMethods.SetInformationJobObject(jobHandle,
                    NativeMethods.JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation,
                    limPtr, (uint)limSize))
                {
                    _notification?.AppendLog(
                        $"EiTRVO Firewall: SetInformationJobObject(ExtendedLimit) 失败 (错误码 {Marshal.GetLastWin32Error()})",
                        EiTRVO.ProEngine.Models.NotificationType.Warning);
                }
            }
            finally { Marshal.FreeHGlobal(limPtr); }

            // b. UI 限制：剪贴板 / 句柄 / 桌面 / 显示 / 系统参数 / 退出 Windows
            var uiLimits = new NativeMethods.JOBOBJECT_BASIC_UI_RESTRICTIONS
            {
                UIRestrictionsClass =
                    NativeMethods.JOB_OBJECT_UILIMIT_READCLIPBOARD |
                    NativeMethods.JOB_OBJECT_UILIMIT_WRITECLIPBOARD |
                    NativeMethods.JOB_OBJECT_UILIMIT_HANDLES |
                    NativeMethods.JOB_OBJECT_UILIMIT_DISPLAYSETTINGS |
                    NativeMethods.JOB_OBJECT_UILIMIT_SYSTEMPARAMETERS |
                    NativeMethods.JOB_OBJECT_UILIMIT_DESKTOP |
                    NativeMethods.JOB_OBJECT_UILIMIT_EXITWINDOWS
            };
            int uiSize = Marshal.SizeOf<NativeMethods.JOBOBJECT_BASIC_UI_RESTRICTIONS>();
            IntPtr uiPtr = Marshal.AllocHGlobal(uiSize);
            try
            {
                Marshal.StructureToPtr(uiLimits, uiPtr, false);
                if (!NativeMethods.SetInformationJobObject(jobHandle,
                    NativeMethods.JOBOBJECTINFOCLASS.JobObjectBasicUIRestrictions,
                    uiPtr, (uint)uiSize))
                {
                    _notification?.AppendLog(
                        $"EiTRVO Firewall: SetInformationJobObject(UIRestrictions) 失败 (错误码 {Marshal.GetLastWin32Error()})",
                        EiTRVO.ProEngine.Models.NotificationType.Warning);
                }
            }
            finally { Marshal.FreeHGlobal(uiPtr); }

            // c. 安全限制：禁止管理员令牌（Windows 8+ 部分 SKU 不支持，静默降级）
            try
            {
                var secLimits = new NativeMethods.JOBOBJECT_SECURITY_LIMIT_INFORMATION
                {
                    SecurityLimitFlags = NativeMethods.JOB_OBJECT_SECURITY_NO_ADMIN
                };
                int secSize = Marshal.SizeOf<NativeMethods.JOBOBJECT_SECURITY_LIMIT_INFORMATION>();
                IntPtr secPtr = Marshal.AllocHGlobal(secSize);
                try
                {
                    Marshal.StructureToPtr(secLimits, secPtr, false);
                    NativeMethods.SetInformationJobObject(jobHandle,
                        NativeMethods.JOBOBJECTINFOCLASS.JobObjectSecurityLimitInformation,
                        secPtr, (uint)secSize);
                }
                finally { Marshal.FreeHGlobal(secPtr); }
            }
            catch { /* 不支持的 SKU，静默跳过 */ }

            // d. 关联 IOCP 端口
            var iocpAssoc = new NativeMethods.JOBOBJECT_ASSOCIATE_COMPLETION_PORT
            {
                CompletionKey = IntPtr.Zero,
                CompletionPort = iocpHandle
            };
            int assocSize = Marshal.SizeOf<NativeMethods.JOBOBJECT_ASSOCIATE_COMPLETION_PORT>();
            IntPtr assocPtr = Marshal.AllocHGlobal(assocSize);
            try
            {
                Marshal.StructureToPtr(iocpAssoc, assocPtr, false);
                if (!NativeMethods.SetInformationJobObject(jobHandle,
                    NativeMethods.JOBOBJECTINFOCLASS.JobObjectAssociateCompletionPortInformation,
                    assocPtr, (uint)assocSize))
                {
                    _notification?.AppendLog(
                        $"EiTRVO Firewall: SetInformationJobObject(AssociateCompletionPort) 失败 (错误码 {Marshal.GetLastWin32Error()})",
                        EiTRVO.ProEngine.Models.NotificationType.Warning);
                }
            }
            finally { Marshal.FreeHGlobal(assocPtr); }

            return true;
        }
        catch
        {
            jobHandle.Dispose();
            jobHandle = null!;
            throw;
        }
    }

    // ==================== CREATE_SUSPENDED: 统一管道创建 + 可选加固 ====================

    /// <summary>
    /// 用 CREATE_SUSPENDED 创建进程 → 管道显式 isAsync:false → 可选加固 → ResumeThread。
    /// harden=false 时跳过 Layer 0/1/2，但仍使用显式管道控制以保证 isAsync: false。
    /// </summary>
    public HardenedProcessHandle StartSuspendedAndHarden(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        bool harden = true)
    {
        // ── 1. 创建匿名管道（stdout / stderr），所有路径统一 ──
        var sa = new NativeMethods.SECURITY_ATTRIBUTES();
        sa.nLength = Marshal.SizeOf<NativeMethods.SECURITY_ATTRIBUTES>();
        sa.lpSecurityDescriptor = IntPtr.Zero;
        sa.bInheritHandle = true;

        SafeFileHandle stdoutWrite = null!;
        SafeFileHandle stderrWrite = null!;
        SafeFileHandle stdoutRead, stderrRead;
        try
        {
            if (!NativeMethods.CreatePipe(out SafeFileHandle stdoutReadInherit, out stdoutWrite, ref sa, 0))
                throw new InvalidOperationException($"CreatePipe (stdout) 失败: {Marshal.GetLastWin32Error()}");

            if (!NativeMethods.CreatePipe(out SafeFileHandle stderrReadInherit, out stderrWrite, ref sa, 0))
                throw new InvalidOperationException($"CreatePipe (stderr) 失败: {Marshal.GetLastWin32Error()}");

            IntPtr curProc = (IntPtr)(-1);
            if (!NativeMethods.DuplicateHandle(curProc, stdoutReadInherit, curProc,
                    out stdoutRead, 0, false, NativeMethods.DUPLICATE_SAME_ACCESS))
                throw new InvalidOperationException($"DuplicateHandle (stdout) 失败: {Marshal.GetLastWin32Error()}");
            stdoutReadInherit.Dispose();

            if (!NativeMethods.DuplicateHandle(curProc, stderrReadInherit, curProc,
                    out stderrRead, 0, false, NativeMethods.DUPLICATE_SAME_ACCESS))
                throw new InvalidOperationException($"DuplicateHandle (stderr) 失败: {Marshal.GetLastWin32Error()}");
            stderrReadInherit.Dispose();
        }
        catch
        {
            stdoutWrite?.Dispose();
            stderrWrite?.Dispose();
            throw;
        }

        IntPtr hProcess = IntPtr.Zero;
        IntPtr hThread = IntPtr.Zero;
        int pid = 0;
        Process? process = null;
        SafeFileHandle? newJobHandle = null;
        IntPtr iocpHandle = IntPtr.Zero;

        try
        {
            // ── 2. Layer 0: STARTUPINFOEX + 可选扩展点禁用 ──
            var siEx = new NativeMethods.STARTUPINFOEX();
            siEx.StartupInfo.cb = Marshal.SizeOf<NativeMethods.STARTUPINFOEX>();
            siEx.StartupInfo.hStdOutput = stdoutWrite.DangerousGetHandle();
            siEx.StartupInfo.hStdError = stderrWrite.DangerousGetHandle();
            siEx.StartupInfo.hStdInput = IntPtr.Zero;
            siEx.StartupInfo.dwFlags = NativeMethods.STARTF_USESTDHANDLES;

            if (harden)
            {
                int attrSize = 0;
                NativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attrSize);
                if (attrSize > 0)
                {
                    siEx.lpAttributeList = Marshal.AllocHGlobal(attrSize);
                    if (NativeMethods.InitializeProcThreadAttributeList(siEx.lpAttributeList, 1, 0, ref attrSize))
                    {
                        IntPtr mitigationValue = Marshal.AllocHGlobal(sizeof(long));
                        try
                        {
                            Marshal.WriteInt64(mitigationValue,
                                NativeMethods.PROCESS_CREATION_MITIGATION_POLICY_EXTENSION_POINT_DISABLE_ALWAYS_ON);
                            IntPtr sizeVal = (IntPtr)sizeof(long);
                            NativeMethods.UpdateProcThreadAttribute(
                                siEx.lpAttributeList, 0,
                                (IntPtr)NativeMethods.PROC_THREAD_ATTRIBUTE_MITIGATION_POLICY,
                                mitigationValue, sizeVal, IntPtr.Zero, IntPtr.Zero);
                        }
                        finally { Marshal.FreeHGlobal(mitigationValue); }
                    }
                }
            }

            // ── 3. 构建命令行 ──
            string cmdLine = BuildCommandLine(fileName, arguments);

            // ── 4. CreateProcess ──
            uint flags = NativeMethods.EXTENDED_STARTUPINFO_PRESENT
                       | NativeMethods.CREATE_SUSPENDED
                       | NativeMethods.CREATE_NO_WINDOW;

            var procInfo = new NativeMethods.PROCESS_INFORMATION();
            bool created = NativeMethods.CreateProcess(
                lpApplicationName: fileName,
                lpCommandLine: cmdLine,
                lpProcessAttributes: IntPtr.Zero,
                lpThreadAttributes: IntPtr.Zero,
                bInheritHandles: true,
                dwCreationFlags: flags,
                lpEnvironment: IntPtr.Zero,
                lpCurrentDirectory: workingDirectory,
                lpStartupInfo: ref siEx,
                lpProcessInformation: out procInfo);

            // 释放扩展属性列表
            if (siEx.lpAttributeList != IntPtr.Zero)
            {
                NativeMethods.DeleteProcThreadAttributeList(siEx.lpAttributeList);
                Marshal.FreeHGlobal(siEx.lpAttributeList);
                siEx.lpAttributeList = IntPtr.Zero;
            }

            // 关闭管道 write 端（子进程通过句柄继承持有副本）
            stdoutWrite.Dispose();
            stdoutWrite = null!;
            stderrWrite.Dispose();
            stderrWrite = null!;

            if (!created)
            {
                int err = Marshal.GetLastWin32Error();
                throw new InvalidOperationException(
                    $"CreateProcess 失败 (错误码 {err})。" +
                    $"\n文件: {fileName}\n参数: {cmdLine}");
            }

            hProcess = procInfo.hProcess;
            hThread = procInfo.hThread;
            pid = procInfo.dwProcessId;

            // ── 5. 可选加固: Job Object + IOCP ──
            if (harden)
            {
                iocpHandle = NativeMethods.CreateIoCompletionPort(
                    (IntPtr)(-1), IntPtr.Zero, IntPtr.Zero, 1);
                if (iocpHandle == IntPtr.Zero || iocpHandle == (IntPtr)(-1))
                    throw new InvalidOperationException($"CreateIoCompletionPort 失败 (句柄={iocpHandle}): {Marshal.GetLastWin32Error()}");

                if (!CreateJobWithLimits(out newJobHandle, iocpHandle))
                    throw new InvalidOperationException("Job Object 创建失败。");

                // 关闭旧 Job（如果存在）
                _jobHandle?.Dispose();
                _jobHandle = newJobHandle;

                if (!NativeMethods.AssignProcessToJobObject(_jobHandle, hProcess))
                {
                    int err = Marshal.GetLastWin32Error();
                    _notification?.AppendLog(
                        $"EiTRVO Firewall: AssignProcessToJobObject 失败 (错误码 {err})，子进程监控不可用！",
                        EiTRVO.ProEngine.Models.NotificationType.Error);
                }
                else
                {
                    // 硬验证：确认 javaw.exe 确实在 Job 中
                    if (NativeMethods.IsProcessInJob(hProcess, _jobHandle, out bool inJob))
                    {
                        _notification?.AppendLog(
                            inJob
                                ? "EiTRVO Firewall: IsProcessInJob 确认 javaw.exe 在 Job 中"
                                : "EiTRVO Firewall: IsProcessInJob 返回 false！javaw.exe 不在 Job 中！",
                            inJob
                                ? EiTRVO.ProEngine.Models.NotificationType.Info
                                : EiTRVO.ProEngine.Models.NotificationType.Error);
                    }
                }

                // 启动 IOCP 工作线程（在 ResumeThread 之前，消除竞态窗口）
                StartIocpThread(iocpHandle);
                _iocpHandle = iocpHandle;
                iocpHandle = IntPtr.Zero; // 所有权转移
            }

            // ── 6. ResumeThread —— 进程开始执行 ──
            uint suspendCount = NativeMethods.ResumeThread(hThread);
            if (suspendCount == uint.MaxValue)
                throw new InvalidOperationException($"ResumeThread 失败: {Marshal.GetLastWin32Error()}");

            // ── 7. 清理 hProcess / hThread，构建托管 Process ──
            NativeMethods.CloseHandle(hThread);
            hThread = IntPtr.Zero;
            NativeMethods.CloseHandle(hProcess);
            hProcess = IntPtr.Zero;

            process = Process.GetProcessById(pid);

            // ── 8. 构建 StreamReader（isAsync: false，匿名管道不支持 Overlapped I/O）──
            var stdoutStream = new FileStream(stdoutRead, FileAccess.Read, bufferSize: 4096, isAsync: false);
            var stderrStream = new FileStream(stderrRead, FileAccess.Read, bufferSize: 4096, isAsync: false);
            var stdoutReader = new StreamReader(stdoutStream, System.Text.Encoding.UTF8);
            var stderrReader = new StreamReader(stderrStream, System.Text.Encoding.UTF8);

            return new HardenedProcessHandle
            {
                Process = process,
                StandardOutput = stdoutReader,
                StandardError = stderrReader,
                StdOutReadHandle = stdoutRead,
                StdErrReadHandle = stderrRead
            };
        }
        catch
        {
            if (hThread != IntPtr.Zero)
                NativeMethods.CloseHandle(hThread);
            if (hProcess != IntPtr.Zero)
            {
                NativeMethods.TerminateProcess(hProcess, 1);
                NativeMethods.CloseHandle(hProcess);
            }
            else if (pid != 0)
            {
                try { using var runaway = Process.GetProcessById(pid); runaway.Kill(); } catch { }
            }
            process?.Dispose();
            stdoutRead?.Dispose();
            stderrRead?.Dispose();
            if (stdoutWrite != null) stdoutWrite.Dispose();
            if (stderrWrite != null) stderrWrite.Dispose();
            if (iocpHandle != IntPtr.Zero) NativeMethods.CloseHandle(iocpHandle);
            newJobHandle?.Dispose();
            throw;
        }
    }

    /// <summary>构建 CreateProcess 兼容的命令行字符串（参数安全转义）。</summary>
    private static string BuildCommandLine(string fileName, IReadOnlyList<string> arguments)
    {
        // Windows 命令行格式：可执行文件 + 空格 + 转义后的参数
        var sb = new System.Text.StringBuilder();
        sb.Append('"').Append(fileName).Append('"');
        foreach (string arg in arguments)
        {
            sb.Append(' ');
            EscapeArg(sb, arg);
        }
        return sb.ToString();
    }

    /// <summary>将单个参数转义为 Windows CommandLineToArgv 兼容格式。</summary>
    private static void EscapeArg(System.Text.StringBuilder sb, string arg)
    {
        if (string.IsNullOrEmpty(arg))
        {
            sb.Append("\"\"");
            return;
        }
        // 参数不含空格/制表符/双引号时无需引号包裹
        if (!arg.AsSpan().ContainsAny(' ', '\t', '"'))
        {
            sb.Append(arg);
            return;
        }
        sb.Append('"');
        for (int i = 0; i < arg.Length; i++)
        {
            char c = arg[i];
            if (c == '"')
            {
                // 双引号前的反斜杠需要加倍，再加 \"
                int backslashCount = 0;
                int j = i - 1;
                while (j >= 0 && arg[j] == '\\')
                {
                    backslashCount++;
                    j--;
                }
                sb.Append('\\', backslashCount * 2);
                sb.Append("\\\"");
            }
            else
            {
                sb.Append(c);
            }
        }
        // 结尾反斜杠要加倍（防止被解释为转义闭合引号）
        int endBackslashes = 0;
        for (int i = arg.Length - 1; i >= 0 && arg[i] == '\\'; i--)
            endBackslashes++;
        if (endBackslashes > 0)
            sb.Append('\\', endBackslashes);
        sb.Append('"');
    }

    // ==================== Layer 3: IOCP 子进程监控 ====================

    // IOCP 是唯一拦截源——内核在 CreateProcess 路径上同步推送 JOB_OBJECT_MSG_NEW_PROCESS，微秒级延迟。
    // 命令行通过 NtQueryInformationProcess(ProcessCommandLineInformation) 按需获取，不依赖 WMI。

    /// <summary>启动 IOCP 后台工作线程。</summary>
    private void StartIocpThread(IntPtr iocpHandle)
    {
        _iocpCts = new CancellationTokenSource();
        var ct = _iocpCts.Token;
        IntPtr port = iocpHandle;

        _iocpThread = new Thread(() =>
        {
            while (!ct.IsCancellationRequested)
            {
                bool ok = NativeMethods.GetQueuedCompletionStatus(
                    port,
                    out uint msgId,
                    out IntPtr key,
                    out IntPtr overlapped,
                    500);

                if (!ok)
                {
                    // WAIT_TIMEOUT (258) → 继续循环
                    if (Marshal.GetLastWin32Error() == 258) continue;
                    // 其他错误 → 退出
                    break;
                }

                if (msgId == NativeMethods.IOCP_QUIT_KEY)
                    break;

                // JOB_OBJECT_MSG 的 PID 存在 lpOverlapped 里，不是 lpCompletionKey
                uint pid = (uint)(ulong)overlapped;

                if (msgId == NativeMethods.JOB_OBJECT_MSG_NEW_PROCESS)
                    HandleNewProcess(pid);
                else if (msgId == NativeMethods.JOB_OBJECT_MSG_EXIT_PROCESS)
                    HandleNewProcessExit(pid);
                // ACTIVE_PROCESS_ZERO 等其他消息忽略
            }
        })
        {
            IsBackground = true,
            Name = "EiTRVO-IOCP"
        };
        _iocpThread.Start();
    }

    /// <summary>停止 IOCP 工作线程并关闭端口。</summary>
    private void StopIocpThread()
    {
        // 1. 发退出信号
        if (_iocpCts != null)
        {
            _iocpCts.Cancel();
        }

        // 2. 唤醒阻塞中的 GetQueuedCompletionStatus
        if (_iocpHandle != IntPtr.Zero)
        {
            NativeMethods.PostQueuedCompletionStatus(
                _iocpHandle,
                NativeMethods.IOCP_QUIT_KEY,
                IntPtr.Zero,
                IntPtr.Zero);
        }

        // 3. 等待线程退出
        if (_iocpThread?.IsAlive == true)
        {
            _iocpThread.Join(2000);
        }
        _iocpThread = null;

        _iocpCts?.Dispose();
        _iocpCts = null;

        // 4. 关闭 IOCP 端口
        if (_iocpHandle != IntPtr.Zero)
        {
            NativeMethods.CloseHandle(_iocpHandle);
            _iocpHandle = IntPtr.Zero;
        }
    }

    // ==================== 反改名：黑名单哈希预缓存 ====================

    /// <summary>
    /// 启动前调用：遍历 blocklist 的 System32/SysWOW64 路径，预计算 SHA-256。
    /// 以 LastWriteTime 判断是否需要刷新，缓存通过 DPAPI 持久化。
    /// </summary>
    public void InitializeBlacklistHashes()
    {
        try
        {
            // 尝试加载持久化缓存
            var diskCache = LoadCacheFromDisk();

            foreach (string name in Blocklist)
            {
                string[] paths = BlocklistPathMap.TryGetValue(name, out var mapped)
                    ? mapped
                    : new[] { Path.Combine(SystemDir, name) };

                foreach (string path in paths)
                {
                    if (!File.Exists(path)) continue;

                    long lastWrite = File.GetLastWriteTimeUtc(path).Ticks;
                    // 缓存命中且 LastWriteTime 未变 → 复用
                    if (diskCache.TryGetValue(name, out var entry)
                        && entry.LastWriteTimeUtc == lastWrite)
                    {
                        _hashCache[name] = entry.Sha256;
                    }
                    else
                    {
                        string sha = ComputeSha256(path);
                        if (!string.IsNullOrEmpty(sha))
                        {
                            _hashCache[name] = sha;
                            diskCache[name] = new BlacklistCacheEntry
                            {
                                FileName = name,
                                Sha256 = sha,
                                LastWriteTimeUtc = lastWrite
                            };
                        }
                    }
                    break; // 第一个存在的路径即为权威来源
                }
            }

            SaveCacheToDisk(diskCache);

            _notification?.AppendLog(
                $"EiTRVO Firewall: 黑名单哈希缓存已就绪（{_hashCache.Count} 条）",
                EiTRVO.ProEngine.Models.NotificationType.Info);
        }
        catch (Exception ex)
        {
            _notification?.AppendLog(
                $"EiTRVO Firewall: 哈希缓存初始化失败，退化为纯文件名匹配: {ex.Message}",
                EiTRVO.ProEngine.Models.NotificationType.Warning);
            _hashCache.Clear();
        }
    }

    /// <summary>删除本地哈希缓存文件（Firewall 关闭时调用）。</summary>
    public void DeleteBlacklistHashCache()
    {
        _hashCache.Clear();
        try { if (File.Exists(CacheFilePath)) File.Delete(CacheFilePath); } catch { }
    }

    /// <summary>计算文件 SHA-256 hex（小写），异常时返回空字符串。</summary>
    private static string ComputeSha256(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536);
            byte[] hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch
        {
            return "";
        }
    }

    // ── DPAPI 持久化 ──

    private static Dictionary<string, BlacklistCacheEntry> LoadCacheFromDisk()
    {
        try
        {
            if (!File.Exists(CacheFilePath)) return new(StringComparer.OrdinalIgnoreCase);
            byte[] encrypted = File.ReadAllBytes(CacheFilePath);
            byte[] plain = System.Security.Cryptography.ProtectedData.Unprotect(
                encrypted, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            var list = JsonSerializer.Deserialize<List<BlacklistCacheEntry>>(plain);
            return list?.ToDictionary(e => e.FileName, StringComparer.OrdinalIgnoreCase)
                   ?? new(StringComparer.OrdinalIgnoreCase);
        }
        catch { return new(StringComparer.OrdinalIgnoreCase); }
    }

    private static void SaveCacheToDisk(Dictionary<string, BlacklistCacheEntry> cache)
    {
        try
        {
            string dir = Path.GetDirectoryName(CacheFilePath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var list = cache.Values.ToList();
            byte[] plain = JsonSerializer.SerializeToUtf8Bytes(list);
            byte[] encrypted = System.Security.Cryptography.ProtectedData.Protect(
                plain, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            // 如果上次写入后已加 ReadOnly 属性，先移除
            if (File.Exists(CacheFilePath))
                File.SetAttributes(CacheFilePath, FileAttributes.Normal);
            File.WriteAllBytes(CacheFilePath, encrypted);
            File.SetAttributes(CacheFilePath, FileAttributes.ReadOnly | FileAttributes.Hidden);
        }
        catch { /* 缓存写入失败不阻塞 */ }
    }

    /// <summary>WinVerifyTrust 检查文件是否有有效微软 Authenticode 签名。</summary>
    private static bool IsMicrosoftSigned(string path)
    {
        try
        {
            var fileInfo = new NativeMethods.WINTRUST_FILE_INFO
            {
                cbStruct = (uint)Marshal.SizeOf<NativeMethods.WINTRUST_FILE_INFO>(),
                pcwszFilePath = Marshal.StringToHGlobalUni(path),
                hFile = IntPtr.Zero,
                pgKnownSubject = IntPtr.Zero
            };

            var fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.WINTRUST_FILE_INFO>());
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);

            var trustData = new NativeMethods.WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<NativeMethods.WINTRUST_DATA>(),
                pPolicyCallbackData = IntPtr.Zero,
                pSIPClientData = IntPtr.Zero,
                dwUIChoice = NativeMethods.WTD_UI_NONE,
                fdwRevocationChecks = NativeMethods.WTD_REVOKE_NONE,
                dwUnionChoice = NativeMethods.WTD_CHOICE_FILE,
                psFileOrBlob = fileInfoPtr,
                dwStateAction = NativeMethods.WTD_STATEACTION_IGNORE,
                hWVTStateData = IntPtr.Zero,
                pwszURLReference = IntPtr.Zero,
                dwProvFlags = NativeMethods.WTD_SAFER_FLAG,
                dwUIContext = 0,
                pSignatureSettings = IntPtr.Zero
            };

            try
            {
                // Step 1: verify
                trustData.dwStateAction = NativeMethods.WTD_STATEACTION_VERIFY;
                uint result = NativeMethods.WinVerifyTrust(
                    IntPtr.Zero, NativeMethods.WINTRUST_ACTION_GENERIC_VERIFY_V2, ref trustData);

                // Step 2: close state (required even on failure)
                trustData.dwStateAction = NativeMethods.WTD_STATEACTION_CLOSE;
                NativeMethods.WinVerifyTrust(
                    IntPtr.Zero, NativeMethods.WINTRUST_ACTION_GENERIC_VERIFY_V2, ref trustData);

                return result == 0; // TRUST_E_SUBJECT_NOT_TRUSTED or any error → false
            }
            finally
            {
                Marshal.FreeHGlobal(fileInfo.pcwszFilePath);
                Marshal.FreeHGlobal(fileInfoPtr);
                // If state was allocated, free it
                if (trustData.hWVTStateData != IntPtr.Zero)
                {
                    trustData.dwStateAction = NativeMethods.WTD_STATEACTION_CLOSE;
                    NativeMethods.WinVerifyTrust(
                        IntPtr.Zero, NativeMethods.WINTRUST_ACTION_GENERIC_VERIFY_V2, ref trustData);
                }
            }
        }
        catch { return false; }
    }

    private class BlacklistCacheEntry
    {
        public string FileName { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public long LastWriteTimeUtc { get; set; }
    }

    // ==================== IOCP 回调 ====================

    /// <summary>IOCP 回调：处理进程退出 Job 的消息。（当前仅用于资源清理，不需要额外处理）</summary>
    private void HandleNewProcessExit(uint pid)
    {
        // 命令行缓存清理等资源回收可在此扩展
    }

    /// <summary>IOCP 回调：处理新进程加入 Job 的消息。</summary>
    private void HandleNewProcess(uint pid)
    {
        try
        {
            string? procName;
            string? procMainModuleFile;
            try
            {
                using var proc = Process.GetProcessById((int)pid);
                procName = (proc.ProcessName ?? "") + ".exe";
                // 进程刚创建时 MainModule 可能尚未就绪；异常 → 回退到仅文件名匹配
                procMainModuleFile = null;
                try { procMainModuleFile = proc.MainModule?.FileName; } catch { /* 快照失败，继续 */ }
            }
            catch { return; } // 进程已退出

            // ── 通道 1: 文件名快反 ──
            if (Blocklist.Contains(Path.GetFileName(procName)))
            {
                try { Process.GetProcessById((int)pid)?.Kill(); } catch { }
                string? commandLine = GetProcessCommandLine((int)pid);
                _onThreat?.Invoke(procName, (int)pid, commandLine);
                return;
            }

            // ── 通道 2: 哈希比对 + Authenticode 签名（反制改名/复制/改字节绕过） ──
            if (!string.IsNullOrEmpty(procMainModuleFile) && _hashCache.Count > 0)
            {
                string ext = Path.GetExtension(procName);
                bool isPE = ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                         || ext.Equals(".scr", StringComparison.OrdinalIgnoreCase);
                if (!isPE) return; // 非 PE 不检查

                string? suspectHash = ComputeSha256(procMainModuleFile);
                if (!string.IsNullOrEmpty(suspectHash))
                {
                    // 哈希命中 → 该进程是某个黑名单文件的副本 → kill
                    foreach (var kv in _hashCache)
                    {
                        if (string.Equals(kv.Value, suspectHash, StringComparison.OrdinalIgnoreCase))
                        {
                            try { Process.GetProcessById((int)pid)?.Kill(); } catch { }
                            string? commandLine = GetProcessCommandLine((int)pid);
                            _onThreat?.Invoke($"{procName} (改名绕过 → 原始: {kv.Key})", (int)pid, commandLine);
                            return;
                        }
                    }
                    // 哈希未命中 → 检查 Authenticode 签名
                    if (!IsMicrosoftSigned(procMainModuleFile))
                    {
                        try { Process.GetProcessById((int)pid)?.Kill(); } catch { }
                        string? commandLine = GetProcessCommandLine((int)pid);
                        _onThreat?.Invoke($"{procName} (无签名可执行文件)", (int)pid, commandLine);
                    }
                }
            }
        }
        catch { }
    }

    /// <summary>通过 NtQueryInformationProcess 获取进程命令行，不需要管理员权限。</summary>
    private static string? GetProcessCommandLine(int pid)
    {
        IntPtr hProcess = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_QUERY_INFORMATION | NativeMethods.PROCESS_VM_READ,
            false, pid);
        if (hProcess == IntPtr.Zero) return null;

        try
        {
            uint returnLength;
            int status = NativeMethods.NtQueryInformationProcess(
                hProcess,
                NativeMethods.ProcessCommandLineInformation,
                IntPtr.Zero, 0,
                out returnLength);

            if (returnLength == 0 || returnLength > 32768) return null;

            IntPtr buffer = Marshal.AllocHGlobal((int)returnLength);
            try
            {
                status = NativeMethods.NtQueryInformationProcess(
                    hProcess,
                    NativeMethods.ProcessCommandLineInformation,
                    buffer, returnLength,
                    out _);
                if (status != 0) return null;

                var us = Marshal.PtrToStructure<NativeMethods.UNICODE_STRING>(buffer);
                if (us.Buffer == IntPtr.Zero || us.Length == 0) return null;

                return Marshal.PtrToStringUni(us.Buffer, us.Length / 2);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch { return null; }
        finally
        {
            NativeMethods.CloseHandle(hProcess);
        }
    }

    // ==================== 监控生命周期 ====================

    /// <summary>IOCP 线程已在 ResumeThread 前启动，此方法仅注册熔断回调。</summary>
    public void StartMonitoring(Process parentProcess, Action<string, int, string?> onThreatDetected)
    {
        _onThreat = onThreatDetected;
    }

    public void TerminateJobProcesses()
    {
        _jobHandle?.Dispose();
        _jobHandle = null;
    }

    public void StopMonitoring()
    {
        // 先清回调，防止 IOCP 线程在停止期间调 _onThreat
        _onThreat = null;

        StopIocpThread();
    }

    // ==================== Layer 4 + 5: 高级防御 ====================

    private static readonly HashSet<string> ExecutableExtensionsSet = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".wsf", ".scr", ".msi"
    };

    private static readonly HashSet<string> ModExtensionsSet = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jar", ".class"
    };

    public void StartAdvancedMonitoring(
        Process gameProcess,
        string gameDir,
        string javaHome,
        Action<string, string> onLayer4Threat,
        Action<string, string> onLayer5Alert)
    {
        _onLayer4Threat = onLayer4Threat;
        _onLayer5Alert = onLayer5Alert;
        _gameJavaHome = javaHome;
        _gameProcessId = gameProcess.Id;
        _seenAlerts.Clear();

        // ── Layer 4: 三层 FileSystemWatcher ──

        // 实例 A: 游戏目录（递归）
        _gameDirWatcher = new FileSystemWatcher(gameDir)
        {
            IncludeSubdirectories = true,
            EnableRaisingEvents = false,
            InternalBufferSize = 65536,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime
        };
        _gameDirWatcher.Created += OnGameDirFileCreated;
        _gameDirWatcher.Error += OnWatcherError;

        // 实例 B: %TEMP%（非递归）
        string tempDir = Path.GetTempPath();
        _tempDirWatcher = new FileSystemWatcher(tempDir)
        {
            IncludeSubdirectories = false,
            EnableRaisingEvents = false,
            InternalBufferSize = 32768,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime
        };
        _tempDirWatcher.Created += OnTempDirFileCreated;
        _tempDirWatcher.Error += OnWatcherError;

        // 实例 C: Startup 文件夹
        string? startupDir = null;
        try
        {
            startupDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "Windows", "Start Menu", "Programs", "Startup");
            if (Directory.Exists(startupDir))
            {
                _startupDirWatcher = new FileSystemWatcher(startupDir)
                {
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = false,
                    InternalBufferSize = 32768,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime
                };
                _startupDirWatcher.Created += OnStartupDirFileCreated;
                _startupDirWatcher.Error += OnWatcherError;
            }
        }
        catch { _startupDirWatcher = null; }

        _gameDirWatcher.EnableRaisingEvents = true;
        _tempDirWatcher.EnableRaisingEvents = true;
        if (_startupDirWatcher != null)
            _startupDirWatcher.EnableRaisingEvents = true;

        // ── Layer 5a: DLL 轮询（每 2s）──
        _dllPollTimer = new System.Threading.Timer(_ => PollLoadedModules(), null, 2000, 2000);

        // ── Layer 5b: TCP 轮询（每 5s）──
        _tcpPollTimer = new System.Threading.Timer(_ => PollTcpConnections(), null, 5000, 5000);
    }

    public void StopAdvancedMonitoring()
    {
        _dllPollTimer?.Dispose();
        _dllPollTimer = null;
        _tcpPollTimer?.Dispose();
        _tcpPollTimer = null;

        if (_gameDirWatcher != null)
        {
            _gameDirWatcher.EnableRaisingEvents = false;
            _gameDirWatcher.Created -= OnGameDirFileCreated;
            _gameDirWatcher.Error -= OnWatcherError;
            _gameDirWatcher.Dispose();
            _gameDirWatcher = null;
        }
        if (_tempDirWatcher != null)
        {
            _tempDirWatcher.EnableRaisingEvents = false;
            _tempDirWatcher.Created -= OnTempDirFileCreated;
            _tempDirWatcher.Error -= OnWatcherError;
            _tempDirWatcher.Dispose();
            _tempDirWatcher = null;
        }
        if (_startupDirWatcher != null)
        {
            _startupDirWatcher.EnableRaisingEvents = false;
            _startupDirWatcher.Created -= OnStartupDirFileCreated;
            _startupDirWatcher.Error -= OnWatcherError;
            _startupDirWatcher.Dispose();
            _startupDirWatcher = null;
        }

        _seenAlerts.Clear();
        _gameProcessId = 0;
    }

    // ── Layer 4 event handlers ──

    private void OnGameDirFileCreated(object sender, FileSystemEventArgs e)
    {
        string ext = Path.GetExtension(e.Name);
        if (ModExtensionsSet.Contains(ext)) return;
        if (!ExecutableExtensionsSet.Contains(ext)) return;

        // 排除 mods/ 子目录
        try
        {
            string relative = Path.GetRelativePath(_gameDirWatcher!.Path, e.FullPath);
            if (relative.StartsWith("mods", StringComparison.OrdinalIgnoreCase)) return;
        }
        catch { return; }

        bool deleted = TryDeleteMaliciousFile(e.FullPath);
        string action = deleted ? "文件已被删除。" : "删除失败（文件可能被进程占用），游戏退出后将自动清理。";
        _onLayer4Threat?.Invoke(e.Name, $"EiTRVO 高级防御：检测到游戏目录内可疑文件创建\n" +
            $"文件：{e.FullPath}\n{action}");
    }

    private void OnTempDirFileCreated(object sender, FileSystemEventArgs e)
    {
        string ext = Path.GetExtension(e.Name);
        if (!ExecutableExtensionsSet.Contains(ext)) return;

        _onLayer4Threat?.Invoke(e.Name, $"EiTRVO 高级防御：检测到 %TEMP% 下有可疑可执行文件创建\n" +
            $"文件：{e.FullPath}\n" +
            $"建议使用 Windows Defender 或安全软件扫描此文件。");
    }

    private void OnStartupDirFileCreated(object sender, FileSystemEventArgs e)
    {
        string ext = Path.GetExtension(e.Name);
        if (!ExecutableExtensionsSet.Contains(ext)) return;

        bool deleted = TryDeleteMaliciousFile(e.FullPath);
        string action = deleted
            ? "文件已被删除，持久化行为已阻止。"
            : $"删除失败，请手动检查：{e.FullPath}";
        _onLayer4Threat?.Invoke(e.Name, $"EiTRVO 高级防御：检测到启动文件夹内可疑文件创建\n" +
            $"文件：{e.FullPath}\n{action}");
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        _onLayer5Alert?.Invoke("文件系统监控错误",
            $"FileSystemWatcher 内部缓冲区溢出或路径不可用。部分文件创建事件可能丢失。\n异常：{e.GetException()?.Message}");
    }

    private static bool TryDeleteMaliciousFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                return true;
            }
        }
        catch { /* 文件被占用或无权限 */ }
        return false;
    }

    // ── Layer 5a: DLL 轮询 ──

    private void PollLoadedModules()
    {
        if (_gameProcessId == 0) return;
        try
        {
            IntPtr snapshot = NativeMethods.CreateToolhelp32Snapshot(
                NativeMethods.TH32CS_SNAPMODULE | NativeMethods.TH32CS_SNAPMODULE32,
                _gameProcessId);
            if (snapshot == IntPtr.Zero || snapshot == (IntPtr)(-1)) return;

            try
            {
                var entry = new NativeMethods.MODULEENTRY32();
                entry.dwSize = Marshal.SizeOf<NativeMethods.MODULEENTRY32>();

                if (NativeMethods.Module32First(snapshot, ref entry))
                {
                    do
                    {
                        string path = entry.szExePath;
                        if (string.IsNullOrEmpty(path)) continue;

                        if (IsNonWhitelistedDll(path) && _seenAlerts.Add(path))
                        {
                            _onLayer5Alert?.Invoke(
                                "检测到非标准 DLL 加载",
                                $"进程加载了非白名单模块：\n{path}\n模块名：{entry.szModule}");
                        }
                    }
                    while (NativeMethods.Module32Next(snapshot, ref entry));
                }
            }
            finally
            {
                NativeMethods.CloseHandle(snapshot);
            }
        }
        catch { }
    }

    private bool IsNonWhitelistedDll(string dllPath)
    {
        if (string.IsNullOrEmpty(dllPath)) return false;
        string lower = dllPath.ToLowerInvariant();

        // Java 运行时目录
        if (!string.IsNullOrEmpty(_gameJavaHome) &&
            lower.StartsWith(_gameJavaHome.ToLowerInvariant() + @"\"))
            return false;

        // Windows 系统目录
        if (lower.StartsWith(@"c:\windows\system32\") ||
            lower.StartsWith(@"c:\windows\syswow64\") ||
            lower.StartsWith(@"c:\windows\winsxs\"))
            return false;

        // GPU 驱动
        if (lower.StartsWith(@"c:\windows\system32\driverstore\fileRepository\"))
            return false;

        // LWJGL / game natives
        if (lower.Contains(@"\natives\"))
            return false;

        // .NET / VC++ 运行时
        if (lower.Contains(@"\microsoft.net\") ||
            lower.Contains(@"\microsoft visual studio\") ||
            lower.Contains(@"\microsoft\vc\"))
            return false;

        // Known system component paths
        if (lower.StartsWith(@"c:\program files") ||
            lower.StartsWith(@"c:\program files (x86)"))
            return false;

        return true;
    }

    // ── Layer 5b: TCP 轮询 ──

    private void PollTcpConnections()
    {
        if (_gameProcessId == 0) return;
        try
        {
            int bufferSize = 0;
            _ = NativeMethods.GetExtendedTcpTable(IntPtr.Zero, ref bufferSize,
                false, NativeMethods.AF_INET, NativeMethods.TCP_TABLE_OWNER_PID_ALL, 0);
            if (bufferSize <= 0) return;

            IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
            try
            {
                if (NativeMethods.GetExtendedTcpTable(buffer, ref bufferSize,
                    false, NativeMethods.AF_INET, NativeMethods.TCP_TABLE_OWNER_PID_ALL, 0) != 0)
                    return;

                uint numEntries = (uint)Marshal.ReadInt32(buffer);
                int rowSize = Marshal.SizeOf<NativeMethods.MIB_TCPROW_OWNER_PID>();

                for (int i = 0; i < numEntries; i++)
                {
                    IntPtr rowPtr = IntPtr.Add(buffer, 4 + i * rowSize);
                    var row = Marshal.PtrToStructure<NativeMethods.MIB_TCPROW_OWNER_PID>(rowPtr);

                    if (row.dwOwningPid != _gameProcessId)
                        continue;

                    int remotePort = NetworkToHostPort(row.dwRemotePort);

                    // Minecraft 标准端口 + HTTPS
                    if (remotePort == 25565 || remotePort == 443)
                        continue;

                    string remoteAddr = IntToIPv4(row.dwRemoteAddr);
                    string alertKey = $"TCP:{remoteAddr}:{remotePort}";

                    if (_seenAlerts.Add(alertKey))
                    {
                        _onLayer5Alert?.Invoke(
                            "检测到非标准端口连接",
                            $"游戏进程连接到非标准端口：{remoteAddr}:{remotePort}");
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch { }
    }

    private static int NetworkToHostPort(int networkPort)
    {
        // dwRemotePort is a DWORD in network byte order; only low 16 bits are the port
        byte[] bytes = BitConverter.GetBytes(networkPort);
        if (BitConverter.IsLittleEndian)
            return (bytes[1] << 8) | bytes[0];
        return networkPort & 0xFFFF;
    }

    private static string IntToIPv4(uint networkAddr)
    {
        byte[] bytes = BitConverter.GetBytes(networkAddr);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.{bytes[3]}";
    }

    // ==================== Dispose ====================

    public void Dispose()
    {
        StopAdvancedMonitoring();
        StopMonitoring();
        _jobHandle?.Dispose();
        _jobHandle = null;
    }
}
