using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EiTRVO.ProEngine.Helpers;
using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Services;

namespace EiTRVO.ProEngine.Orchestrators;

public class LaunchResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? DiagnosticLogPath { get; set; }
    public int? ExitCode { get; set; }
}

/// <summary>
/// 游戏启动编排器 — JVM 参数构建 + 进程生命周期管理。
/// 所有启动逻辑集中在此，无 WPF 依赖，WinUI 3 可直接复用。
/// </summary>
public class LaunchOrchestrator
{
    private readonly HttpClient _httpClient;
    private readonly IAuthService _authService;
    private readonly IModLoaderService _modLoaderService;
    private readonly INotificationService _notificationService;
    private readonly IGameFolderService _gameFolder;
    private readonly SaveLockService _saveLockService;
    private readonly IGameProcessSecurityService? _gameSecurity;
    private readonly IModrinthService _modrinth;

    private Process? _gameProcess;
    private CancellationTokenSource? _gameCts;

    // 启动上下文（失败时写诊断日志用）
    private string? _lastLaunchInstanceName;
    private string? _lastLaunchVersionId;
    private string? _lastLaunchLoaderType;
    private string? _lastLaunchLoaderVersion;
    private string? _lastLaunchJavaPath;
    private string? _lastLaunchGameDir;
    private int _lastLaunchMemory;
    private List<string> _lastLaunchArgs = new();

    // 存档锁：活跃解密密钥 (saveName → (K, lockMode))
    private readonly Dictionary<string, (byte[] Key, SaveLockMode Mode)> _activeSaveKeys = new();

    /// <summary>UI 回调 — 显示解锁对话框。由 MainWindow 注入。</summary>
    public Func<string[], CancellationToken, Task<UnlockResult>>? UnlockHandler { get; set; }

    /// <summary>UI 回调 — 读取 Firewall 开关状态。由 MainWindow 注入。</summary>
    public Func<bool>? FirewallEnabledProvider { get; set; }

    /// <summary>UI 回调 — Mods 完整性校验警告。参数为未在 Modrinth 收录的文件名列表，返回 true=用户确认继续。</summary>
    public Func<List<string>, Task<bool>>? ModsWarningHandler { get; set; }

    /// <summary>UI 回调 — 显示/隐藏/更新存档处理进度。由 MainWindow 注入。</summary>
    public Action? ShowSaveLockProgress { get; set; }
    public Action? HideSaveLockProgress { get; set; }
    public IProgress<(int done, int total)>? SaveLockProgress { get; set; }

    public bool IsGameRunning => _gameProcess != null && !_gameProcess.HasExited;

    public LaunchOrchestrator(
        HttpClient httpClient,
        IAuthService authService,
        IModLoaderService modLoaderService,
        INotificationService notificationService,
        IGameFolderService gameFolder,
        SaveLockService saveLockService,
        IModrinthService modrinth,
        IGameProcessSecurityService? gameSecurity = null)
    {
        _httpClient = httpClient;
        _authService = authService;
        _modLoaderService = modLoaderService;
        _notificationService = notificationService;
        _gameFolder = gameFolder;
        _saveLockService = saveLockService;
        _modrinth = modrinth;
        _gameSecurity = gameSecurity;
    }

    /// <summary>
    /// 启动游戏（离线模式）。
    /// </summary>
    public async Task<LaunchResult> LaunchOfflineAsync(
        GameInstance instance, string playerName, int memory,
        string? width, string? height, JavaInfo javaInfo,
        CancellationToken ct = default)
    {
        return await LaunchInternalAsync(instance, playerName, memory, width, height,
            javaInfo, account: null, yggdrasilServerUrl: null, ct);
    }

    /// <summary>
    /// 启动游戏（Microsoft 正版账号）。
    /// </summary>
    public async Task<LaunchResult> LaunchWithMicrosoftAsync(
        GameInstance instance, Account account, int memory,
        string? width, string? height, JavaInfo javaInfo,
        CancellationToken ct = default)
    {
        return await LaunchInternalAsync(instance, account.Username, memory, width, height,
            javaInfo, account, yggdrasilServerUrl: null, ct);
    }

    /// <summary>
    /// 启动游戏（Yggdrasil 第三方验证）。
    /// </summary>
    public async Task<LaunchResult> LaunchWithYggdrasilAsync(
        GameInstance instance, Account account, int memory,
        string? width, string? height, JavaInfo javaInfo,
        CancellationToken ct = default)
    {
        return await LaunchInternalAsync(instance, account.Username, memory, width, height,
            javaInfo, account, account.YggdrasilServerUrl, ct);
    }

