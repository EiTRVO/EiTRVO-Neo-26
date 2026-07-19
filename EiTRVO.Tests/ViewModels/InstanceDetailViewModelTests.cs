using EiTRVO.ProEngine.Orchestrators;
using EiTRVO.ProEngine.Services;
using EiTRVO.ProEngine.ViewModels;
using EiTRVO.Tests.Fakes;

namespace EiTRVO.Tests.ViewModels;

[TestClass]
public class InstanceDetailViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FakeGameFolderService _gameFolder;
    private readonly FakeNotificationService _notification;
    private readonly FakeDialogService _dialog;
    private readonly SaveLockService _saveLockService;
    private readonly InstanceManager _instanceManager;

    public InstanceDetailViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"eitrvo_inst_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _gameFolder = new FakeGameFolderService { GameDir = _tempDir };
        _notification = new FakeNotificationService();
        _dialog = new FakeDialogService();
        _saveLockService = new SaveLockService();
        _instanceManager = new InstanceManager(_gameFolder);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [TestMethod]
    public void Constructor_InitializesEmpty()
    {
        var vm = new InstanceDetailViewModel(
            _instanceManager, _gameFolder, _notification, _dialog, _saveLockService);

        Assert.IsNotNull(vm.SaveEntries);
        Assert.AreEqual(0, vm.SaveEntries.Count);
    }

    [TestMethod]
    public void LoadInstance_InvalidInstance_DoesNotThrow()
    {
        var vm = new InstanceDetailViewModel(
            _instanceManager, _gameFolder, _notification, _dialog, _saveLockService);

        // LoadInstance with non-existent instance should not throw
        vm.LoadInstance("non_existent_instance");
        // VMs handle missing data gracefully
    }

    [TestMethod]
    public void ToSettings_SaveLockCount_DefaultZero()
    {
        var vm = new InstanceDetailViewModel(
            _instanceManager, _gameFolder, _notification, _dialog, _saveLockService);

        Assert.AreEqual(0, vm.SaveLockCount);
        Assert.IsFalse(vm.HasSaveLockEntries);
    }
}
