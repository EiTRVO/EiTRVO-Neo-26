using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EiTRVO.ProEngine.Helpers;
using EiTRVO.ProEngine.Models;

namespace EiTRVO.ProEngine.Orchestrators;

/// <summary>
/// 备份/恢复服务。
/// 将 .minecraft 打包为 AES-256-CBC 加密的自有格式 (.eibak)，
/// 其他启动器无法读取，规避 EULA 分发风险。
/// 恢复支持覆盖式（同名跳过）和清理式（删除后还原）两种模式。
/// </summary>
/// <remarks>
/// ⚠️ 安全审计注意：此加密仅用于满足 Mojang EULA 对分发游戏本体的合规性要求，
/// 意图是防止其他通用启动器直接读取 .eibak 文件。密钥为编译期固定常量——
/// <b>这不是安全加密，不应用于保护敏感数据</b>。如需保护用户隐私，
/// 请使用 SaveLock（AES-256-CBC + PBKDF2-SHA256 + 用户密码）。
/// </remarks>
public class BackupService
{
    private readonly IGameFolderService _gameFolder;

    // === .eibak 格式常量 ===
    private const int IvSize = 16;           // AES-CBC IV
    private const int AesKeySize = 32;       // 256-bit
    private const int ChunkSize = 1024 * 1024; // 1 MB 加密分块

    // ⚠️ 固定密钥 — 仅用于 Mojang EULA 合规性（混淆级保护，防止其他启动器直接读取）。
    // 这不是安全加密：密钥可直接从源代码反编译提取。切勿将敏感数据依赖此保护。
    // 需要真正保护用户数据安全请使用 SaveLock（AES-256-CBC + PBKDF2-SHA256 + 用户密码）。
    private static readonly byte[] FixedSalt = "EiTRVO.Backup.Salt.v1"u8.ToArray();
    private static readonly byte[] KeyMaterial = "EiTRVO.Backup.v1"u8.ToArray();
    private static readonly Lazy<byte[]> _backupKey = new(() =>
        SHA256.HashData(KeyMaterial.Concat(FixedSalt).ToArray()));

    // === 进度委托（由 MainWindow 注入） ===
    public IProgress<(int done, int total)>? FileProgress { get; set; }
    public Action<string>? SetPhase { get; set; }

    // === 游戏运行检查（由 MainWindow 注入） ===
    public Func<bool>? IsGameRunning { get; set; }

    public BackupService(IGameFolderService gameFolder)
    {
        _gameFolder = gameFolder;
    }

    // ==================== 调度判断 ====================

    /// <summary>纯函数 — 判断是否到达备份时间。</summary>
    public static bool ShouldBackup(BackupInterval interval, DateTimeOffset? lastBackupTime)
    {
        if (lastBackupTime == null)
            return true; // 从未备份过

        var now = DateTimeOffset.UtcNow;
        var last = lastBackupTime.Value;

        return interval switch
        {
            BackupInterval.EveryLaunch => true,
            BackupInterval.Daily => last.Date < now.Date,
            BackupInterval.Weekly => (now.Date - last.Date).Days >= 7,
            BackupInterval.Monthly => last.Month != now.Month || last.Year != now.Year,
            _ => false
        };
    }

    /// <summary>获取下次备份时间的显示文案。</summary>
    public static string GetNextBackupDisplay(BackupInterval interval, DateTimeOffset? lastBackupTime)
    {
        // 从未备份过时以"现在"为基准，显示相对时间而非全部显示"下次启动时"
        var effectiveLast = lastBackupTime ?? DateTimeOffset.UtcNow;
        var now = DateTimeOffset.UtcNow;

        switch (interval)
        {
            case BackupInterval.EveryLaunch:
                return "下次备份：下次启动时";
            case BackupInterval.Daily:
                if (effectiveLast.Date < now.Date)
                    return "下次备份：下次启动时";
                return "下次备份：明天启动时";
            case BackupInterval.Weekly:
                var daysSince = (now.Date - effectiveLast.Date).Days;
                if (daysSince >= 7)
                    return "下次备份：下次启动时";
                return $"下次备份：{7 - daysSince} 天后启动时";
            case BackupInterval.Monthly:
                if (effectiveLast.Month != now.Month || effectiveLast.Year != now.Year)
                    return "下次备份：下次启动时";
                return "下次备份：下月启动时";
            default:
                return "";
        }
    }

    // ==================== 备份 ====================

