using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Orchestrators;
using EiTRVO.Tests.Fakes;

namespace EiTRVO.Tests.Orchestrators;

[TestClass]
public class BackupServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FakeGameFolderService _gameFolder;
    private readonly BackupService _service;

    public BackupServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"eitrvo_backup_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _gameFolder = new FakeGameFolderService { GameDir = _tempDir };
        _service = new BackupService(_gameFolder);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private void CreateTestFiles(string subDir, params (string Name, string Content)[] files)
    {
        var dir = Path.Combine(_tempDir, subDir);
        Directory.CreateDirectory(dir);
        foreach (var (name, content) in files)
        {
            var filePath = Path.Combine(dir, name);
            var parentDir = Path.GetDirectoryName(filePath);
            if (parentDir != null && !Directory.Exists(parentDir))
                Directory.CreateDirectory(parentDir);
            File.WriteAllText(filePath, content);
        }
    }

    // ================================================================
    // ShouldBackup — static pure function
    // ================================================================

    [TestMethod]
    public void ShouldBackup_EveryLaunch_AlwaysTrue()
    {
        Assert.IsTrue(BackupService.ShouldBackup(BackupInterval.EveryLaunch, null));
        Assert.IsTrue(BackupService.ShouldBackup(BackupInterval.EveryLaunch, DateTimeOffset.UtcNow));
        Assert.IsTrue(BackupService.ShouldBackup(BackupInterval.EveryLaunch,
            DateTimeOffset.UtcNow.AddHours(-1)));
    }

    [TestMethod]
    public void ShouldBackup_NoLastBackup_ReturnsTrue()
    {
        Assert.IsTrue(BackupService.ShouldBackup(BackupInterval.Daily, null));
        Assert.IsTrue(BackupService.ShouldBackup(BackupInterval.Weekly, null));
        Assert.IsTrue(BackupService.ShouldBackup(BackupInterval.Monthly, null));
    }

    [TestMethod]
    public void ShouldBackup_Daily_Within24h_ReturnsFalse()
    {
        var justNow = DateTimeOffset.UtcNow.AddMinutes(-5);
        Assert.IsFalse(BackupService.ShouldBackup(BackupInterval.Daily, justNow));
    }

    [TestMethod]
    public void ShouldBackup_Daily_Exceeded24h_ReturnsTrue()
    {
        var yesterday = DateTimeOffset.UtcNow.AddDays(-2);
        Assert.IsTrue(BackupService.ShouldBackup(BackupInterval.Daily, yesterday));
    }

    [TestMethod]
    public void ShouldBackup_Weekly_Within7d_ReturnsFalse()
    {
        var threeDaysAgo = DateTimeOffset.UtcNow.AddDays(-3);
        Assert.IsFalse(BackupService.ShouldBackup(BackupInterval.Weekly, threeDaysAgo));
    }

    [TestMethod]
    public void ShouldBackup_Weekly_Exceeded7d_ReturnsTrue()
    {
        var eightDaysAgo = DateTimeOffset.UtcNow.AddDays(-8);
        Assert.IsTrue(BackupService.ShouldBackup(BackupInterval.Weekly, eightDaysAgo));
    }

    [TestMethod]
    public void ShouldBackup_Monthly_SameMonth_ReturnsFalse()
    {
        // Same calendar month should not trigger
        var fiveDaysAgo = DateTimeOffset.UtcNow.AddDays(-5);
        Assert.IsFalse(BackupService.ShouldBackup(BackupInterval.Monthly, fiveDaysAgo));
    }

    [TestMethod]
    public void ShouldBackup_Monthly_DifferentMonth_ReturnsTrue()
    {
        // Previous calendar month should trigger
        var fortyDaysAgo = DateTimeOffset.UtcNow.AddDays(-40);
        Assert.IsTrue(BackupService.ShouldBackup(BackupInterval.Monthly, fortyDaysAgo));
    }

    [TestMethod]
    public void ShouldBackup_Daily_YesterdayAt2359_ReturnsTrue()
    {
        var yesterdayLate = DateTimeOffset.UtcNow.Date.AddSeconds(-1);
        Assert.IsTrue(BackupService.ShouldBackup(BackupInterval.Daily, yesterdayLate));
    }

    // ================================================================
    // ValidateBackupFile — static
    // ================================================================

    [TestMethod]
    public void ValidateBackupFile_NonExistent_ReturnsFalse()
    {
        Assert.IsFalse(BackupService.ValidateBackupFile(Path.Combine(_tempDir, "nope.eibak")));
    }

    [TestMethod]
    public void ValidateBackupFile_CorruptedFile_ReturnsFalse()
    {
        var badPath = Path.Combine(_tempDir, "bad.eibak");
        File.WriteAllText(badPath, "not a valid backup");

        Assert.IsFalse(BackupService.ValidateBackupFile(badPath));
    }

    [TestMethod]
    public void ValidateBackupFile_EmptyFile_ReturnsFalse()
    {
        var emptyPath = Path.Combine(_tempDir, "empty.eibak");
        File.WriteAllText(emptyPath, "");

        Assert.IsFalse(BackupService.ValidateBackupFile(emptyPath));
    }

    // ================================================================
    // Backup → Restore Round-Trip
    // ================================================================

    [TestMethod]
    public void Backup_Then_Restore_RoundTrip()
    {
        CreateTestFiles("saves/world", ("level.dat", "level content"));
        CreateTestFiles("saves/world/playerdata", ("player1.dat", "player data"));

        var backupDir = Path.Combine(_tempDir, "backups");
        var result = _service.BackupAsync(backupDir, excludeRedownloadable: true,
            CancellationToken.None).GetAwaiter().GetResult();

        Assert.IsTrue(result.Success);
        Assert.IsTrue(File.Exists(result.OutputPath));

        // Delete original and restore
        Directory.Delete(Path.Combine(_tempDir, "saves"), true);
        Assert.IsFalse(Directory.Exists(Path.Combine(_tempDir, "saves")));

        var restoreResult = _service.RestoreAsync(result.OutputPath,
            RestoreMode.Clean, CancellationToken.None).GetAwaiter().GetResult();

        Assert.IsTrue(restoreResult.Success);
        // Restores to _gameFolder.GameDir (_tempDir)
        Assert.IsTrue(File.Exists(Path.Combine(_tempDir, "saves/world/level.dat")));
        Assert.IsTrue(File.Exists(Path.Combine(_tempDir, "saves/world/playerdata/player1.dat")));
        Assert.AreEqual("level content",
            File.ReadAllText(Path.Combine(_tempDir, "saves/world/level.dat")));
        Assert.AreEqual("player data",
            File.ReadAllText(Path.Combine(_tempDir, "saves/world/playerdata/player1.dat")));
    }

    [TestMethod]
    public void Backup_EmptyGameDir_Succeeds()
    {
        var backupDir = Path.Combine(_tempDir, "backups");

        var result = _service.BackupAsync(backupDir, excludeRedownloadable: true,
            CancellationToken.None).GetAwaiter().GetResult();

        Assert.IsTrue(result.Success);
        Assert.IsTrue(File.Exists(result.OutputPath));
    }

    [TestMethod]
    public void Backup_CreatesTimestampedFile()
    {
        CreateTestFiles("data", ("test.txt", "hello"));
        var backupDir = Path.Combine(_tempDir, "backups");

        var result = _service.BackupAsync(backupDir, excludeRedownloadable: true,
            CancellationToken.None).GetAwaiter().GetResult();

        Assert.IsTrue(result.Success);
        StringAssert.Contains(result.OutputPath, "EiTRVO_Backup_");
        StringAssert.EndsWith(result.OutputPath, ".eibak");
    }
}
