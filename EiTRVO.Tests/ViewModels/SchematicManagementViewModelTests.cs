using EiTRVO.ProEngine.ViewModels;
using EiTRVO.Tests.Fakes;

namespace EiTRVO.Tests.ViewModels;

[TestClass]
public class SchematicManagementViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FakeNotificationService _notification;
    private readonly FakeDialogService _dialog;

    public SchematicManagementViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"eitrvo_schem_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _notification = new FakeNotificationService();
        _dialog = new FakeDialogService();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [TestMethod]
    public void Constructor_InitializesEmpty()
    {
        var vm = new SchematicManagementViewModel(_notification, _dialog);

        Assert.IsNotNull(vm.Schematics);
        Assert.AreEqual(0, vm.Schematics.Count);
        Assert.IsTrue(vm.IsEmpty);
    }

    [TestMethod]
    public void LoadSchematics_FindsFiles()
    {
        File.WriteAllText(Path.Combine(_tempDir, "my_build.litematic"), "schematic data");
        File.WriteAllText(Path.Combine(_tempDir, "my_build.schematic"), "schematic data");
        File.WriteAllText(Path.Combine(_tempDir, "notes.txt"), "not a schematic");

        var vm = new SchematicManagementViewModel(_notification, _dialog);
        vm.LoadSchematics("TestInstance", _tempDir);

        Assert.AreEqual(2, vm.Schematics.Count);
        Assert.IsFalse(vm.IsEmpty);
    }

    [TestMethod]
    public void LoadSchematics_EmptyDirectory_IsEmpty()
    {
        var vm = new SchematicManagementViewModel(_notification, _dialog);
        vm.LoadSchematics("TestInstance", _tempDir);

        Assert.AreEqual(0, vm.Schematics.Count);
        Assert.IsTrue(vm.IsEmpty);
    }
}
