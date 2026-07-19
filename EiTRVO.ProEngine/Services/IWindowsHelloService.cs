namespace EiTRVO.ProEngine.Services;

public enum WindowsHelloAvailability
{
    Available,
    DeviceNotPresent,
    NotConfigured,
    DisabledByPolicy
}

public interface IWindowsHelloService
{
    /// <summary>检查设备是否配置了 Windows Hello / PIN</summary>
    Task<WindowsHelloAvailability> CheckAvailabilityAsync();

    /// <summary>弹出系统验证对话框。返回 true 表示验证通过</summary>
    Task<bool> VerifyAsync(string reason);
}
