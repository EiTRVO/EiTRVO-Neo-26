using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EiTRVO.ProEngine.Models;

namespace EiTRVO.ProEngine.Orchestrators;

/// <summary>
/// 账号管理器 — DPAPI 加密持久化 + 账号 CRUD。
/// 单一 ObservableCollection 作为所有 ViewModel 的数据来源。
/// </summary>
public class AccountManager
{
    private readonly IGameFolderService _gameFolder;

    /// <summary>单一来源 — HomeViewModel 和 AccountViewModel 都绑定此集合。</summary>
    public ObservableCollection<Account> Accounts { get; } = new();

    public AccountManager(IGameFolderService gameFolder)
    {
        _gameFolder = gameFolder;
    }

    public void Load()
    {
        Accounts.Clear();
        string path = _gameFolder.AccountsFile;
        if (!File.Exists(path)) return;
        try
        {
            byte[] encrypted = File.ReadAllBytes(path);
            byte[] plaintext = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(plaintext);
            var accounts = JsonSerializer.Deserialize<List<Account>>(json);
            if (accounts != null)
            {
                foreach (var acc in accounts)
                    Accounts.Add(acc);
            }
        }
        catch { /* 损坏的账号文件 — 静默忽略 */ }
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(Accounts.ToList(),
                new JsonSerializerOptions { WriteIndented = true });
            byte[] plaintext = Encoding.UTF8.GetBytes(json);
            byte[] encrypted = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(_gameFolder.AccountsFile, encrypted);
        }
        catch { /* best-effort save */ }
    }

    public void Add(Account account)
    {
        Accounts.Add(account);
        Save();
    }

    public void Remove(string uuid)
    {
        var account = Accounts.FirstOrDefault(a => a.UUID == uuid);
        if (account != null)
        {
            Accounts.Remove(account);
            Save();
        }
    }

    public Account? FindByUuid(string uuid)
        => Accounts.FirstOrDefault(a => a.UUID == uuid);
}
