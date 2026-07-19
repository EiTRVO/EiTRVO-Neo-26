using EiTRVO.ProEngine.ViewModels;
using EiTRVO.Tests.Fakes;

namespace EiTRVO.Tests.ViewModels;

[TestClass]
public class ResourcePackViewModelTests
{
    [TestMethod]
    public void Constructor_CollectionsAreNotNull()
    {
        var vm = new ResourcePackViewModel(
            new FakeNotificationService(), new FakeDialogService(), new FakeModrinthService());

        Assert.IsNotNull(vm.ResourcePacks);
        Assert.IsNotNull(vm.ShaderPacks);
    }

    [TestMethod]
    public void Constructor_SetsDefaults()
    {
        var vm = new ResourcePackViewModel(
            new FakeNotificationService(), new FakeDialogService(), new FakeModrinthService());

        Assert.IsNotNull(vm.SelectedTab);
        Assert.IsNotNull(vm.InstanceName);
    }
}
