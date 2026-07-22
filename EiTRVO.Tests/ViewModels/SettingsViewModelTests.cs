using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Orchestrators;
using EiTRVO.ProEngine.ViewModels;
using EiTRVO.Tests.Fakes;

namespace EiTRVO.Tests.ViewModels;

[TestClass]
public class SettingsViewModelTests
{
    private readonly FakeNotificationService _notification;
    private readonly FakeDialogService _dialog;
    private readonly FakeWindowsHelloService _windowsHello;
    private readonly JavaDetectionService _javaDetection;
    private readonly FakeModrinthService _modrinth;

    public SettingsViewModelTests()
    {
        _notification = new FakeNotificationService();
        _dialog = new FakeDialogService();
        _windowsHello = new FakeWindowsHelloService();
        _javaDetection = new JavaDetectionService();
        _modrinth = new FakeModrinthService();
    }

    [TestMethod]
    public void Constructor_InitializesDefaults()
    {
        var vm = new SettingsViewModel(_javaDetection, _notification, _dialog, _windowsHello, _modrinth);

        Assert.IsNotNull(vm.BackupIntervalOptions);
        Assert.IsTrue(vm.BackupIntervalOptions.Count > 0);
        Assert.IsNotNull(vm.RestoreModeOptions);
        Assert.IsTrue(vm.RestoreModeOptions.Count > 0);
    }

    [TestMethod]
    public void ApplySettings_FromLauncherSettings_MapsCorrectly()
    {
        var vm = new SettingsViewModel(_javaDetection, _notification, _dialog, _windowsHello, _modrinth);
        var settings = new LauncherSettings
        {
            MemoryMB = 8192,
            Resolution = "1920x1080",
            IsolateNewInstancesByDefault = false,
            FirewallEnabled = true,
            BackupEnabled = false,
            BackupInterval = BackupInterval.Weekly,
            BackupFolder = @"C:\Backups",
            ExcludeRedownloadable = true
        };

        vm.ApplySettings(settings);

        Assert.AreEqual(8192, vm.Memory);
        Assert.AreEqual("1920x1080", vm.SelectedResolution);
        Assert.IsTrue(vm.FirewallEnabled);
        Assert.IsFalse(vm.BackupEnabled);
        Assert.AreEqual(BackupInterval.Weekly, vm.BackupInterval);
        Assert.AreEqual(@"C:\Backups", vm.BackupFolder);
    }

    [TestMethod]
    public void ToSettings_SnapshotsCurrentState()
    {
        var vm = new SettingsViewModel(_javaDetection, _notification, _dialog, _windowsHello, _modrinth);
        vm.MemoryText = "4096";
        vm.BackupEnabled = true;
        vm.FirewallEnabled = false;
        vm.IsolateNewInstancesByDefault = false;

        var result = vm.ToSettings();

        Assert.AreEqual(4096, result.MemoryMB);
        Assert.IsTrue(result.BackupEnabled);
        Assert.IsFalse(result.FirewallEnabled);
        Assert.IsFalse(result.IsolateNewInstancesByDefault);
    }

    [TestMethod]
    public async Task WindowsHello_Available_ShowsLockOption()
    {
        _windowsHello.Availability = ProEngine.Services.WindowsHelloAvailability.Available;
        var vm = new SettingsViewModel(_javaDetection, _notification, _dialog, _windowsHello, _modrinth);

        await vm.CheckWindowsHelloAvailabilityAsync();

        Assert.IsTrue(vm.IsWindowsHelloAvailable);
        StringAssert.Contains(vm.SettingsLockHint, "Windows Hello");
    }

    [TestMethod]
    public async Task WindowsHello_NotAvailable_HidesLockOption()
    {
        _windowsHello.Availability = ProEngine.Services.WindowsHelloAvailability.NotConfigured;
        var vm = new SettingsViewModel(_javaDetection, _notification, _dialog, _windowsHello, _modrinth);

        await vm.CheckWindowsHelloAvailabilityAsync();

        Assert.IsFalse(vm.IsWindowsHelloAvailable);
    }

    [TestMethod]
    public void SettingsLock_WhenAvailable_ToggleWorks()
    {
        var vm = new SettingsViewModel(_javaDetection, _notification, _dialog, _windowsHello, _modrinth);
        vm.IsWindowsHelloAvailable = true;

        vm.SettingsLockEnabled = true;
        Assert.IsTrue(vm.SettingsLockEnabled);

        vm.SettingsLockEnabled = false;
        Assert.IsFalse(vm.SettingsLockEnabled);
    }

    // ================================================================
    // BrowseJava — manual Java path validation
    // ================================================================

    [TestMethod]
    public async Task BrowseJava_DialogCancelled_DoesNotSetPath()
    {
        _dialog.OpenFileResult = null;
        var vm = new SettingsViewModel(_javaDetection, _notification, _dialog, _windowsHello, _modrinth);
        string? originalPath = vm.ManualJavaPath;

        await vm.BrowseJavaCommand.ExecuteAsync(null);

        // Nothing should change when user cancels
        Assert.IsNull(vm.ManualJavaPath);
    }

    [TestMethod]
    public async Task BrowseJava_InvalidFile_ShowsWarning()
    {
        // Non-existent path → GetJavaVersionInfoAsync returns null
        _dialog.OpenFileResult = "C:\\nonexistent\\fake_java.exe";
        var vm = new SettingsViewModel(_javaDetection, _notification, _dialog, _windowsHello, _modrinth);

        await vm.BrowseJavaCommand.ExecuteAsync(null);

        // Should show warning notification
        Assert.AreEqual(NotificationType.Warning, _notification.LastShowType);
        StringAssert.Contains(_notification.LastShowMessage, "不是有效的 Java 运行时");
        // ManualJavaPath should NOT be set
        Assert.IsNull(vm.ManualJavaPath);
    }
}
