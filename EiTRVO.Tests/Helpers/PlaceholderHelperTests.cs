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
}