    public void KillGame()
    {
        try { _gameProcess?.Kill(); } catch { }
        _gameCts?.Cancel();
    }

    public void DisposeGameResources()
    {
        _gameSecurity?.StopMonitoring();
        _gameCts?.Cancel();
        _gameCts?.Dispose();
        _gameCts = null;
        try { _gameProcess?.Kill(); } catch { }
        _gameProcess?.Dispose();
        _gameProcess = null;
    }

    // ==================== Internal Launch Logic ====================

    private async Task<LaunchResult> LaunchInternalAsync(
        GameInstance instance, string playerName, int memory,
        string? width, string? height, JavaInfo javaInfo,
        Account? account, string? yggdrasilServerUrl,
        CancellationToken ct)
    {
        try
        {
            string versionDir = Path.Combine(_gameFolder.GameDir, "versions", instance.Name);
            string jsonFile = Path.Combine(versionDir, $"{instance.VersionId}.json");
            if (!File.Exists(jsonFile))
                jsonFile = Path.Combine(versionDir, "version.json");
            if (!File.Exists(jsonFile))
                return new LaunchResult { Success = false, ErrorMessage = $"找不到版本 JSON：{jsonFile}" };

            var detail = JsonSerializer.Deserialize<VersionDetail>(File.ReadAllText(jsonFile))
                         ?? throw new Exception($"版本 JSON 解析失败：{instance.VersionId}");

            // Save inheritsFrom before merge — MergeVersionJson clears it,
            // but we still need it for native library resolution.
            string? inheritsFrom = detail.InheritsFrom;
            detail = ResolveInheritsFrom(detail);

            string versionType = detail.Type ?? "release";
            string accessToken = "";
            string uuid = UuidHelper.OfflineUuid(playerName);
            string userType = "mojang";
            string? instanceGameDir = instance.UseIsolatedDir ? instance.InstanceDir : null;

            if (account != null)
            {
                if (account.Type == AccountType.Microsoft)
                {
                    var (mcToken, name, playerUuid, updatedAccount) =
                        await _authService.RefreshMicrosoftAccessAsync(_httpClient, account, () => { });
                    accessToken = mcToken;
                    playerName = name;
                    uuid = playerUuid;
                    userType = "msa";
                }
                else if (account.Type == AccountType.Yggdrasil && !string.IsNullOrEmpty(yggdrasilServerUrl))
                {
                    var updatedAccount = await _authService.RefreshYggdrasilAsync(_httpClient, account);
                    accessToken = updatedAccount.YggdrasilAccessToken ?? "";
                    uuid = updatedAccount.UUID ?? uuid;
                    playerName = updatedAccount.Username ?? playerName;
                    userType = "mojang";

                    await _authService.DownloadAuthlibInjectorAsync(_httpClient, _gameFolder.GameDir);
                }
            }

            // Legacy resources (≤1.5.2) — only needed for pre-asset-index versions.
            // Respect instance isolation: extract into the effective game directory.
            if (PlatformHelper.IsLegacyVersion(instance.VersionId))
            {
                string effectiveDir = instanceGameDir ?? _gameFolder.GameDir;
                PlatformHelper.EnsureLegacyResources(versionDir, instance.VersionId, effectiveDir);
            }

            // === 存档锁：检测并解锁加密存档 ===
            // 先清理上次启动可能残留的密钥（异常退出场景）
            if (_activeSaveKeys.Count > 0)
            {
                foreach (var (_, (key, _)) in _activeSaveKeys)
                    System.Security.Cryptography.CryptographicOperations.ZeroMemory(key);
                _activeSaveKeys.Clear();
            }

            string savesPath = Path.Combine(
                instanceGameDir ?? _gameFolder.GameDir, "saves");

            if (Directory.Exists(savesPath))
            {
                var encryptedSaves = Directory.GetFiles(savesPath, "*.savenc");
                if (encryptedSaves.Length > 0 && UnlockHandler != null)
                {
                    var unlockResult = await UnlockHandler(encryptedSaves, ct);
                    if (unlockResult.Success)
                    {
                        ShowSaveLockProgress?.Invoke();
                        try
                        {
                            foreach (var (saveName, (key, mode)) in unlockResult.DecryptedSaves)
                            {
                                string savencPath = Path.Combine(savesPath, $"{saveName}.savenc");
                                string outputFolder = Path.Combine(savesPath, saveName);
                                if (File.Exists(savencPath))
                                {
                                    await _saveLockService.UnlockSaveWithKeyAsync(
                                        savencPath, outputFolder, key,
                                        deleteSavencAfter: false,
                                        progress: SaveLockProgress);
                                    _activeSaveKeys[saveName] = (key, mode);
                                }
                            }
                        }
                        finally
                        {
                            HideSaveLockProgress?.Invoke();
                        }
                    }
                    // 失败/取消 → 继续启动，.savenc 保持不变（存档不可见）
                }
            }

            // === Mods 完整性校验：通过 Modrinth API 验证 mods/*.jar 是否收录 ===
            var modsWarning = await VerifyModsAsync(instance, instanceGameDir);
            if (modsWarning != null)
            {
                // 清理已解密的存档：删除明文文件夹，清零密钥
                foreach (var (saveName, (key, _)) in _activeSaveKeys)
                {
                    CryptographicOperations.ZeroMemory(key);
                    string outputFolder = Path.Combine(savesPath, saveName);
                    try { if (Directory.Exists(outputFolder)) Directory.Delete(outputFolder, true); } catch { }
                }
                _activeSaveKeys.Clear();
                return modsWarning;
            }

            // Record launch context for diagnostics
            _lastLaunchInstanceName = instance.Name;
            _lastLaunchVersionId = instance.VersionId;
            _lastLaunchLoaderType = instance.LoaderType;
            _lastLaunchLoaderVersion = instance.LoaderVersion;
            _lastLaunchJavaPath = javaInfo.Path;
            _lastLaunchGameDir = instanceGameDir ?? _gameFolder.GameDir;
            _lastLaunchMemory = memory;

            string nativeVersion = instance.VersionId;
            // For mod loaders with inheritsFrom, use parent vanilla version for natives
            if (!string.IsNullOrEmpty(inheritsFrom))
                nativeVersion = inheritsFrom;
            else if (instance.LoaderType is "Forge" or "NeoForge" or "Fabric" or "Quilt"
                      && !string.IsNullOrEmpty(detail.InheritsFrom))
                nativeVersion = detail.InheritsFrom;

            var args = BuildLaunchArgs(detail, instance.VersionId, versionDir,
                playerName, versionType, accessToken, uuid, memory,
                javaInfo.MajorVersion, width, height, userType,
                instanceGameDir, nativeVersion);

            _lastLaunchArgs = args;

            // Inject authlib-injector agent for Yggdrasil
            if (account?.Type == AccountType.Yggdrasil && !string.IsNullOrEmpty(yggdrasilServerUrl))
            {
                string agentPath = Path.Combine(_gameFolder.GameDir, "authlib-injector", "authlib-injector.jar");
                if (File.Exists(agentPath))
                {
                    string mainClass = detail.MainClass ?? "net.minecraft.client.main.Main";
                    args.Insert(0, $"-javaagent:{agentPath}={yggdrasilServerUrl}");
                    args.Add(mainClass);
                }
            }

            try
            {
                await RunGameProcessAsync(javaInfo, args,
                    instanceGameDir ?? _gameFolder.GameDir);
            }
            finally
            {
                // === 存档锁：游戏退出后处理（无论正常/异常退出均执行） ===
                if (_activeSaveKeys.Count > 0)
                {
                    ShowSaveLockProgress?.Invoke();

                    string reSavesPath = Path.Combine(
                        instanceGameDir ?? _gameFolder.GameDir, "saves");

                    foreach (var (saveName, (key, mode)) in _activeSaveKeys)
                    {
                        if (mode == SaveLockMode.Permanent)
                        {
                            try
                            {
                                string saveFolder = Path.Combine(reSavesPath, saveName);
                                string savencOutput = saveFolder + ".savenc";
                                if (Directory.Exists(saveFolder))
                                {
                                    await _saveLockService.ReEncryptSaveAsync(
                                        saveFolder, savencOutput, savencOutput, key, mode,
                                        progress: SaveLockProgress);
                                }
                            }
                            catch { /* 重加密失败不应阻止启动器关闭 */ }
                        }
                        else // OneTime: 删除 .savenc（解锁时已保留，现在存档已明文）
                        {
                            try
                            {
                                string savencPath = Path.Combine(reSavesPath, $"{saveName}.savenc");
                                if (File.Exists(savencPath))
                                    File.Delete(savencPath);
                            }
                            catch { /* 删除失败不影响 */ }
                        }
                    }

                    // 擦除所有密钥
                    foreach (var (_, (key, _)) in _activeSaveKeys)
                        System.Security.Cryptography.CryptographicOperations.ZeroMemory(key);
                    _activeSaveKeys.Clear();

                    HideSaveLockProgress?.Invoke();
                }
            }

            return new LaunchResult { Success = true };
        }
        catch (Exception ex)
        {
            var briefReason = ParseExitReason(ex);
            var diagPath = WriteDiagnosticLog("启动失败", ex, briefReason);
            return new LaunchResult
            {
                Success = false,
                ErrorMessage = briefReason,
                DiagnosticLogPath = diagPath
            };
        }
    }

