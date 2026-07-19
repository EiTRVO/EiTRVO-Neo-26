using System.Net;
using EiTRVO.ProEngine.Services.Loaders;
using EiTRVO.Tests.Fakes;

namespace EiTRVO.Tests.Loaders;

[TestClass]
public class OptiFineInstallerTests
{
    // ================================================================
    // GetVersionsAsync
    // ================================================================

    [TestMethod]
    public async Task GetVersions_ParsesDownloadLinks()
    {
        var html = @"<html><body>
            <a href=""http://optifine.net/adloadx?f=OptiFine_1.21_HD_U_J1.jar"">Download</a>
            <a href=""http://optifine.net/adloadx?f=OptiFine_1.21_HD_U_J2.jar"">Download</a>
            </body></html>";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, html);
        var http = new HttpClient(handler);

        var versions = await OptiFineInstaller.GetVersionsAsync(http, "1.21");

        Assert.IsTrue(versions.Count >= 1);
        Assert.IsTrue(versions.All(v => v.LoaderType == "OptiFine"));
        Assert.IsTrue(versions.All(v => v.MinecraftVersion == "1.21"));
        // Version IDs should not contain ".jar"
        Assert.IsTrue(versions.All(v => !v.LoaderVersion.EndsWith(".jar")));
    }

    [TestMethod]
    public async Task GetVersions_NoMatches_EmptyList()
    {
        var html = @"<html><body><p>No downloads available</p></body></html>";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, html);
        var http = new HttpClient(handler);

        var versions = await OptiFineInstaller.GetVersionsAsync(http, "1.21");

        Assert.AreEqual(0, versions.Count);
    }

    [TestMethod]
    public async Task GetVersions_Deduplicates()
    {
        // Same jar file linked twice → should be deduplicated
        var html = @"<html><body>
            <a href=""http://optifine.net/adloadx?f=OptiFine_1.21_HD_U_J1.jar"">Mirror 1</a>
            <a href=""http://optifine.net/adloadx?f=OptiFine_1.21_HD_U_J1.jar"">Mirror 2</a>
            </body></html>";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, html);
        var http = new HttpClient(handler);

        var versions = await OptiFineInstaller.GetVersionsAsync(http, "1.21");

        // Should have at most 1 entry for the deduplicated version
        var j1Versions = versions.Where(v => v.LoaderVersion.Contains("OptiFine_1.21_HD_U_J1")).ToList();
        Assert.IsTrue(j1Versions.Count <= 1);
    }

    [TestMethod]
    public async Task GetVersions_FiltersPreviewVersions()
    {
        var html = @"<html><body>
            <a href=""http://optifine.net/adloadx?f=preview_OptiFine_1.21_HD_U_J2_pre1.jar"">Preview</a>
            <a href=""http://optifine.net/adloadx?f=OptiFine_1.21_HD_U_J1.jar"">Stable</a>
            </body></html>";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, html);
        var http = new HttpClient(handler);

        var versions = await OptiFineInstaller.GetVersionsAsync(http, "1.21");

        // Preview versions should be filtered out
        Assert.IsFalse(versions.Any(v => v.LoaderVersion.Contains("preview")));
        // Stable version should be included
        Assert.IsTrue(versions.Any(v => v.LoaderVersion.Contains("OptiFine_1.21_HD_U_J1")));
    }

    [TestMethod]
    public async Task GetVersions_NetworkError_WrapsInException()
    {
        var handler = new FakeHttpMessageHandler(new HttpRequestException("Network error"));
        var http = new HttpClient(handler);

        // OptiFine wraps network errors in its own exception
        await Assert.ThrowsExceptionAsync<Exception>(
            () => OptiFineInstaller.GetVersionsAsync(http, "1.21"));
    }
}
