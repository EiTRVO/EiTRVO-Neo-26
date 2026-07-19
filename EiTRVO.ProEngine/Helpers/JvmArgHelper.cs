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
