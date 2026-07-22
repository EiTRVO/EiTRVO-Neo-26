using EiTRVO.ProEngine.Helpers;

namespace EiTRVO.Tests.Helpers;

[TestClass]
public class DownloadSafetyHelperTests
{
    // ================================================================
    // HTTPS enforcement
    // ================================================================

    [TestMethod]
    public void IsDownloadUrlAllowed_HttpsModrinthCdn_ReturnsTrue()
    {
        Assert.IsTrue(DownloadSafetyHelper.IsDownloadUrlAllowed(
            "https://cdn.modrinth.com/data/abc123/versions/1.0.0/mod.jar"));
    }

    [TestMethod]
    public void IsDownloadUrlAllowed_HttpsMojangCdn_ReturnsTrue()
    {
        Assert.IsTrue(DownloadSafetyHelper.IsDownloadUrlAllowed(
            "https://launchermeta.mojang.com/mc/game/version_manifest.json"));
    }

    [TestMethod]
    public void IsDownloadUrlAllowed_HttpsMavenRepo_ReturnsTrue()
    {
        Assert.IsTrue(DownloadSafetyHelper.IsDownloadUrlAllowed(
            "https://repo1.maven.org/maven2/com/example/lib.jar"));
    }

    [TestMethod]
    public void IsDownloadUrlAllowed_HttpScheme_ReturnsFalse()
    {
        Assert.IsFalse(DownloadSafetyHelper.IsDownloadUrlAllowed(
            "http://cdn.modrinth.com/data/abc123/mod.jar"));
    }

    [TestMethod]
    public void IsDownloadUrlAllowed_NonWhitelistedDomain_ReturnsFalse()
    {
        Assert.IsFalse(DownloadSafetyHelper.IsDownloadUrlAllowed(
            "https://evil.example.com/malware.exe"));
    }

    [TestMethod]
    public void IsDownloadUrlAllowed_FtpScheme_ReturnsFalse()
    {
        Assert.IsFalse(DownloadSafetyHelper.IsDownloadUrlAllowed(
            "ftp://cdn.modrinth.com/data/mod.jar"));
    }

    [TestMethod]
    public void IsDownloadUrlAllowed_Empty_ReturnsFalse()
    {
        Assert.IsFalse(DownloadSafetyHelper.IsDownloadUrlAllowed(""));
    }

    [TestMethod]
    public void IsDownloadUrlAllowed_Null_ReturnsFalse()
    {
        Assert.IsFalse(DownloadSafetyHelper.IsDownloadUrlAllowed(null));
    }
}
