using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Orchestrators;
using EiTRVO.Tests.Fakes;

namespace EiTRVO.Tests.Orchestrators;

[TestClass]
public class AccountManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FakeGameFolderService _gameFolder;
    private readonly AccountManager _manager;

    public AccountManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"eitrvo_acct_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _gameFolder = new FakeGameFolderService { GameDir = _tempDir };
        _manager = new AccountManager(_gameFolder);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ================================================================
    // Load — empty / not found
    // ================================================================

    [TestMethod]
    public void Load_FileNotExists_EmptyCollection()
    {
        _manager.Load();
        Assert.AreEqual(0, _manager.Accounts.Count);
    }

    // ================================================================
    // Add + persistence
    // ================================================================

    [TestMethod]
    public void Add_MicrosoftAccount_IncreasesCount()
    {
        var account = new Account
        {
            Type = AccountType.Microsoft,
            Username = "TestPlayer",
            UUID = "uuid-ms-123",
            MicrosoftRefreshToken = "refresh-token-abc"
        };

        _manager.Add(account);

        Assert.AreEqual(1, _manager.Accounts.Count);
        Assert.AreEqual("TestPlayer", _manager.Accounts[0].Username);
    }

    [TestMethod]
    public void Add_YggdrasilAccount_Persists()
    {
        var account = new Account
        {
            Type = AccountType.Yggdrasil,
            Username = "YggPlayer",
            UUID = "uuid-yg-456",
            YggdrasilAccessToken = "yg-token",
            YggdrasilServerUrl = "https://littleskin.cn/api/yggdrasil"
        };

        _manager.Add(account);

        Assert.AreEqual(1, _manager.Accounts.Count);
        Assert.AreEqual(AccountType.Yggdrasil, _manager.Accounts[0].Type);
    }

    [TestMethod]
    public void Add_SavesToFile()
    {
        var account = new Account
        {
            Type = AccountType.Microsoft,
            Username = "PersistTest",
            UUID = "uuid-persist"
        };

        _manager.Add(account);

        // Create a new AccountManager pointing to same dir
        var manager2 = new AccountManager(_gameFolder);
        manager2.Load();

        Assert.AreEqual(1, manager2.Accounts.Count);
        Assert.AreEqual("PersistTest", manager2.Accounts[0].Username);
    }

    // ================================================================
    // Remove
    // ================================================================

    [TestMethod]
    public void Remove_Existing_DecreasesCount()
    {
        var account = new Account { Username = "ToRemove", UUID = "uuid-rm", Type = AccountType.Microsoft };
        _manager.Add(account);

        Assert.AreEqual(1, _manager.Accounts.Count);

        _manager.Remove("uuid-rm");
        Assert.AreEqual(0, _manager.Accounts.Count);
    }

    [TestMethod]
    public void Remove_NonExisting_DoesNotThrow()
    {
        _manager.Remove("nonexistent-uuid");
        Assert.AreEqual(0, _manager.Accounts.Count);
    }

    [TestMethod]
    public void Remove_PersistsToFile()
    {
        var account = new Account { Username = "Keep", UUID = "uuid-keep", Type = AccountType.Microsoft };
        _manager.Add(account);

        _manager.Remove("uuid-keep");

        var manager2 = new AccountManager(_gameFolder);
        manager2.Load();
        Assert.AreEqual(0, manager2.Accounts.Count);
    }

    // ================================================================
    // FindByUuid
    // ================================================================

    [TestMethod]
    public void FindByUuid_Exists_ReturnsAccount()
    {
        var account = new Account { Username = "FindMe", UUID = "uuid-find", Type = AccountType.Microsoft };
        _manager.Add(account);

        var found = _manager.FindByUuid("uuid-find");
        Assert.IsNotNull(found);
        Assert.AreEqual("FindMe", found!.Username);
    }

    [TestMethod]
    public void FindByUuid_NotExists_ReturnsNull()
    {
        var found = _manager.FindByUuid("no-such-uuid");
        Assert.IsNull(found);
    }

    // ================================================================
    // Multiple accounts
    // ================================================================

    [TestMethod]
    public void Mixed_MicrosoftAndYggdrasil()
    {
        _manager.Add(new Account { Username = "MSPlayer", UUID = "uuid-1", Type = AccountType.Microsoft });
        _manager.Add(new Account { Username = "YGPlayer", UUID = "uuid-2", Type = AccountType.Yggdrasil });

        Assert.AreEqual(2, _manager.Accounts.Count);

        var ms = _manager.FindByUuid("uuid-1");
        var yg = _manager.FindByUuid("uuid-2");

        Assert.AreEqual(AccountType.Microsoft, ms!.Type);
        Assert.AreEqual(AccountType.Yggdrasil, yg!.Type);
    }
}
