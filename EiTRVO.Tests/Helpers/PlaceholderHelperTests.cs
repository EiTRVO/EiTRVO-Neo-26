using EiTRVO.ProEngine.Helpers;

namespace EiTRVO.Tests.Helpers;

[TestClass]

public class PlaceholderHelperTests
{
    [TestMethod]
    public void ReplacePlaceholders_ReplacesAllKnownTokens()
    {
        string result = PlaceholderHelper.ReplacePlaceholders(
            "${auth_player_name} ${version_name} ${game_directory}",
            "Steve", "1.21", "/assets", "legacy", "release", "token123", "uuid456", "/mc");
        Assert.AreEqual("Steve 1.21 /mc", result);
    }

    [TestMethod]
    public void ReplacePlaceholders_ReplacesAccessToken()
    {
        string result = PlaceholderHelper.ReplacePlaceholders(
            "--accessToken ${auth_access_token}", "P", "1.0", "", "", "", "secret", "", "");
        Assert.AreEqual("--accessToken secret", result);
    }

    [TestMethod]
    public void ReplacePlaceholders_ReplacesClientId()
    {
        string result = PlaceholderHelper.ReplacePlaceholders(
            "${clientid}", "", "", "", "", "", "", "", "");
        Assert.AreEqual("5a0b94a6-2810-4a43-a722-ba15271955b4", result);
    }

    [TestMethod]
    public void SplitMinecraftArguments_NullOrEmpty_ReturnsEmpty()
    {
        Assert.AreEqual(0, PlaceholderHelper.SplitMinecraftArguments(null).Count);
        Assert.AreEqual(0, PlaceholderHelper.SplitMinecraftArguments("").Count);
        Assert.AreEqual(0, PlaceholderHelper.SplitMinecraftArguments("   ").Count);
    }

    [TestMethod]
    public void SplitMinecraftArguments_SplitsOnSpaces()
    {
        var result = PlaceholderHelper.SplitMinecraftArguments("arg1 arg2 arg3");
        CollectionAssert.AreEqual(new[] { "arg1", "arg2", "arg3" }, result);
    }

    [TestMethod]
    public void SplitMinecraftArguments_RespectsQuotes()
    {
        var result = PlaceholderHelper.SplitMinecraftArguments("--name \"Steve Jobs\" --version 1.21");
        CollectionAssert.AreEqual(new[] { "--name", "Steve Jobs", "--version", "1.21" }, result);
    }

    [TestMethod]
    public void EnsureParameter_ReplacesExisting()
    {
        var args = new List<string> { "--old", "val", "--name", "oldname" };
        PlaceholderHelper.EnsureParameter(args, "--name", "newname");
        Assert.IsFalse(args.Contains("oldname"));
        CollectionAssert.Contains(args, "newname");
    }

    [TestMethod]
    public void EnsureParameter_AddsIfMissing()
    {
        var args = new List<string> { "--existing", "val" };
        PlaceholderHelper.EnsureParameter(args, "--name", "newname");
        CollectionAssert.Contains(args, "--name");
        CollectionAssert.Contains(args, "newname");
    }

    // ==================== B2: 新增占位符测试 ====================

    [TestMethod]
    public void ReplacePlaceholders_NativesDirectory_Replaced()
    {
        string result = PlaceholderHelper.ReplacePlaceholders(
            "-Djava.library.path=${natives_directory}",
            "", "", "", "", "", "", "", "", null, "D:/natives/1.21");
        Assert.AreEqual("-Djava.library.path=D:/natives/1.21", result);
    }

    [TestMethod]
    public void ReplacePlaceholders_NativesDirectoryNull_ReplacedWithEmpty()
    {
        // When nativesDirectory is not provided (default null), replace with empty string
        string result = PlaceholderHelper.ReplacePlaceholders(
            "-Djava.library.path=${natives_directory}",
            "", "", "", "", "", "", "", "");
        Assert.AreEqual("-Djava.library.path=", result);
    }

    [TestMethod]
    public void ReplacePlaceholders_LauncherNameAndVersion_Replaced()
    {
        string result = PlaceholderHelper.ReplacePlaceholders(
            "-Dminecraft.launcher.brand=${launcher_name} -Dminecraft.launcher.version=${launcher_version}",
            "", "", "", "", "", "", "", "");
        Assert.AreEqual("-Dminecraft.launcher.brand=eitrvo-neo -Dminecraft.launcher.version=26", result);
    }

    [TestMethod]
    public void ReplacePlaceholders_JnaTempDir_Replaced()
    {
        string result = PlaceholderHelper.ReplacePlaceholders(
            "-Djna.tmpdir=${natives_directory}",
            "", "", "", "", "", "", "", "", null, "/mc/natives/1.21");
        Assert.AreEqual("-Djna.tmpdir=/mc/natives/1.21", result);
    }
}
