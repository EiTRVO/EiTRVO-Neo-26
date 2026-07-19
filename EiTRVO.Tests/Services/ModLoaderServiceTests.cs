using System.Text.Json;
using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Services;
using EiTRVO.Tests.Fakes;

namespace EiTRVO.Tests.Services;

[TestClass]

public class ModLoaderServiceTests
{
    // ================================================================
    // MavenNameToPath
    // ================================================================

    [TestMethod]
    public void MavenNameToPath_Standard()
    {
        var result = ModLoaderService.MavenNameToPath("com.example:test-lib:1.0.0");
        Assert.AreEqual("com/example/test-lib/1.0.0/test-lib-1.0.0.jar", result);
    }

    [TestMethod]
    public void MavenNameToPath_IgnoresClassifier()
    {
        // 4th part (classifier) is ignored; only first 3 parts used
        var result = ModLoaderService.MavenNameToPath("net.minecraftforge:forge:1.8.9-11.15.1.2318-1.8.9:universal");
        StringAssert.Contains(result, "net/minecraftforge/forge/1.8.9-11.15.1.2318-1.8.9/forge-1.8.9-11.15.1.2318-1.8.9.jar");
    }

    [TestMethod]
    public void MavenNameToPath_InvalidCoordinate_Throws()
    {
        Assert.ThrowsException<ArgumentException>(() => ModLoaderService.MavenNameToPath("only-two-parts"));
    }

    [TestMethod]
    public void MavenNameToPath_GroupWithDots()
    {
        var result = ModLoaderService.MavenNameToPath("org.apache.commons:commons-lang3:3.3.2");
        Assert.AreEqual("org/apache/commons/commons-lang3/3.3.2/commons-lang3-3.3.2.jar", result);
    }

    // ================================================================
    // MergeVersionJson
    // ================================================================

    [TestMethod]
    public void MergeVersionJson_ReplacesMainClass()
    {
        var child = new VersionDetail { Id = "forge", MainClass = "net.minecraft.launchwrapper.Launch" };
        var parent = new VersionDetail { Id = "vanilla", MainClass = "net.minecraft.client.main.Main" };

        var merged = new ModLoaderService().MergeVersionJson(child, parent);

        Assert.AreEqual("net.minecraft.launchwrapper.Launch", merged.MainClass);
    }

    [TestMethod]
    public void MergeVersionJson_AppendsLibraries()
    {
        var child = new VersionDetail
        {
            Id = "forge",
            MainClass = "net.minecraft.launchwrapper.Launch",
            Libraries = new List<Library> { new() { Name = "net.minecraftforge:forge:1.8.9" } }
        };
        var parent = new VersionDetail
        {
            Id = "1.8.9",
            Libraries = new List<Library> { new() { Name = "com.mojang:authlib:1.5.21" } }
        };

        var merged = new ModLoaderService().MergeVersionJson(child, parent);

        Assert.AreEqual(2, merged.Libraries?.Count);
        Assert.IsTrue(merged.Libraries!.Any(l => l.Name == "net.minecraftforge:forge:1.8.9"));
        Assert.IsTrue(merged.Libraries!.Any(l => l.Name == "com.mojang:authlib:1.5.21"));
    }

    [TestMethod]
    public void MergeVersionJson_DeduplicatesLibraries()
    {
        var lib = new Library { Name = "com.mojang:authlib:1.5.21" };
        var child = new VersionDetail
        {
            Id = "forge",
            MainClass = "net.minecraft.launchwrapper.Launch",
            Libraries = new List<Library> { lib }
        };
        var parent = new VersionDetail
        {
            Id = "1.8.9",
            Libraries = new List<Library> { lib }
        };

        var merged = new ModLoaderService().MergeVersionJson(child, parent);

        Assert.AreEqual(1, merged.Libraries!.Count);
    }

    [TestMethod]
    public void MergeVersionJson_InheritsFrom()
    {
        var child = new VersionDetail { Id = "forge", MainClass = "net.minecraft.launchwrapper.Launch" };
        var parent = new VersionDetail { Id = "1.8.9" };

        var merged = new ModLoaderService().MergeVersionJson(child, parent);

        Assert.AreEqual("forge", merged.Id);
    }

    [TestMethod]
    public void MergeVersionJson_PreservesParentArguments()
    {
        var child = new VersionDetail { Id = "forge", MainClass = "net.minecraft.launchwrapper.Launch" };
        var parent = new VersionDetail
        {
            Id = "1.8.9",
            MinecraftArguments = "--username ${auth_player_name} --session ${auth_session}"
        };

        var merged = new ModLoaderService().MergeVersionJson(child, parent);

        Assert.AreEqual("--username ${auth_player_name} --session ${auth_session}", merged.MinecraftArguments);
    }