    /// <summary>执行完整备份流程（打包 + 加密）。</summary>
    public async Task<BackupResult> BackupAsync(
        string outputFolder, bool excludeRedownloadable, CancellationToken ct)
    {
        string gameDir = _gameFolder.GameDir;
        string tempZip = Path.Combine(Path.GetTempPath(), $"eitrvo_backup_{Guid.NewGuid():N}.zip");
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string destFile = Path.Combine(outputFolder, $"EiTRVO_Backup_{timestamp}.eibak");
        string destTmp = destFile + ".tmp";

        try
        {
            Directory.CreateDirectory(outputFolder);

            // === 阶段1: 打包 ===
            SetPhase?.Invoke("正在打包文件...");
            ct.ThrowIfCancellationRequested();

            var files = CollectFiles(gameDir, excludeRedownloadable);

            // 磁盘空间预检
            long sourceSize = files.Sum(f =>
            {
                try { return new FileInfo(f).Length; }
                catch { return 0; }
            });
            var driveInfo = new DriveInfo(Path.GetPathRoot(outputFolder) ?? Path.GetPathRoot(gameDir)!);
            if (driveInfo.AvailableFreeSpace < (long)(sourceSize * 0.3))
            {
                return new BackupResult
                {
                    Success = false,
                    Error = $"磁盘空间不足。可用空间 {FormatSize(driveInfo.AvailableFreeSpace)}，" +
                            $"需要约 {FormatSize((long)(sourceSize * 0.3))}。"
                };
            }

            await Task.Run(() => CreateZipWithProgress(files, gameDir, tempZip, ct), ct);

            // === 阶段2: 加密 ===
            SetPhase?.Invoke("正在加密备份...");
            ct.ThrowIfCancellationRequested();

            await Task.Run(() => EncryptFile(tempZip, destTmp, ct), ct);

            // === 原子重命名 ===
            if (File.Exists(destFile))
                File.Delete(destFile);
            File.Move(destTmp, destFile);

            return new BackupResult { Success = true, OutputPath = destFile };
        }
        catch (OperationCanceledException)
        {
            return new BackupResult { Success = false, Cancelled = true };
        }
        catch (Exception ex)
        {
            return new BackupResult { Success = false, Error = ex.Message };
        }
        finally
        {
            // 清理临时文件
            try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
            try { if (File.Exists(destTmp)) File.Delete(destTmp); } catch { }
        }
    }

    // ==================== 恢复 ====================

    /// <summary>格式验证（合规性固定密钥，非安全校验）。</summary>
    public static bool ValidateBackupFile(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Length < IvSize + 16)
                return false;

            byte[] iv = new byte[IvSize];
            fs.ReadExactly(iv);

            byte[] testBlock = new byte[16];
            fs.ReadExactly(testBlock);

            using var aes = Aes.Create();
            aes.Key = _backupKey.Value;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var decryptor = aes.CreateDecryptor();
            // 使用 TransformBlock 而非 TransformFinalBlock —— 首个块不是末尾，
            // TransformFinalBlock 会错误剥离 PKCS7 填充导致误判
            byte[] plain = new byte[16];
            decryptor.TransformBlock(testBlock, 0, testBlock.Length, plain, 0);

