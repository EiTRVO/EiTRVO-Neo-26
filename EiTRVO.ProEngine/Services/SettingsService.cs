using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EiTRVO.ProEngine.Models;

namespace EiTRVO.ProEngine.Services;

public static class SettingsService
{
    private const string FileName = "settings.json";

    public static LauncherSettings Load(string gameDir)
    {
        string path = Path.Combine(gameDir, FileName);
        try
        {
            if (File.Exists(path))
            {
                byte[] encrypted = File.ReadAllBytes(path);
                byte[] plaintext = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                string json = Encoding.UTF8.GetString(plaintext);
                return JsonSerializer.Deserialize<LauncherSettings>(json) ?? new LauncherSettings();
            }
        }
        catch
        {
            // DPAPI 解密失败 → 尝试读取旧版明文文件（兼容升级）
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var settings = JsonSerializer.Deserialize<LauncherSettings>(json);
                    if (settings != null)
                    {
                        // 迁移到加密格式
                        Save(gameDir, settings);
                        return settings;
                    }
                }
            }
            catch { /* 明文也读不了 → 用默认值 */ }
        }
        return new LauncherSettings();
    }

    public static void Save(string gameDir, LauncherSettings settings)
    {
        string path = Path.Combine(gameDir, FileName);
        try
        {
            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            byte[] plaintext = Encoding.UTF8.GetBytes(json);
            byte[] encrypted = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(path, encrypted);
        }
        catch { /* best-effort save */ }
    }
}
