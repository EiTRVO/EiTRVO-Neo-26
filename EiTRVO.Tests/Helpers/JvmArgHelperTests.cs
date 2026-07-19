using EiTRVO.ProEngine.Helpers;

namespace EiTRVO.Tests.Helpers;

[TestClass]

public class JvmArgHelperTests
{
    [DataTestMethod]
    [DataRow("--add-opens", 8, false)]
    [DataRow("--add-exports", 8, false)]
    [DataRow("--add-modules", 8, false)]
    [DataRow("--add-reads", 8, false)]
    [DataRow("--patch-module", 8, false)]
    [DataRow("--illegal-access", 8, false)]
    [DataRow("-Xmx2G", 8, true)]
    [DataRow("-Djava.library.path=natives", 8, true)]
    [DataRow("--add-opens", 17, true)]
    [DataRow("--add-exports", 17, true)]
    [DataRow("-XX:+UseG1GC", 8, true)]
    [DataRow("-XX:+UseG1GC", 21, true)]
    public void IsJvmArgCompatible_ReturnsExpected(string arg, int targetJava, bool expected)
    {
        Assert.AreEqual(expected, JvmArgHelper.IsJvmArgCompatible(arg, targetJava));
    }

    [DataTestMethod]
    [DataRow("-Dpath=\"C:\\Test\"", "-Dpath=C:\\Test")]
    [DataRow("simple", "simple")]
    [DataRow("-Dflag=value", "-Dflag=value")]
    [DataRow("-Dflag=\"hello world\"", "-Dflag=hello world")]
    [DataRow("", "")]
    [DataRow("--arg=\"\"", "--arg=")]
    public void StripEmbeddedQuotes_RemovesQuotedValue(string input, string expected)
    {
        Assert.AreEqual(expected, JvmArgHelper.StripEmbeddedQuotes(input));
    }

    [TestMethod]
    public void IsRuleAllowed_NullRules_ReturnsTrue()
    {
        Assert.IsTrue(JvmArgHelper.IsRuleAllowed(null));
    }

    [TestMethod]
    public void IsRuleAllowed_EmptyRules_ReturnsTrue()
    {
        Assert.IsTrue(JvmArgHelper.IsRuleAllowed(new List<EiTRVO.ProEngine.Models.Rule>()));
    }
}
