using System.Reflection;
using System.Text.Json;
using EiTRVO.ProEngine.Helpers;

namespace EiTRVO.Tests.Helpers;

[TestClass]
public class LocalModMetadataCacheTests : IDisposable
{
    private readonly string _tempGameDir;

    public LocalModMetadataCacheTests()
    {
        _tempGameDir = Path.Combine(Path.GetTempPath(), $"eitrvo_cache_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempGameDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempGameDir, true); } catch { }
    }

    // ================================================================
    // Constructor
    // ================================================================

    [TestMethod]
    public void Constructor_CreatesCacheDirectory()
    {
        _ = new LocalModMetadataCache(_tempGameDir);
        string cacheDir = Path.Combine(_tempGameDir, "cache");
        Assert.IsTrue(Directory.Exists(cacheDir), "Cache directory should be created automatically.");
    }

    // ================================================================
    // Put / Get
    // ================================================================

    [TestMethod]
    public void PutThenGet_ReturnsEntry()
    {
        var cache = new LocalModMetadataCache(_tempGameDir);
        cache.Put("abc123", "Just Enough Items", "View items and recipes");

        var result = cache.Get("abc123");

        Assert.IsNotNull(result);
        Assert.AreEqual("Just Enough Items", result!.Value.Title);
        Assert.AreEqual("View items and recipes", result.Value.Description);
    }

    [TestMethod]
    public void Get_MissingHash_ReturnsNull()
    {
        var cache = new LocalModMetadataCache(_tempGameDir);
        var result = cache.Get("nonexistent-hash");
        Assert.IsNull(result, "Missing SHA-1 should return null.");
    }

    [TestMethod]
    public void Put_OverwritesExisting()
    {
        var cache = new LocalModMetadataCache(_tempGameDir);
        cache.Put("hash1", "Old Title", "Old Description");
        cache.Put("hash1", "New Title", "New Description");

        var result = cache.Get("hash1");
        Assert.IsNotNull(result);
        Assert.AreEqual("New Title", result!.Value.Title);
    }

    // ================================================================
    // GetBatch
    // ================================================================

    [TestMethod]
    public void GetBatch_PartialMatch()
    {
        var cache = new LocalModMetadataCache(_tempGameDir);
        cache.Put("sha1-a", "Mod A", "Desc A");
        cache.Put("sha1-b", "Mod B", "Desc B");

        var results = cache.GetBatch(new[] { "sha1-a", "sha1-b", "sha1-missing" });

        Assert.AreEqual(2, results.Count);
        Assert.AreEqual("Mod A", results["sha1-a"].Title);
        Assert.AreEqual("Mod B", results["sha1-b"].Title);
        Assert.IsFalse(results.ContainsKey("sha1-missing"));
    }

    [TestMethod]
    public void GetBatch_EmptyInput_ReturnsEmpty()
    {
        var cache = new LocalModMetadataCache(_tempGameDir);
        var results = cache.GetBatch(Array.Empty<string>());
        Assert.AreEqual(0, results.Count);
    }

    // ================================================================
    // PutBatch
    // ================================================================

    [TestMethod]
    public void PutBatch_ThenGet_ReturnsAll()
    {
        var cache = new LocalModMetadataCache(_tempGameDir);
        cache.PutBatch(new[]
        {
            ("sha1-x", "Mod X", "Desc X"),
            ("sha1-y", "Mod Y", "Desc Y"),
            ("sha1-z", "Mod Z", "Desc Z"),
        });

        Assert.IsNotNull(cache.Get("sha1-x"));
        Assert.IsNotNull(cache.Get("sha1-y"));
        Assert.IsNotNull(cache.Get("sha1-z"));
        Assert.AreEqual("Mod Y", cache.Get("sha1-y")!.Value.Title);
    }

    // ================================================================
    // Save / Load round-trip
    // ================================================================

    [TestMethod]
    public void SaveThenLoad_RoundTrip()
    {
        var cache1 = new LocalModMetadataCache(_tempGameDir);
        cache1.Put("hash-rt", "Round Trip Mod", "Persistence test");
        cache1.Save();

        // Create a new cache instance pointing at the same directory
        var cache2 = new LocalModMetadataCache(_tempGameDir);
        var result = cache2.Get("hash-rt");

        Assert.IsNotNull(result, "Saved entry should be reloaded from disk.");
        Assert.AreEqual("Round Trip Mod", result!.Value.Title);
        Assert.AreEqual("Persistence test", result.Value.Description);
    }

    // ================================================================
    // TTL expiry
    // ================================================================

    [TestMethod]
    public void ExpiredEntry_ReturnsNull()
    {
        var cache = new LocalModMetadataCache(_tempGameDir);
        cache.Put("expired-hash", "Expired Mod", "Should not appear");

        // Use reflection to access _entries and modify CachedAt
        var entriesField = typeof(LocalModMetadataCache).GetField("_entries",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(entriesField, "Could not find _entries field.");

        object entries = entriesField!.GetValue(cache)!;
        Assert.IsNotNull(entries);

        // Use reflection to get the dictionary's indexer: entries["expired-hash"]
        var dictType = entries.GetType();
        var itemProp = dictType.GetProperty("Item");
        Assert.IsNotNull(itemProp, "Dictionary should have Item indexer.");

        object? cachedObj = itemProp!.GetValue(entries, new object[] { "expired-hash" });
        Assert.IsNotNull(cachedObj, "Should find the cached entry.");

        // Set CachedAt to 25 hours ago via reflection
        var cachedAtProp = cachedObj!.GetType().GetProperty("CachedAt");
        Assert.IsNotNull(cachedAtProp, "CachedEntry should have CachedAt property.");
        cachedAtProp!.SetValue(cachedObj, DateTime.UtcNow.AddHours(-25));

        var result = cache.Get("expired-hash");
        Assert.IsNull(result, "Expired entry should return null.");
    }

    // ================================================================
    // Corrupt cache file
    // ================================================================

    [TestMethod]
    public void CorruptCacheFile_DoesNotThrow()
    {
        // Write invalid JSON to the cache file path
        string cacheDir = Path.Combine(_tempGameDir, "cache");
        Directory.CreateDirectory(cacheDir);
        string cacheFile = Path.Combine(cacheDir, "modrinth_mod_meta.json");
        File.WriteAllText(cacheFile, "{ this is not valid json !!! }");

        // Constructing cache should not throw
        LocalModMetadataCache? cache = null;
        try
        {
            cache = new LocalModMetadataCache(_tempGameDir);
        }
        catch (Exception ex)
        {
            Assert.Fail($"Constructor should not throw on corrupt file. Got: {ex.Message}");
        }

        Assert.IsNotNull(cache);
        // Any Get should return null since the cache is empty
        Assert.IsNull(cache!.Get("any-hash"));
    }

    [TestMethod]
    public void MissingCacheFile_NoError()
    {
        // No cache file at all → should work fine
        var cache = new LocalModMetadataCache(_tempGameDir);
        Assert.IsNull(cache.Get("anything"));
    }
}
