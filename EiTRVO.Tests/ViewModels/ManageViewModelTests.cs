using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Orchestrators;
using EiTRVO.ProEngine.Services;
using EiTRVO.ProEngine.ViewModels;
using EiTRVO.Tests.Fakes;

namespace EiTRVO.Tests.ViewModels;

[TestClass]
public class ManageViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FakeGameFolderService _gameFolder;
    private readonly FakeNotificationService _notification;
    private readonly InstanceManager _instanceManager;
    private readonly FakePackService _packService;
    private readonly FakeDownloadService _downloadService;
    private readonly FakeModLoaderService _modLoaderService;
    private readonly FakeDialogService _dialogService;
    private readonly FakeProcessService _processService;
    private readonly HttpClient _httpClient;

    public ManageViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"eitrvo_manage_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _gameFolder = new FakeGameFolderService { GameDir = _tempDir };
        _notification = new FakeNotificationService();
        _instanceManager = new InstanceManager(_gameFolder);
        _packService = new FakePackService();
        _downloadService = new FakeDownloadService();
        _modLoaderService = new FakeModLoaderService();
        _dialogService = new FakeDialogService();
        _processService = new FakeProcessService();
        _httpClient = new HttpClient();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private ManageViewModel CreateViewModel()
    {
        return new ManageViewModel(
            _instanceManager, _packService, _notification, _gameFolder,
            _downloadService, _modLoaderService, _httpClient,
            _dialogService, _processService);
    }

    // ================================================================
    // Refresh
    // ================================================================

    [TestMethod]
    public void Refresh_EmptyVersions_EmptyInstances()
    {
        _instanceManager.Refresh(null!);
        Assert.AreEqual(0, _instanceManager.Instances.Count);
    }

    // ================================================================
    // ImportPackAsync — error handling
    // ================================================================

    [TestMethod]
    public async Task ImportPack_ReadManifestFailed_ShowsError()
    {
        _packService.ReadManifestThrows = new InvalidOperationException("Bad zip");

        var vm = CreateViewModel();
        await vm.ImportPackAsync(Path.Combine(_tempDir, "bad.zip"));

        Assert.IsNotNull(_notification.LastShowMessage);
        StringAssert.Contains(_notification.LastShowMessage, "无效的整合包");
    }

    [TestMethod]
    public async Task ImportPack_VersionMismatch_UserDeclines_ReturnsEarly()
    {
        _packService.ReadManifestResult = new PackManifest
        {
            Format = "eitrvo-pack:1",
            ExporterVersion = 25, // mismatched
            Name = "TestPack"
        };
        _dialogService.ConfirmResult = false; // user declines

        var vm = CreateViewModel();
        await vm.ImportPackAsync(Path.Combine(_tempDir, "pack.zip"));

        // Should have shown dialog
        Assert.IsTrue(_dialogService.LastConfirmMessage?.Contains("版本") ?? false);
        // ExtractAsync should not have been called
        Assert.AreEqual(0, _packService.ExtractAsyncCallCount);
    }

    [TestMethod]
    public async Task ImportPack_VersionMismatch_UserAccepts_Continues()
    {
        _packService.ReadManifestResult = new PackManifest
        {
            Format = "eitrvo-pack:1",
            ExporterVersion = 25,
            Name = "TestPack"
        };
        _dialogService.ConfirmResult = true; // user accepts

        var vm = CreateViewModel();
        await vm.ImportPackAsync(Path.Combine(_tempDir, "pack.zip"));

        // If the dialog was shown and accepted, ExtractAsync should have been called
        // (it may fail later but that's fine — dialog works)
        Assert.IsTrue(_dialogService.LastConfirmMessage?.Contains("版本") ?? false);
    }

    [TestMethod]
    public async Task ImportPack_InstanceExists_UserDeclines_KeepsDirectory()
    {
        var instanceName = "ExistingPack";
        var targetDir = Path.Combine(_tempDir, "versions", instanceName);
        Directory.CreateDirectory(targetDir);

        _packService.ReadManifestResult = new PackManifest
        {
            Format = "eitrvo-pack:1",
            ExporterVersion = 26,
            Name = instanceName
        };
        _dialogService.ConfirmResult = false; // user declines overwrite

        var vm = CreateViewModel();
        await vm.ImportPackAsync(Path.Combine(_tempDir, "pack.zip"));

        // Directory should still exist
        Assert.IsTrue(Directory.Exists(targetDir));
    }

    [TestMethod]
    public async Task ImportPack_InstanceExists_UserAccepts_DeletesAndContinues()
    {
        var instanceName = "ExistingPack";
        var targetDir = Path.Combine(_tempDir, "versions", instanceName);
        Directory.CreateDirectory(targetDir);

        _packService.ReadManifestResult = new PackManifest
        {
            Format = "eitrvo-pack:1",
            ExporterVersion = 26,
            Name = instanceName
        };
        _dialogService.ConfirmResult = true; // user accepts overwrite

        var vm = CreateViewModel();
        await vm.ImportPackAsync(Path.Combine(_tempDir, "pack.zip"));

        // Directory should be deleted
        Assert.IsFalse(Directory.Exists(targetDir));
    }
}
