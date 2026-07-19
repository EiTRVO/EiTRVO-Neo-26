using System;
using System.Threading.Tasks;
using System.Windows.Threading;
using EiTRVO.ProEngine.Services;

namespace EiTRVO.UI.Platforms;

/// <summary>
/// WPF 平台的调度器实现 — 封装 Dispatcher 和 DispatcherTimer。
/// </summary>
public class WpfDispatcherService : IDispatcherService
{
    private readonly Dispatcher _dispatcher;

    public WpfDispatcherService()
    {
        _dispatcher = System.Windows.Application.Current.Dispatcher;
    }

    public void Invoke(Action action)
    {
        _dispatcher.Invoke(action);
    }

    public Task InvokeAsync(Func<Task> callback)
    {
        return _dispatcher.InvokeAsync(callback).Task.Unwrap();
    }

    public IDisposable StartTimer(TimeSpan interval, Action tick)
    {
        var timer = new DispatcherTimer { Interval = interval };
        timer.Tick += (_, _) => tick();
        timer.Start();
        return new TimerHandle(timer);
    }

    private sealed class TimerHandle : IDisposable
    {
        private DispatcherTimer? _timer;
        public TimerHandle(DispatcherTimer timer) => _timer = timer;
        public void Dispose() { _timer?.Stop(); _timer = null; }
    }
}
