using System;
using System.Threading.Tasks;

namespace EiTRVO.ProEngine.Services;

/// <summary>
/// 平台调度器抽象 — 封装 UI 线程调度操作。
/// WPF 实现用 Dispatcher，WinUI 3 实现用 DispatcherQueue。
/// </summary>
public interface IDispatcherService
{
    /// <summary>在 UI 线程同步执行。</summary>
    void Invoke(Action action);

    /// <summary>在 UI 线程异步执行。</summary>
    Task InvokeAsync(Func<Task> callback);

    /// <summary>
    /// 启动一个在 UI 线程触发的定时器。
    /// 返回的 <see cref="IDisposable"/> 用于取消/清理定时器。
    /// </summary>
    IDisposable StartTimer(TimeSpan interval, Action tick);
}
