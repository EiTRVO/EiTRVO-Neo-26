using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using EiTRVO.ProEngine.Helpers;
using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Services;

namespace EiTRVO.ProEngine.Orchestrators;

public class NotificationService : INotificationService
{
    private readonly IDispatcherService _dispatcher;
    private int _notificationIdCounter;
    private readonly Dictionary<int, IDisposable> _notificationTimers = new();

    public ObservableCollection<NotificationItem> Notifications { get; } = new();
    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    public NotificationService(IDispatcherService dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public void Show(string message, NotificationType type = NotificationType.Info, int durationMs = 3000)
    {
        AppendLog(message, type);

        var id = Interlocked.Increment(ref _notificationIdCounter);
        var notification = new NotificationItem { Id = id, Message = message, Type = type };
        _dispatcher.Invoke(() =>
        {
            Notifications.Add(notification);
            var handle = _dispatcher.StartTimer(TimeSpan.FromMilliseconds(durationMs), () =>
            {
                _notificationTimers.Remove(id);
                var item = Notifications.FirstOrDefault(n => n.Id == id);
                if (item != null) Notifications.Remove(item);
            });
            _notificationTimers[id] = handle;
        });
    }

    public void AppendLog(string message, NotificationType type)
    {
        var entry = new LogEntry { Time = DateTime.Now, Message = message, Type = type };
        _dispatcher.Invoke(() =>
        {
            LogEntries.Insert(0, entry);
            const int maxLog = 200;
            while (LogEntries.Count > maxLog)
                LogEntries.RemoveAt(LogEntries.Count - 1);
        });
    }

    public string? WriteDiagnosticLog(string title, string details, bool autoOpen = false)
    {
        AppendLog($"{title}: {details}", NotificationType.Error);
        try
        {
            string logDir = Path.Combine(PlatformHelper.GetBaseDirectory(), "log");
            Directory.CreateDirectory(logDir);
            string logFile = Path.Combine(logDir, $"{DateTime.Now:yyyyMMdd_HHmmssfff}.txt");
            File.WriteAllText(logFile,
                $"===== {title} =====\n" +
                $"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n\n" +
                $"{details}\n");

            if (autoOpen)
            {
                try { Process.Start(new ProcessStartInfo(logFile) { UseShellExecute = true }); }
                catch { /* 无法打开编辑器不影响流程 */ }
            }

            return logFile;
        }
        catch { return null; }
    }

    public void Remove(int notificationId)
    {
        if (_notificationTimers.TryGetValue(notificationId, out var handle))
        {
            handle.Dispose();
            _notificationTimers.Remove(notificationId);
        }
        var item = Notifications.FirstOrDefault(n => n.Id == notificationId);
        if (item != null) Notifications.Remove(item);
    }

    public void ClearLog()
    {
        LogEntries.Clear();
    }
}