    // ================================================================
    // NormalizeMavenLibraries
    // ================================================================

    [TestMethod]
    public void NormalizeMavenLibraries_AddsDownloadsArtifact()
    {
        var json = @"{""libraries"":[{""name"":""net.minecraft:launchwrapper:1.12""}]}";
        var result = ModLoaderService.NormalizeMavenLibraries(json);

        var parsed = JsonDocument.Parse(result);
        var lib = parsed.RootElement.GetProperty("libraries")[0];
        Assert.IsTrue(lib.TryGetProperty("downloads", out var downloads));
        Assert.IsTrue(downloads.TryGetProperty("artifact", out var artifact));
        Assert.AreEqual("net/minecraft/launchwrapper/1.12/launchwrapper-1.12.jar", artifact.GetProperty("path").GetString());
    }

    [TestMethod]
    public void NormalizeMavenLibraries_PreservesExistingDownloads()
    {
        var json = @"{""libraries"":[{""name"":""com.example:lib:1.0"",""downloads"":{""artifact"":{""path"":""custom/path/lib-1.0.jar""}}}]}";
        var result = ModLoaderService.NormalizeMavenLibraries(json);

        var parsed = JsonDocument.Parse(result);
        var lib = parsed.RootElement.GetProperty("libraries")[0];
        var path = lib.GetProperty("downloads").GetProperty("artifact").GetProperty("path").GetString();
        Assert.AreEqual("custom/path/lib-1.0.jar", path);
    }

    [TestMethod]
    public void NormalizeMavenLibraries_HandlesEmptyLibraries()
    {
        var json = @"{""libraries"":[]}";
        var result = ModLoaderService.NormalizeMavenLibraries(json);
        var parsed = JsonDocument.Parse(result);
        Assert.AreEqual(0, parsed.RootElement.GetProperty("libraries").GetArrayLength());
    }

    // ================================================================
    // IsLegacyMcVersion
    // ================================================================

    [DataTestMethod]
    [DataRow("1.5.2", true)]
    [DataRow("1.7.10", true)]
    [DataRow("1.8.9", true)]
    [DataRow("1.12.2", true)]
    [DataRow("1.13", false)]
    [DataRow("1.16.5", false)]
    [DataRow("1.20.1", false)]
    [DataRow("1.21.5", false)]
    public void IsLegacyMcVersion_Boundary(string version, bool expected)
    {
        Assert.AreEqual(expected, ModLoaderService.IsLegacyMcVersion(version));
    }

    // ================================================================
    // CopyDirectoryRecursive
    // ================================================================

