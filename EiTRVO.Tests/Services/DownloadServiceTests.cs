using System.Net;
using System.Text.Json;
using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Services;
using EiTRVO.Tests.Fakes;

namespace EiTRVO.Tests.Services;

[TestClass]
public class DownloadServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly DownloadService _service;

    public DownloadServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"eitrvo_dl_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _service = new DownloadService();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private static HttpClient CreateFakeHttpClient(HttpStatusCode statusCode, string responseJson)
    {
        return new HttpClient(new FakeHttpMessageHandler(statusCode, responseJson));
    }

    // ================================================================
    // LoadOnlineVersionsAsync
    // ================================================================

    [TestMethod]
    public async Task LoadOnlineVersions_Success_ReturnsList()
    {
        var manifest = new VersionManifest
        {
            Latest = new LatestVersions { Release = "1.21.5", Snapshot = "25w14craftmine" },
            Versions = new List<ManifestVersion>
            {
                new() { Id = "1.21.5", Type = "release", Url = "https://example.com/1.21.5.json" },
                new() { Id = "1.20.4", Type = "release", Url = "https://example.com/1.20.4.json" }
            }
        };
        var http = CreateFakeHttpClient(HttpStatusCode.OK, JsonSerializer.Serialize(manifest));

        var result = await _service.LoadOnlineVersionsAsync(http);

        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count >= 2);
        StringAssert.Contains(result[0].Id, "1.21.5");
    }

    [TestMethod]
    public async Task LoadOnlineVersions_NetworkError_Throws()
    {
        var http = new HttpClient(new FakeHttpMessageHandler(
            new HttpRequestException("Connection failed")));

        await Assert.ThrowsExceptionAsync<HttpRequestException>(() =>
            _service.LoadOnlineVersionsAsync(http));
    }

    [TestMethod]
    public async Task LoadOnlineVersions_InvalidJson_Throws()
    {
        var http = CreateFakeHttpClient(HttpStatusCode.OK, "{not valid json!!!");

        await Assert.ThrowsExceptionAsync<JsonException>(() =>
            _service.LoadOnlineVersionsAsync(http));
    }

    // ================================================================
    // DownloadFileWithRetryAsync
    // ================================================================

    [TestMethod]
    public async Task DownloadFileWithRetry_Success_CreatesFile()
    {
        var http = CreateFakeHttpClient(HttpStatusCode.OK, "test file content");
        var destPath = Path.Combine(_tempDir, "downloaded.txt");

        await _service.DownloadFileWithRetryAsync(http, "https://example.com/file.txt",
            destPath, maxRetries: 1);

        Assert.IsTrue(File.Exists(destPath));
        Assert.AreEqual("test file content", File.ReadAllText(destPath));
    }

    // ================================================================
    // ExtractNativeJar
    // ================================================================

    [TestMethod]
    public void ExtractNativeJar_Valid_NativeFiles()
    {
        var nativeJarPath = Path.Combine(_tempDir, "natives.jar");
        var destDir = Path.Combine(_tempDir, "natives_out");
        Directory.CreateDirectory(destDir);

        // Create a minimal ZIP with a .dll
        using (var zip = System.IO.Compression.ZipFile.Open(nativeJarPath,
            System.IO.Compression.ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("lwjgl.dll");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("fake dll content");
        }

        _service.ExtractNativeJar(nativeJarPath, destDir);

        Assert.IsTrue(File.Exists(Path.Combine(destDir, "lwjgl.dll")));
        Assert.AreEqual("fake dll content", File.ReadAllText(Path.Combine(destDir, "lwjgl.dll")));
    }

    [TestMethod]
    public void ExtractNativeJar_SkipsNonDllFiles()
    {
        var nativeJarPath = Path.Combine(_tempDir, "natives2.jar");
        var destDir = Path.Combine(_tempDir, "natives_out2");
        Directory.CreateDirectory(destDir);

        using (var zip = System.IO.Compression.ZipFile.Open(nativeJarPath,
            System.IO.Compression.ZipArchiveMode.Create))
        {
            zip.CreateEntry("META-INF/MANIFEST.MF");
            var entry = zip.CreateEntry("some.dll");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("dll");
        }

        _service.ExtractNativeJar(nativeJarPath, destDir);

        Assert.IsFalse(Directory.Exists(Path.Combine(destDir, "META-INF")));
        Assert.IsTrue(File.Exists(Path.Combine(destDir, "some.dll")));
    }
}
