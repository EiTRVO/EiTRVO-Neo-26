using System.IO.Compression;
using System.Text.Json;
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
    private readonly FakeMrpackInstallService _mrpackInstall;
    private readonly JavaDetectionService _javaDetection;
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
        _mrpackInstall = new FakeMrpackInstallService();
        _javaDetection = new JavaDetectionService();
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
            _dialogService, _processService, _mrpackInstall,
            _javaDetection);
    }

    /// <summary>Create a minimal valid zip file that passes the safety scanner.</summary>
    private string CreateSafeZipFile(string instanceFolder = "TestPack")
    {
        string zipPath = Path.Combine(_tempDir, $"safe_{Guid.NewGuid():N}.zip");
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        // Add a safe manifest entry and version.json so the scanner doesn't block
        var manifestEntry = zip.CreateEntry("manifest.json");
        using (var s = manifestEntry.Open())
            s.Write(new byte[] { 0x7B, 0x7D }, 0, 2); // "{}"

        var versionEntry = zip.CreateEntry($"{instanceFolder}/version.json");
        using (var s = versionEntry.Open())
            s.Write(new byte[] { 0x7B, 0x7D }, 0, 2); // "{}"
        return zipPath;
    }

    /// <summary>Create a minimal valid .mrpack file that passes the safety scanner.</summary>
    private string CreateSafeMrpackFile()
    {
        string path = Path.Combine(_tempDir, $"safe_{Guid.NewGuid():N}.mrpack");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        var manifest = new ModpackManifest
        {
            FormatVersion = 1,
            Game = "minecraft",
            Name = "SafePack",
            VersionId = "1.0",
            Dependencies = new Dictionary<string, string> { ["minecraft"] = "1.21" }
        };
        var entry = zip.CreateEntry("modrinth.index.json");
        using var s = entry.Open();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest);
        s.Write(bytes, 0, bytes.Length);
        return path;
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
        string zipPath = CreateSafeZipFile();

        var vm = CreateViewModel();
        await vm.ImportPackAsync(zipPath);

        Assert.IsNotNull(_notification.LastShowMessage);
        StringAssert.Contains(_notification.LastShowMessage, "无效的整合包");
    }

    [TestMethod]
    public async Task ImportPack_VersionMismatch_UserDeclines_ReturnsEarly()
    {
        _packService.ReadManifestResult = new PackManifest
        {
            Format = "eitrvo-pack:1",
            ExporterVersion = 25,
            Name = "TestPack"
        };
        _dialogService.ConfirmResult = false;
        string zipPath = CreateSafeZipFile();

        var vm = CreateViewModel();
        await vm.ImportPackAsync(zipPath);

        Assert.IsTrue(_dialogService.LastConfirmMessage?.Contains("版本") ?? false);
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
        _packService.PeekVersionJsonResult = new VersionDetail
        {
            Id = "1.21",
            MainClass = "net.minecraft.client.main.Main"
        };
        _dialogService.ConfirmResult = true;
        string zipPath = CreateSafeZipFile();

        var vm = CreateViewModel();
        await vm.ImportPackAsync(zipPath);

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
        _dialogService.ConfirmResult = false;
        string zipPath = CreateSafeZipFile(instanceName);

        var vm = CreateViewModel();
        await vm.ImportPackAsync(zipPath);

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
        _dialogService.ConfirmResult = true;
        string zipPath = CreateSafeZipFile(instanceName);

        var vm = CreateViewModel();
        await vm.ImportPackAsync(zipPath);

        Assert.IsFalse(Directory.Exists(targetDir));
    }

    // ================================================================
    // ImportPackAsync — mainClass security check
    // ================================================================

    [TestMethod]
    public async Task ImportPack_UnknownMainClass_ShowsWarningAndDeclines()
    {
        _packService.ReadManifestResult = new PackManifest
        {
            Format = "eitrvo-pack:1",
            ExporterVersion = 26,
            Name = "TestPack"
        };
        _packService.PeekVersionJsonResult = new VersionDetail
        {
            Id = "1.21",
            MainClass = "com.evil.Hack"
        };
        _dialogService.ConfirmResult = false;
        string zipPath = CreateSafeZipFile();

        var vm = CreateViewModel();
        await vm.ImportPackAsync(zipPath);

        Assert.AreEqual("安全警告", _dialogService.LastConfirmTitle);
        StringAssert.Contains(_dialogService.LastConfirmMessage, "com.evil.Hack");
        StringAssert.Contains(_dialogService.LastConfirmMessage, "恶意代码执行风险");
        Assert.AreEqual(0, _packService.ExtractAsyncCallCount);
    }

    [TestMethod]
    public async Task ImportPack_BlockedMainClass_HardReject()
    {
        _packService.ReadManifestResult = new PackManifest
        {
            Format = "eitrvo-pack:1",
            ExporterVersion = 26,
            Name = "TestPack"
        };
        _packService.PeekVersionJsonResult = new VersionDetail
        {
            Id = "1.21",
            MainClass = "java.lang.Runtime"
        };
        string zipPath = CreateSafeZipFile();

        var vm = CreateViewModel();
        await vm.ImportPackAsync(zipPath);

        Assert.AreEqual(NotificationType.Error, _notification.LastShowType);
        StringAssert.Contains(_notification.LastShowMessage, "已拒绝导入");
        Assert.AreEqual(0, _packService.ExtractAsyncCallCount);
    }

    [TestMethod]
    public async Task ImportPack_MissingMainClass_ShowsWarning()
    {
        _packService.ReadManifestResult = new PackManifest
        {
            Format = "eitrvo-pack:1",
            ExporterVersion = 26,
            Name = "TestPack"
        };
        _packService.PeekVersionJsonResult = new VersionDetail
        {
            Id = "1.21",
            MainClass = null
        };
        _dialogService.ConfirmResult = false;
        string zipPath = CreateSafeZipFile();

        var vm = CreateViewModel();
        await vm.ImportPackAsync(zipPath);

        Assert.AreEqual("安全警告", _dialogService.LastConfirmTitle);
        StringAssert.Contains(_dialogService.LastConfirmMessage, "未指定 mainClass");
        Assert.AreEqual(0, _packService.ExtractAsyncCallCount);
    }

    [TestMethod]
    public async Task ImportPack_KnownMainClass_NoWarning()
    {
        _packService.ReadManifestResult = new PackManifest
        {
            Format = "eitrvo-pack:1",
            ExporterVersion = 26,
            Name = "TestPack"
        };
        _packService.PeekVersionJsonResult = new VersionDetail
        {
            Id = "1.21",
            MainClass = "net.minecraft.client.main.Main"
        };
        string zipPath = CreateSafeZipFile();

        var vm = CreateViewModel();
        await vm.ImportPackAsync(zipPath);

        // The "安全警告" dialog should NOT have been shown for mainClass
        Assert.AreNotEqual("安全警告", _dialogService.LastConfirmTitle);
        Assert.AreEqual(1, _packService.ExtractAsyncCallCount);
    }

    // ================================================================
    // ImportPackAsync — .mrpack format routing
    // ================================================================

    [TestMethod]
    public async Task ImportPack_MrpackExtension_RoutesToMrpackInstaller()
    {
        _mrpackInstall.ParseResult = new MrpackInfo
        {
            Manifest = new ModpackManifest
            {
                FormatVersion = 1,
                Game = "minecraft",
                Name = "TestMrpack",
                VersionId = "1.0",
                Dependencies = new Dictionary<string, string> { ["minecraft"] = "1.21" }
            },
            McVersion = "1.21"
        };
        string mrpackPath = CreateSafeMrpackFile();

        var vm = CreateViewModel();
        await vm.ImportPackAsync(mrpackPath);

        Assert.IsTrue(_mrpackInstall.InstallCalled,
            "InstallMrpackAsync should have been called for .mrpack file.");
        Assert.AreEqual(0, _packService.ExtractAsyncCallCount);
    }

    [TestMethod]
    public async Task ImportMrpack_InvalidManifest_ShowsError()
    {
        _mrpackInstall.ParseThrows = new InvalidDataException("Bad manifest");
        string mrpackPath = CreateSafeMrpackFile();

        var vm = CreateViewModel();
        await vm.ImportPackAsync(mrpackPath);

        Assert.IsNotNull(_notification.LastShowMessage);
        StringAssert.Contains(_notification.LastShowMessage, "无效的整合包");
    }

    [TestMethod]
    public async Task ImportMrpack_NoMcVersion_ShowsError()
    {
        _mrpackInstall.ParseResult = new MrpackInfo
        {
            Manifest = new ModpackManifest
            {
                FormatVersion = 1,
                Game = "minecraft",
                Name = "Test",
                VersionId = "1.0",
                Dependencies = new Dictionary<string, string>()
            },
            McVersion = ""
        };
        string mrpackPath = CreateSafeMrpackFile();

        var vm = CreateViewModel();
        await vm.ImportPackAsync(mrpackPath);

        Assert.IsNotNull(_notification.LastShowMessage);
        StringAssert.Contains(_notification.LastShowMessage, "Minecraft 版本");
    }

    // ================================================================
    // ImportZipPack — ModpackSafetyScanner integration
    // ================================================================

    [TestMethod]
    public async Task ImportZipPack_DangerousFile_BlockedByScanner()
    {
        string zipPath = Path.Combine(_tempDir, "dangerous.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("TestPack/virus.exe");
            using var s = entry.Open();
            s.Write(new byte[] { 0x01 }, 0, 1);
        }

        var vm = CreateViewModel();
        await vm.ImportPackAsync(zipPath);

        Assert.AreEqual(NotificationType.Error, _notification.LastShowType);
        Assert.IsNotNull(_notification.LastShowMessage);
        StringAssert.Contains(_notification.LastShowMessage, "安全检查");
    }
}