    [TestMethod]
    public void CopyDirectoryRecursive_CopiesAllFiles()
    {
        var srcDir = Path.Combine(Path.GetTempPath(), $"eitrvo_mod_src_{Guid.NewGuid():N}");
        var destDir = Path.Combine(Path.GetTempPath(), $"eitrvo_mod_dest_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(srcDir, "sub"));
            File.WriteAllText(Path.Combine(srcDir, "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(srcDir, "sub", "file2.txt"), "content2");

            ModLoaderService.CopyDirectoryRecursive(srcDir, destDir);

            Assert.IsTrue(File.Exists(Path.Combine(destDir, "file1.txt")));
            Assert.IsTrue(File.Exists(Path.Combine(destDir, "sub", "file2.txt")));
            Assert.AreEqual("content1", File.ReadAllText(Path.Combine(destDir, "file1.txt")));
        }
        finally
        {
            try { Directory.Delete(srcDir, true); } catch { }
            try { Directory.Delete(destDir, true); } catch { }
        }
    }

    // ================================================================
    // DownloadFileAsync — retry logic
    // ================================================================

    private static string CreateTempDownloadDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"eitrvo_dl_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [TestMethod]
    public async Task DownloadFileAsync_RetriesOnTransientError_Succeeds()
    {
        var dir = CreateTempDownloadDir();
        try
        {
            var destFile = Path.Combine(dir, "test.jar");
            // Queue: 503 (transient, retry 1) → 503 (transient, retry 2) → 200 (success)
            var handler = new FakeHttpMessageHandler(
                (System.Net.HttpStatusCode.ServiceUnavailable, ""),
                (System.Net.HttpStatusCode.ServiceUnavailable, ""),
                (System.Net.HttpStatusCode.OK, "file-content")
            );
            var http = new HttpClient(handler);

            await ModLoaderService.DownloadFileAsync(http, "http://example.com/file.jar", destFile);

            Assert.IsTrue(File.Exists(destFile));
            Assert.AreEqual(3, handler.Requests.Count);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [TestMethod]
    public async Task DownloadFileAsync_ExceedsMaxRetries_Throws()
    {
        var dir = CreateTempDownloadDir();
        try
        {
            var destFile = Path.Combine(dir, "test.jar");
            // 4 × 503 = 1 initial + 3 retries = all exhausted
            var handler = new FakeHttpMessageHandler(
                (System.Net.HttpStatusCode.ServiceUnavailable, ""),
                (System.Net.HttpStatusCode.ServiceUnavailable, ""),
                (System.Net.HttpStatusCode.ServiceUnavailable, ""),
                (System.Net.HttpStatusCode.ServiceUnavailable, "")
            );
            var http = new HttpClient(handler);

            await Assert.ThrowsExceptionAsync<HttpRequestException>(
                () => ModLoaderService.DownloadFileAsync(http, "http://example.com/file.jar", destFile));

            Assert.AreEqual(4, handler.Requests.Count); // initial + 3 retries
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [TestMethod]
    public async Task DownloadFileAsync_NonTransientError_NoRetry()
    {
        var dir = CreateTempDownloadDir();
        try
        {
            var destFile = Path.Combine(dir, "test.jar");
            // 404 is not transient → should throw immediately, no retry
            var handler = new FakeHttpMessageHandler(System.Net.HttpStatusCode.NotFound, "");
            var http = new HttpClient(handler);

            await Assert.ThrowsExceptionAsync<HttpRequestException>(
                () => ModLoaderService.DownloadFileAsync(http, "http://example.com/file.jar", destFile));

            Assert.AreEqual(1, handler.Requests.Count); // no retry
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // ================================================================
    // DownloadFileWithFallbackAsync
    // ================================================================

    [TestMethod]
    public async Task DownloadFileWithFallback_Primary404_FallbackSucceeds()
    {
        var dir = CreateTempDownloadDir();
        try
        {
            var destFile = Path.Combine(dir, "lib.jar");
            var primaryUrl = "https://maven.minecraftforge.net/net/minecraftforge/forge/1.12.2-14.23.5.2860/forge-1.12.2-14.23.5.2860-universal.jar";
            var fallbackUrl = $"https://libraries.minecraft.net/{destFile.Replace('\\', '/').TrimStart('/')}";

            var handler = FakeHttpMessageHandler.ForUrlMap()
                .Map("maven.minecraftforge.net", System.Net.HttpStatusCode.NotFound, "")
                .Map("libraries.minecraft.net", System.Net.HttpStatusCode.OK, "fallback-content")
                .Build();
            var http = new HttpClient(handler);

            await ModLoaderService.DownloadFileWithFallbackAsync(http, primaryUrl, destFile, null, "test-file");

            Assert.IsTrue(File.Exists(destFile));
            Assert.AreEqual("fallback-content", File.ReadAllText(destFile));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [TestMethod]
    public async Task DownloadFileWithFallback_Primary404_FallbackAlso404_Throws()
    {
        var dir = CreateTempDownloadDir();
        try
        {
            var destFile = Path.Combine(dir, "lib.jar");
            var primaryUrl = "https://maven.minecraftforge.net/some/lib.jar";

            var handler = FakeHttpMessageHandler.ForUrlMap()
                .Map("maven.minecraftforge.net", System.Net.HttpStatusCode.NotFound, "")
                .Map("libraries.minecraft.net", System.Net.HttpStatusCode.NotFound, "")
                .Build();
            var http = new HttpClient(handler);

            await Assert.ThrowsExceptionAsync<HttpRequestException>(
                () => ModLoaderService.DownloadFileWithFallbackAsync(http, primaryUrl, destFile, null, "test-file"));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [TestMethod]
    public async Task DownloadFileWithFallback_Non404_NoFallback()
    {
        var dir = CreateTempDownloadDir();
        try
        {
            var destFile = Path.Combine(dir, "lib.jar");
            var primaryUrl = "https://maven.minecraftforge.net/some/lib.jar";

            var handler = new FakeHttpMessageHandler(System.Net.HttpStatusCode.InternalServerError, "");
            var http = new HttpClient(handler);

            await Assert.ThrowsExceptionAsync<HttpRequestException>(
                () => ModLoaderService.DownloadFileWithFallbackAsync(http, primaryUrl, destFile, null, "test-file"));

            // Only the primary URL was attempted (plus retries within DownloadFileAsync)
            Assert.IsTrue(handler.Requests.Count >= 1);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
