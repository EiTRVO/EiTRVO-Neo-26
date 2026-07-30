namespace EiTRVO.ProEngine.Models;

/// <summary>
/// Minecraft 游戏进程运行时日志条目。
/// 每条对应进程 stdout 或 stderr 的一行输出。
/// </summary>
public class RuntimeLogEntry
{
    public DateTime Timestamp { get; init; }
    public string Message { get; init; } = "";
    public bool IsStderr { get; init; }
    public string TimeDisplay => Timestamp.ToString("HH:mm:ss");
}
