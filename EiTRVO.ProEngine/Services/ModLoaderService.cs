using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Helpers;
using EiTRVO.ProEngine.Services.Loaders;
using static EiTRVO.ProEngine.Helpers.Endpoints;

namespace EiTRVO.ProEngine.Services;

public class ModLoaderService : IModLoaderService
{

    // ================================================================
    // MergeVersionJson — 合并加载器 version JSON 与原版 version JSON
    // ================================================================

    /// <summary>
    /// 合并子版本 JSON（如 Forge / Fabric / Quilt / NeoForge）与父版本 JSON（Vanilla）。
    ///
    /// 合并规则：
    ///   - 标量字段（id, mainClass, type）：child 优先
    ///   - assets / assetIndex / downloads：使用 parent（资源属于原版）
    ///   - logging：child ?? parent
    ///   - libraries：parent + child（按 name 去重，child 同名覆盖 parent）
    ///   - arguments.jvm：parent + child（拼接）
    ///   - arguments.game：parent + child（拼接）
    ///   - minecraftArguments：child ?? parent（旧版兼容）
    ///   - inheritsFrom：null（合并后不再需要）
    /// </summary>
    public VersionDetail MergeVersionJson(VersionDetail child, VersionDetail parent)
    {
        // --- Libraries：合并去重，child 同名库覆盖 parent。
        //     Child (Forge) libraries go FIRST so they take classpath priority
        //     over same-library-different-version parent (vanilla) libraries. ---
        var mergedLibraries = new List<Library>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1) Child libraries first — Forge's version takes precedence
        if (child.Libraries != null)
        {
            foreach (var lib in child.Libraries)
            {
                if (!string.IsNullOrEmpty(lib.Name))
                {
                    seenNames.Add(lib.Name);
                    mergedLibraries.Add(lib);
                }
            }
        }

        // 2) Parent libraries — only if not already provided by child
        if (parent.Libraries != null)
        {
            foreach (var lib in parent.Libraries)
            {
                if (!string.IsNullOrEmpty(lib.Name) && !seenNames.Contains(lib.Name))
                {
                    seenNames.Add(lib.Name);
                    mergedLibraries.Add(lib);
                }
            }
        }

        // --- Arguments：拼接 parent + child ---
        var mergedJvm = new List<JsonElement>();
        if (parent.Arguments?.Jvm != null)
            mergedJvm.AddRange(parent.Arguments.Jvm);
        if (child.Arguments?.Jvm != null)
            mergedJvm.AddRange(child.Arguments.Jvm);

        var mergedGame = new List<JsonElement>();
        if (parent.Arguments?.Game != null)
            mergedGame.AddRange(parent.Arguments.Game);
        if (child.Arguments?.Game != null)
            mergedGame.AddRange(child.Arguments.Game);

