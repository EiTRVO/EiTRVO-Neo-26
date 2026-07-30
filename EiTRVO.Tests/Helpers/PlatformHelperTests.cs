using EiTRVO.ProEngine.Helpers;

namespace EiTRVO.Tests.Helpers;

[TestClass]
public class PlatformHelperTests
{
    [DataTestMethod]
    [DataRow("1.12.2", 8)]
    [DataRow("1.16.5", 8)]
    [DataRow("1.17", 16)]
    [DataRow("1.17.1", 16)]
    [DataRow("1.18", 17)]
    [DataRow("1.18.2", 17)]
    [DataRow("1.19.4", 17)]
    [DataRow("1.20", 17)]
    [DataRow("1.20.1", 17)]
    [DataRow("1.21", 21)]
    [DataRow("1.21.4", 21)]
    [DataRow("1.22", 21)]
    [DataRow("26.1", 25)]
    [DataRow("26.1.0", 25)]
    [DataRow("26.2", 25)]
    [DataRow("21w01a", 17)]
    public void GetMinecraftRequiredJavaVersion_ReturnsCorrectJava(string mcVersion, int expectedJava)
    {
        Assert.AreEqual(expectedJava, PlatformHelper.GetMinecraftRequiredJavaVersion(mcVersion));
    }

    [DataTestMethod]
    [DataRow("1.5.2", true)]
    [DataRow("1.4.7", true)]
    [DataRow("1.6.4", false)]
    [DataRow("1.12.2", false)]
    [DataRow("1.20.1", false)]
    [DataRow("a1.0.4", true)]
    [DataRow("b1.8.1", true)]
    [DataRow("c0.0.13a", true)]
    [DataRow("inf-20100618", true)]
    [DataRow("rd-132211", true)]
    [DataRow("Combat Test 8c", true)]
    public void IsLegacyVersion_ReturnsExpected(string versionId, bool expected)
    {
        Assert.AreEqual(expected, PlatformHelper.IsLegacyVersion(versionId));
    }

    // ================================================================
    // Composite version string extraction (Fabric/Quilt loader format)
    // ================================================================

    [DataTestMethod]
    [DataRow("fabric-loader-0.19.3-1.14.4", 8)]
    [DataRow("fabric-loader-0.16.5-1.21", 21)]
    [DataRow("fabric-loader-0.16.9-1.20.1", 17)]
    [DataRow("quilt-loader-0.24.0-1.20.1", 17)]
    [DataRow("quilt-loader-0.27.0-1.21.4", 21)]
    [DataRow("fabric-loader-0.15.11-1.16.5", 8)]
    public void GetMinecraftRequiredJavaVersion_CompositeVersionString_ReturnsCorrectJava(string versionId, int expectedJava)
    {
        Assert.AreEqual(expectedJava, PlatformHelper.GetMinecraftRequiredJavaVersion(versionId));
    }

    [TestMethod]
    public void GetMinecraftRequiredJavaVersion_UnknownCompositeFormat_ReturnsJava17()
    {
        // Unknown format that doesn't match the "1.x" pattern → falls back to Java 17
        Assert.AreEqual(17, PlatformHelper.GetMinecraftRequiredJavaVersion("unknown-format-string"));
    }
}
