using Windows.Security.Credentials.UI;
using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Services;

namespace EiTRVO.UI.Services;

public class WindowsHelloService : IWindowsHelloService
{
    public async Task<WindowsHelloAvailability> CheckAvailabilityAsync()
    {
        try
        {
            var result = await UserConsentVerifier.CheckAvailabilityAsync();

            // Map WinRT enum via its string representation to avoid projection differences
            return result.ToString() switch
            {
                "Available" => WindowsHelloAvailability.Available,
                "DeviceNotPresent" => WindowsHelloAvailability.DeviceNotPresent,
                "NotConfigured" => WindowsHelloAvailability.NotConfigured,
                "DisabledByPolicy" => WindowsHelloAvailability.DisabledByPolicy,
                _ => WindowsHelloAvailability.DeviceNotPresent
            };
        }
        catch
        {
            return WindowsHelloAvailability.DeviceNotPresent;
        }
    }

    public async Task<bool> VerifyAsync(string reason)
    {
        try
        {
            var available = await CheckAvailabilityAsync();
            if (available != WindowsHelloAvailability.Available)
                return false;

            var result = await UserConsentVerifier.RequestVerificationAsync(reason);
            return result.ToString() == "Verified";
        }
        catch
        {
            return false;
        }
    }
}
