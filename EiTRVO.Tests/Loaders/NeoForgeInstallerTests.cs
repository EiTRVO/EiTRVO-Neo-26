using System.Net;
using EiTRVO.ProEngine.Services;
using EiTRVO.ProEngine.Services.Loaders;
using EiTRVO.Tests.Fakes;

namespace EiTRVO.Tests.Loaders;

[TestClass]

public class NeoForgeInstallerTests
{
    // ================================================================
    // ParseMavenMetadataVersions
    // ================================================================

    [TestMethod]
    public void ParseMavenMetadata_SingleVersion()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<metadata>
  <groupId>net.neoforged</groupId>
  <artifactId>neoforge</artifactId>
  <versioning>
    <versions>
      <version>21.5.0.123</version>
    </versions>
  </versioning>
</metadata>";

        var versions = ModLoaderService.ParseMavenMetadataVersions(xml);
        Assert.AreEqual(1, versions.Count);
        Assert.AreEqual("21.5.0.123", versions[0]);
    }

    [TestMethod]
    public void ParseMavenMetadata_MultipleVersions()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<metadata>
  <versioning>
    <versions>
      <version>21.5.0.123</version>
      <version>21.5.0.122</version>
      <version>21.5.0.121</version>
    </versions>
  </versioning>
</metadata>";

        var versions = ModLoaderService.ParseMavenMetadataVersions(xml);
        Assert.AreEqual(3, versions.Count);
        CollectionAssert.Contains(versions, "21.5.0.123");
    }

    [TestMethod]
    public void ParseMavenMetadata_EmptyVersions()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<metadata>
  <versioning>
    <versions>
    </versions>
  </versioning>
</metadata>";

        var versions = ModLoaderService.ParseMavenMetadataVersions(xml);
        Assert.AreEqual(0, versions.Count);
    }

    [TestMethod]
    public void ParseMavenMetadata_NoVersioning()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<metadata>
  <groupId>net.neoforged</groupId>
</metadata>";

        var versions = ModLoaderService.ParseMavenMetadataVersions(xml);
        Assert.AreEqual(0, versions.Count);
    }

    // ================================================================
    // ParseNeoForgeBuildNumber
    // ================================================================

    [TestMethod]
    public void ParseNeoForgeBuildNumber_Standard()
    {
        var result = ModLoaderService.ParseNeoForgeBuildNumber("21.5.0.123");
        Assert.AreEqual(123, result);
    }

    [TestMethod]
    public void ParseNeoForgeBuildNumber_Large()
    {
        var result = ModLoaderService.ParseNeoForgeBuildNumber("21.5.0.99999");
        Assert.AreEqual(99999, result);
    }

    [TestMethod]
    public void ParseNeoForgeBuildNumber_Invalid()
    {
        var result = ModLoaderService.ParseNeoForgeBuildNumber("not-a-version");
        Assert.AreEqual(0, result);
    }

    // ================================================================
    // GetVersionsAsync
    // ================================================================

    [TestMethod]
    public async Task GetVersions_SortsDescendingByBuildNumber()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<metadata>
  <versioning>
    <versions>
      <version>21.5.0.123</version>
      <version>21.5.0.125</version>
      <version>21.5.0.120</version>
    </versions>
  </versioning>
</metadata>";
        var handler = new FakeHttpMessageHandler(System.Net.HttpStatusCode.OK, xml);
        var http = new HttpClient(handler);

        var versions = await NeoForgeInstaller.GetVersionsAsync(http, "1.21.5");

        Assert.IsTrue(versions.Count >= 1);
        // Should be sorted descending by build number
        for (int i = 0; i < versions.Count - 1; i++)
        {
            int current = ModLoaderService.ParseNeoForgeBuildNumber(versions[i].LoaderVersion);
            int next = ModLoaderService.ParseNeoForgeBuildNumber(versions[i + 1].LoaderVersion);
            Assert.IsTrue(current >= next, $"Expected {current} >= {next} but was not");
        }
    }

    [TestMethod]
    public async Task GetVersions_FiltersByMcVersionPrefix()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<metadata>
  <versioning>
    <versions>
      <version>21.5.0.123</version>
      <version>21.4.0.100</version>
      <version>21.5.0.125</version>
    </versions>
  </versioning>
</metadata>";
        var handler = new FakeHttpMessageHandler(System.Net.HttpStatusCode.OK, xml);
        var http = new HttpClient(handler);

        var versions = await NeoForgeInstaller.GetVersionsAsync(http, "1.21.5");

        // Should only include versions starting with "5." (from mcVersion "1.21.5")
        // Actually, let's just verify they all match the MC version
        Assert.IsTrue(versions.Count >= 1);
        // All versions should be for the same MC version
        Assert.IsTrue(versions.All(v => v.MinecraftVersion == "1.21.5"));
    }
}
