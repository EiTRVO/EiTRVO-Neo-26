using System.Security.Cryptography;
using EiTRVO.ProEngine.Services;
using EiTRVO.Tests.Fakes;

namespace EiTRVO.Tests.Services;

[TestClass]
public class SaveRecoveryFileTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FakeGameFolderService _gameFolder;
    private readonly SaveLockService _saveLockService;
    private readonly SaveRecoveryFile _recovery;

    public SaveRecoveryFileTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"eitrvo_saverec_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _gameFolder = new FakeGameFolderService { GameDir = _tempDir };
        _saveLockService = new SaveLockService();
        _recovery = new SaveRecoveryFile();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private static byte[] CreateTestKey()
    {
        return RandomNumberGenerator.GetBytes(32);
    }

    // ================================================================
    // Export → Import Round-Trip
    // ================================================================

    [TestMethod]
    public async Task Export_Then_Import_RoundTrip_SameMsUuid()
    {
        var key = CreateTestKey();
        var msUuid = "43381ea0-21e5-9839-958a-f459800e4d11";
        var exportPath = Path.Combine(_tempDir, "test.savrec");

        await _recovery.ExportAsync(exportPath, "MyInstance", "MyWorld",
            key, msUuid);

        Assert.IsTrue(File.Exists(exportPath));

        var result = await _recovery.ImportAsync(exportPath, msUuid);
        Assert.IsNotNull(result);
        var (importedKey, saveName, instanceName) = result!.Value;
        CollectionAssert.AreEqual(key, importedKey);
        Assert.AreEqual("MyWorld", saveName);
        Assert.AreEqual("MyInstance", instanceName);
    }

    [TestMethod]
    public async Task Import_DifferentMsUuid_ReturnsNull()
    {
        var key = CreateTestKey();
        var msUuid = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
        var exportPath = Path.Combine(_tempDir, "diff_uuid.savrec");

        await _recovery.ExportAsync(exportPath, "Inst", "Save",
            key, msUuid);

        var result = await _recovery.ImportAsync(exportPath,
            "11111111-2222-3333-4444-555555555555");

        Assert.IsNull(result);
    }

    // ================================================================
    // Validate
    // ================================================================

    [TestMethod]
    public async Task Validate_ValidFile_ReturnsTrue()
    {
        var key = CreateTestKey();
        var exportPath = Path.Combine(_tempDir, "valid.savrec");
        await _recovery.ExportAsync(exportPath, "Inst", "Save",
            key, "uuid-123");

        Assert.IsTrue(_recovery.Validate(exportPath));
    }

    [TestMethod]
    public void Validate_CorruptedFile_ReturnsFalse()
    {
        var badPath = Path.Combine(_tempDir, "bad.savrec");
        File.WriteAllText(badPath, "not a valid recovery file");
        Assert.IsFalse(_recovery.Validate(badPath));
    }

    [TestMethod]
    public void Validate_EmptyFile_ReturnsFalse()
    {
        var emptyPath = Path.Combine(_tempDir, "empty.savrec");
        File.WriteAllText(emptyPath, "");
        Assert.IsFalse(_recovery.Validate(emptyPath));
    }

    [TestMethod]
    public void Validate_NonExistentFile_ReturnsFalse()
    {
        Assert.IsFalse(_recovery.Validate(Path.Combine(_tempDir, "nope.savrec")));
    }

    // ================================================================
    // Edge cases
    // ================================================================

    [TestMethod]
    public async Task Import_NonExistentFile_Throws()
    {
        await Assert.ThrowsExceptionAsync<FileNotFoundException>(() =>
            _recovery.ImportAsync(Path.Combine(_tempDir, "nope.savrec"), "uuid"));
    }

    [TestMethod]
    public async Task Export_OverwritesExisting()
    {
        var key1 = CreateTestKey();
        var key2 = CreateTestKey();
        var exportPath = Path.Combine(_tempDir, "overwrite.savrec");

        await _recovery.ExportAsync(exportPath, "Inst1", "Save1",
            key1, "uuid-1");
        await _recovery.ExportAsync(exportPath, "Inst2", "Save2",
            key2, "uuid-2");

        // Should contain key2 data after overwrite
        var result = await _recovery.ImportAsync(exportPath, "uuid-2");
        Assert.IsNotNull(result);
        CollectionAssert.AreEqual(key2, result!.Value.key);
    }
}
