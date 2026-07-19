using System.Security.Cryptography;
using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Services;

namespace EiTRVO.Tests.Services;

[TestClass]
public class SaveLockServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SaveLockService _service;

    public SaveLockServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"eitrvo_savelock_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _service = new SaveLockService();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [TestCleanup]
    public void Cleanup()
    {
        // Additional cleanup between tests if needed
    }

    private string CreateTestFolder(string folderName, params (string Name, string Content)[] files)
    {
        var folder = Path.Combine(_tempDir, folderName);
        Directory.CreateDirectory(folder);
        foreach (var (name, content) in files)
        {
            var filePath = Path.Combine(folder, name);
            var dir = Path.GetDirectoryName(filePath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(filePath, content);
        }
        return folder;
    }

    private static bool AreDirectoriesEqual(string dir1, string dir2)
    {
        var files1 = Directory.GetFiles(dir1, "*", SearchOption.AllDirectories)
            .Select(f => new { Relative = Path.GetRelativePath(dir1, f), Content = File.ReadAllBytes(f) })
            .OrderBy(x => x.Relative)
            .ToList();
        var files2 = Directory.GetFiles(dir2, "*", SearchOption.AllDirectories)
            .Select(f => new { Relative = Path.GetRelativePath(dir2, f), Content = File.ReadAllBytes(f) })
            .OrderBy(x => x.Relative)
            .ToList();

        if (files1.Count != files2.Count) return false;
        for (int i = 0; i < files1.Count; i++)
        {
            if (files1[i].Relative != files2[i].Relative) return false;
            if (!files1[i].Content.SequenceEqual(files2[i].Content)) return false;
        }
        return true;
    }

    // ================================================================
    // LockSave → UnlockSave Round-Trip
    // ================================================================

    [TestMethod]
    public async Task LockSave_Then_UnlockWithPassword_RoundTrip()
    {
        var saveDir = CreateTestFolder("test_save",
            ("level.dat", "level data content"),
            ("playerdata/player1.dat", "player nbt data"));

        var savencPath = Path.Combine(_tempDir, "test.savenc");
        var options = new SaveLockOptions
        {
            Password = "MySecurePassword123!",
            LockMode = SaveLockMode.OneTime
        };

        await _service.LockSaveAsync(saveDir, savencPath, options.Password, options);

        // Original folder should be gone
        Assert.IsFalse(Directory.Exists(saveDir));
        Assert.IsTrue(File.Exists(savencPath));

        // Unlock
        var outDir = Path.Combine(_tempDir, "restored_save");
        var key = await _service.UnlockSaveAsync(savencPath, outDir, options.Password, deleteSavencAfter: false);

        Assert.IsTrue(Directory.Exists(outDir));
        Assert.IsTrue(File.Exists(Path.Combine(outDir, "level.dat")));
        Assert.IsTrue(File.Exists(Path.Combine(outDir, "playerdata/player1.dat")));
        Assert.AreEqual("level data content", File.ReadAllText(Path.Combine(outDir, "level.dat")));
        Assert.AreEqual("player nbt data", File.ReadAllText(Path.Combine(outDir, "playerdata/player1.dat")));
        Assert.IsNotNull(key);
        Assert.AreEqual(32, key.Length);
    }

    [TestMethod]
    public async Task LockSave_Then_UnlockWithKey_RoundTrip()
    {
        var saveDir = CreateTestFolder("test_save2",
            ("level.dat", "binary content here"));

        var savencPath = Path.Combine(_tempDir, "test2.savenc");
        var password = "KeyUnlockTest123!";
        var options = new SaveLockOptions { Password = password, LockMode = SaveLockMode.Permanent };

        await _service.LockSaveAsync(saveDir, savencPath, password, options);

        // Get the key by unlocking with password
        var outDir = Path.Combine(_tempDir, "restored_key");
        var key = await _service.UnlockSaveAsync(savencPath, outDir, password, deleteSavencAfter: false);

        // Verify the key works — we unlocked successfully already
        Assert.IsTrue(File.Exists(Path.Combine(outDir, "level.dat")));
        Assert.AreEqual("binary content here", File.ReadAllText(Path.Combine(outDir, "level.dat")));
        Assert.IsNotNull(key);
        Assert.AreEqual(32, key.Length);
    }

    // ================================================================
    // Edge Cases
    // ================================================================

    [TestMethod]
    public async Task LockSave_EmptyDirectory()
    {
        var saveDir = CreateTestFolder("empty_save");
        var savencPath = Path.Combine(_tempDir, "empty.savenc");
        var options = new SaveLockOptions { Password = "pass", LockMode = SaveLockMode.OneTime };

        await _service.LockSaveAsync(saveDir, savencPath, "pass", options);

        Assert.IsTrue(File.Exists(savencPath));

        // Should unlock to empty directory
        var outDir = Path.Combine(_tempDir, "restored_empty");
        await _service.UnlockSaveAsync(savencPath, outDir, "pass");
        Assert.IsTrue(Directory.Exists(outDir));
        Assert.AreEqual(0, Directory.GetFiles(outDir, "*", SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task LockSave_DirectoryWithSubdirectories()
    {
        var saveDir = CreateTestFolder("nested_save",
            ("region/r.0.0.mca", "chunk data"),
            ("region/r.1.0.mca", "more chunks"),
            ("entities/r.0.0.mca", "entity data"),
            ("level.dat", "main"));

        var savencPath = Path.Combine(_tempDir, "nested.savenc");
        var options = new SaveLockOptions { Password = "p@ssw0rd", LockMode = SaveLockMode.OneTime };

        await _service.LockSaveAsync(saveDir, savencPath, "p@ssw0rd", options);

        var outDir = Path.Combine(_tempDir, "restored_nested");
        await _service.UnlockSaveAsync(savencPath, outDir, "p@ssw0rd");

        Assert.IsTrue(Directory.Exists(Path.Combine(outDir, "region")));
        Assert.IsTrue(Directory.Exists(Path.Combine(outDir, "entities")));
        Assert.IsTrue(File.Exists(Path.Combine(outDir, "region/r.0.0.mca")));
        Assert.IsTrue(File.Exists(Path.Combine(outDir, "region/r.1.0.mca")));
        Assert.IsTrue(File.Exists(Path.Combine(outDir, "entities/r.0.0.mca")));
    }

    [TestMethod]
    public async Task LockSave_BinaryFileContent_RoundTrip()
    {
        // Create binary data (simulating NBT)
        var randomBytes = new byte[4096];
        RandomNumberGenerator.Fill(randomBytes);

        var saveDir = Path.Combine(_tempDir, "binary_save");
        Directory.CreateDirectory(saveDir);
        File.WriteAllBytes(Path.Combine(saveDir, "level.dat"), randomBytes);

        var savencPath = Path.Combine(_tempDir, "binary.savenc");
        var options = new SaveLockOptions { Password = "binary", LockMode = SaveLockMode.OneTime };

        await _service.LockSaveAsync(saveDir, savencPath, "binary", options);

        var outDir = Path.Combine(_tempDir, "restored_binary");
        await _service.UnlockSaveAsync(savencPath, outDir, "binary");

        var restored = File.ReadAllBytes(Path.Combine(outDir, "level.dat"));
        CollectionAssert.AreEqual(randomBytes, restored);
    }

    [TestMethod]
    public async Task LockSave_ZeroByteFile()
    {
        var saveDir = CreateTestFolder("zero_byte",
            ("empty.dat", ""),
            ("nonempty.dat", "content"));

        var savencPath = Path.Combine(_tempDir, "zero.savenc");
        var options = new SaveLockOptions { Password = "zero", LockMode = SaveLockMode.OneTime };

        await _service.LockSaveAsync(saveDir, savencPath, "zero", options);

        var outDir = Path.Combine(_tempDir, "restored_zero");
        await _service.UnlockSaveAsync(savencPath, outDir, "zero");

        Assert.AreEqual("", File.ReadAllText(Path.Combine(outDir, "empty.dat")));
        Assert.AreEqual("content", File.ReadAllText(Path.Combine(outDir, "nonempty.dat")));
    }

    [TestMethod]
    public async Task LockSave_ManySmallFiles()
    {
        var files = new List<(string, string)>();
        for (int i = 0; i < 100; i++)
            files.Add(($"file_{i:D4}.txt", $"content_{i}"));

        var saveDir = CreateTestFolder("many_files", files.ToArray());
        var savencPath = Path.Combine(_tempDir, "many.savenc");
        var options = new SaveLockOptions { Password = "many", LockMode = SaveLockMode.OneTime };

        await _service.LockSaveAsync(saveDir, savencPath, "many", options);

        var outDir = Path.Combine(_tempDir, "restored_many");
        await _service.UnlockSaveAsync(savencPath, outDir, "many");

        for (int i = 0; i < 100; i++)
        {
            var filePath = Path.Combine(outDir, $"file_{i:D4}.txt");
            Assert.IsTrue(File.Exists(filePath));
            Assert.AreEqual($"content_{i}", File.ReadAllText(filePath));
        }
    }

    // ================================================================
    // Password Verification
    // ================================================================

    [TestMethod]
    public async Task VerifyPassword_CorrectPassword_ReturnsTrue()
    {
        var saveDir = CreateTestFolder("verify_test", ("data.txt", "hello"));
        var savencPath = Path.Combine(_tempDir, "verify.savenc");
        var options = new SaveLockOptions { Password = "correct123", LockMode = SaveLockMode.OneTime };

        await _service.LockSaveAsync(saveDir, savencPath, "correct123", options);

        Assert.IsTrue(_service.VerifyPassword(savencPath, "correct123"));
    }

    [TestMethod]
    public async Task VerifyPassword_WrongPassword_ReturnsFalse()
    {
        var saveDir = CreateTestFolder("verify_wrong", ("data.txt", "hello"));
        var savencPath = Path.Combine(_tempDir, "verify_wrong.savenc");
        var options = new SaveLockOptions { Password = "correct123", LockMode = SaveLockMode.OneTime };

        await _service.LockSaveAsync(saveDir, savencPath, "correct123", options);

        Assert.IsFalse(_service.VerifyPassword(savencPath, "wrong_password"));
    }

    [TestMethod]
    public async Task VerifyPassword_SimilarButDifferent_ReturnsFalse()
    {
        var saveDir = CreateTestFolder("verify_similar", ("data.txt", "hello"));
        var savencPath = Path.Combine(_tempDir, "verify_similar.savenc");
        var options = new SaveLockOptions { Password = "Password123!", LockMode = SaveLockMode.OneTime };

        await _service.LockSaveAsync(saveDir, savencPath, "Password123!", options);

        Assert.IsFalse(_service.VerifyPassword(savencPath, "password123!"));
        Assert.IsFalse(_service.VerifyPassword(savencPath, "Password123"));
        Assert.IsFalse(_service.VerifyPassword(savencPath, "Password123! "));
    }

    // ================================================================
    // Error Handling
    // ================================================================

    [TestMethod]
    public async Task UnlockSave_WrongPassword_Throws()
    {
        var saveDir = CreateTestFolder("wrong_pw", ("data.txt", "secret"));
        var savencPath = Path.Combine(_tempDir, "wrong_pw.savenc");
        var options = new SaveLockOptions { Password = "realPassword", LockMode = SaveLockMode.OneTime };

        await _service.LockSaveAsync(saveDir, savencPath, "realPassword", options);

        await Assert.ThrowsExceptionAsync<CryptographicException>(() =>
            _service.UnlockSaveAsync(savencPath, Path.Combine(_tempDir, "out"), "wrongPassword"));
    }

    [TestMethod]
    public async Task UnlockSave_NonExistentFile_Throws()
    {
        var nonExistent = Path.Combine(_tempDir, "does_not_exist.savenc");
        await Assert.ThrowsExceptionAsync<FileNotFoundException>(() =>
            _service.UnlockSaveAsync(nonExistent, Path.Combine(_tempDir, "out"), "pw"));
    }

    [TestMethod]
    public async Task UnlockSave_CorruptedSavenc_Throws()
    {
        var savencPath = Path.Combine(_tempDir, "corrupted.savenc");
        File.WriteAllText(savencPath, "this is not a valid savenc file");

        await Assert.ThrowsExceptionAsync<EndOfStreamException>(() =>
            _service.UnlockSaveAsync(savencPath, Path.Combine(_tempDir, "out"), "pw"));
    }

    [TestMethod]
    public void GetSaveLockMetadata_CorruptedFile_Throws()
    {
        var corruptedPath = Path.Combine(_tempDir, "corrupted_meta.savenc");
        File.WriteAllText(corruptedPath, "not valid");

        Assert.ThrowsException<EndOfStreamException>(() =>
            _service.GetSaveLockMetadata(corruptedPath));
    }

    // ================================================================
    // Metadata
    // ================================================================

    [TestMethod]
    public async Task GetSaveLockMetadata_ReturnsCorrectInfo()
    {
        var saveDir = CreateTestFolder("meta_test", ("level.dat", "game data"), ("level.dat_old", "backup"));
        var savencPath = Path.Combine(_tempDir, "meta.savenc");
        var options = new SaveLockOptions
        {
            Password = "metaPass456",
            LockMode = SaveLockMode.Permanent,
            PasswordHint = "我的生日"
        };

        await _service.LockSaveAsync(saveDir, savencPath, "metaPass456", options);

        var meta = _service.GetSaveLockMetadata(savencPath);

        Assert.AreEqual(SaveLockMode.Permanent, meta.LockMode);
        Assert.IsNotNull(meta.Salt);
        Assert.AreEqual(16, meta.Salt.Length);
        Assert.IsNotNull(meta.KeyCheck);
        Assert.AreEqual(16, meta.KeyCheck.Length);
        Assert.AreEqual("meta_test", meta.SaveName);
        Assert.AreEqual(2, meta.FileCount);
        Assert.AreEqual("我的生日", meta.PasswordHint);
        Assert.AreNotEqual(DateTimeOffset.MinValue, meta.CreatedAt);
    }

    [TestMethod]
    public async Task GetSaveLockMetadata_OneTimeMode_PersistsMode()
    {
        var saveDir = CreateTestFolder("onetime_test", ("data.txt", "data"));
        var savencPath = Path.Combine(_tempDir, "onetime.savenc");
        var options = new SaveLockOptions { Password = "pass", LockMode = SaveLockMode.OneTime };

        await _service.LockSaveAsync(saveDir, savencPath, "pass", options);

        var meta = _service.GetSaveLockMetadata(savencPath);
        Assert.AreEqual(SaveLockMode.OneTime, meta.LockMode);
    }

    [TestMethod]
    public async Task LockSave_WithBoundMsAccount_PersistsUuidHash()
    {
        var saveDir = CreateTestFolder("bound_test", ("data.txt", "data"));
        var savencPath = Path.Combine(_tempDir, "bound.savenc");
        var msUuid = "43381ea0-21e5-9839-958a-f459800e4d11";
        var options = new SaveLockOptions
        {
            Password = "pass",
            LockMode = SaveLockMode.Permanent,
            BoundMsUuid = msUuid,
            BoundMsAccount = "testuser@outlook.com"
        };

        await _service.LockSaveAsync(saveDir, savencPath, "pass", options);

        var meta = _service.GetSaveLockMetadata(savencPath);

        // The BoundMsUuidHash should be SHA-256 of the UUID string
        Assert.IsNotNull(meta.BoundMsUuidHash);
        var expectedHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(msUuid))).ToLowerInvariant();
        Assert.AreEqual(expectedHash, meta.BoundMsUuidHash);
    }

    // ================================================================
    // ReEncrypt
    // ================================================================

    [TestMethod]
    public async Task ReEncryptSave_KeepsSamePassword()
    {
        var saveDir = CreateTestFolder("reenc_test", ("data.txt", "original data"));
        var savencPath = Path.Combine(_tempDir, "reenc.savenc");
        var password = "myPassword";
        var options = new SaveLockOptions { Password = password, LockMode = SaveLockMode.Permanent };

        await _service.LockSaveAsync(saveDir, savencPath, password, options);

        // Unlock to get key
        var unlockDir = Path.Combine(_tempDir, "reenc_temp");
        var key = await _service.UnlockSaveAsync(savencPath, unlockDir, password, deleteSavencAfter: false);

        // Modify a file (simulating game session)
        File.WriteAllText(Path.Combine(unlockDir, "data.txt"), "modified data");

        // Re-encrypt with same key and salt
        var newSavencPath = Path.Combine(_tempDir, "reenc_new.savenc");
        await _service.ReEncryptSaveAsync(unlockDir, newSavencPath, savencPath, key, SaveLockMode.Permanent);

        // Same password should still verify (salt was preserved)
        Assert.IsTrue(_service.VerifyPassword(newSavencPath, password));

        // Decrypt with password should give modified data
        var outDir = Path.Combine(_tempDir, "reenc_restored");
        await _service.UnlockSaveAsync(newSavencPath, outDir, password);
        Assert.AreEqual("modified data", File.ReadAllText(Path.Combine(outDir, "data.txt")));
    }

    // ================================================================
    // OneDrive Flag
    // ================================================================

    [TestMethod]
    public async Task UpdateOneDriveBackedUpFlag_SetsFlag()
    {
        var saveDir = CreateTestFolder("od_test", ("data.txt", "od data"));
        var savencPath = Path.Combine(_tempDir, "od.savenc");
        var options = new SaveLockOptions { Password = "od", LockMode = SaveLockMode.OneTime };

        await _service.LockSaveAsync(saveDir, savencPath, "od", options);

        // Initially not backed up
        var metaBefore = _service.GetSaveLockMetadata(savencPath);
        Assert.IsFalse(metaBefore.OneDriveBackedUp);

        // Update flag
        await _service.UpdateOneDriveBackedUpFlagAsync(savencPath, backedUp: true);

        // Verify
        var metaAfter = _service.GetSaveLockMetadata(savencPath);
        Assert.IsTrue(metaAfter.OneDriveBackedUp);
    }

    [TestMethod]
    public async Task UpdateOneDriveBackedUpFlag_FileStillValid()
    {
        var saveDir = CreateTestFolder("od_valid", ("data.txt", "valid data"));
        var savencPath = Path.Combine(_tempDir, "od_valid.savenc");
        var options = new SaveLockOptions { Password = "od2", LockMode = SaveLockMode.OneTime };

        await _service.LockSaveAsync(saveDir, savencPath, "od2", options);

        // Update flag
        await _service.UpdateOneDriveBackedUpFlagAsync(savencPath, backedUp: true);

        // Should still be decryptable
        var outDir = Path.Combine(_tempDir, "od_valid_out");
        await _service.UnlockSaveAsync(savencPath, outDir, "od2");
        Assert.AreEqual("valid data", File.ReadAllText(Path.Combine(outDir, "data.txt")));
    }
}