    // ==================== Version Resolution ====================

    private VersionDetail ResolveInheritsFrom(VersionDetail detail)
    {
        if (string.IsNullOrEmpty(detail.InheritsFrom))
            return detail;

        string parentId = detail.InheritsFrom;
        string parentDir = Path.Combine(_gameFolder.VersionsDir, parentId);
        string parentJsonPath = Path.Combine(parentDir, $"{parentId}.json");
        if (!File.Exists(parentJsonPath))
            parentJsonPath = Path.Combine(parentDir, "version.json");

        if (!File.Exists(parentJsonPath))
            throw new Exception($"无法找到父版本 JSON：{parentId}（{parentJsonPath} 不存在）。请先下载原版 {parentId}。");

        var parentDetail = JsonSerializer.Deserialize<VersionDetail>(File.ReadAllText(parentJsonPath))
                           ?? throw new Exception($"父版本 JSON 解析失败：{parentId}");

        _notificationService.AppendLog($"正在合并 inheritsFrom: {detail.Id} → {parentId}", NotificationType.Info);
        return _modLoaderService.MergeVersionJson(detail, parentDetail);
    }

    // ==================== JVM Argument Builder ====================

    internal List<string> BuildLaunchArgs(VersionDetail detail, string version, string versionDir,
        string playerName, string versionType, string accessToken, string uuid,
        int memory, int targetJava, string? width, string? height, string userType,
        string? instanceGameDir = null, string? nativeVersion = null)
    {
        var args = new List<string>();
        string nativeVer = nativeVersion ?? version;
        string nativesDir = Path.Combine(_gameFolder.GameDir, "natives", nativeVer);
        string nativePath = nativesDir.Replace('\\', '/');

        args.Add($"-Xmx{memory}M");
        args.Add($"-Djava.library.path={nativePath}");
        args.Add("-Dminecraft.launcher.brand=eitrvo-neo");
        args.Add("-Dminecraft.launcher.version=26");

        if (targetJava != 8)
        {
            if (detail.Logging?.Client?.File?.Path != null)
            {
                string logPath = Path.Combine(_gameFolder.GameDir, detail.Logging.Client.File.Path);
                if (File.Exists(logPath) && detail.Logging.Client.Argument != null)
                {
                    string logArg = detail.Logging.Client.Argument.Replace("${path}", logPath);
                    args.Add(JvmArgHelper.StripEmbeddedQuotes(logArg));
                }
            }
        }

        bool skipNext = false;
        if (detail.Arguments?.Jvm != null)
        {
            foreach (var elem in detail.Arguments.Jvm)
            {
                try
                {
                    if (elem.ValueKind == JsonValueKind.String)
                    {
                        if (skipNext) { skipNext = false; continue; }
                        string jvmArg = JvmArgHelper.StripEmbeddedQuotes((elem.GetString() ?? ""));
                        if (string.IsNullOrEmpty(jvmArg)) continue;
                        if (jvmArg == "-cp" || jvmArg == "-classpath") { skipNext = true; continue; }
                        jvmArg = PlaceholderHelper.ReplacePlaceholders(jvmArg, playerName, version, "", "", versionType, accessToken, uuid, _gameFolder.GameDir, instanceGameDir);
                        if (JvmArgHelper.IsJvmArgCompatible(jvmArg, targetJava) && !jvmArg.Contains("$(") && !jvmArg.Contains("${"))
                            args.Add(jvmArg);
                    }
                    else if (elem.ValueKind == JsonValueKind.Object && elem.TryGetProperty("value", out var value))
                    {
                        if (!JvmArgHelper.PassesRules(elem)) continue;
                        if (value.ValueKind == JsonValueKind.String)
                        {
                            if (skipNext) { skipNext = false; continue; }
                            string jvmArg = JvmArgHelper.StripEmbeddedQuotes((value.GetString() ?? ""));
                            if (jvmArg == "-cp" || jvmArg == "-classpath") { skipNext = true; continue; }
                            jvmArg = PlaceholderHelper.ReplacePlaceholders(jvmArg, playerName, version, "", "", versionType, accessToken, uuid, _gameFolder.GameDir, instanceGameDir);
                            if (JvmArgHelper.IsJvmArgCompatible(jvmArg, targetJava) && !jvmArg.Contains("$(") && !jvmArg.Contains("${"))
                                args.Add(jvmArg);
                        }
                        else if (value.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var v in value.EnumerateArray())
                            {
                                if (skipNext) { skipNext = false; continue; }
                                string jvmArg = JvmArgHelper.StripEmbeddedQuotes(v.GetString()!);
                                if (jvmArg == "-cp" || jvmArg == "-classpath") { skipNext = true; continue; }
                                jvmArg = PlaceholderHelper.ReplacePlaceholders(jvmArg, playerName, version, "", "", versionType, accessToken, uuid, _gameFolder.GameDir, instanceGameDir);
                                if (JvmArgHelper.IsJvmArgCompatible(jvmArg, targetJava) && !jvmArg.Contains("$(") && !jvmArg.Contains("${"))
                                    args.Add(jvmArg);
                            }
                        }
                    }
                }
                catch (Exception ex) { _notificationService.AppendLog($"JVM 参数解析警告: {ex.Message}", NotificationType.Warning); }
            }
        }

