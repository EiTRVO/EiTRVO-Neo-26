using System.Text.Json;
using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Services;

namespace EiTRVO.Tests.Services;

[TestClass]
public class SettingsServiceTests : IDisposable
{
    private readonly string _tempDir;

    public SettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"eitrvo_settings_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ================================================================
    // Save → Load Round-Trip
    // ================================================================

    [TestMethod]
    public void Save_Then_Load_RoundTrip()
    {
        var settings = new LauncherSettings
        {
            MemoryMB = 8192,
            Resolution = "2560x1440",
            JavaPath = @"C:\Program Files\Java\jdk-21\bin\javaw.exe",
            UseAutoDetectJava = true,
            IsolateNewInstancesByDefault = true,
            SettingsLockEnabled = false,
            FirewallEnabled = true,
            BackupEnabled = true,
            BackupInterval = BackupInterval.Weekly,
            BackupFolder = @"D:\Backups",
            ExcludeRedownloadable = true
        };

        SettingsService.Save(_tempDir, settings);
        var loaded = SettingsService.Load(_tempDir);

        Assert.AreEqual(8192, loaded.MemoryMB);
        Assert.AreEqual("2560x1440", loaded.Resolution);
        Assert.AreEqual(@"C:\Program Files\Java\jdk-21\bin\javaw.exe", loaded.JavaPath);
        Assert.IsTrue(loaded.UseAutoDetectJava);
        Assert.IsTrue(loaded.IsolateNewInstancesByDefault);
        Assert.IsFalse(loaded.SettingsLockEnabled);
        Assert.IsTrue(loaded.FirewallEnabled);
        Assert.IsTrue(loaded.BackupEnabled);
        Assert.AreEqual(BackupInterval.Weekly, loaded.BackupInterval);
        Assert.AreEqual(@"D:\Backups", loaded.BackupFolder);
        Assert.IsTrue(loaded.ExcludeRedownloadable);
    }

    [TestMethod]
    public void Load_FileNotExists_ReturnsDefaults()
    {
        var loaded = SettingsService.Load(_tempDir);

        Assert.IsNotNull(loaded);
        Assert.IsNull(loaded.Resolution);
    }

    [TestMethod]
    public void Load_CorruptedJson_ReturnsDefaults()
    {
        File.WriteAllText(Path.Combine(_tempDir, "settings.json"), "{invalid json!!!");

        var loaded = SettingsService.Load(_tempDir);

        Assert.IsNotNull(loaded);
    }

    [TestMethod]
    public void Save_CreatesSettingsFile()
    {
        var settings = new LauncherSettings { MemoryMB = 4096 };
        SettingsService.Save(_tempDir, settings);

        Assert.IsTrue(File.Exists(Path.Combine(_tempDir, "settings.json")));
    }

    [TestMethod]
    public void Save_PartialUpdate_PreserversOtherFields()
    {
        var original = new LauncherSettings
        {
            MemoryMB = 8192,
            Resolution = "1920x1080",
            BackupEnabled = true,
            BackupInterval = BackupInterval.Daily
        };
        SettingsService.Save(_tempDir, original);

        // Load and modify one field
        var modified = SettingsService.Load(_tempDir);
        modified.MemoryMB = 16384;
        SettingsService.Save(_tempDir, modified);

        var reloaded = SettingsService.Load(_tempDir);
        Assert.AreEqual(16384, reloaded.MemoryMB);
        Assert.AreEqual("1920x1080", reloaded.Resolution); // Preserved
        Assert.IsTrue(reloaded.BackupEnabled); // Preserved
        Assert.AreEqual(BackupInterval.Daily, reloaded.BackupInterval); // Preserved
    }
}
