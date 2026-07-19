using EiTRVO.ProEngine.Services;
using EiTRVO.Tests.Fakes;

namespace EiTRVO.Tests.Services;

[TestClass]
public class LocalKeyStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FakeGameFolderService _gameFolder;
    private readonly LocalKeyStore _store;

    public LocalKeyStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"eitrvo_keystore_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _gameFolder = new FakeGameFolderService { GameDir = _tempDir };
        _store = new LocalKeyStore(_gameFolder);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ================================================================
    // Save → Load Round-Trip
    // ================================================================

    [TestMethod]
    public async Task Save_Then_Load_ReturnsSameKey()
    {
        var key = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16,
                               17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32 };

        await _store.SaveKeyAsync("TestInstance", "TestSave", key);

        var loaded = await _store.LoadKeyAsync("TestInstance", "TestSave");

        Assert.IsNotNull(loaded);
        CollectionAssert.AreEqual(key, loaded);
    }

    [TestMethod]
    public async Task LoadKey_NotExists_ReturnsNull()
    {
        var result = await _store.LoadKeyAsync("NoInstance", "NoSave");
        Assert.IsNull(result);
    }

    // ================================================================
    // KeyExists
    // ================================================================

    [TestMethod]
    public async Task KeyExists_AfterSave_ReturnsTrue()
    {
        await _store.SaveKeyAsync("Inst", "Save", new byte[32]);
        Assert.IsTrue(_store.KeyExists("Inst", "Save"));
    }

    [TestMethod]
    public void KeyExists_BeforeSave_ReturnsFalse()
    {
        Assert.IsFalse(_store.KeyExists("Inst", "Save"));
    }

    // ================================================================
    // Delete
    // ================================================================

    [TestMethod]
    public async Task Delete_Existing_CleansUp()
    {
        await _store.SaveKeyAsync("DelInst", "DelSave", new byte[32]);
        Assert.IsTrue(_store.KeyExists("DelInst", "DelSave"));

        _store.DeleteKey("DelInst", "DelSave");
        Assert.IsFalse(_store.KeyExists("DelInst", "DelSave"));
    }

    [TestMethod]
    public void Delete_NonExisting_DoesNotThrow()
    {
        _store.DeleteKey("NoInst", "NoSave"); // should not throw
    }

    // ================================================================
    // Export / Import
    // ================================================================

    [TestMethod]
    public async Task Export_Then_Import_RoundTrip()
    {
        var key = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x00, 0x11,
                               0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99,
                               0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07,
                               0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F };
        await _store.SaveKeyAsync("ExpInst", "ExpSave", key);

        var exportPath = Path.Combine(_tempDir, "exported.key");
        await _store.ExportKeyToFileAsync(exportPath, "ExpInst", "ExpSave", key);

        Assert.IsTrue(File.Exists(exportPath));

        // Delete local key and re-import
        _store.DeleteKey("ExpInst", "ExpSave");
        var imported = await _store.ImportKeyFromFileAsync(exportPath);

        Assert.IsNotNull(imported);
        CollectionAssert.AreEqual(key, imported);
    }

    [TestMethod]
    public async Task ImportKey_NonExistentFile_ReturnsNull()
    {
        var result = await _store.ImportKeyFromFileAsync(Path.Combine(_tempDir, "nope.key"));
        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task Overwrite_SaveKey_Replaces()
    {
        var key1 = new byte[32]; // all zeros
        var key2 = new byte[32];
        key2[0] = 0xFF;

        await _store.SaveKeyAsync("OverInst", "OverSave", key1);
        await _store.SaveKeyAsync("OverInst", "OverSave", key2);

        var loaded = await _store.LoadKeyAsync("OverInst", "OverSave");
        Assert.IsNotNull(loaded);
        Assert.AreEqual(0xFF, loaded![0]);
    }
}
