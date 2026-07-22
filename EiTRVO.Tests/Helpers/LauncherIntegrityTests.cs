using EiTRVO.ProEngine.Helpers;

namespace EiTRVO.Tests.Helpers;

[TestClass]
public class LauncherIntegrityTests : IDisposable
{
    private readonly string _tempDir;

    public LauncherIntegrityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"eitrvo_integrity_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [TestMethod]
    public void ComputeCurrentHash_ReturnsNonEmptyHex()
    {
        string hash = LauncherIntegrity.ComputeCurrentHash();

        Assert.IsNotNull(hash);
        Assert.AreEqual(64, hash.Length); // SHA-256 = 64 hex chars
        Assert.IsTrue(hash.All(c => char.IsAsciiHexDigit(c)));
    }

    [TestMethod]
    public void Verify_FirstLaunch_ReturnsNull()
    {
        // Clean temp dir = no baseline file
        string result = LauncherIntegrity.Verify(_tempDir);

        Assert.IsNull(result); // Should succeed silently
    }

    [TestMethod]
    public void Verify_AfterFirstLaunch_ReturnsNull()
    {
        // First call establishes baseline
        LauncherIntegrity.Verify(_tempDir);

        // Second call should still return null (hash matches)
        string result = LauncherIntegrity.Verify(_tempDir);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void UpdateBaseline_ThenVerify_ReturnsNull()
    {
        LauncherIntegrity.UpdateBaseline(_tempDir);

        string result = LauncherIntegrity.Verify(_tempDir);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void Verify_HashMismatch_ReturnsWarning()
    {
        // First establish a valid baseline
        LauncherIntegrity.Verify(_tempDir);

        // Tamper: overwrite the baseline with a DPAPI-encrypted wrong hash
        string baselinePath = Path.Combine(_tempDir, "eitrvo", "launcher.hash");
        byte[] salt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
        byte[] fakeHash = System.Text.Encoding.UTF8.GetBytes("0000000000000000000000000000000000000000000000000000000000000000");
        byte[] encrypted = System.Security.Cryptography.ProtectedData.Protect(
            fakeHash, salt, System.Security.Cryptography.DataProtectionScope.CurrentUser);

        string tmpPath = baselinePath + ".tmp";
        using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.Write(salt, 0, salt.Length);
            fs.Write(encrypted, 0, encrypted.Length);
        }
        File.Move(tmpPath, baselinePath, overwrite: true);

        // Verify should now detect mismatch
        string result = LauncherIntegrity.Verify(_tempDir);

        Assert.IsNotNull(result, "Should return warning on hash mismatch");
        StringAssert.Contains(result, "修改");
    }

    [TestMethod]
    public void Verify_CorruptedBaseline_ReestablishesAndLogs()
    {
        // First establish a valid baseline
        LauncherIntegrity.Verify(_tempDir);

        // Corrupt the baseline file
        string baselinePath = Path.Combine(_tempDir, "eitrvo", "launcher.hash");
        File.WriteAllText(baselinePath, "garbage data that cannot be decrypted");

        // Hook up log callback
        string? logMessage = null;
        LauncherIntegrity.LogCallback = msg => logMessage = msg;

        try
        {
            // Verify should silently re-establish and log
            string result = LauncherIntegrity.Verify(_tempDir);

            Assert.IsNull(result, "Corrupted baseline should be silently re-established");
            Assert.IsNotNull(logMessage, "Should have logged the corruption event");
            StringAssert.Contains(logMessage, "损坏");
        }
        finally
        {
            LauncherIntegrity.LogCallback = null;
        }
    }

    [TestMethod]
    public void Verify_FirstLaunch_NoLogCallbackTriggered()
    {
        // Clean temp dir — no baseline exists
        string? logMessage = null;
        LauncherIntegrity.LogCallback = msg => logMessage = msg;

        try
        {
            string result = LauncherIntegrity.Verify(_tempDir);

            Assert.IsNull(result);
            Assert.IsNull(logMessage, "First launch (no baseline file) should NOT trigger corruption log");
        }
        finally
        {
            LauncherIntegrity.LogCallback = null;
        }
    }
}
