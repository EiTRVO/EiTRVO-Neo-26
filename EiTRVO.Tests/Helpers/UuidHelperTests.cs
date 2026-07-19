using EiTRVO.ProEngine.Helpers;

namespace EiTRVO.Tests.Helpers;

[TestClass]

public class UuidHelperTests
{
    [TestMethod]
    public void OfflineUuid_ReturnsValidGuid()
    {
        string result = UuidHelper.OfflineUuid("Steve");
        Assert.IsTrue(Guid.TryParse(result, out _));
    }

    [TestMethod]
    public void OfflineUuid_SameName_ReturnsSameUuid()
    {
        string a = UuidHelper.OfflineUuid("Notch");
        string b = UuidHelper.OfflineUuid("Notch");
        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void OfflineUuid_DifferentName_ReturnsDifferentUuid()
    {
        string a = UuidHelper.OfflineUuid("Steve");
        string b = UuidHelper.OfflineUuid("Alex");
        Assert.AreNotEqual(a, b);
    }

    [TestMethod]
    public void OfflineUuid_ReturnsProperlyFormattedUuid()
    {
        string result = UuidHelper.OfflineUuid("Player");
        Assert.AreEqual(36, result.Length);
        Assert.AreEqual('-', result[8]);
        Assert.AreEqual('-', result[13]);
        Assert.AreEqual('-', result[23]);
        Assert.IsTrue(Guid.TryParse(result, out _));
    }

    [DataTestMethod]
    [DataRow("a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6", "a1b2c3d4-e5f6-a7b8-c9d0-e1f2a3b4c5d6")]
    [DataRow("abc", "abc")] // too short, returned as-is
    public void FormatUuid_FormatsCorrectly(string input, string expected)
    {
        Assert.AreEqual(expected, UuidHelper.FormatUuid(input));
    }
}
