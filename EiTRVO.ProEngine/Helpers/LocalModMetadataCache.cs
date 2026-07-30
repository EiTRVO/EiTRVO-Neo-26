using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace EiTRVO.ProEngine.Helpers;

/// <summary>
/// Disk cache for Modrinth mod metadata keyed by SHA-1 hash.
/// Avoids redundant API calls when re-loading the same mod list.
/// TTL: 24 hours — mod metadata rarely changes.
/// </summary>
public class LocalModMetadataCache
{
    private readonly string _cacheFilePath;
    private Dictionary<string, CachedEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    private sealed record CachedEntry(string Title, string Description, DateTime CachedAt);

    public LocalModMetadataCache(string gameDir)
    {
        string cacheDir = Path.Combine(gameDir, "cache");
        Directory.CreateDirectory(cacheDir);
        _cacheFilePath = Path.Combine(cacheDir, "modrinth_mod_meta.json");
        Load();
    }

    // ---- public API ----

    /// <summary>Get cached (Title, Description) for a SHA-1 if present and not expired. Returns null on miss.</summary>
    public (string Title, string Description)? Get(string sha1)
    {
        if (_entries.TryGetValue(sha1, out var entry) && DateTime.UtcNow - entry.CachedAt < Ttl)
            return (entry.Title, entry.Description);
        return null;
    }

    /// <summary>Bulk get — only returns entries still within TTL.</summary>
    public Dictionary<string, (string Title, string Description)> GetBatch(IEnumerable<string> sha1s)
    {
        var result = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        foreach (var sha1 in sha1s)
        {
            var hit = Get(sha1);
            if (hit.HasValue)
                result[sha1] = hit.Value;
        }
        return result;
    }

    /// <summary>Store a single entry.</summary>
    public void Put(string sha1, string title, string description)
    {
        _entries[sha1] = new CachedEntry(title, description, DateTime.UtcNow);
    }

    /// <summary>Bulk put.</summary>
    public void PutBatch(IEnumerable<(string Sha1, string Title, string Description)> entries)
    {
        var now = DateTime.UtcNow;
        foreach (var (sha1, title, desc) in entries)
            _entries[sha1] = new CachedEntry(title, desc, now);
    }

    /// <summary>Persist to disk.</summary>
    public void Save()
    {
        try
        {
            // Serialize as a simple dict of id→{title,desc,cachedAt}
            var dict = _entries.ToDictionary(
                kv => kv.Key,
                kv => new { kv.Value.Title, kv.Value.Description, CachedAt = kv.Value.CachedAt.ToString("O") },
                StringComparer.OrdinalIgnoreCase);

            string dir = Path.GetDirectoryName(_cacheFilePath)!;
            Directory.CreateDirectory(dir);
            string tmp = _cacheFilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(dict));
            File.Move(tmp, _cacheFilePath, overwrite: true);
        }
        catch { /* best effort — cache is advisory */ }
    }

    // ---- internal ----

    private void Load()
    {
        try
        {
            if (!File.Exists(_cacheFilePath)) return;

            var json = File.ReadAllText(_cacheFilePath);
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            if (dict == null) return;

            foreach (var kv in dict)
            {
                try
                {
                    string title = kv.Value.GetProperty("Title").GetString() ?? "";
                    string desc = kv.Value.GetProperty("Description").GetString() ?? "";
                    string cachedStr = kv.Value.GetProperty("CachedAt").GetString() ?? "";
                    if (DateTime.TryParse(cachedStr, out var cachedAt))
                        _entries[kv.Key] = new CachedEntry(title, desc, cachedAt);
                }
                catch { /* skip corrupt entries */ }
            }
        }
        catch { /* missing or corrupt cache — start fresh */ }
    }
}
