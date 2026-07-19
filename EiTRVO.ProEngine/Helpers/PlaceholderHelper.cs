using System;
using System.Collections.Generic;

namespace EiTRVO.ProEngine.Helpers;

public static class PlaceholderHelper
{
    public static string ReplacePlaceholders(string arg, string playerName, string version,
        string assetsDir, string assetIndex, string versionType, string accessToken, string uuid, string gameDir)
    {
        return ReplacePlaceholders(arg, playerName, version, assetsDir, assetIndex,
            versionType, accessToken, uuid, gameDir, null);
    }

    /// <summary>
    /// 替换启动参数中的占位符。当 instanceGameDir 非空时，
    /// ${game_directory} 解析为实例隔离目录而非全局 .minecraft。
    /// </summary>
    public static string ReplacePlaceholders(string arg, string playerName, string version,
        string assetsDir, string assetIndex, string versionType, string accessToken, string uuid,
        string gameDir, string? instanceGameDir)
    {
        string effectiveGameDir = instanceGameDir ?? gameDir;
        // library_directory is a Maven artifact path, always under the global .minecraft/libraries.
        // It must NOT follow instance isolation — libraries are shared across all instances.
        string libraryDir = (gameDir + "/libraries").Replace('\\', '/');
        // Quote standalone path values that contain spaces so SplitMinecraftArguments
        // doesn't fragment them. Only assets_root and game_directory are standalone args;
        // library_directory is always embedded inside composite paths (e.g. -p, -DlibraryDirectory).
        static string QuoteIfHasSpace(string value) =>
            value.Contains(' ') ? $"\"{value}\"" : value;

        return arg
            .Replace("${auth_player_name}", playerName)
            .Replace("${version_name}", version)
            .Replace("${game_directory}", QuoteIfHasSpace(effectiveGameDir))
            .Replace("${library_directory}", libraryDir)
            .Replace("${classpath_separator}", ";")
            .Replace("${assets_root}", QuoteIfHasSpace(assetsDir))
            .Replace("${assets_index_name}", assetIndex)
            .Replace("${version_type}", versionType)
            .Replace("${auth_uuid}", uuid)
            .Replace("${auth_access_token}", accessToken)
            .Replace("${clientid}", "5a0b94a6-2810-4a43-a722-ba15271955b4")
            .Replace("${auth_xuid}", "0")
            .Replace("${user_type}", "msa")
            .Replace("${auth_session}", "0")
            .Replace("${user_properties}", "{}");
    }

    /// <summary>Splits a legacy Minecraft arguments string (pre-1.13 format) into individual tokens,
    /// respecting quoted strings.</summary>
    public static List<string> SplitMinecraftArguments(string? argsString)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(argsString))
            return result;

        bool inQuotes = false;
        var current = new System.Text.StringBuilder();
        foreach (char c in argsString)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ' ' && !inQuotes)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }
        if (current.Length > 0)
            result.Add(current.ToString());

        return result;
    }

    public static void EnsureParameter(List<string> args, string paramName, string paramValue)
    {
        for (int i = args.Count - 1; i >= 0; i--)
        {
            if (args[i] == paramName)
            {
                args.RemoveAt(i);
                if (i < args.Count && !args[i].StartsWith("-"))
                    args.RemoveAt(i);
            }
        }
        args.Add(paramName);
        args.Add(paramValue);
    }
}