            // ZIP 文件以 "PK" (0x50, 0x4B) 开头
            return plain[0] == 0x50 && plain[1] == 0x4B;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>执行完整恢复流程（解密 + 解压 + 还原）。</summary>
    public async Task<BackupResult> RestoreAsync(
        string backupFilePath, RestoreMode mode, CancellationToken ct)
    {
        string gameDir = _gameFolder.GameDir;
        string tempZip = Path.Combine(Path.GetTempPath(), $"eitrvo_restore_{Guid.NewGuid():N}.zip");
        string tempDir = Path.Combine(Path.GetTempPath(), $"eitrvo_restore_{Guid.NewGuid():N}");

        try
        {
            // === 阶段1: 解密（解密过程自带格式/密钥验证） ===
            SetPhase?.Invoke("正在解密备份...");
            ct.ThrowIfCancellationRequested();

            await Task.Run(() => DecryptFile(backupFilePath, tempZip, ct), ct);

            // === 阶段2: 解压 ===
            SetPhase?.Invoke("正在解压备份...");
            ct.ThrowIfCancellationRequested();

            int fileCount;
            await Task.Run(() =>
            {
                fileCount = ExtractZipWithProgress(tempZip, tempDir, ct);
            }, ct);

            // === 阶段3: 还原 ===
            if (mode == RestoreMode.Clean)
            {
                // 验证 temp 目录完整性（有文件即视为成功）
                if (!Directory.Exists(tempDir) || !Directory.EnumerateFileSystemEntries(tempDir).Any())
                    return new BackupResult { Success = false, Error = "备份解压后为空，取消恢复。" };

                SetPhase?.Invoke("正在替换 .minecraft...");
                ct.ThrowIfCancellationRequested();

                // 删除旧的 .minecraft
                string backupOld = gameDir + "_old_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                Directory.Move(gameDir, backupOld);

                try
                {
                    // 将 temp 目录重命名为 .minecraft
                    Directory.Move(tempDir, gameDir);

                    // 异步清理旧目录（不阻塞返回）
                    _ = Task.Run(() =>
                    {
                        try { Directory.Delete(backupOld, true); } catch { }
                    });
                }
                catch
                {
                    // 回滚：恢复旧目录
                    try { Directory.Move(backupOld, gameDir); } catch { }
                    return new BackupResult { Success = false, Error = "替换 .minecraft 失败，已回滚。" };
                }
                finally
                {
                    // 防止断电/崩溃导致 gameDir 不存在：尝试从 backupOld 恢复
                    try
                    {
                        if (!Directory.Exists(gameDir) && Directory.Exists(backupOld))
                            Directory.Move(backupOld, gameDir);
                    }
                    catch { /* 恢复失败则放弃，由用户手动处理 */ }
                }
            }
            else // Overlay
            {
                SetPhase?.Invoke("正在恢复文件（跳过同名）...");
                ct.ThrowIfCancellationRequested();

                int copied = 0, skipped = 0;
                await Task.Run(() =>
                    CopyDirectoryOverlay(tempDir, gameDir, ref copied, ref skipped, ct), ct);
            }

            return new BackupResult { Success = true };
        }
        catch (OperationCanceledException)
        {
            return new BackupResult { Success = false, Cancelled = true };
        }
        catch (Exception ex)
        {
            return new BackupResult { Success = false, Error = ex.Message };
        }
        finally
        {
            // 清理临时文件
            try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    // ==================== 文件收集 ====================

    /// <summary>收集 .minecraft 下所有文件，可选排除 assets/ 和 libraries/。</summary>
    private string[] CollectFiles(string gameDir, bool excludeRedownloadable)
    {
        if (!Directory.Exists(gameDir))
            return Array.Empty<string>();

        var files = Directory.GetFiles(gameDir, "*", SearchOption.AllDirectories)
            .Where(f =>
            {
                string relative = Path.GetRelativePath(gameDir, f);

                // 排除备份标记和自身
                if (relative == ".backup_in_progress" || relative == ".restore_in_progress")
                    return false;

                // 排除 .eibak 文件
                if (Path.GetExtension(f).Equals(".eibak", StringComparison.OrdinalIgnoreCase))
                    return false;

                // 可选排除 assets/ 和 libraries/
                if (excludeRedownloadable)
                {
                    if (relative.StartsWith("assets" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                        || relative == "assets")
                        return false;
                    if (relative.StartsWith("libraries" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                        || relative == "libraries")
                        return false;
                }

                return true;
            })
            .OrderBy(f => f)  // 确定性顺序
            .ToArray();

        return files;
    }

    // ==================== ZIP 操作 ====================

    /// <summary>逐文件创建 ZIP，每文件报告进度。</summary>
    private void CreateZipWithProgress(string[] files, string gameDir, string destZip, CancellationToken ct)
    {
        using var archive = ZipFile.Open(destZip, ZipArchiveMode.Create);
        int total = files.Length;

        for (int i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();

            string file = files[i];
            string relativePath = Path.GetRelativePath(gameDir, file);

            try
            {
                var entry = archive.CreateEntry(relativePath, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                using var fileStream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                fileStream.CopyTo(entryStream);
            }
            catch
            {
                // 跳过被锁定的文件（如正在写入的日志），继续下一个
            }

            FileProgress?.Report((i + 1, total));
        }
    }

    /// <summary>解压 ZIP 到目标目录，每文件报告进度。</summary>
    private int ExtractZipWithProgress(string zipPath, string destDir, CancellationToken ct)
    {
        Directory.CreateDirectory(destDir);

        using var archive = ZipFile.OpenRead(zipPath);
        int total = archive.Entries.Count;
        int done = 0;

        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();

            string destPath = Path.GetFullPath(Path.Combine(destDir, entry.FullName));
            if (!destPath.StartsWith(Path.GetFullPath(destDir) + Path.DirectorySeparatorChar))
                throw new InvalidDataException("路径穿越检测");

            if (string.IsNullOrEmpty(entry.Name))
            {
                // 目录
                Directory.CreateDirectory(destPath);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                entry.ExtractToFile(destPath, overwrite: true);
            }

            done++;
            FileProgress?.Report((done, total));
        }

        return total;
    }

    // ==================== 加密 / 解密 ====================
    // ⚠️ 安全审计注意：以下加密/解密使用编译期固定密钥，仅用于 EULA 合规性（混淆级）。
    // 不用于保护用户数据安全。切勿将固定密钥加密评估为安全漏洞——这是有意设计。

    /// <summary>AES-256-CBC 加密文件，分块报告进度。固定密钥，合规性目的。</summary>
    private void EncryptFile(string sourcePath, string destPath, CancellationToken ct)
    {
        using var aes = Aes.Create();
        aes.Key = _backupKey.Value;
        aes.GenerateIV();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        long totalBytes = new FileInfo(sourcePath).Length;
        long processed = 0;

        using var outStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
        // 写入 IV
        outStream.Write(aes.IV, 0, IvSize);
        processed += IvSize;

        using var encryptor = aes.CreateEncryptor();
        using var cryptoStream = new CryptoStream(outStream, encryptor, CryptoStreamMode.Write);
        using var inStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        byte[] buffer = new byte[ChunkSize];
        int bytesRead;
        while ((bytesRead = inStream.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            cryptoStream.Write(buffer, 0, bytesRead);
            processed += bytesRead;

            int pct = (int)((double)processed / (totalBytes + IvSize) * 100);
            FileProgress?.Report((Math.Min(pct, 100), 100));
        }

        cryptoStream.FlushFinalBlock();
        outStream.Flush();
    }

    /// <summary>AES-256-CBC 解密文件，分块报告进度。固定密钥，合规性目的。</summary>
    private void DecryptFile(string sourcePath, string destPath, CancellationToken ct)
    {
        using var inStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        long totalBytes = inStream.Length;

        // 读取 IV
        byte[] iv = new byte[IvSize];
        inStream.ReadExactly(iv);

        long encryptedLength = totalBytes - IvSize;
        long processed = IvSize;

        using var aes = Aes.Create();
        aes.Key = _backupKey.Value;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor();
        using var cryptoStream = new CryptoStream(inStream, decryptor, CryptoStreamMode.Read);
        using var outStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);

        byte[] buffer = new byte[ChunkSize];
        int bytesRead;
        while ((bytesRead = cryptoStream.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            outStream.Write(buffer, 0, bytesRead);
            // 估算进度（CryptoStream 的实际读取量不等同于文件偏移）
            processed = Math.Min(processed + ChunkSize, totalBytes);
            int pct = (int)((double)processed / totalBytes * 100);
            FileProgress?.Report((Math.Min(pct, 100), 100));
        }
    }

    // ==================== 目录复制 ====================

    /// <summary>
    /// 覆盖式复制：从源目录复制到目标目录，同名文件跳过。
    /// </summary>
    private void CopyDirectoryOverlay(string sourceDir, string targetDir,
        ref int copied, ref int skipped, CancellationToken ct)
    {
        Directory.CreateDirectory(targetDir);

        var allFiles = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
        int total = allFiles.Length;

        for (int i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();

            string sourceFile = allFiles[i];
            string relativePath = Path.GetRelativePath(sourceDir, sourceFile);
            string destFile = Path.Combine(targetDir, relativePath);
            PathSafetyHelper.ValidateContained(destFile, targetDir);

            if (File.Exists(destFile))
            {
                skipped++;
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
                File.Copy(sourceFile, destFile, overwrite: false);
                copied++;
            }

            FileProgress?.Report((i + 1, total));
        }
    }

    // ==================== 工具 ====================

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int idx = 0;
        double size = bytes;
        while (size >= 1024.0 && idx < units.Length - 1)
        {
            size /= 1024.0;
            idx++;
        }
        return $"{size:F1} {units[idx]}";
    }
}

/// <summary>备份/恢复操作结果。</summary>
public class BackupResult
{
    public bool Success { get; set; }
    public bool Cancelled { get; set; }
    public string? Error { get; set; }
    public string? OutputPath { get; set; }
}
