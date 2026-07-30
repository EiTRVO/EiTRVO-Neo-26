using System.IO.Compression;
using System.Text.Json;
using EiTRVO.ProEngine.Helpers;
using EiTRVO.ProEngine.Models;

namespace EiTRVO.Tests.Helpers;

[TestClass]
public class ModpackSafetyScannerTests : IDisposable
{
    private readonly string _tempDir;

    public ModpackSafetyScannerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"eitrvo_safety_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ================================================================
    // Helper: Create test ZIP files
    // ================================================================

    private string CreateZipWithEntries(params (string Path, byte[] Content)[] entries)
    {
        string zipPath = Path.Combine(_tempDir, $"test_{Guid.NewGuid():N}.zip");
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var (path, content) in entries)
        {
            var entry = zip.CreateEntry(path, CompressionLevel.NoCompression);
            using var stream = entry.Open();
            stream.Write(content, 0, content.Length);
        }
        return zipPath;
    }

    private static byte[] CreateVersionJson(string mainClass = "net.minecraft.client.main.Main",
        string? jvmArg = null)
    {
        var detail = new VersionDetail
        {
            Id = "1.21",
            MainClass = mainClass,
            Arguments = jvmArg != null
                ? new Arguments
                {
                    Jvm = new List<System.Text.Json.JsonElement>
                    {
                        JsonSerializer.SerializeToElement(jvmArg)
                    }
                }
                : null
        };
        return JsonSerializer.SerializeToUtf8Bytes(detail);
    }

    private static byte[] CreateMrpackManifest(
        int formatVersion = 1,
        string game = "minecraft",
        string name = "Test Pack",
        Dictionary<string, string>? dependencies = null,
        List<ModpackFileEntry>? files = null)
    {
        var manifest = new ModpackManifest
        {
            FormatVersion = formatVersion,
            Game = game,
            Name = name,
            VersionId = "1.0.0",
            Dependencies = dependencies ?? new Dictionary<string, string>
            {
                ["minecraft"] = "1.21",
                ["fabric-loader"] = "0.16.0"
            },
            Files = files ?? new List<ModpackFileEntry>
            {
                new()
                {
                    Path = "mods/test.jar",
                    Downloads = new List<string> { "https://cdn.modrinth.com/data/test/mod.jar" },
                    Hashes = new ModpackHashes { Sha1 = "abc123" },
                    FileSize = 1024
                }
            }
        };
        return JsonSerializer.SerializeToUtf8Bytes(manifest,
            new JsonSerializerOptions { WriteIndented = false });
    }

    // ================================================================
    // ScanZipPack — Basic
    // ================================================================

    [TestMethod]
    public void ScanZipPack_SafeFiles_Passes()
    {
        var packPath = CreateZipWithEntries(
            ("manifest.json", "{}"u8.ToArray()),
            ("TestPack/version.json", CreateVersionJson()),
            ("TestPack/mods/test.jar", new byte[] { 0x50, 0x4B, 0x03, 0x04 })
        );

        var report = ModpackSafetyScanner.ScanZipPack(packPath);
        Assert.IsFalse(report.HasBlockingIssues, $"Unexpected blocking issues: {string.Join("; ", report.BlockingIssues)}");
    }

    [TestMethod]
    public void ScanZipPack_DangerousExe_Blocks()
    {
        var packPath = CreateZipWithEntries(
            ("TestPack/mods/test.jar", new byte[] { 0x50, 0x4B }),
            ("TestPack/virus.exe", new byte[] { 0x01, 0x02, 0x03 })
        );

        var report = ModpackSafetyScanner.ScanZipPack(packPath);
        Assert.IsTrue(report.HasBlockingIssues);
        Assert.IsTrue(report.BlockingIssues.Any(i => i.Contains(".exe")));
    }

    [TestMethod]
    public void ScanZipPack_DangerousBat_Blocks()
    {
        var packPath = CreateZipWithEntries(
            ("TestPack/evil.bat", new byte[] { 0x01 })
        );

        var report = ModpackSafetyScanner.ScanZipPack(packPath);
        Assert.IsTrue(report.HasBlockingIssues);
        Assert.IsTrue(report.BlockingIssues.Any(i => i.Contains(".bat")));
    }

    [TestMethod]
    public void ScanZipPack_DangerousDll_Blocks()
    {
        var packPath = CreateZipWithEntries(
            ("TestPack/hack.dll", new byte[] { 0x01 })
        );

        var report = ModpackSafetyScanner.ScanZipPack(packPath);
        Assert.IsTrue(report.HasBlockingIssues);
        Assert.IsTrue(report.BlockingIssues.Any(i => i.Contains(".dll")));
    }

    [TestMethod]
    public void ScanZipPack_DangerousPs1_Blocks()
    {
        var packPath = CreateZipWithEntries(
            ("TestPack/script.ps1", new byte[] { 0x01 })
        );

        var report = ModpackSafetyScanner.ScanZipPack(packPath);
        Assert.IsTrue(report.HasBlockingIssues);
        Assert.IsTrue(report.BlockingIssues.Any(i => i.Contains(".ps1")));
    }

    [TestMethod]
    public void ScanZipPack_DangerousVbs_Blocks()
    {
        var packPath = CreateZipWithEntries(
            ("TestPack/macro.vbs", new byte[] { 0x01 })
        );

        var report = ModpackSafetyScanner.ScanZipPack(packPath);
        Assert.IsTrue(report.HasBlockingIssues);
        Assert.IsTrue(report.BlockingIssues.Any(i => i.Contains(".vbs")));
    }

    [TestMethod]
    public void ScanZipPack_JarFile_Passes()
    {
        var packPath = CreateZipWithEntries(
            ("TestPack/mods/jei.jar", new byte[] { 0x50, 0x4B, 0x03, 0x04 }),
            ("TestPack/mods/fabric-api.jar", new byte[] { 0x50, 0x4B })
        );

        var report = ModpackSafetyScanner.ScanZipPack(packPath);
        Assert.IsFalse(report.HasBlockingIssues,
            $"Unexpected blocks: {string.Join("; ", report.BlockingIssues)}");
    }

    // ================================================================
    // ScanZipPack — Path traversal
    // ================================================================

    [TestMethod]
    public void ScanZipPack_PathTraversal_Blocks()
    {
        var packPath = CreateZipWithEntries(
            ("TestPack/../../../etc/hacked", new byte[] { 0x01 })
        );

        var report = ModpackSafetyScanner.ScanZipPack(packPath);
        Assert.IsTrue(report.HasBlockingIssues);
        Assert.IsTrue(report.BlockingIssues.Any(i => i.Contains("..")));
    }

    [TestMethod]
    public void ScanZipPack_DeepNesting_Blocks()
    {
        var deepPath = string.Join("/", Enumerable.Range(0, 15).Select(i => $"d{i}")) + "/file.txt";
        var packPath = CreateZipWithEntries(
            (deepPath, new byte[] { 0x01 })
        );

        var report = ModpackSafetyScanner.ScanZipPack(packPath);
        Assert.IsTrue(report.HasBlockingIssues);
        Assert.IsTrue(report.BlockingIssues.Any(i => i.Contains("嵌套深度")));
    }

    // ================================================================
    // ScanZipPack — Resource limits
    // ================================================================

    [TestMethod]
    public void ScanZipPack_TooManyFiles_Blocks()
    {
        var entries = new List<(string, byte[])>();
        for (int i = 0; i < 100; i++)
        {
            entries.Add(($"TestPack/mods/mod_{i:D6}.jar", new byte[] { 0x50, 0x4B }));
        }
        var packPath = CreateZipWithEntries(entries.ToArray());

        var report = ModpackSafetyScanner.ScanZipPack(packPath);
        // 100 files should pass
        Assert.IsFalse(report.HasBlockingIssues, "100 files should be allowed.");
    }

    // ================================================================
    // ScanMrpack — Basic
    // ================================================================

    [TestMethod]
    public void ScanMrpack_ValidManifest_Passes()
    {
        var mrpackPath = CreateZipWithEntries(
            ("modrinth.index.json", CreateMrpackManifest()),
            ("overrides/options.txt", "lang:en_us"u8.ToArray())
        );

        var report = ModpackSafetyScanner.ScanMrpack(mrpackPath);
        Assert.IsFalse(report.HasBlockingIssues,
            $"Unexpected blocking issues: {string.Join("; ", report.BlockingIssues)}");
    }

    [TestMethod]
    public void ScanMrpack_MissingIndexJson_Blocks()
    {
        var mrpackPath = CreateZipWithEntries(
            ("overrides/readme.txt", "hello"u8.ToArray())
        );

        var report = ModpackSafetyScanner.ScanMrpack(mrpackPath);
        Assert.IsTrue(report.HasBlockingIssues);
        Assert.IsTrue(report.BlockingIssues.Any(i => i.Contains("modrinth.index.json")));
    }

    [TestMethod]
    public void ScanMrpack_InvalidFormatVersion_Blocks()
    {
        var mrpackPath = CreateZipWithEntries(
            ("modrinth.index.json", CreateMrpackManifest(formatVersion: 2))
        );

        var report = ModpackSafetyScanner.ScanMrpack(mrpackPath);
        Assert.IsTrue(report.HasBlockingIssues);
        Assert.IsTrue(report.BlockingIssues.Any(i => i.Contains("格式版本")));
    }

    [TestMethod]
    public void ScanMrpack_WrongGame_Blocks()
    {
        var mrpackPath = CreateZipWithEntries(
            ("modrinth.index.json", CreateMrpackManifest(game: "terraria"))
        );

        var report = ModpackSafetyScanner.ScanMrpack(mrpackPath);
        Assert.IsTrue(report.HasBlockingIssues);
        Assert.IsTrue(report.BlockingIssues.Any(i => i.Contains("game")));
    }

    [TestMethod]
    public void ScanMrpack_DangerousExeInOverrides_Blocks()
    {
        var mrpackPath = CreateZipWithEntries(
            ("modrinth.index.json", CreateMrpackManifest()),
            ("overrides/virus.exe", new byte[] { 0x01, 0x02 })
        );

        var report = ModpackSafetyScanner.ScanMrpack(mrpackPath);
        Assert.IsTrue(report.HasBlockingIssues);
        Assert.IsTrue(report.BlockingIssues.Any(i => i.Contains("overrides") && i.Contains(".exe")));
    }

    [TestMethod]
    public void ScanMrpack_DangerousBatInOverrides_Blocks()
    {
        var mrpackPath = CreateZipWithEntries(
            ("modrinth.index.json", CreateMrpackManifest()),
            ("overrides/evil.bat", new byte[] { 0x01 })
        );

        var report = ModpackSafetyScanner.ScanMrpack(mrpackPath);
        Assert.IsTrue(report.HasBlockingIssues);
        Assert.IsTrue(report.BlockingIssues.Any(i => i.Contains(".bat")));
    }

    // ================================================================
    // ScanMrpack — Download URL checks
    // ================================================================

    [TestMethod]
    public void ScanMrpack_NonHttpsUrl_Warns()
    {
        var manifest = new ModpackManifest
        {
            FormatVersion = 1,
            Game = "minecraft",
            Name = "Test",
            VersionId = "1.0",
            Dependencies = new Dictionary<string, string> { ["minecraft"] = "1.21" },
            Files = new List<ModpackFileEntry>
            {
                new()
                {
                    Path = "mods/test.jar",
                    Downloads = new List<string> { "http://evil.com/mod.jar" },
                    FileSize = 1024
                }
            }
        };
        var mrpackPath = CreateZipWithEntries(
            ("modrinth.index.json", JsonSerializer.SerializeToUtf8Bytes(manifest))
        );

        var report = ModpackSafetyScanner.ScanMrpack(mrpackPath);
        Assert.IsTrue(report.HasWarnings, "Non-HTTPS URL should produce warning.");
        Assert.IsTrue(report.Warnings.Any(w => w.Contains("非 HTTPS")),
            $"Expected HTTPS warning, got: {string.Join("; ", report.Warnings)}");
    }

    [TestMethod]
    public void ScanMrpack_NonWhitelistedUrl_Warns()
    {
        var manifest = new ModpackManifest
        {
            FormatVersion = 1,
            Game = "minecraft",
            Name = "Test",
            VersionId = "1.0",
            Dependencies = new Dictionary<string, string> { ["minecraft"] = "1.21" },
            Files = new List<ModpackFileEntry>
            {
                new()
                {
                    Path = "mods/test.jar",
                    Downloads = new List<string> { "https://untrusted-site.example/mod.jar" },
                    FileSize = 1024
                }
            }
        };
        var mrpackPath = CreateZipWithEntries(
            ("modrinth.index.json", JsonSerializer.SerializeToUtf8Bytes(manifest))
        );

        var report = ModpackSafetyScanner.ScanMrpack(mrpackPath);
        Assert.IsTrue(report.HasWarnings, "Non-whitelisted URL should produce warning.");
        Assert.IsTrue(report.Warnings.Any(w => w.Contains("信任域名")),
            $"Expected whitelist warning, got: {string.Join("; ", report.Warnings)}");
    }

    // ================================================================
    // ScanVersionJson — mainClass + JVM args
    // ================================================================

    [TestMethod]
    public void ScanVersionJson_BlockedMainClass_Blocks()
    {
        var report = new ModpackSafetyScanner.SafetyReport();
        var versionDetail = new VersionDetail
        {
            Id = "1.21",
            MainClass = "java.lang.Runtime"
        };

        ModpackSafetyScanner.ScanVersionJson(versionDetail, report);
        Assert.IsTrue(report.HasBlockingIssues);
        Assert.IsTrue(report.BlockingIssues.Any(i => i.Contains("java.lang.Runtime")));
    }

    [TestMethod]
    public void ScanVersionJson_UnknownMainClass_Warns()
    {
        var report = new ModpackSafetyScanner.SafetyReport();
        var versionDetail = new VersionDetail
        {
            Id = "1.21",
            MainClass = "com.unknown.Hack"
        };

        ModpackSafetyScanner.ScanVersionJson(versionDetail, report);
        Assert.IsTrue(report.HasWarnings);
        Assert.IsTrue(report.Warnings.Any(i => i.Contains("com.unknown.Hack")));
    }

    [TestMethod]
    public void ScanVersionJson_MissingMainClass_Warns()
    {
        var report = new ModpackSafetyScanner.SafetyReport();
        var versionDetail = new VersionDetail
        {
            Id = "1.21",
            MainClass = null
        };

        ModpackSafetyScanner.ScanVersionJson(versionDetail, report);
        Assert.IsTrue(report.HasWarnings);
        Assert.IsTrue(report.Warnings.Any(i => i.Contains("未指定 mainClass")));
    }

    [TestMethod]
    public void ScanVersionJson_SafeMainClass_Passes()
    {
        var report = new ModpackSafetyScanner.SafetyReport();
        var versionDetail = new VersionDetail
        {
            Id = "1.21",
            MainClass = "net.minecraft.client.main.Main"
        };

        ModpackSafetyScanner.ScanVersionJson(versionDetail, report);
        Assert.IsFalse(report.HasBlockingIssues);
        Assert.IsFalse(report.HasWarnings);
    }

    [TestMethod]
    public void ScanVersionJson_DangerousJvmArg_Blocks()
    {
        var report = new ModpackSafetyScanner.SafetyReport();
        var versionDetail = new VersionDetail
        {
            Id = "1.21",
            MainClass = "net.minecraft.client.main.Main",
            Arguments = new Arguments
            {
                Jvm = new List<System.Text.Json.JsonElement>
                {
                    System.Text.Json.JsonSerializer.SerializeToElement("-javaagent:evil.jar")
                }
            }
        };

        ModpackSafetyScanner.ScanVersionJson(versionDetail, report);
        Assert.IsTrue(report.HasBlockingIssues,
            $"Expected blocking issues, got: {string.Join("; ", report.BlockingIssues)}");
        Assert.IsTrue(report.BlockingIssues.Any(i => i.Contains("javaagent") || i.Contains("危险 JVM")),
            $"Expected JVM arg warning, got: {string.Join("; ", report.BlockingIssues)}");
    }

    [TestMethod]
    public void ScanVersionJson_SafeJvmArg_Passes()
    {
        var report = new ModpackSafetyScanner.SafetyReport();
        var versionDetail = new VersionDetail
        {
            Id = "1.21",
            MainClass = "net.minecraft.client.main.Main",
            Arguments = new Arguments
            {
                Jvm = new List<System.Text.Json.JsonElement>
                {
                    System.Text.Json.JsonSerializer.SerializeToElement("-Xmx2G"),
                    System.Text.Json.JsonSerializer.SerializeToElement("-Dfml.ignoreInvalidMinecraftCertificates=true")
                }
            }
        };

        ModpackSafetyScanner.ScanVersionJson(versionDetail, report);
        Assert.IsFalse(report.HasBlockingIssues,
            $"Unexpected blocking issues: {string.Join("; ", report.BlockingIssues)}");
    }

    // ================================================================
    // SanitizePath
    // ================================================================

    [TestMethod]
    public void SanitizePath_DeepPath_Truncates()
    {
        var result = ModpackSafetyScanner.SanitizePath("a/b/c/d/e/f/g/h/file.txt");
        Assert.IsTrue(result.StartsWith("…/"));
        Assert.IsTrue(result.EndsWith("file.txt"));
    }

    [TestMethod]
    public void SanitizePath_ShortPath_KeptIntact()
    {
        var result = ModpackSafetyScanner.SanitizePath("mods/jei.jar");
        Assert.AreEqual("mods/jei.jar", result);
    }

    // ================================================================
    // FormatSize
    // ================================================================

    [TestMethod]
    public void FormatSize_Bytes()
    {
        var result = ModpackSafetyScanner.FormatSize(500);
        Assert.IsTrue(result.Contains("B"));
    }

    [TestMethod]
    public void FormatSize_Megabytes()
    {
        var result = ModpackSafetyScanner.FormatSize(5L * 1024 * 1024);
        Assert.IsTrue(result.Contains("MB"));
    }

    [TestMethod]
    public void FormatSize_Gigabytes()
    {
        var result = ModpackSafetyScanner.FormatSize(3L * 1024 * 1024 * 1024);
        Assert.IsTrue(result.Contains("GB"));
    }
}
