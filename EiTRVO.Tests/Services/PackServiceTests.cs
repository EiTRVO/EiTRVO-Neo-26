using System.IO.Compression;
using System.Text.Json;
using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Services;

namespace EiTRVO.Tests.Services;

[TestClass]

public class PackServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly PackService _packService;

    public PackServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"eitrvo_pack_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _packService = new PackService();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ================================================================
    // ReadManifest
    // ================================================================

    [TestMethod]
    public void ReadManifest_ValidPack()
    {
        var zipPath = CreatePackZip("eitrvo-pack:1");

        var manifest = _packService.ReadManifest(zipPath);
        Assert.AreEqual("eitrvo-pack:1", manifest.Format);
    }

    [TestMethod]
    public void ReadManifest_InvalidJson()
    {
        var zipPath = Path.Combine(_tempDir, "bad.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("manifest.json");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("{invalid json");
        }

        Assert.ThrowsException<JsonException>(() => _packService.ReadManifest(zipPath));
    }

    [TestMethod]
    public void ReadManifest_MissingManifest()
    {
        var zipPath = Path.Combine(_tempDir, "nomanifest.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            zip.CreateEntry("random.txt");
        }

        Assert.ThrowsException<InvalidDataException>(() => _packService.ReadManifest(zipPath));
    }

    // ================================================================
    // PeekVersionJson
    // ================================================================

    [TestMethod]
    public void PeekVersionJson_ExtractsInheritsFrom()
    {
        var versionDetail = new VersionDetail
        {
            Id = "1.21-forge-50.1.0",
            InheritsFrom = "1.21"
        };
        var zipPath = Path.Combine(_tempDir, "withversion.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            // PeekVersionJson expects {instanceFolder}/version.json structure
            var entry = zip.CreateEntry("MyPack/version.json");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(JsonSerializer.Serialize(versionDetail));
        }

        var detail = _packService.PeekVersionJson(zipPath);
        Assert.IsNotNull(detail);
        Assert.AreEqual("1.21", detail!.InheritsFrom);
    }

    // ================================================================
    // Path traversal protection
    // ================================================================

    [TestMethod]
    public async Task Extract_PathTraversal_Rejected()
    {
        // Create a ZIP containing a path-traversal entry
        var zipPath = Path.Combine(_tempDir, "traversal.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var evil = zip.CreateEntry("../../../etc/hacked.txt");
            using var writer = new StreamWriter(evil.Open());
            writer.Write("pwned");
        }

        var gameDir = Path.Combine(_tempDir, "game");
        Directory.CreateDirectory(gameDir);

        // Should not crash and should not write outside target
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => _packService.ExtractAsync(zipPath, "test", gameDir, null!, null!));
    }

    // ================================================================
    // Helpers
    // ================================================================

    private string CreatePackZip(string formatVersion)
    {
        var zipPath = Path.Combine(_tempDir, "pack.zip");
        var manifest = new PackManifest
        {
            Format = formatVersion,
            Name = "Test Pack",
            ExporterVersion = 26
        };

        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("manifest.json");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(JsonSerializer.Serialize(manifest));
        }
        return zipPath;
    }
}
