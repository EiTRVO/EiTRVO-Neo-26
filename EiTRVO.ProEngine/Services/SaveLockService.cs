using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EiTRVO.ProEngine.Models;

namespace EiTRVO.ProEngine.Services;

/// <summary>
/// 存档加密/解密的唯一入口。
/// 无状态 — 所有方法接受参数、返回结果，不持有内部状态。
/// 注册为 Singleton。
/// </summary>
public class SaveLockService
{
    // .savenc 格式常量
    private static readonly byte[] MagicNumber = "SAVL"u8.ToArray();
    private const byte FormatVersion = 0x01;
    private const int SaltSize = 16;          // 128-bit
    private const int KeyCheckSize = 16;      // encrypted "SAVELOCK_OK" = 1 AES block
    private const int IvSize = 16;            // 128-bit AES-CBC IV
    private const int AesKeySize = 32;        // 256-bit
    private const int HeaderFixedSize = 82;   // 4 + 1 + 1 + 16 + 16 + 12 + 32
    private const int TrailerSize = 32;       // SHA-256
    private const int Pbkdf2Iterations = 100_000;
    private const string KeyCheckPlaintext = "SAVELOCK_OK";

    // ==================== 元数据读取 ====================

    /// <summary>
    /// 读取 .savenc 文件的 Header 和 Metadata，返回 SaveLockMetadata。
    /// 不解密存档内容，仅读取头部信息。
    /// </summary>
    public async Task<SaveLockMetadata> GetSaveLockMetadataAsync(string savencPath)
    {
        await using var fs = new FileStream(savencPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return ReadMetadata(fs);
    }

    /// <summary>同步版本</summary>
    public SaveLockMetadata GetSaveLockMetadata(string savencPath)
    {
        using var fs = new FileStream(savencPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return ReadMetadata(fs);
    }

    private static SaveLockMetadata ReadMetadata(Stream stream)
    {
        // Header
        Span<byte> header = stackalloc byte[HeaderFixedSize];
        stream.ReadExactly(header);

        // 验证 Magic
        if (!header[..4].SequenceEqual(MagicNumber))
            throw new InvalidDataException("不是有效的 .savenc 文件（Magic 不匹配）");

        byte version = header[4];
        if (version != FormatVersion)
            throw new InvalidDataException($"不支持的 .savenc 格式版本: {version}");

        var lockMode = (SaveLockMode)header[5];
        byte[] salt = header.Slice(6, SaltSize).ToArray();
        byte[] keyCheck = header.Slice(22, KeyCheckSize).ToArray();

        // Reserved: 12 bytes (skip)
        byte[] boundMsUuidHash = header.Slice(50, 32).ToArray();

        // Metadata: 2-byte length prefix (big-endian) + UTF-8 JSON
        const int MaxMetadataStackAlloc = 4096;

        Span<byte> metaLenBuf = stackalloc byte[2];
        stream.ReadExactly(metaLenBuf);
        int metadataLength = (metaLenBuf[0] << 8) | metaLenBuf[1];

        byte[]? metaBytesRented = null;
        Span<byte> metaBytes = metadataLength <= MaxMetadataStackAlloc
            ? stackalloc byte[metadataLength]
            : (metaBytesRented = new byte[metadataLength]).AsSpan();
        stream.ReadExactly(metaBytes);
        string metaJson = Encoding.UTF8.GetString(metaBytes);

        var meta = JsonSerializer.Deserialize<SaveLockMetadataJson>(metaJson)
                   ?? new SaveLockMetadataJson();

        return new SaveLockMetadata
        {
            Version = version,
            LockMode = lockMode,
            Salt = salt,
            KeyCheck = keyCheck,
            BoundMsUuidHash = boundMsUuidHash.Any(b => b != 0)
                ? Convert.ToHexString(boundMsUuidHash).ToLowerInvariant()
                : null,
            SaveName = meta.SaveName ?? "",
            InstanceName = meta.InstanceName ?? "",
            CreatedAt = DateTimeOffset.TryParse(meta.CreatedAt, out var dt) ? dt : DateTimeOffset.MinValue,
            OriginalSize = meta.OriginalSize,
            FileCount = meta.FileCount,
            PasswordHint = meta.PasswordHint,
            OneDriveBackedUp = meta.OneDriveBackedUp
        };
    }

    // ==================== 密码验证 ====================

    /// <summary>
    /// 通过 KeyCheck 快速验证密码是否正确（只解密 16 字节，不解密整个存档）。
    /// </summary>
    public bool VerifyPassword(string savencPath, string password)
    {
        using var fs = new FileStream(savencPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        Span<byte> headerPart = stackalloc byte[HeaderFixedSize];
        fs.ReadExactly(headerPart);

        byte[] salt = headerPart.Slice(6, SaltSize).ToArray();
        byte[] keyCheck = headerPart.Slice(22, KeyCheckSize).ToArray();

        byte[] key = DeriveKey(password, salt);
        try
        {
            return ValidateKeyCheck(key, keyCheck, salt);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    // ==================== 加密 ====================

    /// <summary>
    /// 将存档文件夹加密为 .savenc 文件。
    /// </summary>
    /// <param name="saveFolderPath">存档文件夹的完整路径</param>
    /// <param name="savencOutputPath">输出的 .savenc 文件路径</param>
    /// <param name="password">用户密码</param>
    /// <param name="options">加密选项</param>
    /// <param name="progress">进度报告：(filesDone, totalFiles)</param>
    /// <param name="ct">取消令牌</param>
    public async Task LockSaveAsync(
        string saveFolderPath,
        string savencOutputPath,
        string password,
        SaveLockOptions options,
        IProgress<(int filesDone, int totalFiles)>? progress = null,
        CancellationToken ct = default)
    {
        // 1. 生成随机 Salt
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);

        // 2. PBKDF2 派生 AES 密钥
        byte[] key = DeriveKey(password, salt);

        try
        {
            // 3. 生成 KeyCheck
            byte[] keyCheck = ComputeKeyCheck(key, salt);

            // 4. 生成随机 IV_base
            byte[] ivBase = RandomNumberGenerator.GetBytes(IvSize);

            // 5. 收集所有文件
            string[] allFiles = Directory.GetFiles(saveFolderPath, "*", SearchOption.AllDirectories);
            string baseDir = Path.GetFullPath(saveFolderPath);
            int totalFiles = allFiles.Length;

            // 6. 构建 Metadata JSON
            long totalSize = 0;
            foreach (var f in allFiles)
            {
                try { totalSize += new FileInfo(f).Length; }
                catch { /* skip inaccessible */ }
            }

            var metadata = new SaveLockMetadataJson
            {
                SaveName = Path.GetFileName(saveFolderPath),
                InstanceName = Path.GetFileName(Path.GetDirectoryName(saveFolderPath)) ?? "",
                CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
                OriginalSize = totalSize,
                FileCount = totalFiles,
                PasswordHint = options.PasswordHint,
                OneDriveBackedUp = false
            };
            string metaJson = JsonSerializer.Serialize(metadata);
            byte[] metaJsonBytes = Encoding.UTF8.GetBytes(metaJson);

            // 7. 计算 BoundMsUuid（如果有）
            byte[] msUuidHash = !string.IsNullOrEmpty(options.BoundMsUuid)
                ? SHA256.HashData(Encoding.UTF8.GetBytes(options.BoundMsUuid))
                : new byte[32];

            // 8. 原子写入 .savenc.tmp
            string tmpPath = savencOutputPath + ".tmp";

            await using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                // Header
                fs.Write(MagicNumber);
                fs.WriteByte(FormatVersion);
                fs.WriteByte((byte)options.LockMode);
                fs.Write(salt);
                fs.Write(keyCheck);
                fs.Write(new byte[12]); // Reserved
                fs.Write(msUuidHash);

                // Metadata length prefix (big-endian uint16)
                fs.WriteByte((byte)((metaJsonBytes.Length >> 8) & 0xFF));
                fs.WriteByte((byte)(metaJsonBytes.Length & 0xFF));
                fs.Write(metaJsonBytes);

                // IV_base
                fs.Write(ivBase);

                // 逐文件加密写入
                int done = 0;
                using var aes = Aes.Create();
                aes.KeySize = 256;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = key;

                foreach (string filePath in allFiles)
                {
                    ct.ThrowIfCancellationRequested();

                    string relativePath = Path.GetRelativePath(baseDir, filePath)
                        .Replace('\\', '/');
                    byte[] pathBytes = Encoding.UTF8.GetBytes(relativePath);

                    byte[] fileData = await File.ReadAllBytesAsync(filePath, ct);
                    byte[] encryptedData = EncryptFileData(aes, ivBase, done, fileData);

                    // pathLen (2 bytes, big-endian)
                    fs.WriteByte((byte)((pathBytes.Length >> 8) & 0xFF));
                    fs.WriteByte((byte)(pathBytes.Length & 0xFF));
                    fs.Write(pathBytes);

                    // dataLen (4 bytes, big-endian)
                    fs.WriteByte((byte)((encryptedData.Length >> 24) & 0xFF));
                    fs.WriteByte((byte)((encryptedData.Length >> 16) & 0xFF));
                    fs.WriteByte((byte)((encryptedData.Length >> 8) & 0xFF));
                    fs.WriteByte((byte)(encryptedData.Length & 0xFF));
                    fs.Write(encryptedData);

                    done++;
                    progress?.Report((done, totalFiles));
                }
            }

            // 9. 计算并追加 SHA-256 Trailer
            byte[] trailer = await ComputeTrailerAsync(tmpPath);
            await using (var fs = new FileStream(tmpPath, FileMode.Append, FileAccess.Write, FileShare.None))
            {
                fs.Write(trailer);
            }

            // 10. 原子重命名 .savenc.tmp → .savenc
            File.Move(tmpPath, savencOutputPath, overwrite: true);

            // 11. 删除原始存档文件夹
            Directory.Delete(saveFolderPath, true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    // ==================== 解密 ====================

    /// <summary>
    /// 使用已派生的 AES 密钥直接解密 .savenc（跳过密码派生和 KeyCheck 验证）。
    /// 用于启动时解锁——UnlockPanel 已验证密码，K 已派生。
    /// </summary>
    public async Task UnlockSaveWithKeyAsync(
        string savencPath,
        string outputFolderPath,
        byte[] key,
        bool deleteSavencAfter = false,
        IProgress<(int filesDone, int totalFiles)>? progress = null,
        CancellationToken ct = default)
    {
        await using var fs = new FileStream(savencPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        // Skip Header + Metadata
        byte[] header = new byte[HeaderFixedSize];
        fs.ReadExactly(header);

        byte[] metaLenBuf = new byte[2];
        fs.ReadExactly(metaLenBuf);
        int metadataLength = (metaLenBuf[0] << 8) | metaLenBuf[1];
        fs.Seek(metadataLength, SeekOrigin.Current);

        // Read IV_base
        byte[] ivBase = new byte[IvSize];
        fs.ReadExactly(ivBase);

        // Decrypt files
        Directory.CreateDirectory(outputFolderPath);

        int fileIndex = 0;
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;

        while (fs.Position < fs.Length - TrailerSize)
        {
            ct.ThrowIfCancellationRequested();

            byte[] pathLenBuf = new byte[2];
            if (fs.Read(pathLenBuf) < 2) break;
            int pathLen = (pathLenBuf[0] << 8) | pathLenBuf[1];

            byte[] pathBytes = new byte[pathLen];
            fs.ReadExactly(pathBytes);
            string relativePath = Encoding.UTF8.GetString(pathBytes);

            byte[] dataLenBuf = new byte[4];
            fs.ReadExactly(dataLenBuf);
            int dataLen = (dataLenBuf[0] << 24) | (dataLenBuf[1] << 16) | (dataLenBuf[2] << 8) | dataLenBuf[3];

            byte[] encryptedData = new byte[dataLen];
            fs.ReadExactly(encryptedData);

            byte[] plaintext = DecryptFileData(aes, ivBase, fileIndex, encryptedData);
            fileIndex++;

            string destPath = Path.Combine(outputFolderPath, relativePath);
            // 防止路径遍历攻击
            string fullDestPath = Path.GetFullPath(destPath);
            string fullOutputPath = Path.GetFullPath(outputFolderPath).TrimEnd(Path.DirectorySeparatorChar);
            if (!fullDestPath.StartsWith(fullOutputPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(fullDestPath, fullOutputPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"存档文件包含非法路径: {relativePath}");
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            await File.WriteAllBytesAsync(destPath, plaintext, ct);

            progress?.Report((fileIndex, fileIndex));
        }

        // Verify SHA-256 trailer
        byte[] storedTrailer = new byte[TrailerSize];
        fs.ReadExactly(storedTrailer);
        fs.Position = 0;
        byte[] trailerData = new byte[fs.Length - TrailerSize];
        await fs.ReadExactlyAsync(trailerData, 0, trailerData.Length, ct);
        byte[] computedTrailer = SHA256.HashData(trailerData);
        if (!storedTrailer.AsSpan().SequenceEqual(computedTrailer))
            throw new InvalidDataException("存档文件完整性校验失败——文件可能已被损坏或篡改。");

        if (deleteSavencAfter)
            File.Delete(savencPath);
    }

    /// <summary>
    /// 将 .savenc 文件解密恢复为存档文件夹（使用密码派生 K）。
    /// </summary>
    /// <param name="savencPath">.savenc 文件路径</param>
    /// <param name="outputFolderPath">输出的存档文件夹路径</param>
    /// <param name="password">用户密码</param>
    /// <param name="deleteSavencAfter">解密后是否删除 .savenc（一次性模式/手动解密为 true）</param>
    /// <param name="progress">进度报告：(filesDone, totalFiles)</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>解密后的 AES 密钥（永久模式需要保留）</returns>
    public async Task<byte[]> UnlockSaveAsync(
        string savencPath,
        string outputFolderPath,
        string password,
        bool deleteSavencAfter = true,
        IProgress<(int filesDone, int totalFiles)>? progress = null,
        CancellationToken ct = default)
    {
        // 1. 读取 Header → 获取 salt, keyCheck
        await using var fs = new FileStream(savencPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        byte[] header = new byte[HeaderFixedSize];
        fs.ReadExactly(header);

        if (!header.AsSpan(0, 4).SequenceEqual(MagicNumber))
            throw new InvalidDataException("不是有效的 .savenc 文件");

        byte[] salt = header.AsSpan(6, SaltSize).ToArray();
        byte[] keyCheck = header.AsSpan(22, KeyCheckSize).ToArray();

        // 2. PBKDF2 派生 K' + 验证 KeyCheck
        byte[] key = DeriveKey(password, salt);
        if (!ValidateKeyCheck(key, keyCheck, salt))
        {
            CryptographicOperations.ZeroMemory(key);
            throw new CryptographicException("密码错误");
        }

        // 3. 读取 Metadata 长度并跳过
        byte[] metaLenBuf = new byte[2];
        fs.ReadExactly(metaLenBuf);
        int metadataLength = (metaLenBuf[0] << 8) | metaLenBuf[1];
        fs.Seek(metadataLength, SeekOrigin.Current);

        // 4. 读取 IV_base
        byte[] ivBase = new byte[IvSize];
        fs.ReadExactly(ivBase);

        // 5. 逐文件解密写出
        Directory.CreateDirectory(outputFolderPath);

        int fileIndex = 0;
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;

        while (fs.Position < fs.Length - TrailerSize)
        {
            ct.ThrowIfCancellationRequested();

            // pathLen
            byte[] pathLenBuf = new byte[2];
            if (fs.Read(pathLenBuf) < 2) break;
            int pathLen = (pathLenBuf[0] << 8) | pathLenBuf[1];

            // path
            byte[] pathBytes = new byte[pathLen];
            fs.ReadExactly(pathBytes);
            string relativePath = Encoding.UTF8.GetString(pathBytes);

            // dataLen
            byte[] dataLenBuf = new byte[4];
            fs.ReadExactly(dataLenBuf);
            int dataLen = (dataLenBuf[0] << 24) | (dataLenBuf[1] << 16) | (dataLenBuf[2] << 8) | dataLenBuf[3];

            // encrypted data
            byte[] encryptedData = new byte[dataLen];
            fs.ReadExactly(encryptedData);

            // 解密
            byte[] plaintext = DecryptFileData(aes, ivBase, fileIndex, encryptedData);
            fileIndex++;

            // 写出
            string destPath = Path.Combine(outputFolderPath, relativePath);
            // 防止路径遍历攻击
            string fullDestPath = Path.GetFullPath(destPath);
            string fullOutputPath = Path.GetFullPath(outputFolderPath).TrimEnd(Path.DirectorySeparatorChar);
            if (!fullDestPath.StartsWith(fullOutputPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(fullDestPath, fullOutputPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"存档文件包含非法路径: {relativePath}");
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            await File.WriteAllBytesAsync(destPath, plaintext, ct);

            progress?.Report((fileIndex, fileIndex)); // 预读无法知道总数，用当前数报告
        }

        // Verify SHA-256 trailer
        byte[] storedTrailer2 = new byte[TrailerSize];
        fs.ReadExactly(storedTrailer2);
        fs.Position = 0;
        byte[] trailerData2 = new byte[fs.Length - TrailerSize];
        await fs.ReadExactlyAsync(trailerData2, 0, trailerData2.Length, ct);
        byte[] computedTrailer2 = SHA256.HashData(trailerData2);
        if (!storedTrailer2.AsSpan().SequenceEqual(computedTrailer2))
            throw new InvalidDataException("存档文件完整性校验失败——文件可能已被损坏或篡改。");

        // 6. 删除 .savenc（如果需要）
        if (deleteSavencAfter)
        {
            File.Delete(savencPath);
        }

        return key;
    }

    /// <summary>
    /// 永久模式重加密——使用内存中已有的 K 直接加密存档文件夹。
    /// 跳过密码派生和 KeyCheck 生成（K 已验证过）。
    /// </summary>
    public async Task ReEncryptSaveAsync(
        string saveFolderPath,
        string savencOutputPath,
        string originalSavencPath,
        byte[] key,
        SaveLockMode lockMode,
        IProgress<(int filesDone, int totalFiles)>? progress = null,
        CancellationToken ct = default)
    {
        // 读取原始 .savenc 的 Metadata（如果 .savenc 还存在）或重建
        // 简化：从文件夹重新构建 Metadata

        byte[] ivBase = RandomNumberGenerator.GetBytes(IvSize);
        string[] allFiles = Directory.GetFiles(saveFolderPath, "*", SearchOption.AllDirectories);
        string baseDir = Path.GetFullPath(saveFolderPath);

        // 从原始 .savenc 读取 Salt 和元数据 — Salt 必须复用，否则 PBKDF2 派生不一致
        byte[] salt;
        string metaJson;
        byte[] boundMsUuid = new byte[32];

        if (File.Exists(originalSavencPath))
        {
            var originalMeta = GetSaveLockMetadata(originalSavencPath);
            salt = originalMeta.Salt;
            boundMsUuid = string.IsNullOrEmpty(originalMeta.BoundMsUuidHash)
                ? new byte[32]
                : Convert.FromHexString(originalMeta.BoundMsUuidHash);

            var carryMeta = new SaveLockMetadataJson
            {
                SaveName = originalMeta.SaveName,
                InstanceName = originalMeta.InstanceName,
                CreatedAt = originalMeta.CreatedAt.ToString("O"),
                OriginalSize = originalMeta.OriginalSize,
                FileCount = allFiles.Length,
                PasswordHint = originalMeta.PasswordHint,
                OneDriveBackedUp = originalMeta.OneDriveBackedUp
            };
            metaJson = JsonSerializer.Serialize(carryMeta);
        }
        else
        {
            salt = RandomNumberGenerator.GetBytes(SaltSize);
            var fallback = new SaveLockMetadataJson
            {
                SaveName = Path.GetFileName(saveFolderPath),
                InstanceName = "",
                CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
                OriginalSize = 0,
                FileCount = allFiles.Length,
                PasswordHint = null,
                OneDriveBackedUp = false
            };
            metaJson = JsonSerializer.Serialize(fallback);
        }

        byte[] keyCheck = ComputeKeyCheck(key, salt);
        byte[] metaJsonBytes = Encoding.UTF8.GetBytes(metaJson);

        string tmpPath = savencOutputPath + ".tmp";

        await using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.Write(MagicNumber);
            fs.WriteByte(FormatVersion);
            fs.WriteByte((byte)lockMode);
            fs.Write(salt);
            fs.Write(keyCheck);
            fs.Write(new byte[12]);  // Reserved
            fs.Write(boundMsUuid);

            fs.WriteByte((byte)((metaJsonBytes.Length >> 8) & 0xFF));
            fs.WriteByte((byte)(metaJsonBytes.Length & 0xFF));
            fs.Write(metaJsonBytes);

            fs.Write(ivBase);

            int done = 0;
            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = key;

            foreach (string filePath in allFiles)
            {
                ct.ThrowIfCancellationRequested();
                string relativePath = Path.GetRelativePath(baseDir, filePath).Replace('\\', '/');
                byte[] pathBytes = Encoding.UTF8.GetBytes(relativePath);
                byte[] fileData = await File.ReadAllBytesAsync(filePath, ct);
                byte[] encryptedData = EncryptFileData(aes, ivBase, done, fileData);

                fs.WriteByte((byte)((pathBytes.Length >> 8) & 0xFF));
                fs.WriteByte((byte)(pathBytes.Length & 0xFF));
                fs.Write(pathBytes);
                fs.WriteByte((byte)((encryptedData.Length >> 24) & 0xFF));
                fs.WriteByte((byte)((encryptedData.Length >> 16) & 0xFF));
                fs.WriteByte((byte)((encryptedData.Length >> 8) & 0xFF));
                fs.WriteByte((byte)(encryptedData.Length & 0xFF));
                fs.Write(encryptedData);

                done++;
                progress?.Report((done, allFiles.Length));
            }
        }

        byte[] trailer = await ComputeTrailerAsync(tmpPath);
        await using (var fs = new FileStream(tmpPath, FileMode.Append, FileAccess.Write, FileShare.None))
        {
            fs.Write(trailer);
        }

        if (File.Exists(savencOutputPath))
            File.Delete(savencOutputPath);
        File.Move(tmpPath, savencOutputPath);

        Directory.Delete(saveFolderPath, true);
    }

    // ==================== 更新 Metadata ====================

    /// <summary>
    /// 更新 .savenc 文件中 Metadata JSON 的 oneDriveBackedUp 标志。
    /// 用于补传成功后回写。
    /// </summary>
    public async Task UpdateOneDriveBackedUpFlagAsync(string savencPath, bool backedUp)
    {
        byte[] allBytes = await File.ReadAllBytesAsync(savencPath);

        int offset = HeaderFixedSize;
        int metaLen = (allBytes[offset] << 8) | allBytes[offset + 1];
        offset += 2;

        string metaJson = Encoding.UTF8.GetString(allBytes, offset, metaLen);
        var meta = JsonSerializer.Deserialize<SaveLockMetadataJson>(metaJson) ?? new SaveLockMetadataJson();
        meta.OneDriveBackedUp = backedUp;
        string newMetaJson = JsonSerializer.Serialize(meta);
        byte[] newMetaBytes = Encoding.UTF8.GetBytes(newMetaJson);

        // 始终写 .tmp + 重算 Trailer + 原子 rename
        string tmpPath = savencPath + ".tmp";
        int bodyStart = offset + metaLen;
        int trailerStart = allBytes.Length - TrailerSize;

        await using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            // Header (up to metadata length prefix)
            fs.Write(allBytes, 0, offset - 2);
            // New metadata with length prefix
            fs.WriteByte((byte)((newMetaBytes.Length >> 8) & 0xFF));
            fs.WriteByte((byte)(newMetaBytes.Length & 0xFF));
            fs.Write(newMetaBytes);
            // Body (encrypted archive, without old trailer)
            fs.Write(allBytes, bodyStart, trailerStart - bodyStart);
        }

        // 重计算 SHA-256 Trailer 并追加
        byte[] trailer = SHA256.HashData(await File.ReadAllBytesAsync(tmpPath));
        await using (var fs = new FileStream(tmpPath, FileMode.Append, FileAccess.Write, FileShare.None))
        {
            fs.Write(trailer);
        }

        File.Move(tmpPath, savencPath, overwrite: true);
    }

    // ==================== 内部方法 ====================

    /// <summary>PBKDF2 密钥派生</summary>
    private static byte[] DeriveKey(string password, byte[] salt)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(
            password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(AesKeySize);
    }

    /// <summary>计算 KeyCheck: AES-256-CBC(K, salt_as_IV, "SAVELOCK_OK")</summary>
    private static byte[] ComputeKeyCheck(byte[] key, byte[] salt)
    {
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        aes.IV = salt[..IvSize];

        byte[] plaintext = Encoding.UTF8.GetBytes(KeyCheckPlaintext);
        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
    }

    /// <summary>验证 KeyCheck</summary>
    private static bool ValidateKeyCheck(byte[] key, byte[] keyCheck, byte[] salt)
    {
        try
        {
            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = key;
            aes.IV = salt[..IvSize];

            using var decryptor = aes.CreateDecryptor();
            byte[] decrypted = decryptor.TransformFinalBlock(keyCheck, 0, keyCheck.Length);
            string result = Encoding.UTF8.GetString(decrypted);
            return result == KeyCheckPlaintext;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>加密单个文件数据：IV_i = IV_base XOR counter_i</summary>
    private static byte[] EncryptFileData(Aes aes, byte[] ivBase, int fileIndex, byte[] plaintext)
    {
        byte[] iv = DeriveFileIv(ivBase, fileIndex);
        aes.IV = iv;
        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
    }

    /// <summary>解密单个文件数据</summary>
    private static byte[] DecryptFileData(Aes aes, byte[] ivBase, int fileIndex, byte[] ciphertext)
    {
        byte[] iv = DeriveFileIv(ivBase, fileIndex);
        aes.IV = iv;
        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
    }

    /// <summary>IV_i = IV_base XOR counter_i（counter_i 转为 16 字节大端）</summary>
    private static byte[] DeriveFileIv(byte[] ivBase, int counter)
    {
        byte[] counterBytes = new byte[IvSize];
        counterBytes[12] = (byte)((counter >> 24) & 0xFF);
        counterBytes[13] = (byte)((counter >> 16) & 0xFF);
        counterBytes[14] = (byte)((counter >> 8) & 0xFF);
        counterBytes[15] = (byte)(counter & 0xFF);

        byte[] result = new byte[IvSize];
        for (int i = 0; i < IvSize; i++)
            result[i] = (byte)(ivBase[i] ^ counterBytes[i]);
        return result;
    }

    /// <summary>计算文件 SHA-256（含 Header + Metadata + IV + Encrypted Data，不含 Trailer 自身）</summary>
    private static async Task<byte[]> ComputeTrailerAsync(string filePath)
    {
        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return await SHA256.HashDataAsync(fs);
    }

    /// <summary>Metadata JSON 反序列化用的内部类</summary>
    private class SaveLockMetadataJson
    {
        public string? SaveName { get; set; }
        public string? InstanceName { get; set; }
        public string? CreatedAt { get; set; }
        public long OriginalSize { get; set; }
        public int FileCount { get; set; }
        public string? PasswordHint { get; set; }
        public bool OneDriveBackedUp { get; set; }
    }
}
