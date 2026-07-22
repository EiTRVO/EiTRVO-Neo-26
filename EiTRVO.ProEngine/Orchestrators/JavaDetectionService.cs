using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EiTRVO.ProEngine.Models;

namespace EiTRVO.ProEngine.Orchestrators;

/// <summary>
/// Java 运行时自动检测服务 — 扫描 PATH 和常见安装目录。
/// 纯逻辑，无 WPF 依赖，WinUI 3 可直接复用。
/// </summary>
public class JavaDetectionService
{
    public async Task<List<JavaInfo>> DetectAsync()
    {
        var javas = new List<JavaInfo>();
        var exePaths = FindJavaExecutables();
        var tasks = exePaths.Select(async exe =>
        {
            var info = await GetJavaVersionInfoAsync(exe);
            return info != null
                ? new JavaInfo { Path = exe, Version = info.Value.full, ShortVersion = info.Value.shortVer, MajorVersion = info.Value.major }
                : null;
        });
        var results = await Task.WhenAll(tasks);
        foreach (var java in results) { if (java != null) javas.Add(java); }
        return javas;
    }

    private static List<string> FindJavaExecutables()
    {
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool isWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;
        // On Windows, prefer javaw.exe (GUI subsystem → proper Minecraft window icon).
        // Also detect java.exe as a fallback. If both exist in the same dir, only javaw.exe is kept.
        string[] exeNames = isWindows ? new[] { "javaw.exe", "java.exe" } : new[] { "java" };
        char pathSep = isWindows ? ';' : ':';
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathEnv))
            foreach (string dir in pathEnv.Split(pathSep, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (string exeName in exeNames)
                {
                    string fullPath = Path.Combine(dir.Trim(), exeName);
                    if (File.Exists(fullPath)) results.Add(fullPath);
                }
            }
        if (isWindows)
        {
            string[] baseDirs = { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) };
            string[] vendors = { "Java", "Eclipse Adoptium", "Eclipse Temurin", "Microsoft", "BellSoft", "Zulu" };
            foreach (string baseDir in baseDirs)
            {
                if (string.IsNullOrEmpty(baseDir)) continue;
                foreach (string vendor in vendors)
                {
                    string searchRoot = Path.Combine(baseDir, vendor);
                    if (Directory.Exists(searchRoot))
                        try { FindJavaInDir(searchRoot, results, 2); } catch { }
                }
            }
        }
        else
        {
            string[] possiblePaths = { "/usr/bin/java", "/usr/local/bin/java", "/opt/homebrew/bin/java", "/Library/Java/JavaVirtualMachines" };
            foreach (string path in possiblePaths)
            {
                if (File.Exists(path)) results.Add(path);
                else if (Directory.Exists(path))
                    try { foreach (string file in Directory.GetFiles(path, "java", SearchOption.AllDirectories)) results.Add(file); } catch { }
            }
        }

        // Deduplicate: if both javaw.exe and java.exe exist in the same dir, prefer javaw.exe
        if (isWindows)
        {
            var deduped = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // dir → full path
            foreach (string path in results)
            {
                string? dir = Path.GetDirectoryName(path);
                if (dir == null) continue;
                string name = Path.GetFileName(path).ToLowerInvariant();
                if (!deduped.ContainsKey(dir))
                    deduped[dir] = path;
                else if (name == "javaw.exe")
                    deduped[dir] = path; // javaw.exe wins over java.exe in same directory
            }
            return deduped.Values.ToList();
        }

        return results.ToList();
    }

    private static void FindJavaInDir(string dir, HashSet<string> results, int maxDepth)
    {
        if (maxDepth < 0) return;
        try
        {
            foreach (string file in Directory.GetFiles(dir, "javaw.exe"))
                results.Add(file);
            foreach (string file in Directory.GetFiles(dir, "java.exe"))
                results.Add(file);
            foreach (string subDir in Directory.GetDirectories(dir))
                FindJavaInDir(subDir, results, maxDepth - 1);
        }
        catch { }
    }

    internal static async Task<(string full, string shortVer, int major)?> GetJavaVersionInfoAsync(string javaPath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = javaPath,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-version");
            using var process = Process.Start(psi);
            if (process == null) return null;
            string output = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            string shortVer = "";
            int major = 0;
            foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Contains("version"))
                {
                    int firstQuote = line.IndexOf('"');
                    int lastQuote = line.IndexOf('"', firstQuote + 1);
                    if (firstQuote >= 0 && lastQuote > firstQuote) shortVer = line[(firstQuote + 1)..lastQuote].Trim();
                    else foreach (string part in line.Split(' ')) if (part.Contains('.') && char.IsDigit(part[0])) { shortVer = part.Trim('"'); break; }
                    break;
                }
            }
            if (!string.IsNullOrEmpty(shortVer))
            {
                string[] parts = shortVer.Split('.');
                if (parts.Length >= 2)
                {
                    if (parts[0] == "1" && int.TryParse(parts[1], out int parsed)) major = parsed;
                    else if (int.TryParse(parts[0], out parsed)) major = parsed;
                }
            }
            return (output.Trim().Replace('\n', ' ').Replace('\r', ' '), shortVer, major);
        }
        catch { return null; }
    }
}
