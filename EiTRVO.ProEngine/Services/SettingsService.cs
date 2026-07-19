using System;
using System.IO;
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
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<LauncherSettings>(json) ?? new LauncherSettings();
            }
        }
        catch { /* corrupt settings — use defaults */ }
        return new LauncherSettings();
    }

    public static void Save(string gameDir, LauncherSettings settings)
    {
        string path = Path.Combine(gameDir, FileName);
        try
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch { /* best-effort save */ }
    }
}
