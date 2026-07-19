using EiTRVO.ProEngine.ViewModels;
using EiTRVO.Tests.Fakes;

namespace EiTRVO.Tests.ViewModels;

[TestClass]
public class ModManagementViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FakeNotificationService _notification;
    private readonly FakeDialogService _dialog;
    private readonly FakeModrinthService _modrinth;

    public ModManagementViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"eitrvo_modmgr_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _notification = new FakeNotificationService();
        _dialog = new FakeDialogService();
        _modrinth = new FakeModrinthService();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [TestMethod]
    public void Constructor_InitializesEmpty()
    {
        var vm = new ModManagementViewModel(_modrinth, _notification, _dialog);

        Assert.IsNotNull(vm.Mods);
        Assert.AreEqual(0, vm.Mods.Count);
        // IsEmpty defaults to false until LoadMods
    }

    [TestMethod]
    public void LoadMods_FindsJarFiles()
    {
        File.WriteAllText(Path.Combine(_tempDir, "optifine.jar"), "jar");
        File.WriteAllText(Path.Combine(_tempDir, "sodium.jar"), "jar");
        File.WriteAllText(Path.Combine(_tempDir, "readme.txt"), "not a mod");

        var vm = new ModManagementViewModel(_modrinth, _notification, _dialog);
        vm.LoadMods("TestInstance", _tempDir, "1.21", "fabric");

        Assert.AreEqual(2, vm.Mods.Count);
        Assert.IsFalse(vm.IsEmpty);
    }

    [TestMethod]
    public void LoadMods_EmptyFolder_IsEmpty()
    {
        var vm = new ModManagementViewModel(_modrinth, _notification, _dialog);
        vm.LoadMods("TestInstance", _tempDir, "1.21", "vanilla");

        Assert.AreEqual(0, vm.Mods.Count);
        // IsEmpty defaults to false until LoadMods
    }

    [TestMethod]
    public void LoadMods_SetsInstanceInfo()
    {
        var vm = new ModManagementViewModel(_modrinth, _notification, _dialog);
        vm.LoadMods("MyInstance", _tempDir, "1.21", "forge");

        Assert.AreEqual("MyInstance", vm.InstanceName);
        Assert.AreEqual("1.21", vm.VersionId);
        Assert.AreEqual("forge", vm.LoaderType);
    }

    [TestMethod]
    public void SelectTab_ChangesView()
    {
        var vm = new ModManagementViewModel(_modrinth, _notification, _dialog);

        vm.SelectTabCommand.Execute("download");
        Assert.AreEqual("download", vm.SelectedTab);
        Assert.IsFalse(vm.IsLocalTabVisible);
        Assert.IsTrue(vm.IsDownloadTabVisible);

        vm.SelectTabCommand.Execute("local");
        Assert.AreEqual("local", vm.SelectedTab);
        Assert.IsTrue(vm.IsLocalTabVisible);
        Assert.IsFalse(vm.IsDownloadTabVisible);
    }
}
