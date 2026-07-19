using System.Net;
using EiTRVO.ProEngine.Services.Loaders;
using EiTRVO.Tests.Fakes;

namespace EiTRVO.Tests.Loaders;

[TestClass]
public class QuiltInstallerTests
{
    // ================================================================
    // GetVersionsAsync
    // ================================================================

    [TestMethod]
    public async Task GetVersions_Success_ReturnsList()
    {
        var json = @"[
            {""loader"":{""version"":""1.0.0"",""stable"":true},""hashed"":{""version"":""1.21"",""stable"":true}},
            {""loader"":{""version"":""0.26.3"",""stable"":false},""hashed"":{""version"":""1.21"",""stable"":true}}
        ]";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, json);
        var http = new HttpClient(handler);

        var versions = await QuiltInstaller.GetVersionsAsync(http, "1.21");

        Assert.IsTrue(versions.Count >= 1);
        Assert.IsTrue(versions.All(v => v.LoaderType == "Quilt"));
        Assert.IsTrue(versions.All(v => v.MinecraftVersion == "1.21"));
    }

    [TestMethod]
    public async Task GetVersions_EmptyResult_EmptyList()
    {
        var json = @"[]";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, json);
        var http = new HttpClient(handler);

        var versions = await QuiltInstaller.GetVersionsAsync(http, "1.21");

        Assert.AreEqual(0, versions.Count);
    }

    [TestMethod]
    public async Task GetVersions_NetworkError_Throws()
    {
        var handler = new FakeHttpMessageHandler(new HttpRequestException("Connection refused"));
        var http = new HttpClient(handler);

        await Assert.ThrowsExceptionAsync<HttpRequestException>(
            () => QuiltInstaller.GetVersionsAsync(http, "1.21"));
    }

    [TestMethod]
    public async Task GetVersions_FiltersNullLoaderEntries()
    {
        // Entries with null loader.version should be filtered out
        var json = @"[
            {""hashed"":{""version"":""1.21"",""stable"":true}},
            {""loader"":{""version"":""1.0.0"",""stable"":true},""hashed"":{""version"":""1.21"",""stable"":true}}
        ]";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, json);
        var http = new HttpClient(handler);

        var versions = await QuiltInstaller.GetVersionsAsync(http, "1.21");

        // Only the entry with loader.version should be included
        Assert.AreEqual(1, versions.Count);
        Assert.AreEqual("1.0.0", versions[0].LoaderVersion);
    }

    [TestMethod]
    public async Task GetVersions_SetsRecommendedFlag()
    {
        var json = @"[
            {""loader"":{""version"":""1.0.0"",""stable"":true},""hashed"":{""version"":""1.21"",""stable"":true}}
        ]";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, json);
        var http = new HttpClient(handler);

        var versions = await QuiltInstaller.GetVersionsAsync(http, "1.21");

        Assert.AreEqual(1, versions.Count);
        Assert.IsTrue(versions[0].IsRecommended); // stable=true → IsRecommended=true
    }
}
