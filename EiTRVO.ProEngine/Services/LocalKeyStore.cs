using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using EiTRVO.ProEngine.Orchestrators;

namespace EiTRVO.ProEngine.Services;

/// <summary>
/// 本地密钥存储 — 用 DPAPI 加密保存 AES 密钥到本地，替代 OneDrive 云端备份。
/// 路径：{GameDir}/eitrvo/savekeys/{instanceName}/{saveName}.key
/// </summary>
public class LocalKeyStore
{
    private readonly IGameFolderService _gameFolder;

    public LocalKeyStore(IGameFolderService gameFolder)
    {
        _gameFolder = gameFolder;
    }

    private string GetKeyPath(string instanceName, string saveName)
        => Path.Combine(_gameFolder.GameDir, "eitrvo", "savekeys",
            instanceName, $"{saveName}.key");

    /// <summary>DPAPI 加密保存 AES 密钥到本地</summary>
    public async Task SaveKeyAsync(string instanceName, string saveName, byte[] aesKey, CancellationToken ct = default)
    {
        var payload = new LocalKeyPayload
        {
            AesKey = Convert.ToBase64String(aesKey),
            InstanceName = instanceName,
            SaveName = saveName,
            CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
            Version = 1
        };

        byte[] plaintext = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        byte[] encrypted = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);

        string path = GetKeyPath(instanceName, saveName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        string tmpPath = path + ".tmp";
        await File.WriteAllBytesAsync(tmpPath, encrypted, ct);
        File.Move(tmpPath, path, overwrite: true);
    }

    /// <summary>DPAPI 解密读取本地存储的 AES 密钥</summary>
    public async Task<byte[]?> LoadKeyAsync(string instanceName, string saveName, CancellationToken ct = default)
    {
        string path = GetKeyPath(instanceName, saveName);
        if (!File.Exists(path)) return null;

        try
        {
            byte[] encrypted = await File.ReadAllBytesAsync(path, ct);
            byte[] plaintext = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            var payload = JsonSerializer.Deserialize<LocalKeyPayload>(
                Encoding.UTF8.GetString(plaintext));
            return payload?.AesKey != null ? Convert.FromBase64String(payload.AesKey) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>删除本地存储的密钥</summary>
    public void DeleteKey(string instanceName, string saveName)
    {
        string path = GetKeyPath(instanceName, saveName);
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    /// <summary>检查密钥是否已本地存储</summary>
    public bool KeyExists(string instanceName, string saveName)
        => File.Exists(GetKeyPath(instanceName, saveName));

    /// <summary>DPAPI 加密导出密钥到用户指定路径 (.savkey)</summary>
    public async Task ExportKeyToFileAsync(string outputPath, string instanceName,
        string saveName, byte[] aesKey, CancellationToken ct = default)
    {
        var payload = new LocalKeyPayload
        {
            AesKey = Convert.ToBase64String(aesKey),
            InstanceName = instanceName,
            SaveName = saveName,
            CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
            Version = 1
        };

        byte[] plaintext = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        byte[] encrypted = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);

        string tmpPath = outputPath + ".tmp";
        await File.WriteAllBytesAsync(tmpPath, encrypted, ct);
        File.Move(tmpPath, outputPath, overwrite: true);
    }

    /// <summary>DPAPI 解密从文件中导入密钥</summary>
    public async Task<byte[]?> ImportKeyFromFileAsync(string filePath, CancellationToken ct = default)
    {
        try
        {
            byte[] encrypted = await File.ReadAllBytesAsync(filePath, ct);
            byte[] plaintext = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            var payload = JsonSerializer.Deserialize<LocalKeyPayload>(
                Encoding.UTF8.GetString(plaintext));
            return payload?.AesKey != null ? Convert.FromBase64String(payload.AesKey) : null;
        }
        catch
        {
            return null;
        }
    }

    private class LocalKeyPayload
    {
        public string? AesKey { get; set; }
        public string? InstanceName { get; set; }
        public string? SaveName { get; set; }
        public string? CreatedAt { get; set; }
        public int Version { get; set; }
    }
}
