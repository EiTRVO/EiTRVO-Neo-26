using EiTRVO.ProEngine.Orchestrators;

namespace EiTRVO.Tests.Orchestrators;

[TestClass]
public class JavaDetectionServiceTests
{
    [TestMethod]
    public async Task Detect_ReturnsList_NotNull()
    {
        var service = new JavaDetectionService();
        var results = await service.DetectAsync();
        Assert.IsNotNull(results);
    }

    [TestMethod]
    public async Task Detect_DeduplicatesByPath()
    {
        var service = new JavaDetectionService();
        var results = await service.DetectAsync();
        var paths = results.Select(j => j.Path).ToList();
        Assert.AreEqual(paths.Distinct().Count(), paths.Count);
    }

    [TestMethod]
    public async Task Detect_ResultsHaveValidPaths()
    {
        var service = new JavaDetectionService();
        var results = await service.DetectAsync();

        foreach (var java in results)
        {
            Assert.IsNotNull(java.Path);
            Assert.IsTrue(java.Path.Length > 0);
            if (java.Version != null)
                Assert.IsTrue(java.MajorVersion > 0,
                    $"MajorVersion should be >0 for {java.Path}, got {java.MajorVersion}");
        }
    }

    [TestMethod]
    public async Task Detect_DoesNotThrow()
    {
        var service = new JavaDetectionService();
        await service.DetectAsync();
    }
}
