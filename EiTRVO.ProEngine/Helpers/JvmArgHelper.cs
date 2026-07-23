using System;
using System.Collections.Generic;
using System.Text.Json;
using EiTRVO.ProEngine.Models;

namespace EiTRVO.ProEngine.Helpers;

public static class JvmArgHelper
{
    public static bool IsJvmArgCompatible(string arg, int targetJavaVersion)
    {
        if (targetJavaVersion == 8)
        {
            string[] blocked =
            {
                "--add-opens", "--add-exports", "--add-modules",
                "--add-reads", "--patch-module", "--illegal-access"
            };
            foreach (var p in blocked)
                if (arg.StartsWith(p)) return false;
        }
        return true;
    }

    /// <summary>需要从 version.json JVM 参数中阻止的危险标志（安全策略）。</summary>
    private static readonly string[] DangerousJvmArgPrefixes =
    {
        "-javaagent:",          // JVM 代理 JAR 注入
        "-agentlib:",           // 本机代理库注入
        "-agentpath:",          // 本机代理路径注入
        "-XX:OnOutOfMemoryError",  // OOM 时执行任意命令
        "-XX:OnError",             // 致命错误时执行任意命令
    };

    /// <summary>检查 JVM 参数是否被安全策略阻止（防止版本 JSON 中的恶意代理注入）。</summary>
    public static bool IsJvmArgSafe(string arg)
    {
        foreach (var prefix in DangerousJvmArgPrefixes)
            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;
        return true;
    }

    /// <summary>已知安全的 mainClass 命名空间前缀（Minecraft + 全部支持的 Mod 加载器）。</summary>
    private static readonly string[] SafeMainClassPrefixes =
    {
        "net.minecraft.",
        "cpw.mods.",
        "net.minecraftforge.",
        "net.fabricmc.",
        "net.neoforged.",
        "org.quiltmc.",
    };

    /// <summary>检查 mainClass 是否属于已知安全的命名空间。</summary>
    public static bool IsMainClassSafe(string? mainClass)
    {
        if (string.IsNullOrWhiteSpace(mainClass))
            return false;

        foreach (var prefix in SafeMainClassPrefixes)
            if (mainClass.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        return false;
    }

    /// <summary>需要被硬封禁的 mainClass 命名空间前缀（JRE 内部危险类）。</summary>
    private static readonly string[] BlockedMainClassPrefixes =
    {
        "java.lang.",
        "javax.script.",
        "java.lang.reflect.",
        "jdk.jshell.",
        "javax.tools.",
        "com.sun.",
        "sun.",
    };

    /// <summary>检查 mainClass 是否为被显式封禁的 JRE 内部危险类。命中则硬阻断，不给用户选择。</summary>
    public static bool IsMainClassBlocked(string? mainClass)
    {
        if (string.IsNullOrWhiteSpace(mainClass))
            return false;

        foreach (var prefix in BlockedMainClassPrefixes)
            if (mainClass.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        return false;
    }

    public static string StripEmbeddedQuotes(string arg)
    {
        int eq = arg.IndexOf('=');
        if (eq < 0) return arg;
        string left = arg[..(eq + 1)];
        string right = arg[(eq + 1)..];
        if (right.Length >= 2 && right.StartsWith('"') && right.EndsWith('"'))
            right = right[1..^1];
        return left + right;
    }

    public static bool IsRuleAllowed(List<Rule>? rules)
    {
        if (rules == null || rules.Count == 0) return true;
        bool allowed = false;
        foreach (var rule in rules)
        {
            bool applies = true;
            if (rule.Os != null)
            {
                string osName = rule.Os.Name ?? "";
                applies = osName switch
                {
                    "windows" => OperatingSystem.IsWindows(),
                    "osx" => OperatingSystem.IsMacOS(),
                    "linux" => OperatingSystem.IsLinux(),
                    _ => true
                };
            }
            if (applies) allowed = rule.Action == "allow";
        }
        return allowed;
    }

    public static bool PassesRules(JsonElement elem)
    {
        if (!elem.TryGetProperty("rules", out var rulesElement)) return true;
        var rules = JsonSerializer.Deserialize<List<Rule>>(rulesElement.GetRawText());
        return IsRuleAllowed(rules);
    }
}
