using System.Collections.ObjectModel;
using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Orchestrators;

namespace EiTRVO.Tests.Fakes;

/// <summary>Test-only INotificationService — records calls for test assertions.</summary>
public class FakeNotificationService : INotificationService
{
    public ObservableCollection<NotificationItem> Notifications { get; } = new();
    public ObservableCollection<LogEntry> LogEntries { get; } = new();
    public event Action<NotificationItem>? NotificationAdded;
    public event Action<LogEntry>? LogAppended;

    // --- Call recording ---
    public string? LastShowMessage { get; private set; }
    public NotificationType? LastShowType { get; private set; }
    public string? LastDiagnosticLogTitle { get; private set; }
    public string? LastDiagnosticLogDetails { get; private set; }
    public int ShowCallCount { get; private set; }
    public string? LastLogMessage { get; private set; }
    public NotificationType? LastLogType { get; private set; }

    public void Show(string message, NotificationType type = NotificationType.Info, int durationMs = 3000)
    {
        ShowCallCount++;
        LastShowMessage = message;
        LastShowType = type;
    }

    public void AppendLog(string message, NotificationType type = NotificationType.Info)
    {
        LastLogMessage = message;
        LastLogType = type;
    }
    public void ShowDiagnosticLog(string message) { }

    public string? WriteDiagnosticLog(string title, string details, bool autoOpen = false)
    {
        LastDiagnosticLogTitle = title;
        LastDiagnosticLogDetails = details;
        return null;
    }

    public void Remove(int notificationId) { }
    public void ClearLog() { }
    public string LastLogPath { get; set; } = "";
}
