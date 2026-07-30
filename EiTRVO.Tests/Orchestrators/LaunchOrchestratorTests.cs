using System.Text.Json;
using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Orchestrators;
using EiTRVO.ProEngine.Services;
using EiTRVO.Tests.Fakes;

namespace EiTRVO.Tests.Orchestrators;

[TestClass]

public class LaunchOrchestratorTests : IDisposable
{
    private readonly string _tempGameDir;
    private readonly FakeGameFolderService _gameFolder;
    private readonly FakeNotificationService _notificationService;
    private readonly LaunchOrchestrator _orchestrator;

    public LaunchOrchestratorTests()
    {
        _tempGameDir = Path.Combine(Path.GetTempPath(), $"eitrvo_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempGameDir);

        _gameFolder = new FakeGameFolderService { GameDir = _tempGameDir };
        _notificationService = new FakeNotificationService();

        var httpClient = new HttpClient();

        _orchestrator = new LaunchOrchestrator(
            httpClient,
            new FakeAuthService(),
            new FakeModLoaderService(),
            _notificationService,
            _gameFolder,
            new SaveLockService(),
            new FakeModrinthService());
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempGameDir, true); } catch { }
    }

    // ================================================================
    // SanitizeArgs
    // ================================================================

    [TestMethod]
    public void SanitizeArgs_RedactsAccessToken()
    {
        var args = new List<string> { "--accessToken", "eyJhbGciOi...", "--width", "1920" };
        var result = LaunchOrchestrator.SanitizeArgs(args);
        StringAssert.Contains(result, "***REDACTED***");
        Assert.IsFalse(result.Contains("eyJhbGciOi..."));
        StringAssert.Contains(result, "1920");
    }

    [TestMethod]
    public void SanitizeArgs_RedactsUuid()
    {
        var args = new List<string> { "--uuid", "43381ea0-21e5-9839-958a-f459800e4d11", "--userType", "msa" };
        var result = LaunchOrchestrator.SanitizeArgs(args);
        StringAssert.Contains(result, "***REDACTED***");
        Assert.IsFalse(result.Contains("43381ea0"));
        StringAssert.Contains(result, "msa");
    }

    [TestMethod]
    public void SanitizeArgs_NormalArgs_Preserved()
    {
        var args = new List<string> { "-Xmx2048M", "--width", "1920", "--height", "1080" };
        var result = LaunchOrchestrator.SanitizeArgs(args);
        Assert.AreEqual("-Xmx2048M --width 1920 --height 1080", result);
    }

    [TestMethod]
    public void SanitizeArgs_NoAccessToken_Unchanged()
    {
        var args = new List<string> { "net.minecraft.client.main.Main" };
        var result = LaunchOrchestrator.SanitizeArgs(args);
        Assert.AreEqual("net.minecraft.client.main.Main", result);
    }

    // ================================================================
    // BuildLaunchArgs — JVM args
    // ================================================================

    [TestMethod]
    public void BuildLaunchArgs_IncludesMemoryArg()
    {
        var detail = CreateMinimalDetail();
        var args = _orchestrator.BuildLaunchArgs(detail, "1.21", _tempGameDir,
            "Player", "release", "token", "uuid-123",
            memory: 4096, targetJava: 21, width: null, height: null, "mojang");

        CollectionAssert.Contains(args, "-Xmx4096M");
    }

    [TestMethod]
    public void BuildLaunchArgs_IncludesNativePath()
    {
        var detail = CreateMinimalDetail();
        var args = _orchestrator.BuildLaunchArgs(detail, "1.21", _tempGameDir,
            "Player", "release", "token", "uuid-123",
            memory: 2048, targetJava: 21, width: null, height: null, "mojang");

        var nativeArg = args.FirstOrDefault(a => a.StartsWith("-Djava.library.path="));
        Assert.IsNotNull(nativeArg);
        StringAssert.Contains(nativeArg, "natives/1.21");
    }

    [TestMethod]
    public void BuildLaunchArgs_IncludesLauncherBrand()
    {
        var detail = CreateMinimalDetail();
        var args = _orchestrator.BuildLaunchArgs(detail, "1.21", _tempGameDir,
            "Player", "release", "token", "uuid-123",
            memory: 2048, targetJava: 21, width: null, height: null, "mojang");

        CollectionAssert.Contains(args, "-Dminecraft.launcher.brand=eitrvo-neo");
        CollectionAssert.Contains(args, "-Dminecraft.launcher.version=26");
    }

    [TestMethod]
    public void BuildLaunchArgs_ResolutionArgs()
    {
        var detail = CreateMinimalDetail();
        var args = _orchestrator.BuildLaunchArgs(detail, "1.21", _tempGameDir,
            "Player", "release", "token", "uuid-123",
            memory: 2048, targetJava: 21, width: "1920", height: "1080", "mojang");

        var mainIdx = args.IndexOf("net.minecraft.client.main.Main");
        Assert.IsTrue(mainIdx > 0);
        Assert.AreEqual("--width", args[mainIdx + 1]);
        Assert.AreEqual("1920", args[mainIdx + 2]);
        Assert.AreEqual("--height", args[mainIdx + 3]);
        Assert.AreEqual("1080", args[mainIdx + 4]);
    }

    [TestMethod]
    public void BuildLaunchArgs_NoResolution_OmitsWidthHeight()
    {
        var detail = CreateMinimalDetail();
        var args = _orchestrator.BuildLaunchArgs(detail, "1.21", _tempGameDir,
            "Player", "release", "token", "uuid-123",
            memory: 2048, targetJava: 21, width: null, height: null, "mojang");

        Assert.IsFalse(args.Contains("--width"));
        Assert.IsFalse(args.Contains("--height"));
    }

    [TestMethod]
    public void BuildLaunchArgs_Java8_AddsLegacyFlags()
    {
        var detail = CreateMinimalDetail();
        var args = _orchestrator.BuildLaunchArgs(detail, "1.8.9", _tempGameDir,
            "Player", "release", "token", "uuid-123",
            memory: 1024, targetJava: 8, width: null, height: null, "mojang");

        CollectionAssert.Contains(args, "-Xss1M");
        Assert.IsTrue(args.Any(a => a.Contains("MojangTricksIntelDriversForPerformance")));
    }

    [TestMethod]
    public void BuildLaunchArgs_Java21_SkipsLegacyFlags()
    {
        var detail = CreateMinimalDetail();
        var args = _orchestrator.BuildLaunchArgs(detail, "1.21", _tempGameDir,
            "Player", "release", "token", "uuid-123",
            memory: 2048, targetJava: 21, width: null, height: null, "mojang");

        Assert.IsFalse(args.Contains("-Xss1M"));
    }

    // ================================================================
    // BuildLaunchArgs — classpath
    // ================================================================

    [TestMethod]
    public void BuildLaunchArgs_StandardLibrary_InClasspath()
    {
        var libPath = CreateFakeLibrary("com/example/test/1.0/test-1.0.jar");
        var detail = CreateDetailWithLibrary(new Library
        {
            Name = "com.example:test:1.0",
            Downloads = new LibraryDownloads
            {
                Artifact = new DownloadInfo { Path = "com/example/test/1.0/test-1.0.jar", Url = "https://example.com/test.jar" }
            }
        });

        var args = _orchestrator.BuildLaunchArgs(detail, "1.21", _tempGameDir,
            "Player", "release", "token", "uuid-123",
            memory: 2048, targetJava: 21, width: null, height: null, "mojang");

        // The classpath value follows "-cp"
        var cpIdx = args.IndexOf("-cp");
        Assert.IsTrue(cpIdx > 0);
        StringAssert.Contains(libPath, args[cpIdx + 1]);
    }

    [TestMethod]
    public void BuildLaunchArgs_LegacyMavenLibrary_UsesNameFallback()
    {
        var libPath = CreateFakeLibrary("net/minecraft/launchwrapper/1.12/launchwrapper-1.12.jar");
        var detail = CreateDetailWithLibrary(new Library
        {
            Name = "net.minecraft:launchwrapper:1.12"
            // No Downloads property — legacy Forge format
        });

        var args = _orchestrator.BuildLaunchArgs(detail, "1.21", _tempGameDir,
            "Player", "release", "token", "uuid-123",
            memory: 2048, targetJava: 21, width: null, height: null, "mojang");

        var cpIdx = args.IndexOf("-cp");
        Assert.IsTrue(cpIdx > 0);
        StringAssert.Contains(libPath, args[cpIdx + 1]);
    }

    [TestMethod]
    public void BuildLaunchArgs_MissingLibraryFile_Skipped()
    {
        var detail = CreateDetailWithLibrary(new Library
        {
            Name = "com.example:missing:1.0",
            Downloads = new LibraryDownloads
            {
                Artifact = new DownloadInfo { Path = "com/example/missing/1.0/missing-1.0.jar", Url = "https://example.com/missing.jar" }
            }
        });

        var args = _orchestrator.BuildLaunchArgs(detail, "1.21", _tempGameDir,
            "Player", "release", "token", "uuid-123",
            memory: 2048, targetJava: 21, width: null, height: null, "mojang");

        var cpIdx = args.IndexOf("-cp");
        Assert.IsTrue(cpIdx > 0);
        // The missing library should NOT appear in the classpath
        Assert.IsFalse(args[cpIdx + 1].Contains("missing-1.0.jar"));
    }

    // ================================================================
    // BuildLaunchArgs — NeoForge bootstrap
    // ================================================================

    [TestMethod]
    public void BuildLaunchArgs_NeoForgeBootstrap_AddsOpens()
    {
        var detail = CreateMinimalDetail();
        detail.MainClass = "cpw.mods.bootstraplauncher.BootstrapLauncher";

        var args = _orchestrator.BuildLaunchArgs(detail, "1.21.5", _tempGameDir,
            "Player", "release", "token", "uuid-123",
            memory: 4096, targetJava: 21, width: null, height: null, "mojang");

        CollectionAssert.Contains(args, "--add-opens");
        CollectionAssert.Contains(args, "java.base/java.lang.invoke=ALL-UNNAMED");
        CollectionAssert.Contains(args, "java.base/java.util.jar=ALL-UNNAMED");
    }

    [TestMethod]
    public void BuildLaunchArgs_VanillaMain_SkipsOpens()
    {
        var detail = CreateMinimalDetail();
        detail.MainClass = "net.minecraft.client.main.Main";

        var args = _orchestrator.BuildLaunchArgs(detail, "1.21.5", _tempGameDir,
            "Player", "release", "token", "uuid-123",
            memory: 4096, targetJava: 21, width: null, height: null, "mojang");

        Assert.IsFalse(args.Contains("--add-opens"));
    }

    // ================================================================
    // BuildLaunchArgs — rules evaluation
    // ================================================================

    [TestMethod]
    public void BuildLaunchArgs_LibraryRule_Allow_OsMatch()
    {
        var detail = CreateMinimalDetail();
        detail.Libraries!.Add(new Library
        {
            Name = "com.example:natives:1.0",
            Downloads = new LibraryDownloads
            {
                Artifact = new DownloadInfo { Path = "com/example/natives/1.0/natives-1.0.jar", Url = "https://example.com/natives.jar" }
            },
            Rules = new List<Rule>
            {
                new() { Action = "allow", Os = new OsRule { Name = "windows" } }
            }
        });

        var libPath = CreateFakeLibrary("com/example/natives/1.0/natives-1.0.jar");
        var args = _orchestrator.BuildLaunchArgs(detail, "1.21", _tempGameDir,
            "Player", "release", "token", "uuid-123",
            memory: 2048, targetJava: 21, width: null, height: null, "mojang");

        var cpIdx = args.IndexOf("-cp");
        Assert.IsTrue(cpIdx > 0);
        StringAssert.Contains(args[cpIdx + 1], Path.GetFileName(libPath));
    }

    [TestMethod]
    public void BuildLaunchArgs_LibraryRule_Disallow_OsMatch()
    {
        var detail = CreateMinimalDetail();
        CreateFakeLibrary("com/example/blocked/1.0/blocked-1.0.jar");
        detail.Libraries!.Add(new Library
        {
            Name = "com.example:blocked:1.0",
            Downloads = new LibraryDownloads
            {
                Artifact = new DownloadInfo { Path = "com/example/blocked/1.0/blocked-1.0.jar", Url = "https://example.com/blocked.jar" }
            },
            Rules = new List<Rule>
            {
                new() { Action = "disallow", Os = new OsRule { Name = "windows" } }
            }
        });

        var args = _orchestrator.BuildLaunchArgs(detail, "1.21", _tempGameDir,
            "Player", "release", "token", "uuid-123",
            memory: 2048, targetJava: 21, width: null, height: null, "mojang");

        var cpIdx = args.IndexOf("-cp");
        Assert.IsTrue(cpIdx > 0);
        Assert.IsFalse(args[cpIdx + 1].Contains("blocked-1.0.jar"));
    }

    [TestMethod]
    public void BuildLaunchArgs_OsRule_NonWindows_Skipped()
    {
        var detail = CreateMinimalDetail();
        var libPath = CreateFakeLibrary("com/example/mac/1.0/mac-1.0.jar");
        detail.Libraries!.Add(new Library
        {
            Name = "com.example:mac:1.0",
            Downloads = new LibraryDownloads
            {
                Artifact = new DownloadInfo { Path = "com/example/mac/1.0/mac-1.0.jar", Url = "https://example.com/mac.jar" }
            },
            Rules = new List<Rule>
            {
                new() { Action = "allow", Os = new OsRule { Name = "osx" } }
            }
        });

        var args = _orchestrator.BuildLaunchArgs(detail, "1.21", _tempGameDir,
            "Player", "release", "token", "uuid-123",
            memory: 2048, targetJava: 21, width: null, height: null, "mojang");

        var cpIdx = args.IndexOf("-cp");
        Assert.IsTrue(cpIdx > 0);
        Assert.IsFalse(args[cpIdx + 1].Contains("mac-1.0.jar"));
    }

    // ================================================================
    // BuildLaunchArgs — JVM security filtering
    // ================================================================

    [TestMethod]
    public void BuildLaunchArgs_ObjectArgStringValue_JavaAgent_Filtered()
    {
        var detail = CreateMinimalDetailWithJvmArgs(new object[]
        {
            new
            {
                rules = new[] { new { action = "allow", os = new { name = "windows" } } },
                value = "-javaagent:evil.jar"
            }
        });

        var args = _orchestrator.BuildLaunchArgs(detail, "1.21", _tempGameDir,
            "Player", "release", "token", "uuid-123",
            memory: 2048, targetJava: 21, width: null, height: null, "mojang");

        Assert.IsFalse(args.Any(a => a.StartsWith("-javaagent:", StringComparison.OrdinalIgnoreCase)),
            "Object-format -javaagent: should be filtered");
    }

    [TestMethod]
    public void BuildLaunchArgs_ObjectArgArrayValue_AgentLib_Filtered()
    {
        var detail = CreateMinimalDetailWithJvmArgs(new object[]
        {
            new
            {
                rules = new[] { new { action = "allow", os = new { name = "windows" } } },
                value = new[] { "-Xmx2048M", "-agentlib:jdwp=transport=dt_socket" }
            }
        });

        var args = _orchestrator.BuildLaunchArgs(detail, "1.21", _tempGameDir,
            "Player", "release", "token", "uuid-123",
            memory: 2048, targetJava: 21, width: null, height: null, "mojang");

        Assert.IsFalse(args.Any(a => a.StartsWith("-agentlib:", StringComparison.OrdinalIgnoreCase)),
            "Object-format -agentlib: should be filtered");
        Assert.IsTrue(args.Contains("-Xmx2048M"),
            "Safe args in array value should pass through");
    }

    [TestMethod]
    public void BuildLaunchArgs_ObjectArgStringValue_AgentPath_Filtered()
    {
        var detail = CreateMinimalDetailWithJvmArgs(new object[]
        {
            new
            {
                rules = new[] { new { action = "allow" } },
                value = "-agentpath:C:\\malicious.dll"
            }
        });

        var args = _orchestrator.BuildLaunchArgs(detail, "1.21", _tempGameDir,
            "Player", "release", "token", "uuid-123",
            memory: 2048, targetJava: 21, width: null, height: null, "mojang");

        Assert.IsFalse(args.Any(a => a.StartsWith("-agentpath:", StringComparison.OrdinalIgnoreCase)),
            "Object-format -agentpath: should be filtered");
    }

    [TestMethod]
    public void BuildLaunchArgs_ObjectArgStringValue_CaseInsensitive_Filtered()
    {
        var detail = CreateMinimalDetailWithJvmArgs(new object[]
        {
            new
            {
                rules = new[] { new { action = "allow", os = new { name = "windows" } } },
                value = "-JAVAAGENT:C:\\path\\agent.jar"
            }
        });

        var args = _orchestrator.BuildLaunchArgs(detail, "1.21", _tempGameDir,
            "Player", "release", "token", "uuid-123",
            memory: 2048, targetJava: 21, width: null, height: null, "mojang");

        Assert.IsFalse(args.Any(a => a.StartsWith("-javaagent:", StringComparison.OrdinalIgnoreCase)),
            "Object-format -JAVAAGENT: (uppercase) should be filtered case-insensitively");
    }

    [TestMethod]
    public void BuildLaunchArgs_ObjectArgStringValue_SafeArg_Passes()
    {
        var detail = CreateMinimalDetailWithJvmArgs(new object[]
        {
            new
            {
                rules = new[] { new { action = "allow", os = new { name = "windows" } } },
                value = "-Dcustom.property=value"
            }
        });

        var args = _orchestrator.BuildLaunchArgs(detail, "1.21", _tempGameDir,
            "Player", "release", "token", "uuid-123",
            memory: 2048, targetJava: 21, width: null, height: null, "mojang");

        CollectionAssert.Contains(args, "-Dcustom.property=value",
            "Safe Object-format args should pass through");
    }

    [TestMethod]
    public void BuildLaunchArgs_ObjectArgStringValue_RuleDisallowed_Skipped()
    {
        var detail = CreateMinimalDetailWithJvmArgs(new object[]
        {
            new
            {
                rules = new[] { new { action = "allow", os = new { name = "osx" } } },
                value = "-Dmac.property=value"
            }
        });

        var args = _orchestrator.BuildLaunchArgs(detail, "1.21", _tempGameDir,
            "Player", "release", "token", "uuid-123",
            memory: 2048, targetJava: 21, width: null, height: null, "mojang");

        Assert.IsFalse(args.Contains("-Dmac.property=value"),
            "Non-matching rule should skip the arg entirely");
    }

    // ================================================================
    // BuildLaunchArgs — mainClass handling
    // ================================================================

    [TestMethod]
    public void BuildLaunchArgs_KnownMainClass_AppendedToArgs()
    {
        var detail = CreateMinimalDetail();
        detail.MainClass = "net.minecraft.client.main.Main";

        var args = _orchestrator.BuildLaunchArgs(detail, "1.21", _tempGameDir,
            "Player", "release", "token", "uuid-123",
            memory: 2048, targetJava: 21, width: null, height: null, "mojang");

        CollectionAssert.Contains(args, "net.minecraft.client.main.Main");
    }

    [TestMethod]
    public void BuildLaunchArgs_UnknownMainClass_StillAppended()
    {
        // BuildLaunchArgs does NOT filter mainClass — the blocking check
        // happens in LaunchInternalAsync before BuildLaunchArgs is called.
        var detail = CreateMinimalDetail();
        detail.MainClass = "com.evil.Hack";

        var args = _orchestrator.BuildLaunchArgs(detail, "1.21", _tempGameDir,
            "Player", "release", "token", "uuid-123",
            memory: 2048, targetJava: 21, width: null, height: null, "mojang");

        CollectionAssert.Contains(args, "com.evil.Hack");
    }

    // ================================================================
    // VerifyModsAsync
    // ================================================================

    [TestMethod]
    public async Task VerifyMods_VanillaLoader_ReturnsNull()
    {
        var instance = new GameInstance { LoaderType = "Vanilla", VersionId = "1.21" };
        var result = await _orchestrator.VerifyModsAsync(instance, null);
        Assert.IsNull(result, "Vanilla instance should skip verification entirely.");
    }

    [TestMethod]
    public async Task VerifyMods_NullLoaderType_ReturnsNull()
    {
        var instance = new GameInstance { LoaderType = null, VersionId = "1.21" };
        var result = await _orchestrator.VerifyModsAsync(instance, null);
        Assert.IsNull(result, "Null loader type should skip verification.");
    }

    [TestMethod]
    public async Task VerifyMods_NoModsDir_ReturnsNull()
    {
        var instance = new GameInstance { LoaderType = "Forge", VersionId = "1.21" };
        var result = await _orchestrator.VerifyModsAsync(instance, null);
        Assert.IsNull(result, "Missing mods directory should skip verification.");
    }

    [TestMethod]
    public async Task VerifyMods_EmptyModsDir_ReturnsNull()
    {
        string modsDir = Path.Combine(_tempGameDir, "mods");
        Directory.CreateDirectory(modsDir);

        var instance = new GameInstance { LoaderType = "Forge", VersionId = "1.21" };
        var result = await _orchestrator.VerifyModsAsync(instance, null);
        Assert.IsNull(result, "Empty mods directory should skip verification.");
    }

    [TestMethod]
    public async Task VerifyMods_AllVerified_ReturnsNull()
    {
        // Create a test JAR and compute its SHA-1
        string modsDir = Path.Combine(_tempGameDir, "mods");
        Directory.CreateDirectory(modsDir);
        string jarPath = Path.Combine(modsDir, "test-mod.jar");
        string jarContent = "fake jar content for hash";
        File.WriteAllText(jarPath, jarContent);
        string sha1 = Convert.ToHexString(
            System.Security.Cryptography.SHA1.HashData(
                System.Text.Encoding.UTF8.GetBytes(jarContent))).ToLowerInvariant();

        // Configure FakeModrinthService to return a match
        var fakeModrinth = new FakeModrinthService
        {
            GetVersionFilesByHashesResult = new Dictionary<string, VersionFileResponse>
            {
                [sha1] = new VersionFileResponse { Id = "vf-1", ProjectId = "proj-1", VersionId = "ver-1" }
            }
        };

        // Create orchestrator with the configured fake
        var orchestrator = CreateTestOrchestrator(fakeModrinth);

        var instance = new GameInstance { LoaderType = "Forge", VersionId = "1.21" };
        var result = await orchestrator.VerifyModsAsync(instance, null);
        Assert.IsNull(result, "All mods verified should return null (no warning needed).");
    }

    [TestMethod]
    public async Task VerifyMods_SomeUnverified_UserApproves_ReturnsNull()
    {
        string modsDir = Path.Combine(_tempGameDir, "mods");
        Directory.CreateDirectory(modsDir);
        string jarPath = Path.Combine(modsDir, "unverified-mod.jar");
        File.WriteAllText(jarPath, "unknown mod content");

        // Empty response → no SHA-1s match
        var fakeModrinth = new FakeModrinthService
        {
            GetVersionFilesByHashesResult = new Dictionary<string, VersionFileResponse>()
        };

        var orchestrator = CreateTestOrchestrator(fakeModrinth);
        bool handlerCalled = false;
        orchestrator.ModsWarningHandler = (files) =>
        {
            handlerCalled = true;
            Assert.AreEqual(1, files.Count);
            return Task.FromResult(true); // user approves
        };

        var instance = new GameInstance { LoaderType = "Forge", VersionId = "1.21" };
        var result = await orchestrator.VerifyModsAsync(instance, null);
        Assert.IsNull(result, "User approved → should return null.");
        Assert.IsTrue(handlerCalled, "ModsWarningHandler should have been called.");
    }

    [TestMethod]
    public async Task VerifyMods_UserDeclines_ReturnsLaunchResult()
    {
        string modsDir = Path.Combine(_tempGameDir, "mods");
        Directory.CreateDirectory(modsDir);
        File.WriteAllText(Path.Combine(modsDir, "suspicious.jar"), "suspicious content");

        var fakeModrinth = new FakeModrinthService
        {
            GetVersionFilesByHashesResult = new Dictionary<string, VersionFileResponse>()
        };

        var orchestrator = CreateTestOrchestrator(fakeModrinth);
        orchestrator.ModsWarningHandler = (files) => Task.FromResult(false); // user declines

        var instance = new GameInstance { LoaderType = "Forge", VersionId = "1.21" };
        var result = await orchestrator.VerifyModsAsync(instance, null);
        Assert.IsNotNull(result, "User declined → should return LaunchResult.");
        Assert.IsFalse(result!.Success, "LaunchResult.Success should be false.");
        StringAssert.Contains(result.ErrorMessage!, "suspicious.jar");
    }

    [TestMethod]
    public async Task VerifyMods_ApiError_SilentlyReturnsNull()
    {
        string modsDir = Path.Combine(_tempGameDir, "mods");
        Directory.CreateDirectory(modsDir);
        File.WriteAllText(Path.Combine(modsDir, "test.jar"), "content");

        var fakeModrinth = new FakeModrinthService
        {
            GetVersionFilesByHashesThrows = new HttpRequestException("Network error")
        };

        var orchestrator = CreateTestOrchestrator(fakeModrinth);

        var instance = new GameInstance { LoaderType = "Forge", VersionId = "1.21" };
        var result = await orchestrator.VerifyModsAsync(instance, null);
        Assert.IsNull(result, "API error should be silently swallowed.");
    }

    [TestMethod]
    public async Task VerifyMods_IsolatedInstance_UsesInstanceDir()
    {
        string isolatedDir = Path.Combine(_tempGameDir, "instances", "test-instance");
        string modsDir = Path.Combine(isolatedDir, "mods");
        Directory.CreateDirectory(modsDir);
        File.WriteAllText(Path.Combine(modsDir, "isolated-mod.jar"), "content");

        var fakeModrinth = new FakeModrinthService
        {
            GetVersionFilesByHashesResult = new Dictionary<string, VersionFileResponse>()
        };

        var orchestrator = CreateTestOrchestrator(fakeModrinth);
        int warnedFileCount = 0;
        orchestrator.ModsWarningHandler = (files) =>
        {
            warnedFileCount = files.Count;
            return Task.FromResult(true);
        };

        var instance = new GameInstance
        {
            LoaderType = "Fabric",
            VersionId = "1.21",
            UseIsolatedDir = true,
            InstanceDir = isolatedDir
        };

        await orchestrator.VerifyModsAsync(instance, null);
        Assert.AreEqual(1, warnedFileCount, "Isolated instance mods should be found.");
    }

    private LaunchOrchestrator CreateTestOrchestrator(FakeModrinthService modrinth)
    {
        return new LaunchOrchestrator(
            new HttpClient(),
            new FakeAuthService(),
            new FakeModLoaderService(),
            _notificationService,
            _gameFolder,
            new SaveLockService(),
            modrinth);
    }

    // ================================================================
    // B1: JVM arg trimming
    // ================================================================

    [TestMethod]
    public void BuildLaunchArgs_JvmArgWithSpaces_Trimmed()
    {
        // Simulate Fabric API's malformed JVM arg with leading/trailing spaces
        var detail = CreateMinimalDetailWithJvmArgs(new object[]
        {
            "-DFabricMcEmu= net.minecraft.client.main.Main "
        });

        var args = _orchestrator.BuildLaunchArgs(detail, "1.21", _tempGameDir,
            "Player", "release", "token", "uuid-123",
            memory: 2048, targetJava: 21, width: null, height: null, "mojang");

        // The trimmed arg should be present
        CollectionAssert.Contains(args, "-DFabricMcEmu=net.minecraft.client.main.Main",
            "JVM arg with spaces should be trimmed");
        // The original untrimmed arg should NOT be present
        Assert.IsFalse(args.Any(a => a.Contains(" net.minecraft")),
            "No arg should contain leading space before net.minecraft");
    }

    [TestMethod]
    public void BuildLaunchArgs_LeadingTrailingSpaces_Removed()
    {
        var detail = CreateMinimalDetailWithJvmArgs(new object[]
        {
            "  -Dfoo=bar  "
        });

        var args = _orchestrator.BuildLaunchArgs(detail, "1.21", _tempGameDir,
            "Player", "release", "token", "uuid-123",
            memory: 2048, targetJava: 21, width: null, height: null, "mojang");

        CollectionAssert.Contains(args, "-Dfoo=bar",
            "JVM arg with surrounding spaces should be trimmed");
    }

    // ================================================================
    // B4: Arch rule filtering (integration tests)
    // ================================================================

    [TestMethod]
    public void BuildLaunchArgs_ArchRuleX86_On64Bit_Filtered()
    {
        var detail = CreateMinimalDetailWithJvmArgs(new object[]
        {
            new
            {
                rules = new[] { new { action = "allow", os = new { arch = "x86" } } },
                value = "-Xss1M"
            }
        });

        var args = _orchestrator.BuildLaunchArgs(detail, "1.21", _tempGameDir,
            "Player", "release", "token", "uuid-123",
            memory: 2048, targetJava: 21, width: null, height: null, "mojang");

        if (Environment.Is64BitOperatingSystem)
            Assert.IsFalse(args.Contains("-Xss1M"),
                "-Xss1M with arch=x86 rule should be filtered on 64-bit OS");
        else
            CollectionAssert.Contains(args, "-Xss1M",
                "-Xss1M with arch=x86 rule should pass on 32-bit OS");
    }

    [TestMethod]
    public void BuildLaunchArgs_ArchRuleX86_WithOsWindows()
    {
        var detail = CreateMinimalDetailWithJvmArgs(new object[]
        {
            new
            {
                rules = new[] { new { action = "allow", os = new { name = "windows", arch = "x86" } } },
                value = "-Dlegacy.flag=true"
            }
        });

        var args = _orchestrator.BuildLaunchArgs(detail, "1.21", _tempGameDir,
            "Player", "release", "token", "uuid-123",
            memory: 2048, targetJava: 21, width: null, height: null, "mojang");

        bool expected = OperatingSystem.IsWindows() && !Environment.Is64BitOperatingSystem;
        Assert.AreEqual(expected, args.Contains("-Dlegacy.flag=true"),
            "JVM arg with os=windows+arch=x86 should only pass on 32-bit Windows");
    }

    // ================================================================
    // Helpers
    // ================================================================

    private VersionDetail CreateMinimalDetail()
    {
        return new VersionDetail
        {
            Id = "1.21",
            Type = "release",
            MainClass = "net.minecraft.client.main.Main",
            Assets = "16",
            Libraries = new List<Library>()
        };
    }

    /// <summary>Creates a minimal detail with JVM arguments constructed from anonymous objects.</summary>
    private VersionDetail CreateMinimalDetailWithJvmArgs(object[] jvmObjects)
    {
        var detail = CreateMinimalDetail();
        var jvmList = new List<System.Text.Json.JsonElement>();
        foreach (var obj in jvmObjects)
        {
            var json = JsonSerializer.Serialize(obj);
            jvmList.Add(JsonSerializer.Deserialize<JsonElement>(json));
        }
        detail.Arguments = new Arguments { Jvm = jvmList };
        return detail;
    }

    private VersionDetail CreateDetailWithLibrary(Library lib)
    {
        var detail = CreateMinimalDetail();
        detail.Libraries!.Add(lib);
        return detail;
    }

    private string CreateFakeLibrary(string relativePath)
    {
        var fullPath = Path.Combine(_tempGameDir, "libraries", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, "fake jar content");
        return fullPath;
    }
}
