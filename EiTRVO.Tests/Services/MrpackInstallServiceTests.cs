using System.IO.Compression;
using System.Text.Json;
using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Services;
using EiTRVO.Tests.Fakes;

namespace EiTRVO.Tests.Services;

[TestClass]
public class MrpackInstallServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FakeDownloadService _downloadService;
    private readonly FakeModLoaderService _modLoaderService;
    private readonly FakeModrinthService _modrinth;
    private readonly FakeGameFolderService _gameFolder;
    private readonly HttpClient _httpClient;
    private readonly MrpackInstallService _service;

    public MrpackInstallServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"eitrvo_mrpack_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _downloadService = new FakeDownloadService();
        _modLoaderService = new FakeModLoaderService();
        _modrinth = new FakeModrinthService();
        _gameFolder = new FakeGameFolderService { GameDir = _tempDir };
        _httpClient = new HttpClient();
        _service = new MrpackInstallService(
            _downloadService, _modLoaderService, _modrinth, _httpClient);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private string CreateMrpackFile(int formatVersion = 1, string game = "minecraft",
        string name = "Test Pack", Dictionary<string, string>? dependencies = null)
    {
        string path = Path.Combine(_tempDir, $"test_{Guid.NewGuid():N}.mrpack");
        var manifest = new ModpackManifest
        {
            FormatVersion = formatVersion,
            Game = game,
            Name = name,
            VersionId = "1.0.0",
            Dependencies = dependencies ?? new Dictionary<string, string>
            {
                ["minecraft"] = "1.21",
                ["fabric-loader"] = "0.16.0"
            },
            Files = new List<ModpackFileEntry>
            {
                new()
                {
                    Path = "mods/test.jar",
                    Downloads = new List<string> { "https://cdn.modrinth.com/data/test/mod.jar" },
                    Hashes = new ModpackHashes { Sha1 = "abc123" },
                    FileSize = 1024
                }
            }
        };

        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = zip.CreateEntry("modrinth.index.json", CompressionLevel.NoCompression);
        using var stream = entry.Open();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest);
        stream.Write(bytes, 0, bytes.Length);

        return path;
    }

    // ================================================================
    // ParseMrpackAsync
    // ================================================================

    [TestMethod]
    public async Task ParseMrpack_ValidFile_ParsesCorrectly()
    {
        var path = CreateMrpackFile();
        var info = await _service.ParseMrpackAsync(path);

        Assert.AreEqual("Test Pack", info.Manifest.Name);
        Assert.AreEqual("1.21", info.McVersion);
        Assert.AreEqual("Fabric", info.LoaderType);
        Assert.AreEqual("0.16.0", info.LoaderVersionSpec);
    }

    [TestMethod]
    public async Task ParseMrpack_MissingIndex_Throws()
    {
        string path = Path.Combine(_tempDir, "empty.mrpack");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            zip.CreateEntry("readme.txt", CompressionLevel.NoCompression);
        }

        await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
            _service.ParseMrpackAsync(path));
    }

    [TestMethod]
    public async Task ParseMrpack_InvalidFormatVersion_Throws()
    {
        var path = CreateMrpackFile(formatVersion: 2);

        await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
            _service.ParseMrpackAsync(path));
    }

    [TestMethod]
    public async Task ParseMrpack_WrongGame_Throws()
    {
        var path = CreateMrpackFile(game: "terraria");

        await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
            _service.ParseMrpackAsync(path));
    }

    [TestMethod]
    public async Task ParseMrpack_NoDependencies_ReturnsEmptyVersions()
    {
        var path = CreateMrpackFile(dependencies: new Dictionary<string, string>());

        var info = await _service.ParseMrpackAsync(path);

        Assert.AreEqual("", info.McVersion, "MC version should be empty when no minecraft dependency.");
        Assert.IsNull(info.LoaderType, "Loader should be null when no loader dependency.");
    }

    [TestMethod]
    public async Task ParseMrpack_ForgeLoader_Detected()
    {
        var path = CreateMrpackFile(dependencies: new Dictionary<string, string>
        {
            ["minecraft"] = "1.20.1",
            ["forge"] = "47.2.0"
        });

        var info = await _service.ParseMrpackAsync(path);

        Assert.AreEqual("Forge", info.LoaderType);
        Assert.AreEqual("47.2.0", info.LoaderVersionSpec);
    }

    [TestMethod]
    public async Task ParseMrpack_QuiltLoader_Detected()
    {
        var path = CreateMrpackFile(dependencies: new Dictionary<string, string>
        {
            ["minecraft"] = "1.19.4",
            ["quilt-loader"] = "0.24.0"
        });

        var info = await _service.ParseMrpackAsync(path);

        Assert.AreEqual("Quilt", info.LoaderType);
    }

    [TestMethod]
    public async Task ParseMrpack_NeoForgeLoader_Detected()
    {
        var path = CreateMrpackFile(dependencies: new Dictionary<string, string>
        {
            ["minecraft"] = "1.21",
            ["neoforge"] = "21.1.0"
        });

        var info = await _service.ParseMrpackAsync(path);

        Assert.AreEqual("NeoForge", info.LoaderType);
    }

    [TestMethod]
    public async Task ParseMrpack_Vanilla_NoLoaderDetected()
    {
        var path = CreateMrpackFile(dependencies: new Dictionary<string, string>
        {
            ["minecraft"] = "1.20.4"
        });

        var info = await _service.ParseMrpackAsync(path);

        Assert.AreEqual("1.20.4", info.McVersion);
        Assert.IsNull(info.LoaderType);
    }
}
