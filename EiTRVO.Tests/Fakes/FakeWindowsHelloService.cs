using EiTRVO.ProEngine.Services;

namespace EiTRVO.Tests.Fakes;

public class FakeWindowsHelloService : IWindowsHelloService
{
    public WindowsHelloAvailability Availability { get; set; } = WindowsHelloAvailability.Available;
    public bool VerifyResult { get; set; } = true;
    public string? LastVerifyReason { get; private set; }

    public Task<WindowsHelloAvailability> CheckAvailabilityAsync()
        => Task.FromResult(Availability);

    public Task<bool> VerifyAsync(string reason)
    {
        LastVerifyReason = reason;
        return Task.FromResult(VerifyResult);
    }
}
