using System.Text.Json;
using EiTRVO.ProEngine.Models;

namespace EiTRVO.Tests.Models;

[TestClass]

public class ModelSerializationTests
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    // ================================================================
    // VersionDetail
    // ================================================================

    [TestMethod]
    public void VersionDetail_RoundTrip()
    {
        var original = new VersionDetail
        {
            Id = "1.21-forge-50.1.0",
            Type = "release",
            MainClass = "net.minecraft.launchwrapper.Launch",
            InheritsFrom = "1.21",
            Assets = "16",
            Libraries = new List<Library>
            {
                new() { Name = "net.minecraft:launchwrapper:1.12" },
                new() { Name = "com.example:lib:1.0", Downloads = new LibraryDownloads { Artifact = new DownloadInfo { Path = "com/example/lib/1.0/lib-1.0.jar", Url = "https://example.com/lib.jar" } } }
            }
        };

        var json = JsonSerializer.Serialize(original, JsonOpts);
        var restored = JsonSerializer.Deserialize<VersionDetail>(json);

        Assert.IsNotNull(restored);
        Assert.AreEqual(original.Id, restored!.Id);
        Assert.AreEqual(original.MainClass, restored.MainClass);
        Assert.AreEqual(original.InheritsFrom, restored.InheritsFrom);
        Assert.AreEqual(2, restored.Libraries!.Count);
    }

    [TestMethod]
    public void VersionDetail_DeserializesLegacyLibrary()
    {
        var json = @"{""id"":""1.8.9-forge"",""libraries"":[{""name"":""net.minecraft:launchwrapper:1.12""}]}";
        var detail = JsonSerializer.Deserialize<VersionDetail>(json);

        Assert.IsNotNull(detail);
        Assert.AreEqual(1, detail!.Libraries!.Count);
        Assert.AreEqual("net.minecraft:launchwrapper:1.12", detail.Libraries![0].Name);
        Assert.IsNull(detail.Libraries[0].Downloads);
    }

    // ================================================================
    // LauncherSettings
    // ================================================================

    [TestMethod]
    public void LauncherSettings_RoundTrip()
    {
        var original = new LauncherSettings
        {
            MemoryMB = 8192,
            Resolution = "1920x1080",
            JavaPath = @"C:\Program Files\Java\jdk-21\bin\javaw.exe",
            IsolateNewInstancesByDefault = true
        };

        var json = JsonSerializer.Serialize(original, JsonOpts);
        var restored = JsonSerializer.Deserialize<LauncherSettings>(json);

        Assert.IsNotNull(restored);
        Assert.AreEqual(8192, restored!.MemoryMB);
        Assert.AreEqual("1920x1080", restored.Resolution);
        Assert.AreEqual(@"C:\Program Files\Java\jdk-21\bin\javaw.exe", restored.JavaPath);
        Assert.IsTrue(restored.IsolateNewInstancesByDefault);
    }

    [TestMethod]
    public void LauncherSettings_Defaults()
    {
        var settings = new LauncherSettings();
        Assert.AreEqual(2048, settings.MemoryMB);
        Assert.IsNull(settings.Resolution);
        Assert.IsTrue(settings.IsolateNewInstancesByDefault);
    }

    // ================================================================
    // PackManifest
    // ================================================================

    [TestMethod]
    public void PackManifest_RoundTrip()
    {
        var original = new PackManifest
        {
            Format = "eitrvo-pack:1",
            Name = "Test Pack",
            Author = "Tester",
            ExporterVersion = 26,
            InheritsFrom = "1.21",
            Minecraft = new PackMinecraftInfo
            {
                Version = "1.21",
                ModLoader = "Forge",
                ModLoaderVersion = "50.1.0"
            },
            Mods = new List<PackModEntry>
            {
                new() { Name = "mod.jar", Sha256 = "abc123" },
                new() { Name = "lib.jar", Sha256 = "def456" }
            }
        };

        var json = JsonSerializer.Serialize(original, JsonOpts);
        var restored = JsonSerializer.Deserialize<PackManifest>(json);

        Assert.IsNotNull(restored);
        Assert.AreEqual("eitrvo-pack:1", restored!.Format);
        Assert.AreEqual("Test Pack", restored.Name);
        Assert.AreEqual("Forge", restored.Minecraft!.ModLoader);
        Assert.AreEqual(2, restored.Mods!.Count);
    }

    // ================================================================
    // DownloadProgress
    // ================================================================

    [TestMethod]
    public void DownloadProgress_Overall()
    {
        var p = DownloadProgress.Overall(5, 10);
        Assert.AreEqual(5, p.BytesDownloaded);
        Assert.AreEqual(10, p.TotalBytes);
        Assert.IsNull(p.CurrentFileName);
    }

    [TestMethod]
    public void DownloadProgress_FileProgress()
    {
        var p = DownloadProgress.FileProgress("test.jar", 5000, 10000, 250000, 3, 8);
        Assert.AreEqual("test.jar", p.CurrentFileName);
        Assert.AreEqual(5000, p.CurrentFileDownloadedBytes);
        Assert.AreEqual(10000, p.CurrentFileTotalBytes);
        Assert.AreEqual(250000, p.DownloadSpeedBytesPerSecond);
        Assert.AreEqual(3, p.BytesDownloaded); // overall
        Assert.AreEqual(8, p.TotalBytes);
    }

    // ================================================================
    // ModLoaderVersion — DisplayText computed property
    // ================================================================

    [TestMethod]
    public void ModLoaderVersion_DisplayText_RecommendedLatest()
    {
        var v = new ModLoaderVersion { LoaderVersion = "0.16.10", IsRecommended = true, IsLatest = true };
        Assert.AreEqual("0.16.10 (最新, 推荐)", v.DisplayText);
    }

    [TestMethod]
    public void ModLoaderVersion_DisplayText_RecommendedOnly()
    {
        var v = new ModLoaderVersion { LoaderVersion = "0.16.10", IsRecommended = true, IsLatest = false };
        Assert.AreEqual("0.16.10 (推荐)", v.DisplayText);
    }

    [TestMethod]
    public void ModLoaderVersion_DisplayText_LatestOnly()
    {
        var v = new ModLoaderVersion { LoaderVersion = "0.16.10", IsRecommended = false, IsLatest = true };
        Assert.AreEqual("0.16.10 (最新)", v.DisplayText);
    }

    [TestMethod]
    public void ModLoaderVersion_DisplayText_None()
    {
        var v = new ModLoaderVersion { LoaderVersion = "0.16.10", IsRecommended = false, IsLatest = false };
        Assert.AreEqual("0.16.10", v.DisplayText);
    }

    // ================================================================
    // GameInstance — LoaderBadgeText computed property
    // ================================================================

    [TestMethod]
    public void GameInstance_LoaderBadgeText_Forge()
    {
        var inst = new GameInstance { LoaderType = "Forge" };
        Assert.AreEqual("Forge", inst.LoaderBadgeText);
    }

    [TestMethod]
    public void GameInstance_LoaderBadgeText_Fabric()
    {
        var inst = new GameInstance { LoaderType = "Fabric" };
        Assert.AreEqual("Fabric", inst.LoaderBadgeText);
    }

    [TestMethod]
    public void GameInstance_LoaderBadgeText_Quilt()
    {
        var inst = new GameInstance { LoaderType = "Quilt" };
        Assert.AreEqual("Quilt", inst.LoaderBadgeText);
    }

    [TestMethod]
    public void GameInstance_LoaderBadgeText_NeoForge()
    {
        var inst = new GameInstance { LoaderType = "NeoForge" };
        Assert.AreEqual("NeoForge", inst.LoaderBadgeText);
    }

    [TestMethod]
    public void GameInstance_LoaderBadgeText_OptiFine()
    {
        var inst = new GameInstance { LoaderType = "OptiFine" };
        Assert.AreEqual("OptiFine", inst.LoaderBadgeText);
    }

    [TestMethod]
    public void GameInstance_LoaderBadgeText_Vanilla_OrNull()
    {
        // null and "Vanilla" both default to "Vanilla" in the switch
        var inst = new GameInstance { LoaderType = null };
        Assert.AreEqual("Vanilla", inst.LoaderBadgeText);

        var inst2 = new GameInstance { LoaderType = "Vanilla" };
        Assert.AreEqual("Vanilla", inst2.LoaderBadgeText);
    }

    [TestMethod]
    public void GameInstance_ShowLoaderBadge_True()
    {
        var inst = new GameInstance { LoaderType = "Forge" };
        Assert.IsTrue(inst.ShowLoaderBadge);
    }

    [TestMethod]
    public void GameInstance_ShowLoaderBadge_False_WhenVanilla()
    {
        var inst = new GameInstance { LoaderType = "Vanilla" };
        Assert.IsFalse(inst.ShowLoaderBadge);
    }

    [TestMethod]
    public void GameInstance_ShowLoaderBadge_False_WhenNull()
    {
        var inst = new GameInstance { LoaderType = null };
        Assert.IsFalse(inst.ShowLoaderBadge);
    }

    // ================================================================
    // VersionEntry — TypeDisplay + TypeLabel
    // ================================================================

    [TestMethod]
    public void VersionEntry_TypeDisplay_Release()
    {
        var entry = new VersionEntry { Type = "release" };
        Assert.AreEqual("正式版", entry.TypeDisplay);
    }

    [TestMethod]
    public void VersionEntry_TypeDisplay_Snapshot()
    {
        var entry = new VersionEntry { Type = "snapshot" };
        Assert.AreEqual("快照版", entry.TypeDisplay);
    }

    [TestMethod]
    public void VersionEntry_TypeDisplay_OldBeta()
    {
        var entry = new VersionEntry { Type = "old_beta" };
        Assert.AreEqual("旧测试版", entry.TypeDisplay);
    }

    [TestMethod]
    public void VersionEntry_TypeLabel_Release()
    {
        var entry = new VersionEntry { Type = "release" };
        Assert.AreEqual("Release", entry.TypeLabel);
    }

    // ================================================================
    // LogEntry — TypeLabel
    // ================================================================

    [TestMethod]
    public void LogEntry_TypeLabel_All()
    {
        Assert.AreEqual("INFO", new LogEntry { Type = NotificationType.Info }.TypeLabel);
        Assert.AreEqual("OK", new LogEntry { Type = NotificationType.Success }.TypeLabel);
        Assert.AreEqual("WARN", new LogEntry { Type = NotificationType.Warning }.TypeLabel);
        Assert.AreEqual("ERR", new LogEntry { Type = NotificationType.Error }.TypeLabel);
    }

    // ================================================================
    // Account — MaskedUUID
    // ================================================================

    [TestMethod]
    public void Account_MaskedUUID_Normal()
    {
        var account = new Account { UUID = "abcdef0123456789abcdef0123456789" };
        // 32-char UUID → first 8 chars + 24 asterisks
        StringAssert.StartsWith(account.MaskedUUID, "abcdef01");
        Assert.AreEqual(32, account.MaskedUUID.Length);
    }

    [TestMethod]
    public void Account_MaskedUUID_Empty()
    {
        var account = new Account { UUID = "" };
        Assert.AreEqual("", account.MaskedUUID);
    }

    [TestMethod]
    public void Account_MaskedUUID_Short()
    {
        var account = new Account { UUID = "abc" };
        Assert.AreEqual("***", account.MaskedUUID);
    }

    // ================================================================
    // ModEntry — factory method FromFile
    // ================================================================

    [TestMethod]
    public void ModEntry_FromFile_NormalJar()
    {
        var entry = ModEntry.FromFile(@"C:\mods\jei-1.21.jar");
        Assert.AreEqual("jei-1.21", entry.Name);
        Assert.AreEqual("jei-1.21.jar", entry.FileName);
        Assert.IsFalse(entry.IsDisabled);
        Assert.AreEqual("禁用", entry.DisableButtonText);
    }

    [TestMethod]
    public void ModEntry_FromFile_DisabledModtemp()
    {
        var entry = ModEntry.FromFile(@"C:\mods\broken.jar.modtemp");
        // .modtemp → strips .modtemp first (→ broken.jar), then strips .jar (→ broken)
        Assert.AreEqual("broken", entry.Name);
        Assert.AreEqual("broken.jar.modtemp", entry.FileName);
        Assert.IsTrue(entry.IsDisabled);
        Assert.AreEqual("启用", entry.DisableButtonText);
    }

    [TestMethod]
    public void ModEntry_DisableButtonText_Toggles()
    {
        var entry = new ModEntry { IsDisabled = false };
        Assert.AreEqual("禁用", entry.DisableButtonText);
        entry.IsDisabled = true;
        Assert.AreEqual("启用", entry.DisableButtonText);
    }

    // ================================================================
    // ModEntry — Modrinth metadata computed properties
    // ================================================================

    [TestMethod]
    public void ModEntry_HasModrinthMetadata_NoTitle_ReturnsFalse()
    {
        var entry = new ModEntry { ModrinthTitle = "" };
        Assert.IsFalse(entry.HasModrinthMetadata);
    }

    [TestMethod]
    public void ModEntry_HasModrinthMetadata_TitleSet_ReturnsTrue()
    {
        var entry = new ModEntry { ModrinthTitle = "Just Enough Items" };
        Assert.IsTrue(entry.HasModrinthMetadata);
    }

    [TestMethod]
    public void ModEntry_DisplayTitle_FallsBackToName()
    {
        var entry = new ModEntry { Name = "jei-1.21", ModrinthTitle = "" };
        Assert.AreEqual("jei-1.21", entry.DisplayTitle);
    }

    [TestMethod]
    public void ModEntry_DisplayTitle_PrefersModrinthTitle()
    {
        var entry = new ModEntry { Name = "jei-1.21", ModrinthTitle = "Just Enough Items" };
        Assert.AreEqual("Just Enough Items", entry.DisplayTitle);
    }

    [TestMethod]
    public void ModEntry_DisplaySubtitle_FallsBackToFullPath()
    {
        var entry = new ModEntry { FullPath = @"C:\mods\jei-1.21.jar", ModrinthDescription = "" };
        Assert.AreEqual(@"C:\mods\jei-1.21.jar", entry.DisplaySubtitle);
    }

    [TestMethod]
    public void ModEntry_DisplaySubtitle_PrefersModrinthDescription()
    {
        var entry = new ModEntry
        {
            FullPath = @"C:\mods\jei-1.21.jar",
            ModrinthTitle = "JEI",
            ModrinthDescription = "View items and recipes"
        };
        Assert.AreEqual("View items and recipes", entry.DisplaySubtitle);
    }

    [TestMethod]
    public void ModEntry_NotifyDisplayPropertiesChanged_RaisesAllProperties()
    {
        var entry = new ModEntry { ModrinthTitle = "JEI", ModrinthDescription = "Desc" };
        var raised = new List<string>();
        entry.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);
        entry.NotifyDisplayPropertiesChanged();
        CollectionAssert.Contains(raised, nameof(ModEntry.HasModrinthMetadata));
        CollectionAssert.Contains(raised, nameof(ModEntry.DisplayTitle));
        CollectionAssert.Contains(raised, nameof(ModEntry.DisplaySubtitle));
    }

    // ================================================================
    // ResourcePackEntry — factory method FromPath
    // ================================================================

    [TestMethod]
    public void ResourcePackEntry_FromPath_ZipFile()
    {
        var entry = ResourcePackEntry.FromPath(@"C:\resourcepacks\Faithful.zip");
        Assert.AreEqual("Faithful", entry.Name);
        Assert.AreEqual("Faithful.zip", entry.FileName);
        Assert.IsFalse(entry.IsFolder);
        Assert.IsFalse(entry.IsDisabled);
    }

    [TestMethod]
    public void ResourcePackEntry_FromPath_DisabledRestemp()
    {
        var entry = ResourcePackEntry.FromPath(@"C:\resourcepacks\Pack.zip.restemp");
        Assert.AreEqual("Pack.zip", entry.Name); // .restemp stripped
        Assert.AreEqual("Pack.zip.restemp", entry.FileName);
        Assert.IsTrue(entry.IsDisabled);
        Assert.AreEqual("启用", entry.DisableButtonText);
    }

    // ================================================================
    // AuthModels — Serialization Round-Trip
    // ================================================================

    [TestMethod]
    public void AuthModels_DeviceCodeFlow_RoundTrip()
    {
        var original = new DeviceCodeResponse
        {
            DeviceCode = "abc-device-code",
            UserCode = "USER123",
            VerificationUri = "https://microsoft.com/link",
            ExpiresIn = 900,
            Interval = 5
        };
        var json = JsonSerializer.Serialize(original, JsonOpts);
        var restored = JsonSerializer.Deserialize<DeviceCodeResponse>(json);
        Assert.IsNotNull(restored);
        Assert.AreEqual("abc-device-code", restored!.DeviceCode);
        Assert.AreEqual("USER123", restored.UserCode);
        Assert.AreEqual(900, restored.ExpiresIn);
    }

    [TestMethod]
    public void XblXsts_McAuth_RoundTrip()
    {
        var xblResp = new XblAuthResponse
        {
            Token = "xbl-token-abc",
            DisplayClaims = new XblDisplayClaims
            {
                Xui = new List<XblXui> { new() { Uhs = "uhs-xyz" } }
            }
        };
        var xblJson = JsonSerializer.Serialize(xblResp, JsonOpts);
        var xblRestored = JsonSerializer.Deserialize<XblAuthResponse>(xblJson);
        Assert.IsNotNull(xblRestored);
        Assert.AreEqual("xbl-token-abc", xblRestored!.Token);
        Assert.AreEqual("uhs-xyz", xblRestored.DisplayClaims.Xui[0].Uhs);

        var mcResp = new McAuthResponse { AccessToken = "mc-access-token" };
        var mcJson = JsonSerializer.Serialize(mcResp, JsonOpts);
        var mcRestored = JsonSerializer.Deserialize<McAuthResponse>(mcJson);
        Assert.IsNotNull(mcRestored);
        Assert.AreEqual("mc-access-token", mcRestored!.AccessToken);
    }

    // ================================================================
    // ModrinthModels — Serialization Round-Trip
    // ================================================================

    [TestMethod]
    public void Modrinth_Search_RoundTrip()
    {
        var original = new ModrinthSearchResponse
        {
            Hits = new List<ModrinthHit>
            {
                new()
                {
                    ProjectId = "abc-123",
                    Title = "Test Mod",
                    Description = "A test mod",
                    Author = "Tester",
                    IconUrl = null,
                    LatestVersion = "1.0.0"
                }
            },
            TotalHits = 1,
            Offset = 0,
            Limit = 20
        };
        var json = JsonSerializer.Serialize(original, JsonOpts);
        var restored = JsonSerializer.Deserialize<ModrinthSearchResponse>(json);
        Assert.IsNotNull(restored);
        Assert.AreEqual(1, restored!.Hits.Count);
        Assert.AreEqual("abc-123", restored.Hits[0].ProjectId);
        Assert.AreEqual("Test Mod", restored.Hits[0].Title);
        Assert.IsNull(restored.Hits[0].IconUrl);
    }

    [TestMethod]
    public void Modrinth_Version_RoundTrip()
    {
        var original = new ModrinthVersion
        {
            Id = "ver-001",
            ProjectId = "proj-001",
            Name = "Release 1.0",
            VersionNumber = "1.0.0",
            GameVersions = new List<string> { "1.21", "1.21.5" },
            Files = new List<ModrinthFile>
            {
                new() { Url = "https://example.com/file.jar", Filename = "mod.jar" }
            }
        };
        var json = JsonSerializer.Serialize(original, JsonOpts);
        var restored = JsonSerializer.Deserialize<ModrinthVersion>(json);
        Assert.IsNotNull(restored);
        Assert.AreEqual("ver-001", restored!.Id);
        Assert.AreEqual("1.0.0", restored.VersionNumber);
        Assert.AreEqual(2, restored.GameVersions.Count);
        Assert.AreEqual(1, restored.Files.Count);
    }

    // ================================================================
    // ModrinthModels — New model serialization
    // ================================================================

    [TestMethod]
    public void VersionFileResponse_Deserializes()
    {
        var json = @"{""id"":""vid-001"",""project_id"":""proj-abc"",""version_id"":""ver-xyz""}";
        var result = JsonSerializer.Deserialize<VersionFileResponse>(json);
        Assert.IsNotNull(result);
        Assert.AreEqual("vid-001", result!.Id);
        Assert.AreEqual("proj-abc", result.ProjectId);
        Assert.AreEqual("ver-xyz", result.VersionId);
    }

    [TestMethod]
    public void VersionFilesBulkResponse_Deserializes()
    {
        var json = @"{""a1b2c3"":{""id"":""vf1"",""project_id"":""p1"",""version_id"":""v1""},""d4e5f6"":{""id"":""vf2"",""project_id"":""p2"",""version_id"":""v2""}}";
        var result = JsonSerializer.Deserialize<VersionFilesBulkResponse>(json);
        Assert.IsNotNull(result);
        Assert.AreEqual(2, result!.Count);
        Assert.AreEqual("p1", result["a1b2c3"].ProjectId);
        Assert.AreEqual("p2", result["d4e5f6"].ProjectId);
    }

    [TestMethod]
    public void ModrinthProject_Deserializes()
    {
        var json = @"{""id"":""proj-001"",""title"":""Just Enough Items"",""description"":""View items and recipes"",""slug"":""jei"",""icon_url"":""https://cdn.modrinth.com/jei.png""}";
        var result = JsonSerializer.Deserialize<ModrinthProject>(json);
        Assert.IsNotNull(result);
        Assert.AreEqual("proj-001", result!.Id);
        Assert.AreEqual("Just Enough Items", result.Title);
        Assert.AreEqual("View items and recipes", result.Description);
        Assert.AreEqual("jei", result.Slug);
        Assert.AreEqual("https://cdn.modrinth.com/jei.png", result.IconUrl);
    }

    [TestMethod]
    public void ModrinthProject_Deserializes_NullIcon()
    {
        var json = @"{""id"":""proj-002"",""title"":""NoIcon Mod""}";
        var result = JsonSerializer.Deserialize<ModrinthProject>(json);
        Assert.IsNotNull(result);
        Assert.IsNull(result!.IconUrl);
        Assert.AreEqual("", result.Description); // omitted → default
    }

    [TestMethod]
    public void InstanceMeta_RoundTrip()
    {
        var original = new InstanceMeta
        {
            UseIsolatedDir = true,
            InstanceDir = @"C:\mc\instances\MyPack",
            LoaderType = "Forge",
            LoaderVersion = "50.1.0",
            TotalPlayTimeSeconds = 3600,
            LastPlayedAt = new DateTimeOffset(2025, 7, 1, 12, 0, 0, TimeSpan.Zero)
        };
        var json = JsonSerializer.Serialize(original, JsonOpts);
        var restored = JsonSerializer.Deserialize<InstanceMeta>(json);
        Assert.IsNotNull(restored);
        Assert.IsTrue(restored!.UseIsolatedDir);
        Assert.AreEqual(@"C:\mc\instances\MyPack", restored.InstanceDir);
        Assert.AreEqual("Forge", restored.LoaderType);
        Assert.AreEqual("50.1.0", restored.LoaderVersion);
        Assert.AreEqual(3600, restored.TotalPlayTimeSeconds);
    }

    [TestMethod]
    public void InstanceMeta_Defaults()
    {
        var meta = new InstanceMeta();
        Assert.IsFalse(meta.UseIsolatedDir);
        Assert.IsNull(meta.InstanceDir);
        Assert.IsNull(meta.LoaderType);
        Assert.IsNull(meta.LoaderVersion);
        Assert.IsNull(meta.TotalPlayTimeSeconds);
        Assert.IsNull(meta.LastPlayedAt);
    }
}
