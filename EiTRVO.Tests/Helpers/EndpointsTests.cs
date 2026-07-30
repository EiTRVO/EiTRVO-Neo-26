using EiTRVO.ProEngine.Helpers;

namespace EiTRVO.Tests.Helpers;

[TestClass]
public class EndpointsTests
{
    [TestMethod]
    public void ModrinthVersionFiles_IsCorrectUrl()
    {
        Assert.AreEqual("https://api.modrinth.com/v2/version_files", Endpoints.ModrinthVersionFiles);
    }

    [TestMethod]
    public void ModrinthProjects_FormatsCorrectly()
    {
        var url = Endpoints.ModrinthProjects("abc,def");
        StringAssert.Contains(url, "api.modrinth.com/v2/projects");
        StringAssert.Contains(url, "ids=");
    }

    [TestMethod]
    public void ModrinthApi_IsHttps()
    {
        StringAssert.StartsWith(Endpoints.ModrinthApi, "https://");
        StringAssert.StartsWith(Endpoints.ModrinthVersionFiles, "https://");
    }

    [TestMethod]
    public void AssetDownload_ValidHash_ReturnsCorrectUrl()
    {
        string hash = "a1b2c3d4e5f67890123456789012345678901234";
        var url = Endpoints.AssetDownload(hash);
        StringAssert.StartsWith(url, "https://resources.download.minecraft.net/");
        StringAssert.Contains(url, hash);
    }

    [TestMethod]
    public void AssetDownload_InvalidHash_Throws()
    {
        Assert.ThrowsException<ArgumentException>(() => Endpoints.AssetDownload("abc"));
        Assert.ThrowsException<ArgumentNullException>(() => Endpoints.AssetDownload(null!));
    }
}
