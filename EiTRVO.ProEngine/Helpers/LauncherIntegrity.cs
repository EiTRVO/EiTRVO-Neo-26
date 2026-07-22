using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace EiTRVO.ProEngine.Helpers;

/// <summary>
/// 启动器自完整性校验 —— 通过 DPAPI 加密的 SHA-256 基线检测 EXE 是否被篡改。
/// 首次启动自动建立基线；后续启动比对哈希；更新后经用户确认自动刷新基线。
/// </summary>
public static class LauncherIntegrity
{
    private const string BaselineFileName = "launcher.hash";

    /// <summary>
    /// 日志回调 — 由宿主注入，用于在基线异常（损坏/重建）时写入诊断信息。
    /// 参数为日志消息字符串。
    /// </summary>
    public static Action<string>? LogCallback { get; set; }

    /// <summary>计算当前 EXE 文件的 SHA-256 哈希。</summary>
    public static string ComputeCurrentHash()
    {
        string exePath = GetExePath();
        using var stream = File.OpenRead(exePath);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// 验证当前 EXE 是否与基线一致。
    /// 返回 <c>null</c> = 通过或首次建立基线；返回字符串 = 不匹配的警告消息。
    /// </summary>
    public static string? Verify(string gameDir)
    {
        string baselinePath = GetBaselinePath(gameDir);
        string currentHash = ComputeCurrentHash();

        if (!File.Exists(baselinePath))
        {
            // First launch — establish baseline silently
            SaveBaseline(baselinePath, currentHash);
            return null;
        }

        bool baselineFileExisted = File.Exists(baselinePath);
        string? storedHash = LoadBaseline(baselinePath);
        if (storedHash == null)
        {
            // Baseline file existed but was corrupted/unreadable → log the event
            if (baselineFileExisted)
                LogCallback?.Invoke("启动器完整性基线已损坏，已自动重建。如非本人操作，请检查启动器文件来源。");

            SaveBaseline(baselinePath, currentHash);
            return null;
        }

        if (string.Equals(currentHash, storedHash, StringComparison.OrdinalIgnoreCase))
            return null; // Match — all good

        return "启动器程序文件已被修改。\n\n" +
               "这可能是正常更新，也可能意味着启动器已被篡改。\n" +
               "如果你最近更新了启动器，可以放心继续。\n" +
               "如果你没有更新过，建议退出并检查文件来源。\n\n" +
               "是否信任此版本并继续启动？";
    }

    /// <summary>用户确认后更新基线（信任当前 EXE）。</summary>
    public static void UpdateBaseline(string gameDir)
    {
        string baselinePath = GetBaselinePath(gameDir);
        SaveBaseline(baselinePath, ComputeCurrentHash());
    }

    // ==================== Internal ====================

    private static string GetBaselinePath(string gameDir)
        => Path.Combine(gameDir, "eitrvo", BaselineFileName);

    private static string GetExePath()
    {
        using var process = Process.GetCurrentProcess();
        return process.MainModule?.FileName
            ?? Environment.ProcessPath
            ?? throw new InvalidOperationException("无法获取启动器 EXE 路径。");
    }

    private static void SaveBaseline(string path, string hash)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // Generate random entropy (salt) to strengthen DPAPI binding
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] plaintext = System.Text.Encoding.UTF8.GetBytes(hash);

        // DPAPI encrypt the hash
        byte[] encrypted = System.Security.Cryptography.ProtectedData.Protect(
            plaintext, salt, System.Security.Cryptography.DataProtectionScope.CurrentUser);

        string tmpPath = path + ".tmp";
        using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.Write(salt, 0, salt.Length);
            fs.Write(encrypted, 0, encrypted.Length);
        }
        File.Move(tmpPath, path, overwrite: true);
    }

    private static string? LoadBaseline(string path)
    {
        try
        {
            byte[] data = File.ReadAllBytes(path);
            if (data.Length < 16) return null;

            byte[] salt = data.AsSpan(0, 16).ToArray();
            byte[] encrypted = data.AsSpan(16).ToArray();

            byte[] plaintext = System.Security.Cryptography.ProtectedData.Unprotect(
                encrypted, salt, System.Security.Cryptography.DataProtectionScope.CurrentUser);

            return System.Text.Encoding.UTF8.GetString(plaintext);
        }
        catch
        {
            return null; // Any error → treat as corrupted baseline
        }
    }
}
