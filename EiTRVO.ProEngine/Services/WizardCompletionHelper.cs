using System;
using System.IO;
using System.Text.Json;

namespace EiTRVO.ProEngine.Services;

/// <summary>
/// Tracks whether the first-run wizard has been completed, using a separate
/// <c>wizard.json</c> file so that <c>settings.json</c> overwrites (e.g. from
/// <see cref="SettingsService.Save"/>) cannot accidentally reset the flag.
/// </summary>
public static class WizardCompletionHelper
{
    private const string FileName = "wizard.json";

    /// <summary>
    /// Check whether the first-run wizard has been completed.
    /// Falls back to the legacy <c>WizardCompleted</c> flag in
    /// <c>settings.json</c> for upgrades from older versions.
    /// </summary>
    public static bool IsCompleted(string gameDir)
    {
        string path = Path.Combine(gameDir, FileName);
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var doc = JsonSerializer.Deserialize<WizardMarker>(json);
                return doc?.Completed == true;
            }
        }
        catch { /* corrupt — fall through to legacy check */ }

        // === Legacy fallback: check settings.json WizardCompleted ===
        // On first run after upgrade, this auto-migrates to wizard.json.
        try
        {
            var settings = SettingsService.Load(gameDir);
            if (settings.WizardCompleted)
            {
                // Auto-migrate: write the standalone marker so future
                // settings.json overwrites cannot reset the flag.
                MarkCompleted(gameDir);
                return true;
            }
        }
        catch { /* couldn't read settings either — assume not completed */ }

        return false;
    }

    /// <summary>
    /// Persist the wizard-completed marker. Idempotent — safe to call
    /// multiple times.
    /// </summary>
    public static void MarkCompleted(string gameDir)
    {
        string path = Path.Combine(gameDir, FileName);
        try
        {
            var marker = new WizardMarker
            {
                Completed = true,
                CompletedAt = DateTimeOffset.UtcNow
            };
            var json = JsonSerializer.Serialize(marker, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch { /* best-effort write — will retry on next launch */ }
    }

    private sealed class WizardMarker
    {
        public bool Completed { get; set; }
        public DateTimeOffset CompletedAt { get; set; }
    }
}
