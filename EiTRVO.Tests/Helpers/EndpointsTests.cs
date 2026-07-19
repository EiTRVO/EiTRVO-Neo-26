using EiTRVO.ProEngine.Helpers;

namespace EiTRVO.Tests.Helpers;

[TestClass]

public class EndpointsTests
{
    [TestMethod]
    public void VersionManifestUrl_IsCorrect()
    {
        Assert.AreEqual("https://launchermeta.mojang.com/mc/game/version_manifest.json",
            Endpoints.VersionManifestUrl);
    }

    [TestMethod]
    public void AssetDownload_FormatsCorrectly()
    {
        string result = Endpoints.AssetDownload("abcdef1234567890");
        StringAssert.StartsWith(result, "https://resources.download.minecraft.net/ab/");
        StringAssert.EndsWith(result, "abcdef1234567890");
    }

    [TestMethod]
    public void AssetDownload_ShortHash_StillWorks()
    {
        string result = Endpoints.AssetDownload("ab");
        StringAssert.Contains(result, "/ab/ab");
    }

    [TestMethod]
    public void MicrosoftEndpoints_AreHttps()
    {
        StringAssert.StartsWith(Endpoints.MicrosoftDeviceCode, "https://");
        StringAssert.StartsWith(Endpoints.MicrosoftToken, "https://");
    }

    [TestMethod]
    public void XboxEndpoints_AreHttps()
    {
        StringAssert.StartsWith(Endpoints.XboxLiveAuth, "https://");
        StringAssert.StartsWith(Endpoints.XstsAuth, "https://");
    }

    [TestMethod]
    public void MinecraftEndpoints_AreHttps()
    {
        StringAssert.StartsWith(Endpoints.MinecraftAuth, "https://");
        StringAssert.StartsWith(Endpoints.MinecraftProfile, "https://");
    }
}
