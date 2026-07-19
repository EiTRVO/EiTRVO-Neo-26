using System.Net;
using EiTRVO.ProEngine.Services;
using EiTRVO.ProEngine.Services.Loaders;
using EiTRVO.Tests.Fakes;

namespace EiTRVO.Tests.Loaders;

[TestClass]
public class FabricInstallerTests
{
    // ================================================================
    // GetVersionsAsync
    // ================================================================

    [TestMethod]
    public async Task GetVersions_Success_ReturnsList()
    {
        var json = @"[
            {""loader"":{""version"":""0.16.10"",""stable"":true},""intermediary"":{""version"":""1.21"",""stable"":true}},
            {""loader"":{""version"":""0.16.9"",""stable"":false},""intermediary"":{""version"":""1.21"",""stable"":true}}
        ]";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, json);
        var http = new HttpClient(handler);

        var versions = await FabricInstaller.GetVersionsAsync(http, "1.21");

        Assert.IsTrue(versions.Count >= 1);
        Assert.IsTrue(versions.All(v => v.LoaderType == "Fabric"));
        Assert.IsTrue(versions.All(v => v.MinecraftVersion == "1.21"));
        // First version should be 0.16.10
        var first = versions.FirstOrDefault(v => v.LoaderVersion == "0.16.10");
        Assert.IsNotNull(first);
        Assert.IsTrue(first!.IsRecommended);
    }

    [TestMethod]
    public async Task GetVersions_NetworkError_Throws()
    {
        var handler = new FakeHttpMessageHandler(new HttpRequestException("Network error"));
        var http = new HttpClient(handler);

        await Assert.ThrowsExceptionAsync<HttpRequestException>(
            () => FabricInstaller.GetVersionsAsync(http, "1.21"));
    }

    [TestMethod]
    public async Task GetVersions_FiltersOldVersions()
    {
        // Versions < 0.10 should be filtered (IsModernFabric check)
        var json = @"[
            {""loader"":{""version"":""0.9.2"",""stable"":true},""intermediary"":{""version"":""1.21"",""stable"":true}},
            {""loader"":{""version"":""0.16.10"",""stable"":true},""intermediary"":{""version"":""1.21"",""stable"":true}}
        ]";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, json);
        var http = new HttpClient(handler);

        var versions = await FabricInstaller.GetVersionsAsync(http, "1.21");

        // 0.9.2 should be filtered out (< 0.10)
        Assert.IsFalse(versions.Any(v => v.LoaderVersion == "0.9.2"));
        Assert.IsTrue(versions.Any(v => v.LoaderVersion == "0.16.10"));
    }

    // ================================================================
    // MavenNameToPath (via ModLoaderService, already tested but extra coverage)
    // ================================================================

    [TestMethod]
    public void MavenNameToPath_StandardThreePart()
    {
        var path = ModLoaderService.MavenNameToPath("net.fabricmc:fabric-loader:0.16.10");
        Assert.AreEqual("net/fabricmc/fabric-loader/0.16.10/fabric-loader-0.16.10.jar", path);
    }

    [TestMethod]
    public void MavenNameToPath_IgnoresExtraParts()
    {
        // MavenNameToPath only uses parts[0..2] (group, artifact, version).
        // Extra parts like classifiers are not included in the path.
        var path = ModLoaderService.MavenNameToPath("com.example:lib:1.0:sources");
        Assert.AreEqual("com/example/lib/1.0/lib-1.0.jar", path);
    }
}