        if (targetJava == 8)
        {
            args.Add("-Xss1M");
            args.Add("-XX:HeapDumpPath=MojangTricksIntelDriversForPerformance_javaw.exe_minecraft.exe.heapdump");
        }

        // NeoForge / modern Forge: ensure --add-opens for unnamed module access.
        // The installer's version JSON may only open packages to named modules,
        // but the bootstrapper runs from the classpath (unnamed module).
        if (detail.MainClass is "cpw.mods.bootstraplauncher.BootstrapLauncher"
            or "net.minecraftforge.bootstrap.ForgeBootstrap")
        {
            // Remove existing ALL-UNNAMED opens/exports (stored as separate key/value args)
            for (int i = args.Count - 2; i >= 0; i--)
            {
                if ((args[i] == "--add-opens" || args[i] == "--add-exports")
                    && args[i + 1].EndsWith("=ALL-UNNAMED"))
                {
                    args.RemoveAt(i + 1);
                    args.RemoveAt(i);
                }
            }
            args.Add("--add-opens");
            args.Add("java.base/java.lang.invoke=ALL-UNNAMED");
            args.Add("--add-opens");
            args.Add("java.base/java.util.jar=ALL-UNNAMED");
            args.Add("--add-exports");
            args.Add("java.base/sun.security.util=ALL-UNNAMED");
        }

