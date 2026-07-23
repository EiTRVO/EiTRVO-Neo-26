using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

    // ==================== CREATE_SUSPENDED: Layer 0 + 1 + 2 原子化 ====================

    /// <summary>
    /// 用 CREATE_SUSPENDED 创建进程 → Layer 0 (扩展点禁用) / Layer 1 (特权移除) /
    /// Layer 2 (JobObject) → ResumeThread → 返回 HardenedProcessHandle。
    /// 恢复执行前所有加固已就位，消除 Process.Start → HardenProcess 之间的竞态窗口。
    /// </summary>
    public HardenedProcessHandle StartSuspendedAndHarden(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory)
    {
        // ── 1. 创建匿名管道（stdout / stderr）──
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

            // 管道 read 端改为不可继承：子进程不应读取自己的 stdout/stderr
            IntPtr curProc = (IntPtr)(-1); // GetCurrentProcess pseudo-handle
            if (!NativeMethods.DuplicateHandle(curProc, stdoutReadInherit, curProc,
                    out stdoutRead, 0, false, NativeMethods.DUPLICATE_SAME_ACCESS))
                throw new InvalidOperationException($"DuplicateHandle (stdout) 失败: {Marshal.GetLastWin32Error()}");
            stdoutReadInherit.Dispose(); // 关闭可继承原始句柄

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

        try
        {
            // ── 2. Layer 0: 构建 STARTUPINFOEX + 扩展点禁用策略 ──
            var siEx = new NativeMethods.STARTUPINFOEX();
            siEx.StartupInfo.cb = Marshal.SizeOf<NativeMethods.STARTUPINFOEX>();
            siEx.StartupInfo.hStdOutput = stdoutWrite.DangerousGetHandle();
            siEx.StartupInfo.hStdError = stderrWrite.DangerousGetHandle();
            siEx.StartupInfo.hStdInput = IntPtr.Zero; // no stdin — game doesn't need it
            siEx.StartupInfo.dwFlags = NativeMethods.STARTF_USESTDHANDLES;

            // 初始化扩展属性列表（两次调用：查询大小 + 分配）
            int attrSize = 0;
            NativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attrSize);
            // 预期 GetLastWin32Error == 122 (ERROR_INSUFFICIENT_BUFFER)，attrSize > 0

            if (attrSize > 0)
            {
                siEx.lpAttributeList = Marshal.AllocHGlobal(attrSize);
                if (NativeMethods.InitializeProcThreadAttributeList(siEx.lpAttributeList, 1, 0, ref attrSize))
                {
                    // 注入 EXTENSION_POINT_DISABLE_ALWAYS_ON 策略（按值传递，需分配内存）
                    IntPtr mitigationValue = Marshal.AllocHGlobal(sizeof(long));
                    try
                    {
                        Marshal.WriteInt64(mitigationValue,
                            NativeMethods.PROCESS_CREATION_MITIGATION_POLICY_EXTENSION_POINT_DISABLE_ALWAYS_ON);
                        IntPtr sizeVal = (IntPtr)sizeof(long);
                        NativeMethods.UpdateProcThreadAttribute(
                            siEx.lpAttributeList,
                            0,
                            (IntPtr)NativeMethods.PROC_THREAD_ATTRIBUTE_MITIGATION_POLICY,
                            mitigationValue,
                            sizeVal,
                            IntPtr.Zero,
                            IntPtr.Zero);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(mitigationValue);
                    }
                }
            }

            // ── 3. 构建命令行 ──
            string cmdLine = BuildCommandLine(fileName, arguments);

            // ── 4. CreateProcess (EXTENDED_STARTUPINFO_PRESENT | CREATE_SUSPENDED) ──
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

            // 释放扩展属性列表（CreateProcess 后即可释放，无论成败）
            if (siEx.lpAttributeList != IntPtr.Zero)
            {
                NativeMethods.DeleteProcThreadAttributeList(siEx.lpAttributeList);
                Marshal.FreeHGlobal(siEx.lpAttributeList);
                siEx.lpAttributeList = IntPtr.Zero;
            }

            // 关闭管道 write 端（子进程已通过句柄继承持有副本）
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

            // ── 5. Layer 1: 移除特权（在挂起态操作 token）──
            if (NativeMethods.OpenProcessToken(hProcess,
                NativeMethods.TOKEN_ADJUST_PRIVILEGES | NativeMethods.TOKEN_QUERY,
                out IntPtr hToken))
            {
                try
                {
                    foreach (var priv in NativeMethods.PrivilegesToRemove)
                    {
                        if (NativeMethods.LookupPrivilegeValue(null, priv, out var luid))
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

            // ── 6. Layer 2: Job Object ──
            _jobHandle = NativeMethods.CreateJobObject(IntPtr.Zero, null);
            if (!_jobHandle.IsInvalid)
            {
                var limits = new NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                limits.BasicLimitInformation.LimitFlags =
                    NativeMethods.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE |
                    NativeMethods.JOB_OBJECT_LIMIT_ACTIVE_PROCESS;
                limits.BasicLimitInformation.ActiveProcessLimit = 50;

                int limSize = Marshal.SizeOf<NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
                IntPtr ptr = Marshal.AllocHGlobal(limSize);
                try
                {
                    Marshal.StructureToPtr(limits, ptr, false);
                    NativeMethods.SetInformationJobObject(
                        _jobHandle,
                        NativeMethods.JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation,
                        ptr, (uint)limSize);
                }
                finally { Marshal.FreeHGlobal(ptr); }

                NativeMethods.AssignProcessToJobObject(_jobHandle, hProcess);
            }

            // ── 7. ResumeThread —— 进程开始执行，Layer 0/1/2 全部就位 ──
            uint suspendCount = NativeMethods.ResumeThread(hThread);
            if (suspendCount == uint.MaxValue) // (DWORD)-1 = failure
                throw new InvalidOperationException($"ResumeThread 失败: {Marshal.GetLastWin32Error()}");

            // ── 8. 清理 hProcess / hThread，构建托管 Process 对象 ──
            NativeMethods.CloseHandle(hThread);
            hThread = IntPtr.Zero;
            NativeMethods.CloseHandle(hProcess);
            hProcess = IntPtr.Zero;

            process = Process.GetProcessById(pid);

            // ── 9. 构建 StreamReader ──
            var stdoutStream = new FileStream(stdoutRead, FileAccess.Read, bufferSize: 4096, isAsync: true);
            var stderrStream = new FileStream(stderrRead, FileAccess.Read, bufferSize: 4096, isAsync: true);
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
            // 错误清理：终止挂起进程 + 关闭所有句柄
            // hProcess 在 ResumeThread+CloseHandle 成功后已置零，非零说明进程仍未恢复
            if (hThread != IntPtr.Zero)
                NativeMethods.CloseHandle(hThread);
            if (hProcess != IntPtr.Zero)
            {
                NativeMethods.TerminateProcess(hProcess, 1);
                NativeMethods.CloseHandle(hProcess);
            }
            process?.Dispose();
            stdoutRead?.Dispose();
            stderrRead?.Dispose();
            if (stdoutWrite != null) stdoutWrite.Dispose();
            if (stderrWrite != null) stderrWrite.Dispose();
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
