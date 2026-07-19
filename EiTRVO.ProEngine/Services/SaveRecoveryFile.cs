using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace EiTRVO.ProEngine.Services;

/// <summary>
/// 本地恢复文件 (.savrec) 的导出/导入。
/// .savrec 文件将 AES 密钥 K 用 K_ms 包裹，K_ms 由用户 MS UUID 派生。
/// 注册为 Singleton。
/// </summary>
public class SaveRecoveryFile
{
    // .savrec 格式常量
    private static readonly byte[] MagicNumber = "SAVR"u8.ToArray();
    private const byte FormatVersion = 0x01;
    private const int IvSize = 16;
    private const int SaltSize = 32;
    private const int Pbkdf2Iterations = 10_000;
    private static readonly byte[] FixedSalt = "EiTRVO.SaveRecovery.v1"u8.ToArray();
    private const int AesKeySize = 32;
    private const int TrailerSize = 32;

    /// <summary>
    /// 导出 .savrec 恢复文件。
    /// K 被 K_ms = PBKDF2(MS_UUID, "EiTRVO.SaveRecovery.v1", 10000) 包裹。
    /// </summary>
    /// <param name="outputPath">输出文件路径</param>
    /// <param name="instanceName">实例名称</param>
    /// <param name="saveName">存档名称</param>
    /// <param name="aesKey">AES-256 密钥 K（32 字节）</param>
    /// <param name="msUuid">用户 Minecraft UUID</param>
    public async Task ExportAsync(string outputPath, string instanceName, string saveName,
        byte[] aesKey, string msUuid, CancellationToken ct = default)
    {
        // 1. 生成随机 IV 和 Salt
        byte[] iv = RandomNumberGenerator.GetBytes(IvSize);
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);

        // 2. 派生包裹密钥 K_ms（使用随机 salt + 固定 domain separation）
        byte[] wrappingKey = DeriveWrappingKey(msUuid, salt);

