using System;

namespace EiTRVO.ProEngine.Models;

public class NotificationItem
{
    public int Id { get; set; }
    public string Message { get; set; } = "";
    public NotificationType Type { get; set; }
}

public class LogEntry
{
    public DateTime Time { get; set; }
    public string Message { get; set; } = "";
    public NotificationType Type { get; set; }

    public string TimeDisplay => Time.ToString("HH:mm:ss");

    public string TypeLabel => Type switch
    {
        NotificationType.Info => "INFO",
        NotificationType.Success => "OK",
        NotificationType.Warning => "WARN",
        NotificationType.Error => "ERR",
        _ => "INFO"
    };
}

public enum NotificationType
{
    Info,
    Success,
    Warning,
    Error
}
