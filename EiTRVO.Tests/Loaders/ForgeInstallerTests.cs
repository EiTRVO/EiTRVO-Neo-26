using System.Net;
using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Services;
using EiTRVO.ProEngine.Services.Loaders;
using EiTRVO.Tests.Fakes;

namespace EiTRVO.Tests.Loaders;

[TestClass]

public class ForgeInstallerTests
{
    // ================================================================
    // ResolveLibraryUrl
    // ================================================================

    [TestMethod]
    public void ResolveLibraryUrl_ForgeMavenBase()
    {
        var lib = new Library
        {
            Name = "net.minecraftforge:forge:1.8.9-11.15.1.2318-1.8.9",
            Url = "https://maven.minecraftforge.net/"
        };
        var url = ModLoaderService.ResolveLibraryUrl(lib, "net/minecraftforge/forge/1.8.9-11.15.1.2318-1.8.9/forge-1.8.9-11.15.1.2318-1.8.9-universal.jar");
        StringAssert.StartsWith(url, "https://maven.minecraftforge.net/");
    }

    [TestMethod]
    public void ResolveLibraryUrl_ModernDownloadsArtifact()
    {
        var lib = new Library
        {
            Name = "com.example:lib:1.0",
            Downloads = new LibraryDownloads
            {
                Artifact = new DownloadInfo
                {
                    Path = "com/example/lib/1.0/lib-1.0.jar",
                    Url = "https://custom-maven.example.com/com/example/lib/1.0/lib-1.0.jar"
                }
            }
        };
        var url = ModLoaderService.ResolveLibraryUrl(lib, "com/example/lib/1.0/lib-1.0.jar");
        Assert.AreEqual("https://custom-maven.example.com/com/example/lib/1.0/lib-1.0.jar", url);
    }

    [TestMethod]
    public void ResolveLibraryUrl_FallbackToMinecraftLibraries()
    {
        var lib = new Library
        {
            Name = "net.minecraft:launchwrapper:1.12"
            // No Url, no Downloads
        };
        var url = ModLoaderService.ResolveLibraryUrl(lib, "net/minecraft/launchwrapper/1.12/launchwrapper-1.12.jar");
        StringAssert.StartsWith(url, "https://libraries.minecraft.net/");
        StringAssert.EndsWith(url, "net/minecraft/launchwrapper/1.12/launchwrapper-1.12.jar");
    }

    // ================================================================
    // ResolveMavenUrl
    // ================================================================

    [TestMethod]
    public void ResolveMavenUrl_AppendsPathToBase()
    {
        var url = ModLoaderService.ResolveMavenUrl("net/minecraftforge/forge/1.8.9/forge-1.8.9-universal.jar", "https://maven.minecraftforge.net/");
        Assert.AreEqual("https://maven.minecraftforge.net/net/minecraftforge/forge/1.8.9/forge-1.8.9-universal.jar", url);
    }

    [TestMethod]
    public void ResolveMavenUrl_TrimsTrailingSlash()
    {
        var url = ModLoaderService.ResolveMavenUrl("a/b/1.0/b-1.0.jar", "https://example.com/maven");
        Assert.AreEqual("https://example.com/maven/a/b/1.0/b-1.0.jar", url);
    }

    // ================================================================
    // GetVersionsAsync
    // ================================================================

    [TestMethod]
    public async Task GetVersions_ParsesPromotionsJson()
    {
        var json = @"{
            ""promos"": {
                ""1.21-latest"": ""51.0.0"",
                ""1.21-recommended"": ""50.1.0""
            },
            ""homepage"": ""https://minecraftforge.net""
        }";
        var handler = new FakeHttpMessageHandler(System.Net.HttpStatusCode.OK, json);
        var http = new HttpClient(handler);

        var versions = await ForgeInstaller.GetVersionsAsync(http, "1.21");

        Assert.IsTrue(versions.Count >= 1);
        Assert.IsTrue(versions.All(v => v.LoaderType == "Forge"));
        // Recommended version should be present
        var rec = versions.FirstOrDefault(v => v.LoaderVersion == "50.1.0");
        Assert.IsNotNull(rec);
        Assert.IsTrue(rec!.IsRecommended);
    }

    [TestMethod]
    public async Task GetVersions_NoMatchingMcVersion_ReturnsEmpty()
    {
        var json = @"{
            ""promos"": {
                ""1.20-latest"": ""49.0.0"",
                ""1.20-recommended"": ""48.1.0""
            }
        }";
        var handler = new FakeHttpMessageHandler(System.Net.HttpStatusCode.OK, json);
        var http = new HttpClient(handler);

        var versions = await ForgeInstaller.GetVersionsAsync(http, "1.21");

        Assert.AreEqual(0, versions.Count);
    }

    // ================================================================
    // IsTransientError
    // ================================================================

    [TestMethod]
    public void IsTransientError_NullStatusCode_ReturnsTrue()
    {
        // No HTTP response (network-level error) → transient
        var ex = new HttpRequestException("Network error");
        Assert.IsTrue(ModLoaderService.IsTransientError(ex));
    }

    [TestMethod]
    public void IsTransientError_NotFound_ReturnsFalse()
    {
        var ex = new HttpRequestException("Not found", null, System.Net.HttpStatusCode.NotFound);
        Assert.IsFalse(ModLoaderService.IsTransientError(ex));
    }

    [TestMethod]
    public void IsTransientError_ServerError_ReturnsTrue()
    {
        var ex = new HttpRequestException("Server error", null, System.Net.HttpStatusCode.InternalServerError);
        Assert.IsTrue(ModLoaderService.IsTransientError(ex));
    }
}