        return new VersionDetail
        {
            Id = child.Id,
            Type = child.Type ?? parent.Type,
            MainClass = child.MainClass,             // 加载器主类
            InheritsFrom = null,                     // 合并后不再需要
            Assets = parent.Assets,                  // 资源索引使用原版
            AssetIndex = parent.AssetIndex,          // 资源索引使用原版
            Downloads = parent.Downloads,            // Client JAR 是原版的
            Logging = child.Logging ?? parent.Logging,
            Libraries = mergedLibraries,
            Arguments = new Arguments
            {
                Jvm = mergedJvm.Count > 0 ? mergedJvm : null,
                Game = mergedGame.Count > 0 ? mergedGame : null
            },
            MinecraftArguments = child.MinecraftArguments ?? parent.MinecraftArguments
        };
    }

    // ================================================================
    // Fabric
    // ================================================================

    public async Task<List<ModLoaderVersion>> GetFabricLoaderVersionsAsync(HttpClient http, string mcVersion, CancellationToken ct = default)
        => await FabricInstaller.GetVersionsAsync(http, mcVersion, ct);

    public async Task InstallFabricAsync(HttpClient http, string gameDir, string mcVersion,
        string loaderVersion, string instanceName, IProgress<DownloadProgress> progress,
        Action<string, NotificationType, int> showNotification,
        CancellationToken ct = default)
        => await FabricInstaller.InstallAsync(http, gameDir, mcVersion, loaderVersion, instanceName, progress, showNotification, ct);

    // ================================================================
    // Quilt
    // ================================================================

    public async Task<List<ModLoaderVersion>> GetQuiltLoaderVersionsAsync(HttpClient http, string mcVersion, CancellationToken ct = default)
        => await QuiltInstaller.GetVersionsAsync(http, mcVersion, ct);

    public async Task InstallQuiltAsync(HttpClient http, string gameDir, string mcVersion,
        string loaderVersion, string instanceName, IProgress<DownloadProgress> progress,
        Action<string, NotificationType, int> showNotification,
        CancellationToken ct = default)
        => await QuiltInstaller.InstallAsync(http, gameDir, mcVersion, loaderVersion, instanceName, progress, showNotification, ct);

    // ================================================================
    // Forge
    // ================================================================

    public async Task<List<ModLoaderVersion>> GetForgeVersionsAsync(HttpClient http, string mcVersion, CancellationToken ct = default)
        => await Loaders.ForgeInstaller.GetVersionsAsync(http, mcVersion, ct);

    public async Task InstallForgeAsync(HttpClient http, string gameDir, string mcVersion,
        string forgeVersion, string instanceName, string javaPath,
        IProgress<DownloadProgress> progress,
        Action<string, NotificationType, int> showNotification,
        CancellationToken ct = default)
        => await Loaders.ForgeInstaller.InstallAsync(http, gameDir, mcVersion, forgeVersion, instanceName, javaPath, progress, showNotification, ct);

    // ================================================================
    // NeoForge
    // ================================================================

    public async Task<List<ModLoaderVersion>> GetNeoForgeVersionsAsync(HttpClient http, string mcVersion, CancellationToken ct = default)
        => await Loaders.NeoForgeInstaller.GetVersionsAsync(http, mcVersion, ct);

    public async Task InstallNeoForgeAsync(HttpClient http, string gameDir, string mcVersion,
        string neoForgeVersion, string instanceName, string javaPath,
        IProgress<DownloadProgress> progress,
        Action<string, NotificationType, int> showNotification,
        CancellationToken ct = default)
        => await Loaders.NeoForgeInstaller.InstallAsync(http, gameDir, mcVersion, neoForgeVersion, instanceName, javaPath, progress, showNotification, ct);

    // ================================================================
    // OptiFine
    // ================================================================

    public async Task<List<ModLoaderVersion>> GetOptiFineVersionsAsync(HttpClient http, string mcVersion, CancellationToken ct = default)
        => await OptiFineInstaller.GetVersionsAsync(http, mcVersion, ct);

    public async Task InstallOptiFineAsync(HttpClient http, string gameDir, string mcVersion,
        string optiFineVersion, string instanceName, string javaPath,
        IProgress<DownloadProgress> progress,
        Action<string, NotificationType, int> showNotification,
        CancellationToken ct = default)
        => await OptiFineInstaller.InstallAsync(http, gameDir, mcVersion, optiFineVersion, instanceName, javaPath, progress, showNotification, ct);

    // ================================================================
    // Internal helpers
    // ================================================================

    /// <summary>判断 MC 版本是否为 Legacy (≤1.12.2)，决定 Forge 安装方式。</summary>
    internal static bool IsLegacyMcVersion(string mcVersion)
    {
        if (Version.TryParse(mcVersion, out var ver))
        {
            // 1.12.2 and below → legacy Forge (no Processor system)
            return ver.Major == 1 && ver.Minor <= 12;
        }
        // Snapshots / non-standard version strings: treat as modern
        return false;
    }

    /// <summary>根据 Maven path 构造下载 URL（用于没有显式 URL 的 library artifact）。</summary>
    internal static string ResolveMavenUrl(string path, string mavenBase)
    {
        string baseUrl = mavenBase.TrimEnd('/');
        return $"{baseUrl}/{path.TrimStart('/')}";
    }

    /// <summary>
    /// <summary>
    /// 将 Maven 坐标 (group:artifact:version) 转换为 jar 文件路径。
    /// 例如 "net.minecraft:launchwrapper:1.12" → "net/minecraft/launchwrapper/1.12/launchwrapper-1.12.jar"
    /// </summary>
    public static string MavenNameToPath(string mavenName)
    {
        var parts = mavenName.Split(':');
        if (parts.Length < 3)
            throw new ArgumentException($"无效的 Maven 坐标: {mavenName}");

        string group = parts[0];
        string artifact = parts[1];
        string version = parts[2];

        string groupPath = group.Replace('.', '/');
        return $"{groupPath}/{artifact}/{version}/{artifact}-{version}.jar";
    }

    /// <summary>
    /// Extract the Forge universal/library JAR from inside the installer JAR.
    /// Tries two locations:
    ///   1. maven/{path}  — used by Forge 1.12.2+
    ///   2. {fileName}    — root-level universal JAR (Forge ≤1.8.9, e.g. "forge-...-universal.jar")
    /// </summary>
    internal static bool ExtractForgeJar(string installerPath, string mavenPath,
        string? rootFileName, string destPath)
    {
        using var zip = ZipFile.OpenRead(installerPath);

        // Try 1: maven/{path}
        var entry = zip.GetEntry($"maven/{mavenPath}");
        if (entry != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            entry.ExtractToFile(destPath, overwrite: false);
            return true;
        }

        // Try 2: root-level file (install.filePath from install_profile.json)
        if (!string.IsNullOrEmpty(rootFileName))
        {
            entry = zip.GetEntry(rootFileName);
            if (entry != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                entry.ExtractToFile(destPath, overwrite: false);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 解析 library 的下载 URL。
    /// 优先使用 library 上的 legacy URL (Forge ≤1.12.2)，
    /// 否则回退到 libraries.minecraft.net。
    /// </summary>
    internal static string ResolveLibraryUrl(Library lib, string path)
    {
        // Preferred: legacy Forge library has top-level "url" field → Maven repo base
        if (!string.IsNullOrEmpty(lib.Url))
            return ResolveMavenUrl(path, lib.Url);

        // Modern format: downloads.artifact.url (e.g. maven-artifact from maven.minecraftforge.net)
        if (!string.IsNullOrEmpty(lib.Downloads?.Artifact?.Url))
            return lib.Downloads.Artifact.Url;

        // Default: Minecraft libraries
        return $"https://libraries.minecraft.net/{path.TrimStart('/')}";
    }

    /// <summary>解析 Maven metadata XML，提取 &lt;version&gt; 元素列表。</summary>
    internal static List<string> ParseMavenMetadataVersions(string xml)
    {
        var versions = new List<string>();
        using var reader = new System.Xml.XmlTextReader(new System.IO.StringReader(xml))
        {
            DtdProcessing = System.Xml.DtdProcessing.Prohibit,
            Namespaces = false
        };

        bool inVersioning = false;
        bool inVersions = false;

        while (reader.Read())
        {
            if (reader.NodeType == System.Xml.XmlNodeType.Element)
            {
                if (reader.Name == "versioning") inVersioning = true;
                else if (reader.Name == "versions" && inVersioning) inVersions = true;
                else if (reader.Name == "version" && inVersions)
                {
                    reader.Read();
                    if (reader.NodeType == System.Xml.XmlNodeType.Text)
                        versions.Add(reader.Value);
                }
            }
            else if (reader.NodeType == System.Xml.XmlNodeType.EndElement)
            {
                if (reader.Name == "versions") inVersions = false;
                else if (reader.Name == "versioning") inVersioning = false;
            }
        }

        return versions;
    }

    /// <summary>从 NeoForge 完整版本号（如 "21.1.234"）中提取构建号用于排序。</summary>
    internal static int ParseNeoForgeBuildNumber(string version)
    {
        int lastDot = version.LastIndexOf('.');
        if (lastDot >= 0 && int.TryParse(version[(lastDot + 1)..], out int build))
            return build;
        return 0;
    }

    /// <summary>
    /// Download a file with optional per-file byte-level progress reporting.
    /// When <paramref name="progress"/> is non-null, reports <see cref="DownloadProgress"/>
    /// with <see cref="DownloadProgress.CurrentFileName"/> set, along with downloaded bytes,
    /// total size, and current speed, approximately every 64 KB.
    /// The file is written to a .part temp file and atomically renamed on success.
    /// </summary>
    private static async Task DownloadFileCoreAsync(HttpClient http, string url, string path,
        IProgress<DownloadProgress>? progress = null, string? displayName = null,
        CancellationToken ct = default)
    {
        string? dir = Path.GetDirectoryName(path);
        if (dir != null) Directory.CreateDirectory(dir);

        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        long totalBytes = resp.Content.Headers.ContentLength ?? -1;
        string fileName = displayName ?? Path.GetFileName(path);

        // Reject unreasonably large files (malicious or misconfigured)
        const long maxFileSize = 200 * 1024 * 1024; // 200 MB
        if (totalBytes > maxFileSize)
            throw new InvalidOperationException(
                $"文件 {fileName} 大小 ({totalBytes / 1024 / 1024} MB) 超过上限 (200 MB)，已拒绝。");

        string tmp = path + ".part";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long downloadedBytes = 0;
        long lastReportedBytes = 0;
        double lastReportTime = 0;

        try
        {
            await using var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true);
            await using var contentStream = await resp.Content.ReadAsStreamAsync();

            byte[] buffer = new byte[8192];
            int bytesRead;
            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                await fs.WriteAsync(buffer, 0, bytesRead);
                downloadedBytes += bytesRead;

                // Report progress every ~64 KB or when complete
                if (progress != null && (downloadedBytes - lastReportedBytes >= 65536 || downloadedBytes == totalBytes))
                {
                    double now = sw.Elapsed.TotalSeconds;
                    double deltaTime = now - lastReportTime;
                    double speed = deltaTime > 0.05 ? (downloadedBytes - lastReportedBytes) / deltaTime : 0;
                    progress.Report(DownloadProgress.FileProgress(
                        fileName, downloadedBytes, totalBytes, speed));
                    lastReportedBytes = downloadedBytes;
                    lastReportTime = now;
                }
            }
        }
        catch
        {
            try { File.Delete(tmp); } catch { /* best effort */ }
            throw;
        }
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>判断 HTTP 错误是否为可重试的临时错误（5xx / 408 / 429 / 网络异常）。</summary>
    internal static bool IsTransientError(HttpRequestException ex)
        => ex.StatusCode is null
        || (int)ex.StatusCode >= 500
        || ex.StatusCode == System.Net.HttpStatusCode.RequestTimeout
        || ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests;

    /// <summary>
    /// Download a file with built-in retry on transient errors.
    /// Retries up to <paramref name="maxRetries"/> times with progressive backoff (800ms → 1600ms → 3200ms).
    /// 4xx errors (except 408/429) are NOT retried and throw immediately.
    /// </summary>
    internal static async Task DownloadFileAsync(HttpClient http, string url, string path,
        IProgress<DownloadProgress>? progress = null, string? displayName = null,
        int maxRetries = 3, CancellationToken ct = default)
    {
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await DownloadFileCoreAsync(http, url, path, progress, displayName, ct);
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (HttpRequestException ex) when (IsTransientError(ex))
            {
                if (attempt == maxRetries) throw;
                try { await Task.Delay(800 * (1 << attempt), ct); }
                catch (OperationCanceledException) { throw; }
            }
        }
    }

    /// <summary>
    /// Download a file with URL fallback: try <paramref name="primaryUrl"/> first;
    /// on 404, fall back to <c>https://libraries.minecraft.net/{path}</c>.
    /// Used by legacy Forge installation where some libraries are only hosted on
    /// Mojang's CDN and not on Forge's Maven.
    /// </summary>
    internal static async Task DownloadFileWithFallbackAsync(HttpClient http, string primaryUrl,
        string path, IProgress<DownloadProgress>? progress, string? displayName,
        CancellationToken ct = default)
    {
        try
        {
            await DownloadFileAsync(http, primaryUrl, path, progress, displayName, ct: ct);
            return;
        }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Build fallback URL using libraries.minecraft.net
            string fallbackUrl = $"https://libraries.minecraft.net/{path.Replace('\\', '/').TrimStart('/')}";
            // Only try fallback if it differs from the primary
            if (!string.Equals(fallbackUrl, primaryUrl, StringComparison.OrdinalIgnoreCase))
            {
                await DownloadFileAsync(http, fallbackUrl, path, progress, displayName, ct: ct);
                return;
            }
        }
        // If we get here, both URLs failed — rethrow (no retry, just propagate)
        await DownloadFileCoreAsync(http, primaryUrl, path, progress, displayName, ct);
    }

    /// <summary>Recursively copy a directory tree, overwriting existing files.</summary>
    internal static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (string file in Directory.GetFiles(sourceDir))
        {
            string dest = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, dest, overwrite: true);
        }
        foreach (string subDir in Directory.GetDirectories(sourceDir))
        {
            string destSub = Path.Combine(destDir, Path.GetFileName(subDir));
            CopyDirectoryRecursive(subDir, destSub);
        }
    }

    internal static void SaveInstanceMeta(string versionDir, string loaderType, string loaderVersion)
    {
        var meta = new InstanceMeta
        {
            LoaderType = loaderType,
            LoaderVersion = loaderVersion
        };
        string metaPath = Path.Combine(versionDir, "instance.json");
        // Preserve existing fields if instance.json already exists
        if (File.Exists(metaPath))
        {
            try
            {
                var existing = JsonSerializer.Deserialize<InstanceMeta>(File.ReadAllText(metaPath));
                if (existing != null)
                {
                    meta.UseIsolatedDir = existing.UseIsolatedDir;
                    meta.InstanceDir = existing.InstanceDir;
                }
            }
            catch { /* ignore corrupt meta */ }
        }
        File.WriteAllText(metaPath, JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Persist installer stdout/stderr to version directory for post-install diagnostics.</summary>
    internal static void SaveInstallerLog(string versionDir, string stdout, string stderr)
    {
        try
        {
            string logPath = Path.Combine(versionDir, "installer.log");
            var log = new System.Text.StringBuilder();
            log.AppendLine($"===== Installer Log — {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} =====");
            log.AppendLine();
            if (!string.IsNullOrWhiteSpace(stdout))
            {
                log.AppendLine("--- stdout ---");
                log.AppendLine(stdout.Trim());
            }
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                log.AppendLine("--- stderr ---");
                log.AppendLine(stderr.Trim());
            }
            File.WriteAllText(logPath, log.ToString());
        }
        catch { /* best-effort — logging failure should not fail the install */ }
    }

    /// <summary>
    /// Converts Fabric/Quilt Maven-coordinate library entries into standard Mojang
    /// library format with <c>downloads.artifact.path</c> and <c>downloads.artifact.url</c>.
    /// </summary>
    internal static string NormalizeMavenLibraries(string profileJson)
    {
        var root = JsonNode.Parse(profileJson)?.AsObject()
                   ?? throw new InvalidOperationException("无法解析 profile JSON");

        if (!root.TryGetPropertyValue("libraries", out var libsNode) || libsNode is not JsonArray libsArray)
            return profileJson;

        bool modified = false;

        foreach (var libNode in libsArray)
        {
            if (libNode is not JsonObject libObj)
                continue;

            // Already has downloads.artifact.path → skip
            if (libObj.TryGetPropertyValue("downloads", out var dlNode) &&
                dlNode is JsonObject dlObj &&
                dlObj.TryGetPropertyValue("artifact", out var artNode) &&
                artNode is JsonObject artObj &&
                artObj.ContainsKey("path"))
                continue;

            // Parse Maven coordinate: groupId:artifactId:version
            string? name = libObj["name"]?.GetValue<string>();
            if (string.IsNullOrEmpty(name))
                continue;

            string[] parts = name.Split(':');
            if (parts.Length < 3)
                continue;

            string groupId = parts[0];
            string artifactId = parts[1];
            string version = parts[2];

            string mavenPath = $"{groupId.Replace('.', '/')}/{artifactId}/{version}/{artifactId}-{version}.jar";

            // Get base repo URL, then remove from output (consolidated into downloads)
            string baseUrl = libObj["url"]?.GetValue<string>() ?? "";
            libObj.Remove("url");
            if (!baseUrl.EndsWith('/'))
                baseUrl += "/";

            var downloadInfo = new JsonObject
            {
                ["path"] = mavenPath,
                ["url"] = baseUrl + mavenPath
            };
            // Carry over optional fields
            if (libObj.TryGetPropertyValue("size", out var sz)) downloadInfo["size"] = sz?.DeepClone();
            if (libObj.TryGetPropertyValue("sha1", out var s1)) downloadInfo["sha1"] = s1?.DeepClone();

            libObj["downloads"] = new JsonObject { ["artifact"] = downloadInfo };
            modified = true;
        }

        return modified ? root.ToJsonString() : profileJson;
    }

    /// <summary>
    /// Parses installer stderr for failed Maven coordinates (e.g. "group:artifact:version"),
    /// downloads them to the libraries directory using the launcher's own HttpClient,
    /// then returns how many were successfully pre-downloaded.
    /// </summary>
    internal static async Task<int> PreDownloadMavenLibrariesAsync(
        HttpClient http, string gameDir, string stderrText,
        IProgress<DownloadProgress> progress,
        Action<string, NotificationType, int> showNotification,
        CancellationToken ct = default)
    {
        // Maven repositories to try in order
        string[] repos = {
            "https://maven.neoforged.net/releases/",
            "https://maven.fabricmc.net/",
            "https://repo1.maven.org/maven2/",
            "https://libraries.minecraft.net/"
        };

        // Extract "group:artifact:version" patterns from the installer output
        var coords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in stderrText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = line.Trim();
            // Match Maven coordinates: at least 2 colons, not a URL, not indented with dots
            if (trimmed.Contains(':') && !trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                && !trimmed.StartsWith("at ", StringComparison.OrdinalIgnoreCase)
                && !trimmed.StartsWith('[') && !trimmed.Contains(' ') && trimmed.Count(c => c == ':') >= 2)
            {
                string[] parts = trimmed.Split(':');
                if (parts.Length >= 3 && parts[0].Contains('.'))
                    coords.Add(trimmed);
            }
        }

        if (coords.Count == 0) return 0;

        string libDir = Path.Combine(gameDir, "libraries");
        int downloaded = 0;

        foreach (string coord in coords)
        {
            string[] parts = coord.Split(':');
            if (parts.Length < 3) continue;

            string groupId = parts[0];
            string artifactId = parts[1];
            string version = parts[2];
            string mavenPath = $"{groupId.Replace('.', '/')}/{artifactId}/{version}/{artifactId}-{version}.jar";
            string destPath = Path.Combine(libDir, mavenPath);

            if (File.Exists(destPath)) continue; // already present

            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

            bool ok = false;
            foreach (string repo in repos)
            {
                try
                {
                    string url = repo + mavenPath;
                    await DownloadFileAsync(http, url, destPath, progress, Path.GetFileName(destPath), ct: ct);
                    ok = true;
                    downloaded++;
                    break;
                }
                catch (OperationCanceledException) { throw; }
                catch { /* try next repo */ }
            }

            if (!ok)
                showNotification($"预处理库下载失败: {coord}", NotificationType.Warning, 4000);
        }

        if (downloaded > 0)
            showNotification($"预下载了 {downloaded} 个缺失库，正在重试安装器...", NotificationType.Info, 3000);

        return downloaded;
    }
}