        try
        {
            // 3. 构建 payload JSON
            var payload = new RecoveryPayload
            {
                AesKey = Convert.ToBase64String(aesKey),
                InstanceName = instanceName,
                SaveName = saveName,
                CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
                Version = 1
            };
            byte[] payloadJson = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));

            // 4. AES-256-CBC 加密 payload
            byte[] ciphertext;
            using (var aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = wrappingKey;
                aes.IV = iv;
                using var encryptor = aes.CreateEncryptor();
                ciphertext = encryptor.TransformFinalBlock(payloadJson, 0, payloadJson.Length);
            }

            // 5. 计算 BoundMsUuidHash
            byte[] msUuidHash = SHA256.HashData(Encoding.UTF8.GetBytes(msUuid));

            // 6. 写入文件
            string tmpPath = outputPath + ".tmp";
            await using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                // Header
                fs.Write(MagicNumber);
                fs.WriteByte(FormatVersion);
                fs.Write(iv);
                fs.Write(salt);

                // MsUuidLen + BoundMsUuidHash
                byte[] msUuidBytes = Encoding.UTF8.GetBytes(msUuid);
                ushort uuidLen = (ushort)msUuidBytes.Length;
                fs.WriteByte((byte)((uuidLen >> 8) & 0xFF));
                fs.WriteByte((byte)(uuidLen & 0xFF));
                fs.Write(msUuidBytes);

                // Encrypted payload
                fs.Write(ciphertext);
            }

            // 7. 计算并写入 SHA-256 Trailer
            byte[] trailer = await ComputeTrailerAsync(tmpPath);
            await using (var fs = new FileStream(tmpPath, FileMode.Append, FileAccess.Write, FileShare.None))
            {
                fs.Write(trailer);
            }

            // 8. 原子重命名
            if (File.Exists(outputPath))
                File.Delete(outputPath);
            File.Move(tmpPath, outputPath);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
        }
    }

    /// <summary>
    /// 从 .savrec 文件导入 AES 密钥。
    /// </summary>
    /// <returns>(key, saveName, instanceName)，验证失败返回 null</returns>
    public async Task<(byte[] key, string saveName, string instanceName)?> ImportAsync(
        string recoveryFilePath, string msUuid, CancellationToken ct = default)
    {
        // 1. 读取文件
        await using var fs = new FileStream(recoveryFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        // 验证 Magic
        byte[] magicBuf = new byte[4];
        fs.ReadExactly(magicBuf);
        if (!magicBuf.SequenceEqual(MagicNumber))
            return null;

        // Version
        int version = fs.ReadByte();
        if (version != FormatVersion)
            return null;

        // IV
        byte[] iv = new byte[IvSize];
        fs.ReadExactly(iv);

        // Salt
        byte[] salt = new byte[SaltSize];
        fs.ReadExactly(salt);

        // MsUuidLen
        byte[] uuidLenBuf = new byte[2];
        fs.ReadExactly(uuidLenBuf);
        int uuidLen = (uuidLenBuf[0] << 8) | uuidLenBuf[1];

        // BoundMsUuid
        byte[] boundMsUuid = new byte[uuidLen];
        fs.ReadExactly(boundMsUuid);
        string boundUuid = Encoding.UTF8.GetString(boundMsUuid);

        // 验证 UUID
        byte[] expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(msUuid));
        byte[] boundHash = SHA256.HashData(Encoding.UTF8.GetBytes(boundUuid));
        if (!CryptographicOperations.FixedTimeEquals(expectedHash, boundHash))
            return null; // UUID 不匹配

        // 读取剩余密文（文件剩余 = 总长 - 当前位置 - Trailer）
        long remaining = fs.Length - fs.Position - TrailerSize;
        if (remaining <= 0 || remaining > 100_000_000)
            return null; // 文件损坏或异常大小
        byte[] ciphertext = new byte[remaining];
        fs.ReadExactly(ciphertext);

        // 2. 派生包裹密钥 K_ms（使用文件中存储的随机 salt）
        byte[] wrappingKey = DeriveWrappingKey(msUuid, salt);

        try
        {
            // 3. 解密 payload
            byte[] plaintext;
            using (var aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = wrappingKey;
                aes.IV = iv;

                using var decryptor = aes.CreateDecryptor();
                plaintext = decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
            }

            // 4. 反序列化
            string json = Encoding.UTF8.GetString(plaintext);
            var payload = JsonSerializer.Deserialize<RecoveryPayload>(json);
            if (payload == null || string.IsNullOrEmpty(payload.AesKey))
                return null;

            byte[] key = Convert.FromBase64String(payload.AesKey);
            return (key, payload.SaveName ?? "", payload.InstanceName ?? "");
        }
        catch (CryptographicException)
        {
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
        }
    }

    /// <summary>
    /// 验证 .savrec 文件格式完整性（Magic + Version + Trailer）。
    /// </summary>
    public bool Validate(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Span<byte> magic = stackalloc byte[4];
            fs.ReadExactly(magic);
            if (!magic.SequenceEqual(MagicNumber))
                return false;

            int version = fs.ReadByte();
            if (version != FormatVersion)
                return false;

            // 检查文件大小足够容纳 Trailer
            if (fs.Length < TrailerSize)
                return false;

            return true;
        }
        catch
        {
            return false;
        }
    }

    // ==================== 内部方法 ====================

    /// <summary>
    /// K_ms = PBKDF2(MS_UUID, FixedSalt || randomSalt, 10000, SHA-256)
    /// Domain-separation via fixed prefix + per-file random salt for defense-in-depth.
    /// </summary>
    private static byte[] DeriveWrappingKey(string msUuid, byte[] randomSalt)
    {
        // Combine fixed domain-separation salt with per-file random salt
        byte[] combinedSalt = new byte[FixedSalt.Length + randomSalt.Length];
        Buffer.BlockCopy(FixedSalt, 0, combinedSalt, 0, FixedSalt.Length);
        Buffer.BlockCopy(randomSalt, 0, combinedSalt, FixedSalt.Length, randomSalt.Length);

        using var pbkdf2 = new Rfc2898DeriveBytes(
            msUuid, combinedSalt, Pbkdf2Iterations, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(AesKeySize);
    }

    private static async Task<byte[]> ComputeTrailerAsync(string filePath)
    {
        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return await SHA256.HashDataAsync(fs);
    }

    private class RecoveryPayload
    {
        public string? AesKey { get; set; }
        public string? InstanceName { get; set; }
        public string? SaveName { get; set; }
        public string? CreatedAt { get; set; }
        public int Version { get; set; }
    }
}
