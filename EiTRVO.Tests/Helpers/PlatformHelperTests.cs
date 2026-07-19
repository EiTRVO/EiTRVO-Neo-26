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
    [DataRow("1.21", 17)]
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
}
