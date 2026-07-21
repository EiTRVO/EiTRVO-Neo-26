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

    [DataTestMethod]
    [DataRow("-javaagent:authlib-injector.jar=url", false)]
    [DataRow("-javaagent:../evil.jar", false)]
    [DataRow("-JAVAAGENT:C:\\path\\agent.jar", false)]
    [DataRow("-agentlib:jdwp=transport=dt_socket", false)]
    [DataRow("-agentlib:hprof", false)]
    [DataRow("-agentpath:C:\\agent.dll", false)]
    [DataRow("-agentpath:/opt/agent.so", false)]
    [DataRow("-Djava.security.manager", true)]    // optional — not in our filter
    [DataRow("-Dfoo=bar", true)]
    [DataRow("-Xmx2G", true)]
    [DataRow("-Xss1M", true)]
    [DataRow("--add-opens=java.base/java.lang=ALL-UNNAMED", true)]
    [DataRow("-cp", true)]
    [DataRow("-jar", true)]
    [DataRow("", true)]
    public void IsJvmArgSafe_ReturnsExpected(string arg, bool expected)
    {
        Assert.AreEqual(expected, JvmArgHelper.IsJvmArgSafe(arg));
    }

    [TestMethod]
    public void IsRuleAllowed_EmptyRules_ReturnsTrue()
    {
        Assert.IsTrue(JvmArgHelper.IsRuleAllowed(new List<EiTRVO.ProEngine.Models.Rule>()));
    }

    [DataTestMethod]
    [DataRow("net.minecraft.client.main.Main", true)]
    [DataRow("net.minecraft.launchwrapper.Launch", true)]
    [DataRow("cpw.mods.bootstraplauncher.BootstrapLauncher", true)]
    [DataRow("net.minecraftforge.bootstrap.ForgeBootstrap", true)]
    [DataRow("net.fabricmc.loader.impl.launch.knot.KnotClient", true)]
    [DataRow("net.fabricmc.loader.launch.knot.KnotClient", true)]
    [DataRow("net.neoforged.fancymodloader.launch.FMLServerLaunch", true)]
    [DataRow("org.quiltmc.loader.impl.launch.knot.KnotClient", true)]
    [DataRow("", false)]
    [DataRow("com.evil.MaliciousClass", false)]
    [DataRow("java.lang.Runtime", false)]
    [DataRow("net.malicious.agent.Agent", false)]
    public void IsMainClassSafe_ReturnsExpected(string? mainClass, bool expected)
    {
        Assert.AreEqual(expected, JvmArgHelper.IsMainClassSafe(mainClass));
    }

    [TestMethod]
    public void IsMainClassSafe_Null_ReturnsFalse()
    {
        Assert.IsFalse(JvmArgHelper.IsMainClassSafe(null));
    }

    [DataTestMethod]
    [DataRow("java.lang.Runtime", true)]
    [DataRow("java.lang.ProcessBuilder", true)]
    [DataRow("javax.script.ScriptEngineManager", true)]
    [DataRow("java.lang.reflect.Proxy", true)]
    [DataRow("jdk.jshell.JShell", true)]
    [DataRow("javax.tools.ToolProvider", true)]
    [DataRow("com.sun.internal.Foo", true)]
    [DataRow("sun.misc.Unsafe", true)]
    [DataRow("net.minecraft.client.main.Main", false)]
    [DataRow("", false)]
    public void IsMainClassBlocked_ReturnsExpected(string? mainClass, bool expected)
    {
        Assert.AreEqual(expected, JvmArgHelper.IsMainClassBlocked(mainClass));
    }

    [TestMethod]
    public void IsMainClassBlocked_Null_ReturnsFalse()
    {
        Assert.IsFalse(JvmArgHelper.IsMainClassBlocked(null));
    }
}
