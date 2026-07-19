using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EiTRVO.ProEngine.Models;
using static EiTRVO.ProEngine.Helpers.Endpoints;

namespace EiTRVO.ProEngine.Services.Loaders;

internal static class FabricInstaller
{
    public static async Task<List<ModLoaderVersion>> GetVersionsAsync(HttpClient http, string mcVersion, CancellationToken ct = default)
    {
        string url = FabricLoaderVersions(mcVersion);
        string json = await http.GetStringAsync(url, ct);
        var entries = JsonSerializer.Deserialize<List<FabricLoaderEntry>>(json)
                      ?? new List<FabricLoaderEntry>();

        static bool IsModernFabric(string version)
        {
            string clean = version;
            int plus = clean.IndexOf('+');
            if (plus >= 0) clean = clean.Substring(0, plus);
            if (Version.TryParse(clean, out var v))
                return v.Major > 0 || v.Minor >= 10;
            return false;
        }

        return entries
            .Where(e => e.Loader?.Version != null && IsModernFabric(e.Loader.Version))
            .Select(e => new ModLoaderVersion
            {
                LoaderType = "Fabric",
                LoaderVersion = e.Loader!.Version!,
                MinecraftVersion = mcVersion,
                IsRecommended = e.Loader.Stable
            })
            .ToList();
    }

    public static async Task InstallAsync(HttpClient http, string gameDir, string mcVersion,
        string loaderVersion, string instanceName, IProgress<DownloadProgress> progress,
        Action<string, NotificationType, int> showNotification,
        CancellationToken ct = default)
    {
        string profileUrl = FabricLoaderProfile(mcVersion, loaderVersion);
        string rawJson = await http.GetStringAsync(profileUrl, ct);

        string profileJson = ModLoaderService.NormalizeMavenLibraries(rawJson);
        var profile = JsonSerializer.Deserialize<VersionDetail>(profileJson)
                      ?? throw new Exception("Fabric profile JSON 解析失败。");

        string versionDir = Path.Combine(gameDir, "versions", instanceName);
        Directory.CreateDirectory(versionDir);

        var libsToDownload = new List<(string url, string path)>();
        string libDir = Path.Combine(gameDir, "libraries");

        if (profile.Libraries != null)
        {
            foreach (var lib in profile.Libraries)
            {
                var artifact = lib.Downloads?.Artifact;
                if (artifact?.Url != null && artifact.Path != null)
                {
                    string dest = Path.Combine(libDir, artifact.Path);
                    if (!File.Exists(dest))
                        libsToDownload.Add((artifact.Url, dest));
                }
            }
        }

        int total = libsToDownload.Count + 1;
        int completed = 0;

        if (libsToDownload.Count > 0)
        {
            using var sem = new SemaphoreSlim(16);
            var tasks = libsToDownload.Select(async item =>
            {
                await sem.WaitAsync(ct);
                try
                {
                    await ModLoaderService.DownloadFileAsync(http, item.url, item.path, progress, Path.GetFileName(item.path), ct: ct);
                    int c = Interlocked.Increment(ref completed);
                    progress.Report(DownloadProgress.Overall(c, total));
                }
                finally { sem.Release(); }
            });
            await Task.WhenAll(tasks);
        }

        string versionJsonPath = Path.Combine(versionDir, "version.json");
        await File.WriteAllTextAsync(versionJsonPath, profileJson);

        completed++;
        progress.Report(DownloadProgress.Overall(completed, total));

        string vanillaJar = Path.Combine(gameDir, "versions", mcVersion, $"{mcVersion}.jar");
        string targetJar = Path.Combine(versionDir, $"{profile.Id}.jar");
        if (File.Exists(vanillaJar) && !File.Exists(targetJar))
            File.Copy(vanillaJar, targetJar);

        ModLoaderService.SaveInstanceMeta(versionDir, "Fabric", loaderVersion);
    }
}
