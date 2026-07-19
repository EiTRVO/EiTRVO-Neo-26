using EiTRVO.Tests.Fakes;

namespace EiTRVO.Tests.Orchestrators;

[TestClass]
public class GameFolderServiceTests
{
    [TestMethod]
    public void VersionsDir_CanBeSetIndependently()
    {
        var fake = new FakeGameFolderService
        {
            GameDir = @"C:\mc\.minecraft",
            VersionsDir = @"C:\mc\.minecraft\versions"
        };
        Assert.AreEqual(@"C:\mc\.minecraft\versions", fake.VersionsDir);
    }

    [TestMethod]
    public void GetVersionDir_ReturnsCorrectPath()
    {
        var fake = new FakeGameFolderService
        {
            GameDir = @"C:\mc\.minecraft",
            VersionsDir = @"C:\mc\.minecraft\versions"
        };
        var result = fake.GetVersionDir("1.21");
        Assert.AreEqual(@"C:\mc\.minecraft\versions\1.21", result);
    }

    [TestMethod]
    public void GetVersionJsonPath_ReturnsCorrectFile()
    {
        var fake = new FakeGameFolderService
        {
            GameDir = @"C:\mc\.minecraft",
            VersionsDir = @"C:\mc\.minecraft\versions"
        };
        var result = fake.GetVersionJsonPath("1.21");
        Assert.AreEqual(@"C:\mc\.minecraft\versions\1.21\1.21.json", result);
    }

    [TestMethod]
    public void GetInstanceMetaPath_ReturnsCorrectFile()
    {
        var fake = new FakeGameFolderService
        {
            GameDir = @"C:\mc\.minecraft",
            VersionsDir = @"C:\mc\.minecraft\versions"
        };
        var result = fake.GetInstanceMetaPath("1.21");
        Assert.AreEqual(@"C:\mc\.minecraft\versions\1.21\instance.json", result);
    }
}