        // Collect module-path JAR artifact names (version-agnostic) so we can
        // exclude matching classpath entries. Otherwise the same module loaded
        // via both -p and -cp (even at different versions) causes conflicts.
        static string MavenArtifactName(string jarFileName)
        {
            string name = Path.GetFileNameWithoutExtension(jarFileName);
            // Maven version always starts with a digit — find the first "-" followed by one.
            for (int j = 0; j < name.Length - 1; j++)
            {
                if (name[j] == '-' && char.IsDigit(name[j + 1]))
                    return name[..j];
            }
            return name;
        }

        var modulePathArtifacts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Count; i++)
        {
            if (args[i] == "-p" && i + 1 < args.Count)
            {
                foreach (string segment in args[i + 1].Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    string art = MavenArtifactName(segment);
                    if (art.Length > 0) modulePathArtifacts.Add(art);
                }
            }
        }

        // Helper: compare two Maven version strings, preferring the higher one.
        // Tries System.Version first; falls back to ordinal string comparison.
        static bool IsHigherVersion(string? a, string? b)
        {
            if (a == null) return false;
            if (b == null) return true;
            if (Version.TryParse(a, out var va) && Version.TryParse(b, out var vb))
                return va > vb;
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase) > 0;
        }

        // Classpath — deduplicated by artifact identity, keeping highest version.
        // Natives JARs (with classifier e.g. lwjgl-natives-windows) get distinct keys
        // so they are not incorrectly deduped against the main JAR.
        string libDir = Path.Combine(_gameFolder.GameDir, "libraries");
        var cpMap = new Dictionary<string, (string Path, string? Version)>(StringComparer.OrdinalIgnoreCase);
        if (detail.Libraries != null)
        {
            foreach (var lib in detail.Libraries)
            {
                if (!JvmArgHelper.IsRuleAllowed(lib.Rules)) continue;
                string? artifactPath = lib.Downloads?.Artifact?.Path;
                if (string.IsNullOrEmpty(artifactPath) && !string.IsNullOrEmpty(lib.Name))
                {
                    try { artifactPath = ModLoaderService.MavenNameToPath(lib.Name); }
                    catch { continue; }
                }
                if (string.IsNullOrEmpty(artifactPath)) continue;
                string libPath = Path.Combine(libDir, artifactPath);
                if (!File.Exists(libPath)) continue;
                // Skip if this artifact is already on the module path
                if (modulePathArtifacts.Contains(MavenArtifactName(libPath))) continue;

                // Extract version from the parent directory name
                string? ver = Path.GetFileName(Path.GetDirectoryName(libPath));

                // Build dedup key: strip only the version segment, preserve classifier.
                // "asm-9.10.1.jar"       → "asm.jar"
                // "lwjgl-3.3.3-natives-windows.jar" → "lwjgl-natives-windows.jar"
                string fileName = Path.GetFileName(libPath);
                string dedupKey = fileName;
                if (ver != null)
                {
                    string verSuffix = "-" + ver;
                    int idx = fileName.IndexOf(verSuffix, StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                        dedupKey = fileName[..idx] + fileName[(idx + verSuffix.Length)..];
                }

                if (cpMap.TryGetValue(dedupKey, out var existing))
                {
                    if (IsHigherVersion(ver, existing.Version))
                        cpMap[dedupKey] = (libPath, ver);
                }
                else
                {
                    cpMap[dedupKey] = (libPath, ver);
                }
            }
        }
        var classpathParts = cpMap.Values.Select(v => v.Path).ToList();
        string versionJar = Path.Combine(versionDir, $"{version}.jar");
        if (!File.Exists(versionJar))
        {
            // Fallback: vanilla/legacy instances where the jar lives in the parent version dir
            // e.g., instance "1.12.2 Vanilla" inherits 1.12.2.jar from "versions/1.12.2/"
            string? parentDir = Path.GetDirectoryName(versionDir);
            if (parentDir != null)
            {
                string fallbackJar = Path.Combine(parentDir, version, $"{version}.jar");
                if (File.Exists(fallbackJar)) versionJar = fallbackJar;
            }
        }
        if (File.Exists(versionJar)) classpathParts.Add(versionJar);
        args.Add("-cp");
        args.Add(string.Join(Path.PathSeparator.ToString(), classpathParts));

        // Main class
        string mainClass = detail.MainClass ?? "net.minecraft.client.main.Main";
        args.Add(mainClass);

        // Resolution (game arguments, must be AFTER main class)
        if (!string.IsNullOrEmpty(width) && !string.IsNullOrEmpty(height))
        {
            args.Add("--width");
            args.Add(width);
            args.Add("--height");
            args.Add(height);
        }

        // Game arguments
        string assetsDir = Path.Combine(_gameFolder.GameDir, "assets");
        string assetIndex = detail.Assets ?? version;
        string effectiveGameDir = (instanceGameDir ?? _gameFolder.GameDir).Replace('\\', '/');
        if (detail.Arguments?.Game != null)
        {
            foreach (var elem in detail.Arguments.Game)
            {
                try
                {
                    if (elem.ValueKind == JsonValueKind.String)
                    {
                        string gameArg = PlaceholderHelper.ReplacePlaceholders((elem.GetString() ?? ""), playerName, version,
                            assetsDir, assetIndex, versionType, accessToken, uuid, _gameFolder.GameDir, instanceGameDir);
                        args.AddRange(PlaceholderHelper.SplitMinecraftArguments(gameArg));
                    }
                }
                catch (Exception ex) { _notificationService.AppendLog($"游戏参数解析警告: {ex.Message}", NotificationType.Warning); }
            }
        }
        else if (!string.IsNullOrEmpty(detail.MinecraftArguments))
        {
            string gameArgs = PlaceholderHelper.ReplacePlaceholders(detail.MinecraftArguments, playerName, version,
                assetsDir, assetIndex, versionType, accessToken, uuid, _gameFolder.GameDir, instanceGameDir);
            args.AddRange(PlaceholderHelper.SplitMinecraftArguments(gameArgs));
        }

        return args;
    }

    // ==================== Mods Integrity ====================

    /// <summary>
    /// 扫描 mods 目录，通过 Modrinth API 验证每个 .jar 是否被收录。
    /// 仅在实例使用模组加载器时执行。未收录的文件通过 ModsWarningHandler 提示用户。
    /// 返回 null 表示通过或跳过；返回非 null LaunchResult 表示用户拒绝，应中止启动。
    /// </summary>
    private async Task<LaunchResult?> VerifyModsAsync(GameInstance instance, string? instanceGameDir)
    {
        // 仅模组加载器版本需要检查
        if (string.IsNullOrEmpty(instance.LoaderType) || instance.LoaderType == "Vanilla")
            return null;

        string modsDir = instance.UseIsolatedDir && !string.IsNullOrEmpty(instance.InstanceDir)
            ? Path.Combine(instance.InstanceDir, "mods")
            : Path.Combine(_gameFolder.GameDir, "mods");

        if (!Directory.Exists(modsDir))
            return null;

        string[] jarFiles;
        try
        {
            jarFiles = Directory.GetFiles(modsDir, "*.jar");
        }
        catch { return null; }

        if (jarFiles.Length == 0)
            return null;

        var unverified = new List<string>();

        foreach (var jarPath in jarFiles)
        {
            string fileName = Path.GetFileName(jarPath);
            try
            {
                string sha1 = await Task.Run(() => ComputeSha1(jarPath));
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                bool verified = await _modrinth.VerifyFileByHashAsync(sha1, cts.Token);
                if (!verified)
                    unverified.Add(fileName);
            }
            catch
            {
                // 网络错误/超时 → 该文件静默跳过，不阻塞启动
            }
        }

        if (unverified.Count == 0)
            return null;

        // 有未收录模组 → 弹框确认
        if (ModsWarningHandler != null)
        {
            bool approved = await ModsWarningHandler(unverified);
            if (!approved)
            {
                string fileList = string.Join("、", unverified);
                return new LaunchResult
                {
                    Success = false,
                    ErrorMessage = $"用户取消了启动（检测到 {unverified.Count} 个非官方模组文件：{fileList}）。"
                };
            }
        }

        return null;
    }

    /// <summary>计算文件 SHA-1 哈希（小写十六进制）。</summary>
    private static string ComputeSha1(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        byte[] hash = System.Security.Cryptography.SHA1.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // ==================== Process Management ====================

    private async Task RunGameProcessAsync(JavaInfo javaInfo, List<string> args, string? workingDirectory = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = javaInfo.Path,
            WorkingDirectory = workingDirectory ?? _gameFolder.GameDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        // Clean up any previous process before starting a new one
        if (_gameCts != null) { _gameCts.Cancel(); _gameCts.Dispose(); }
        if (_gameProcess != null) { try { _gameProcess.Kill(); } catch { } _gameProcess.Dispose(); }

        _gameCts = new CancellationTokenSource();
        _gameProcess = Process.Start(psi) ?? throw new Exception("无法启动 Java 进程。");

        // === EiTRVO Firewall ===
        bool firewallEnabled = FirewallEnabledProvider?.Invoke() ?? false;
        if (_gameSecurity != null && firewallEnabled)
        {
            _gameSecurity.HardenProcess(_gameProcess);
            _gameSecurity.StartMonitoring(_gameProcess, (processName, pid, commandLine) =>
            {
                var cmdInfo = commandLine != null ? $"\n命令行：{commandLine}" : "";
                _notificationService.Show(
                    $"EiTRVO Firewall 已阻止危险程序调用\n\n" +
                    $"检测到游戏进程试图调用 {processName} (PID: {pid})。{cmdInfo}\n" +
                    $"已触发熔断保护，游戏进程及所有子进程已被强制终止。",
                    NotificationType.Error);
                _notificationService.WriteDiagnosticLog(
                    "EiTRVO Firewall 熔断事件",
                    $"触发进程：{processName} (PID: {pid})\n" +
                    $"命令行：{commandLine ?? "(未捕获)"}\n" +
                    $"父进程：javaw.exe (PID: {_gameProcess.Id})\n" +
                    $"响应动作：已终止子进程 + 已熔断游戏进程 + Job Object 回收集");
                KillGame();
            });
        }
        // === 原有逻辑继续 ===

        var token = _gameCts.Token;

        // Capture last 20 lines of stderr for diagnostics on non-zero exit
        var stderrQueue = new System.Collections.Concurrent.ConcurrentQueue<string?>();
        var stderrSb = new System.Text.StringBuilder();

        _ = Task.Run(async () =>
        {
            try
            {
                while (!_gameProcess.HasExited && !token.IsCancellationRequested)
                {
                    await _gameProcess.StandardOutput.ReadLineAsync();
                    // Game stdout is consumed silently; launcher events use Show/AppendLog
                }
            }
            catch { }
        });

        _ = Task.Run(async () =>
        {
            try
            {
                while (!_gameProcess.HasExited && !token.IsCancellationRequested)
                {
                    string? line = await _gameProcess.StandardError.ReadLineAsync();
                    if (line != null)
                    {
                        stderrQueue.Enqueue(line);
                        while (stderrQueue.Count > 20) stderrQueue.TryDequeue(out _);
                        stderrSb.AppendLine(line);
                    }
                }
            }
            catch { }
        });

        var gameStartTime = DateTimeOffset.UtcNow;
        await _gameProcess.WaitForExitAsync(token);

        if (_gameProcess.ExitCode != 0)
        {
            // Collect last lines from ring buffer
            var lastLines = new System.Text.StringBuilder();
            foreach (string? line in stderrQueue)
            {
                if (line != null)
                    lastLines.AppendLine(line);
            }
            string tail = lastLines.ToString().Trim();
            if (tail.Length > 1200)
                tail = tail[^1200..];

            throw new Exception($"退出码: {_gameProcess.ExitCode}\n" +
                $"实例：{_lastLaunchInstanceName}\n" +
                $"版本：{_lastLaunchVersionId}\n" +
                $"Java：{_lastLaunchJavaPath}" +
                (tail.Length > 0 ? $"\n\n--- stderr 尾部 ---\n{tail}" : ""));
        }

        // === 游戏时长统计：仅记录正常退出（ExitCode == 0）且超过 30 秒的会话 ===
        var elapsedSeconds = (long)(DateTimeOffset.UtcNow - gameStartTime).TotalSeconds;
        if (elapsedSeconds >= 30)
            RecordPlayTime(_lastLaunchInstanceName!, elapsedSeconds, DateTimeOffset.UtcNow);
    }

    // ==================== Play Time Tracking ====================

    /// <summary>将本次会话时长累加到 instance.json。失败静默忽略。</summary>
    private void RecordPlayTime(string? instanceName, long elapsedSeconds, DateTimeOffset playedAt)
    {
        if (string.IsNullOrEmpty(instanceName)) return;
        try
        {
            string metaPath = Path.Combine(_gameFolder.GameDir, "versions", instanceName, "instance.json");
            var meta = File.Exists(metaPath)
                ? (JsonSerializer.Deserialize<InstanceMeta>(File.ReadAllText(metaPath)) ?? new InstanceMeta())
                : new InstanceMeta();
            meta.TotalPlayTimeSeconds = (meta.TotalPlayTimeSeconds ?? 0) + elapsedSeconds;
            meta.LastPlayedAt = playedAt;
            File.WriteAllText(metaPath, JsonSerializer.Serialize(meta,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* best effort — 不阻塞游戏退出流程 */ }
    }

    // ==================== Diagnostics ====================

    private static string SanitizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path ?? "";
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(userProfile))
            return path.Replace(userProfile, "%USERPROFILE%");
        return path;
    }

    private string? WriteDiagnosticLog(string title, Exception ex, string briefReason)
    {
        var log = new System.Text.StringBuilder();
        log.AppendLine($"实例名称：{_lastLaunchInstanceName ?? "—"}");
        log.AppendLine($"版本 ID：  {_lastLaunchVersionId ?? "—"}");
        if (!string.IsNullOrEmpty(_lastLaunchLoaderType))
            log.AppendLine($"Mod 加载器：{_lastLaunchLoaderType} {_lastLaunchLoaderVersion ?? ""}");
        log.AppendLine($"Java 路径：{SanitizePath(_lastLaunchJavaPath)}");
        log.AppendLine($"游戏目录：{SanitizePath(_lastLaunchGameDir)}");
        log.AppendLine($"分配内存：{_lastLaunchMemory} MB");
        log.AppendLine($"启动参数：{(_lastLaunchArgs.Count > 0 ? SanitizeArgs(_lastLaunchArgs) : "—")}");
        log.AppendLine();
        log.AppendLine("--- 错误详情 ---");
        log.AppendLine(briefReason);
        log.AppendLine();
        log.AppendLine(ex.Message);

        return _notificationService.WriteDiagnosticLog(title, log.ToString(), autoOpen: true);
    }

    private static string ParseExitReason(Exception ex)
    {
        var msg = ex.Message;
        int exitIdx = msg.IndexOf("退出码:");
        if (exitIdx >= 0)
        {
            int endIdx = msg.IndexOf('\n', exitIdx);
            if (endIdx < 0) endIdx = msg.Length;
            string exitCodeStr = msg.Substring(exitIdx, endIdx - exitIdx).Trim();
            if (exitCodeStr.EndsWith("1"))
                return "游戏非正常退出。\n可能原因：版本文件不完整、Java 版本不匹配或内存不足。";
            return $"游戏异常退出（{exitCodeStr}）。";
        }
        return ex.Message;
    }

    /// <summary>Redacts sensitive values (e.g. --accessToken, --uuid) from the argument list for safe logging.</summary>
    internal static string SanitizeArgs(List<string> args)
    {
        var sanitized = new List<string>(args.Count);
        for (int i = 0; i < args.Count; i++)
        {
            if ((args[i] == "--accessToken" || args[i] == "--uuid") && i + 1 < args.Count)
            {
                sanitized.Add(args[i]);
                sanitized.Add("***REDACTED***");
                i++;
            }
            else
            {
                sanitized.Add(args[i]);
            }
        }
        return string.Join(" ", sanitized);
    }
}
