using EiTRVO.ProEngine.ViewModels;

namespace EiTRVO.Tests.ViewModels;

[TestClass]
public class AboutViewModelTests
{
    [TestMethod]
    public void Properties_AllReturnNonNullOrEmpty()
    {
        var vm = new AboutViewModel();

        Assert.IsNotNull(vm.Version);
        Assert.IsTrue(vm.Version.Length > 0);
    }

    [TestMethod]
    public void Version_IsNonEmpty()
    {
        var vm = new AboutViewModel();
        Assert.IsTrue(vm.Version.Length > 0);
    }
}
