using EiTRVO.ProEngine.Helpers;

namespace EiTRVO.Tests.Helpers;

[TestClass]
public class PathSafetyHelperTests
{
    // ================================================================
    // SanitizeNameComponent
    // ================================================================

    [DataTestMethod]
    [DataRow("normal", "normal")]
    [DataRow("../../../evil", "evil")]
    [DataRow("test\\evil", "evil")]
    [DataRow("test/evil", "evil")]
    [DataRow("..\\..\\Windows\\System32", "System32")]
    [DataRow("foo/bar/baz", "baz")]
    [DataRow("", "unnamed")]
    [DataRow("   ", "unnamed")]
    [DataRow("  valid  ", "valid")]
    public void SanitizeNameComponent_ReturnsExpected(string? input, string expected)
    {
        Assert.AreEqual(expected, PathSafetyHelper.SanitizeNameComponent(input));
    }

    [TestMethod]
    public void SanitizeNameComponent_Null_ReturnsUnnamed()
    {
        Assert.AreEqual("unnamed", PathSafetyHelper.SanitizeNameComponent(null));
    }

    // ================================================================
    // IsContained
    // ================================================================

    [TestMethod]
    public void IsContained_NormalPath_ReturnsTrue()
    {
        string baseDir = Path.GetFullPath(Path.GetTempPath());
        string destPath = Path.Combine(baseDir, "subfolder", "file.txt");
        Assert.IsTrue(PathSafetyHelper.IsContained(destPath, baseDir));
    }

    [TestMethod]
    public void IsContained_ExactBase_ReturnsTrue()
    {
        string baseDir = Path.GetFullPath(Path.GetTempPath());
        Assert.IsTrue(PathSafetyHelper.IsContained(baseDir, baseDir));
    }

    [TestMethod]
    public void IsContained_Traversal_ReturnsFalse()
    {
        string baseDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "safe"));
        string destPath = Path.Combine(baseDir, "..", "..", "Windows", "System32", "evil.exe");
        Assert.IsFalse(PathSafetyHelper.IsContained(destPath, baseDir));
    }

    [TestMethod]
    public void IsContained_SiblingPrefix_ReturnsFalse()
    {
        // "C:\Temp\Safe" should NOT match "C:\Temp\SafeMalicious"
        string baseDir = Path.Combine(Path.GetTempPath(), "SafeDir");
        string destPath = Path.Combine(Path.GetTempPath(), "SafeDirMalicious", "file.txt");
        Assert.IsFalse(PathSafetyHelper.IsContained(destPath, baseDir));
    }

    // ================================================================
    // ValidateContained
    // ================================================================

    [TestMethod]
    public void ValidateContained_NormalPath_DoesNotThrow()
    {
        string baseDir = Path.GetFullPath(Path.GetTempPath());
        string destPath = Path.Combine(baseDir, "subfolder", "file.txt");
        PathSafetyHelper.ValidateContained(destPath, baseDir);
        // No exception = pass
    }

    [TestMethod]
    public void ValidateContained_Traversal_ThrowsInvalidDataException()
    {
        string baseDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "safe"));
        string destPath = Path.Combine(baseDir, "..", "..", "Windows", "System32", "evil.exe");

        Assert.ThrowsException<InvalidDataException>(() =>
            PathSafetyHelper.ValidateContained(destPath, baseDir));
    }

    [TestMethod]
    public void ValidateContained_ExceptionMessage_DoesNotLeakFullPath()
    {
        string baseDir = Path.Combine(Path.GetTempPath(), "secrets");
        string destPath = Path.Combine(baseDir, "..", "..", "C", "Users", "victim", "Desktop", "malware.exe");

        var ex = Assert.ThrowsException<InvalidDataException>(() =>
            PathSafetyHelper.ValidateContained(destPath, baseDir));

        // Message must NOT contain the full resolved path
        StringAssert.Contains(ex.Message, "路径穿越检测");
        Assert.IsFalse(ex.Message.Contains(":\\"), "should not leak absolute path");
        Assert.IsFalse(ex.Message.Contains("Users"), "should not leak path components");
    }

    // ================================================================
    // Reparse point / Junction detection
    // ================================================================

    [TestMethod]
    public void IsContained_DirectoryJunction_ReturnsFalse()
    {
        // Create a sandbox directory
        string sandbox = Path.Combine(Path.GetTempPath(), $"eitrvo_junction_{Guid.NewGuid():N}");
        string outsideDir = Path.Combine(Path.GetTempPath(), $"eitrvo_outside_{Guid.NewGuid():N}");
        Directory.CreateDirectory(sandbox);
        Directory.CreateDirectory(outsideDir);

        try
        {
            // Create a junction inside sandbox pointing to outsideDir
            string junctionPath = Path.Combine(sandbox, "escape_link");
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c mklink /J \"{junctionPath}\" \"{outsideDir}\"",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(psi)!;
            process.WaitForExit(5000);

            if (process.ExitCode != 0)
            {
                // Junction creation requires admin or Developer Mode — skip gracefully
                Assert.Inconclusive("无法创建目录 Junction（需要管理员权限或开发者模式）。");
                return;
            }

            // Verify the junction exists
            Assert.IsTrue(Directory.Exists(junctionPath), "Junction should exist");

            // The junction itself is OK (inside sandbox), but accessing through it
            // should detect the reparse point
            string escapedPath = Path.Combine(junctionPath, "malicious.dll");
            Assert.IsFalse(PathSafetyHelper.IsContained(escapedPath, sandbox),
                "Path through junction pointing outside sandbox should be rejected");
        }
        finally
        {
            try
            {
                if (Directory.Exists(sandbox))
                {
                    // Remove junction first (otherwise recursive delete may follow it)
                    string junctionPath = Path.Combine(sandbox, "escape_link");
                    if (Directory.Exists(junctionPath))
                    {
                        var attrs = File.GetAttributes(junctionPath);
                        if ((attrs & FileAttributes.ReparsePoint) != 0)
                            Directory.Delete(junctionPath, recursive: false);
                    }
                    Directory.Delete(sandbox, true);
                }
            }
            catch { }
            try { Directory.Delete(outsideDir, true); } catch { }
        }
    }

    [TestMethod]
    public void IsContained_RegularDirectory_ReturnsTrue()
    {
        // Ensure normal directories without reparse points still pass
        string sandbox = Path.Combine(Path.GetTempPath(), $"eitrvo_normal_{Guid.NewGuid():N}");
        string subDir = Path.Combine(sandbox, "subdir");
        Directory.CreateDirectory(subDir);

        try
        {
            string destPath = Path.Combine(subDir, "file.txt");
            Assert.IsTrue(PathSafetyHelper.IsContained(destPath, sandbox),
                "Normal nested directory should pass containment check");
        }
        finally
        {
            try { Directory.Delete(sandbox, true); } catch { }
        }
    }
}
