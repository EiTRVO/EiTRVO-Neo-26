using EiTRVO.ProEngine.Services;

namespace EiTRVO.Tests.Fakes;

/// <summary>Test-only IDispatcherService — invokes actions synchronously on the current thread.</summary>
public class FakeDispatcherService : IDispatcherService
{
    public void Invoke(Action action) => action();

    public Task InvokeAsync(Func<Task> callback) => callback();

    public IDisposable StartTimer(TimeSpan interval, Action tick) => throw new NotSupportedException();
}
