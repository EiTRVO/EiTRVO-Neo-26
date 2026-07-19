using System.Collections.ObjectModel;
using EiTRVO.ProEngine.Models;

namespace EiTRVO.ProEngine.Orchestrators;

/// <summary>
/// 通知与日志服务 — 管理 UI 通知、日志条目和诊断日志文件。
/// 所有 Dispatcher 调用局限在此实现中，WinUI 3 迁移时仅需修改此文件。
/// </summary>
public interface INotificationService
{
    ObservableCollection<NotificationItem> Notifications { get; }
    ObservableCollection<LogEntry> LogEntries { get; }
    void Show(string message, NotificationType type = NotificationType.Info, int durationMs = 3000);
    void AppendLog(string message, NotificationType type = NotificationType.Info);
    string? WriteDiagnosticLog(string title, string details, bool autoOpen = false);
    void Remove(int notificationId);
    void ClearLog();
}
