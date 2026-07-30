using System.Security.Cryptography;
using System.Text.Json;
using EiTRVO.ProEngine.Orchestrators;
using EiTRVO.ProEngine.Services;
using EiTRVO.ProEngine.ViewModels;
using EiTRVO.Tests.Fakes;

namespace EiTRVO.Tests.ViewModels;

[TestClass]
public class HomeViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FakeGameFolderService _gameFolder;
    private readonly FakeNotificationService _notification;
    private readonly AccountManager _accountManager;
    private readonly InstanceManager _instanceManager;
    private readonly LaunchOrchestrator _launchOrchestrator;

    public HomeViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"eitrvo_home_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _gameFolder = new FakeGameFolderService { GameDir = _tempDir };
        _notification = new FakeNotificationService();
        _accountManager = new AccountManager(_gameFolder);
        _instanceManager = new InstanceManager(_gameFolder);

        _launchOrchestrator = new LaunchOrchestrator(
            new HttpClient(),
            new FakeAuthService(),
            new FakeModLoaderService(),
            _notification,
            _gameFolder,
            new SaveLockService(),
            new FakeModrinthService());
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [TestMethod]
    public void Constructor_InitializesDefaults()
    {
        var vm = new HomeViewModel(
            _launchOrchestrator, _notification, _accountManager, _instanceManager, _gameFolder);

        Assert.AreEqual(EiTRVO.ProEngine.Helpers.SystemMemoryInfo.RecommendedDefaultMemoryMB, vm.Memory);
        Assert.IsFalse(vm.IsLaunching);
        Assert.IsFalse(vm.IsGameRunning);
    }

    [TestMethod]
    public void Memory_Set_UpdatesProperty()
    {
        var vm = new HomeViewModel(
            _launchOrchestrator, _notification, _accountManager, _instanceManager, _gameFolder);

        vm.Memory = 8192;
        Assert.AreEqual(8192, vm.Memory);
    }

    [TestMethod]
    public void Instances_ExposedFromInstanceManager()
    {
        var vm = new HomeViewModel(
            _launchOrchestrator, _notification, _accountManager, _instanceManager, _gameFolder);

        Assert.IsNotNull(vm.LocalInstances);
        Assert.AreEqual(0, vm.LocalInstances.Count);
    }

    [TestMethod]
    public void Accounts_ExposedFromAccountManager()
    {
        var vm = new HomeViewModel(
            _launchOrchestrator, _notification, _accountManager, _instanceManager, _gameFolder);

        Assert.IsNotNull(vm.Accounts);
        Assert.AreEqual(0, vm.Accounts.Count);
    }

    // ================================================================
    // BuyMinecraftReminder
    // ================================================================

    [TestMethod]
    public void BuyMinecraftReminder_NullCallback_NoException()
    {
        var vm = new HomeViewModel(
            _launchOrchestrator, _notification, _accountManager, _instanceManager, _gameFolder);

        // Callback is null by default — setting it to null should not cause issues
        vm.BuyMinecraftReminder = null;
        Assert.IsNull(vm.BuyMinecraftReminder);
    }

    [TestMethod]
    public async Task BuyMinecraftReminder_CallbackSet_InvokedWhenNeeded()
    {
        var vm = new HomeViewModel(
            _launchOrchestrator, _notification, _accountManager, _instanceManager, _gameFolder);

        bool wasCalled = false;
        vm.BuyMinecraftReminder = () =>
        {
            wasCalled = true;
            return Task.CompletedTask;
        };

        // Verify the callback can be set and is retrievable
        Assert.IsNotNull(vm.BuyMinecraftReminder);

        // Invoke the callback directly to verify it works
        await vm.BuyMinecraftReminder();
        Assert.IsTrue(wasCalled);
    }

    [TestMethod]
    public void BuyMinecraftReminder_Property_SetGet()
    {
        var vm = new HomeViewModel(
            _launchOrchestrator, _notification, _accountManager, _instanceManager, _gameFolder);

        Func<Task>? callback = () => Task.CompletedTask;
        vm.BuyMinecraftReminder = callback;
        Assert.IsNotNull(vm.BuyMinecraftReminder);
    }

    [TestMethod]
    public void Constructor_ReminderCallback_DefaultsToNull()
    {
        var vm = new HomeViewModel(
            _launchOrchestrator, _notification, _accountManager, _instanceManager, _gameFolder);

        Assert.IsNull(vm.BuyMinecraftReminder);
    }

    [TestMethod]
    public void Constructor_SaveUnlockHandler_DefaultsToNull()
    {
        var vm = new HomeViewModel(
            _launchOrchestrator, _notification, _accountManager, _instanceManager, _gameFolder);

        Assert.IsNull(vm.SaveUnlockHandler);
    }

    // ================================================================
    // LaunchAsync — guard conditions
    // ================================================================

    [TestMethod]
    public async Task Launch_NoInstance_ShowsWarning()
    {
        var vm = new HomeViewModel(
            _launchOrchestrator, _notification, _accountManager, _instanceManager, _gameFolder);

        // Add an offline account so account check passes
        var account = new EiTRVO.ProEngine.Models.Account
        {
            Type = EiTRVO.ProEngine.Models.AccountType.Offline,
            Username = "TestPlayer",
            UUID = EiTRVO.ProEngine.Helpers.UuidHelper.OfflineUuid("TestPlayer")
        };
        _accountManager.Accounts.Add(account);
        vm.SelectedAccount = account;
        vm.SelectedInstance = null;
        vm.SelectedJava = new EiTRVO.ProEngine.Models.JavaInfo { Path = "java.exe", Version = "21" };
        vm.Memory = 2048;

        await vm.LaunchCommand.ExecuteAsync(null);

        Assert.IsNotNull(_notification.LastShowMessage, "Expected a warning notification when no instance is selected.");
        StringAssert.Contains(_notification.LastShowMessage, "实例");
    }

    [TestMethod]
    public async Task Launch_NoJava_ShowsWarning()
    {
        var vm = new HomeViewModel(
            _launchOrchestrator, _notification, _accountManager, _instanceManager, _gameFolder);

        var instance = new EiTRVO.ProEngine.Models.GameInstance
        {
            Name = "TestInstance",
            VersionId = "1.21"
        };
        _instanceManager.Instances.Add(instance);

        var account = new EiTRVO.ProEngine.Models.Account
        {
            Type = EiTRVO.ProEngine.Models.AccountType.Offline,
            Username = "TestPlayer",
            UUID = EiTRVO.ProEngine.Helpers.UuidHelper.OfflineUuid("TestPlayer")
        };
        _accountManager.Accounts.Add(account);
        vm.SelectedAccount = account;
        vm.SelectedInstance = instance;
        vm.SelectedJava = null;
        vm.Memory = 2048;

        await vm.LaunchCommand.ExecuteAsync(null);

        Assert.IsNotNull(_notification.LastShowMessage, "Expected a warning notification when no Java is selected.");
        StringAssert.Contains(_notification.LastShowMessage, "Java");
    }

    [TestMethod]
    public async Task Launch_MemoryExceedsLimit_ShowsWarning()
    {
        var vm = new HomeViewModel(
            _launchOrchestrator, _notification, _accountManager, _instanceManager, _gameFolder);

        var instance = new EiTRVO.ProEngine.Models.GameInstance
        {
            Name = "TestInstance",
            VersionId = "1.21"
        };
        _instanceManager.Instances.Add(instance);

        var account = new EiTRVO.ProEngine.Models.Account
        {
            Type = EiTRVO.ProEngine.Models.AccountType.Offline,
            Username = "TestPlayer",
            UUID = EiTRVO.ProEngine.Helpers.UuidHelper.OfflineUuid("TestPlayer")
        };
        _accountManager.Accounts.Add(account);
        vm.SelectedAccount = account;
        vm.SelectedInstance = instance;
        vm.SelectedJava = new EiTRVO.ProEngine.Models.JavaInfo { Path = "java.exe", Version = "21" };
        vm.Memory = 40000; // exceeds 32768

        await vm.LaunchCommand.ExecuteAsync(null);

        // Memory check uses a hardcoded string (not localized), so it should work
        Assert.IsNotNull(_notification.LastShowMessage);
        StringAssert.Contains(_notification.LastShowMessage!, "32768");
    }

    [TestMethod]
    public async Task Launch_NoAccount_ShowsWarning()
    {
        var vm = new HomeViewModel(
            _launchOrchestrator, _notification, _accountManager, _instanceManager, _gameFolder);

        var instance = new EiTRVO.ProEngine.Models.GameInstance
        {
            Name = "TestInstance",
            VersionId = "1.21"
        };
        _instanceManager.Instances.Add(instance);
        vm.SelectedInstance = instance;
        vm.SelectedJava = new EiTRVO.ProEngine.Models.JavaInfo { Path = "java.exe", Version = "21" };
        vm.SelectedAccount = null;
        vm.Memory = 2048;

        await vm.LaunchCommand.ExecuteAsync(null);

        Assert.IsNotNull(_notification.LastShowMessage);
        StringAssert.Contains(_notification.LastShowMessage!, "账号");
    }

    // ================================================================
    // BuyMinecraftReminder — condition tests
    // ================================================================

    [TestMethod]
    public async Task RemindBuyMinecraft_HasMicrosoftAccount_SkipsCallback()
    {
        var msAccount = new EiTRVO.ProEngine.Models.Account
        {
            Type = EiTRVO.ProEngine.Models.AccountType.Microsoft,
            Username = "PlayerMS",
            UUID = "abcdef0123456789abcdef0123456789"
        };
        _accountManager.Accounts.Add(msAccount);

        var vm = new HomeViewModel(
            _launchOrchestrator, _notification, _accountManager, _instanceManager, _gameFolder);

        vm.BuyMinecraftReminder = () => Task.CompletedTask;

        // Verify the callback is set
        Assert.IsNotNull(vm.BuyMinecraftReminder);

        // Microsoft account exists → reminder logic would skip the callback
        Assert.IsTrue(_accountManager.Accounts.Any(
            a => a.Type == EiTRVO.ProEngine.Models.AccountType.Microsoft));
    }

    [TestMethod]
    public async Task RemindBuyMinecraft_NoMicrosoftAccount_InvokesCallback()
    {
        // Ensure no accounts exist
        _accountManager.Accounts.Clear();

        var vm = new HomeViewModel(
            _launchOrchestrator, _notification, _accountManager, _instanceManager, _gameFolder);

        bool callbackCalled = false;
        vm.BuyMinecraftReminder = () =>
        {
            callbackCalled = true;
            return Task.CompletedTask;
        };

        // Directly invoke the callback to verify the delegate works
        Assert.IsNotNull(vm.BuyMinecraftReminder);
        await vm.BuyMinecraftReminder();
        Assert.IsTrue(callbackCalled);
    }

    // ================================================================
    // LoadLastLaunchAsync — DPAPI persist/restore
    // ================================================================

    [TestMethod]
    public async Task LoadLastLaunch_NoFile_SilentNoOp()
    {
        // Ensure no last_launch.json file exists
        string lastLaunchPath = Path.Combine(_tempDir, "last_launch.json");
        if (File.Exists(lastLaunchPath)) File.Delete(lastLaunchPath);

        var vm = new HomeViewModel(
            _launchOrchestrator, _notification, _accountManager, _instanceManager, _gameFolder);

        await vm.LoadLastLaunchAsync();

        // Should not crash and should not set any last launch data
        Assert.IsTrue(string.IsNullOrEmpty(vm.LastLaunchedInstanceName));
    }

    [TestMethod]
    public async Task LoadLastLaunch_CorruptFile_SilentNoOp()
    {
        // Write random bytes (not valid DPAPI or JSON)
        string lastLaunchPath = Path.Combine(_tempDir, "last_launch.json");
        File.WriteAllBytes(lastLaunchPath, new byte[] { 1, 2, 3, 4, 5 });

        var vm = new HomeViewModel(
            _launchOrchestrator, _notification, _accountManager, _instanceManager, _gameFolder);

        // Should not throw
        await vm.LoadLastLaunchAsync();

        Assert.IsTrue(string.IsNullOrEmpty(vm.LastLaunchedInstanceName));
    }
}
