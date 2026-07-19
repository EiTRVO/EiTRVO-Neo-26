using System.Text.Json;
using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Orchestrators;
using EiTRVO.Tests.Fakes;

namespace EiTRVO.Tests.Orchestrators;

[TestClass]

public class InstanceManagerTests : IDisposable
{
    private readonly string _tempGameDir;
    private readonly FakeGameFolderService _gameFolder;
    private readonly InstanceManager _instanceManager;

    public InstanceManagerTests()
    {
        _tempGameDir = Path.Combine(Path.GetTempPath(), $"eitrvo_inst_test_{Guid.NewGuid():N}");
        var minecraftDir = Path.Combine(_tempGameDir, ".minecraft");
        Directory.CreateDirectory(minecraftDir);

        var versionsDir = Path.Combine(minecraftDir, "versions");
        Directory.CreateDirectory(versionsDir);

        _gameFolder = new FakeGameFolderService { GameDir = minecraftDir, VersionsDir = versionsDir };
        _instanceManager = new InstanceManager(_gameFolder);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempGameDir, true); } catch { }
    }

    // ================================================================
    // Scan
    // ================================================================

    [TestMethod]
    public void Refresh_EmptyVersionsDirectory()
    {
        _instanceManager.Refresh(null!);
        Assert.AreEqual(0, _instanceManager.Instances.Count);
    }

    [TestMethod]
    public void Refresh_SingleInstance()
    {
        CreateVersion("1.21", "1.21", "net.minecraft.client.main.Main");
        _instanceManager.Refresh(null!);
        Assert.AreEqual(1, _instanceManager.Instances.Count);
    }

    [TestMethod]
    public void Refresh_MultipleInstances()
    {
        CreateVersion("1.20.1", "1.20.1", "net.minecraft.client.main.Main");
        CreateVersion("1.21.5", "1.21.5", "net.minecraft.client.main.Main");
        _instanceManager.Refresh(null!);
        Assert.AreEqual(2, _instanceManager.Instances.Count);
    }

    // ================================================================
    // mainClass detection
    // ================================================================

    [TestMethod]
    public void Refresh_DetectsForge_Legacy()
    {
        CreateVersion("Forge 1.8.9", "1.8.9-forge", "net.minecraft.launchwrapper.Launch",
            inheritsFrom: "1.8.9",
            libraries: new List<Library>
            {
                new() { Name = "net.minecraftforge:forge:1.8.9-11.15.1.1722" }
            });
        _instanceManager.Refresh(null!);
        var inst = _instanceManager.Instances[0];
        Assert.AreEqual("Forge", inst.LoaderType);
    }

    [TestMethod]
    public void Refresh_DetectsForge_Modern()
    {
        CreateVersion("Forge 1.20.1", "1.20.1-forge-50.1.0", "cpw.mods.bootstraplauncher.BootstrapLauncher");
        _instanceManager.Refresh(null!);
        var inst = _instanceManager.Instances[0];
        Assert.AreEqual("Forge", inst.LoaderType);
    }

    [TestMethod]
    public void Refresh_DetectsFabric()
    {
        CreateVersion("Fabric 1.21", "fabric-loader-0.16.0-1.21", "net.fabricmc.loader.impl.launch.knot.KnotClient");
        _instanceManager.Refresh(null!);
        var inst = _instanceManager.Instances[0];
        Assert.AreEqual("Fabric", inst.LoaderType);
    }

    [TestMethod]
    public void Refresh_DetectsVanilla()
    {
        CreateVersion("Vanilla 1.21", "1.21", "net.minecraft.client.main.Main");
        _instanceManager.Refresh(null!);
        var inst = _instanceManager.Instances[0];
        // Vanilla main class doesn't match any loader pattern → LoaderType is null (displayed as "Vanilla")
        Assert.IsNull(inst.LoaderType);
    }

    [TestMethod]
    public void Refresh_DetectsNeoForge()
    {
        CreateVersion("NeoForge 1.21.5", "neoforge-21.5.0", "net.neoforged.fancymodloader.launch.FMLServerLaunch");
        _instanceManager.Refresh(null!);
        var inst = _instanceManager.Instances[0];
        Assert.AreEqual("NeoForge", inst.LoaderType);
    }

    [TestMethod]
    public void Refresh_DetectsVanilla_LaunchwrapperMainClass()
    {
        // Pre-1.6 vanilla versions (Alpha/Beta/1.0-1.5.2) have
        // mainClass = "net.minecraft.launchwrapper.Launch" but no Forge libraries.
        // They should NOT be detected as Forge.
        CreateVersion("a1.0.4", "a1.0.4", "net.minecraft.launchwrapper.Launch",
            libraries: new List<Library>
            {
                new() { Name = "net.minecraft:launchwrapper:1.5" }
            });
        _instanceManager.Refresh(null!);
        var inst = _instanceManager.Instances[0];
        Assert.IsNull(inst.LoaderType); // Vanilla — not Forge
    }

    // ================================================================
    // Helpers
    // ================================================================

    // ================================================================
    // FindByName
    // ================================================================

    [TestMethod]
    public void FindByName_Exists_ReturnsInstance()
    {
        CreateVersion("1.21", "1.21", "net.minecraft.client.main.Main");
        _instanceManager.Refresh(null!);
        var instance = _instanceManager.FindByName("1.21");
        Assert.IsNotNull(instance);
        Assert.AreEqual("1.21", instance!.Name);
    }

    [TestMethod]
    public void FindByName_NotExists_ReturnsNull()
    {
        CreateVersion("1.21", "1.21", "net.minecraft.client.main.Main");
        _instanceManager.Refresh(null!);
        var instance = _instanceManager.FindByName("nonexistent");
        Assert.IsNull(instance);
    }

    // ================================================================
    // Scan — corrupt version.json
    // ================================================================

    [TestMethod]
    public void Scan_SkipsCorruptVersionJson()
    {
        var versionDir = Path.Combine(_gameFolder.VersionsDir, "corruptInstance");
        Directory.CreateDirectory(versionDir);
        File.WriteAllText(Path.Combine(versionDir, "version.json"), "this is not valid json!!!");
        File.WriteAllText(Path.Combine(versionDir, "corruptInstance.jar"), "fake jar");

        // Pass a real callback instead of null to avoid NullReferenceException
        Action<string, EiTRVO.ProEngine.Models.NotificationType> logWarning = (msg, type) => { };
        _instanceManager.Refresh(logWarning);
        // Corrupt version.json → parsed[].detail is null → filtered in first pass → no instance added
        Assert.AreEqual(0, _instanceManager.Instances.Count);
    }

    private void CreateVersion(string instanceName, string versionId, string mainClass,
        string? inheritsFrom = null, List<Library>? libraries = null)
    {
        var versionDir = Path.Combine(_gameFolder.VersionsDir, instanceName);
        Directory.CreateDirectory(versionDir);

        var detail = new VersionDetail
        {
            Id = versionId,
            MainClass = mainClass,
            Type = "release",
            InheritsFrom = inheritsFrom,
            Libraries = libraries ?? new List<Library>()
        };
        var json = JsonSerializer.Serialize(detail);
        File.WriteAllText(Path.Combine(versionDir, "version.json"), json);

        // Create the jar file that Scan() expects
        string jarPath;
        if (!string.IsNullOrEmpty(inheritsFrom))
        {
            // For inheritsFrom, jar is in parent's directory
            var parentDir = Path.Combine(_gameFolder.VersionsDir, inheritsFrom);
            Directory.CreateDirectory(parentDir);
            jarPath = Path.Combine(parentDir, $"{inheritsFrom}.jar");
        }
        else
        {
            jarPath = Path.Combine(versionDir, $"{versionId}.jar");
        }
        if (!File.Exists(jarPath))
            File.WriteAllText(jarPath, "fake jar");

        // Write instance.json
        var meta = new InstanceMeta { LoaderType = null, LoaderVersion = null };
        File.WriteAllText(Path.Combine(versionDir, "instance.json"),
            JsonSerializer.Serialize(meta));
    }
}
