using System;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EiTRVO.ProEngine.Helpers;
using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Orchestrators;
using EiTRVO.ProEngine.Services;

namespace EiTRVO.ProEngine.ViewModels;

public partial class InstanceDetailViewModel : BaseViewModel
{
    private readonly InstanceManager _instanceManager;
    private readonly IGameFolderService _gameFolder;
    private readonly INotificationService _notificationService;
    private readonly IDialogService _dialogService;
    private readonly SaveLockService _saveLockService;

    // === Observable Properties ===

    [ObservableProperty]
    private string _instanceName = "";

    [ObservableProperty]
    private string _versionId = "";

    [ObservableProperty]
    private string _loaderType = "Vanilla";

    [ObservableProperty]
    private string _loaderVersion = "";

    [ObservableProperty]
    private string _instancePath = "";

    [ObservableProperty]
    private bool _useIsolatedDir;

    [ObservableProperty]
    private string _isolatedDir = "";

    [ObservableProperty]
    private bool _showModManagement;

    [ObservableProperty]
    private string _modsFolder = "";

    // === Save / World management ===

    [ObservableProperty]
    private string _savesFolder = "";

    /// <summary>存档列表</summary>
    public ObservableCollection<SaveEntry> SaveEntries { get; } = new();

    [ObservableProperty]
    private bool _hasSaves;

    [ObservableProperty]
    private int _saveCount;

    [ObservableProperty]
    private string _resourcePacksFolder = "";

    [ObservableProperty]
    private string _shaderPacksFolder = "";

    [ObservableProperty]
    private string _schematicsFolder = "";

    [ObservableProperty]
    private bool _showSchematicManagement;

    [ObservableProperty]
    private string _playTimeDisplay = "";

    [ObservableProperty]
    private string _lastPlayedDisplay = "";

    [ObservableProperty]
    private bool _showPlayTime;

    /// <summary>Triggers MainWindow to navigate back to the manage panel.</summary>
    public event Action? BackRequested;

    /// <summary>Triggers MainWindow to navigate to the mod management panel.</summary>
    public event Action<string, string, string, string>? NavigateToModManagement; // (name, modsFolder, versionId, loaderType)

    /// <summary>Triggers MainWindow to navigate to the resource &amp; shader pack management panel.</summary>
    public event Action<string, string, string, string>? NavigateToResourcePacks; // (name, resourcePacksFolder, shaderPacksFolder, versionId)

    /// <summary>Triggers MainWindow to navigate to the save lock detail panel.</summary>
    public event Action<string, string, string, bool>? NavigateToSaveLockDetail; // (instanceName, saveName, savePath, isLocked)

    /// <summary>Triggers MainWindow to navigate to the schematic management panel.</summary>
    public event Action<string, string>? NavigateToSchematicManagement; // (instanceName, schematicsFolder)

    // === 存档锁 ===

    /// <summary>存档锁列表（加密 + 未加密）</summary>
    public ObservableCollection<SaveEntry> SaveLockEntries { get; } = new();

    [ObservableProperty]
    private bool _hasSaveLockEntries;

    [ObservableProperty]
    private int _saveLockCount;

    public InstanceDetailViewModel(
        InstanceManager instanceManager,
        IGameFolderService gameFolder,
        INotificationService notificationService,
        IDialogService dialogService,
        SaveLockService saveLockService)
    {
        _instanceManager = instanceManager;
        _gameFolder = gameFolder;
        _notificationService = notificationService;
        _dialogService = dialogService;
        _saveLockService = saveLockService;
    }

    /// <summary>Load instance data by name. Called by MainWindow before navigating to this panel.</summary>
    public void LoadInstance(string instanceName)
    {
        var inst = _instanceManager.FindByName(instanceName);
        if (inst == null)
        {
            _notificationService.Show($"实例 {instanceName} 不存在。", NotificationType.Warning);
            BackRequested?.Invoke();
            return;
        }

        InstanceName = inst.Name;
        VersionId = inst.VersionId;
        LoaderType = inst.LoaderType ?? "Vanilla";
        LoaderVersion = inst.LoaderVersion ?? "";
        UseIsolatedDir = inst.UseIsolatedDir;
        IsolatedDir = inst.InstanceDir ?? "";
        InstancePath = Path.Combine(_gameFolder.GameDir, "versions", inst.Name);

        // Resolve actual MC version from version.json (inheritsFrom for modded instances)
        VersionId = ResolveMcVersion(inst);

        ShowModManagement = LoaderType != "Vanilla";
        ModsFolder = UseIsolatedDir && !string.IsNullOrEmpty(IsolatedDir)
            ? Path.Combine(IsolatedDir, "mods")
            : Path.Combine(_gameFolder.GameDir, "mods");

        SavesFolder = UseIsolatedDir && !string.IsNullOrEmpty(IsolatedDir)
            ? Path.Combine(IsolatedDir, "saves")
            : Path.Combine(_gameFolder.GameDir, "saves");

        ResourcePacksFolder = UseIsolatedDir && !string.IsNullOrEmpty(IsolatedDir)
            ? Path.Combine(IsolatedDir, "resourcepacks")
            : Path.Combine(_gameFolder.GameDir, "resourcepacks");

        ShaderPacksFolder = UseIsolatedDir && !string.IsNullOrEmpty(IsolatedDir)
            ? Path.Combine(IsolatedDir, "shaderpacks")
            : Path.Combine(_gameFolder.GameDir, "shaderpacks");

        SchematicsFolder = UseIsolatedDir && !string.IsNullOrEmpty(IsolatedDir)
            ? Path.Combine(IsolatedDir, "schematics")
            : Path.Combine(_gameFolder.GameDir, "schematics");
        ShowSchematicManagement = Directory.Exists(SchematicsFolder);

        // === 游戏时长统计：从 GameInstance 读取并格式化 ===
        if (inst.TotalPlayTimeSeconds > 0)
        {
            var ts = TimeSpan.FromSeconds(inst.TotalPlayTimeSeconds);
            PlayTimeDisplay = ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours} 小时 {ts.Minutes} 分钟"
                : ts.TotalMinutes >= 1
                    ? $"{ts.Minutes} 分钟"
                    : "不到 1 分钟";
            LastPlayedDisplay = inst.LastPlayedAt.HasValue
                ? inst.LastPlayedAt.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm")
                : "";
            ShowPlayTime = true;
        }
        else
        {
            PlayTimeDisplay = "";
            LastPlayedDisplay = "";
            ShowPlayTime = false;
        }

        LoadSaves();
        LoadSaveLockEntries();
    }

    /// <summary>
    /// Read version.json and return the actual Minecraft version.
    /// For modded instances (Forge/Fabric/etc.), this is the inheritsFrom value.
    /// For vanilla, this is the version Id itself.
    /// </summary>
    private string ResolveMcVersion(GameInstance inst)
    {
        try
        {
            string jsonPath = Path.Combine(_gameFolder.GameDir, "versions", inst.Name, "version.json");
            if (File.Exists(jsonPath))
            {
                var vd = System.Text.Json.JsonSerializer.Deserialize<VersionDetail>(File.ReadAllText(jsonPath));
                if (vd != null && !string.IsNullOrEmpty(vd.InheritsFrom))
                    return vd.InheritsFrom;
            }
        }
        catch { }
        return inst.VersionId;
    }

    [RelayCommand]
    private void GoBack() => BackRequested?.Invoke();

    [RelayCommand]
    private void OpenModManagement()
        => NavigateToModManagement?.Invoke(InstanceName, ModsFolder, VersionId, LoaderType);

    [RelayCommand]
    private void OpenResourcePacks()
        => NavigateToResourcePacks?.Invoke(InstanceName, ResourcePacksFolder, ShaderPacksFolder, VersionId);

    [RelayCommand]
    private void OpenSchematicManagement()
        => NavigateToSchematicManagement?.Invoke(InstanceName, SchematicsFolder);

    // ==================== 存档管理 ====================

    /// <summary>扫描 saves 文件夹，刷新存档列表</summary>
    private void LoadSaves()
    {
        SaveEntries.Clear();
        if (!Directory.Exists(SavesFolder))
        {
            HasSaves = false;
            SaveCount = 0;
            return;
        }

        var dirs = Directory.GetDirectories(SavesFolder)
            .OrderByDescending(d => Directory.GetLastWriteTime(d))
            .ToList();

        foreach (var dir in dirs)
        {
            try
            {
                var info = new DirectoryInfo(dir);
                string loc = UseIsolatedDir
                    ? $"隔离存档 · {IsolatedDir}"
                    : ".minecraft\\saves";
                string size = FormatByteSize(CalculateDirectorySize(dir));
                string time = Directory.GetLastWriteTime(dir).ToString("yyyy-MM-dd HH:mm");

                SaveEntries.Add(new SaveEntry
                {
                    Name = info.Name,
                    FullPath = dir,
                    LocationDisplay = loc,
                    SizeDisplay = size,
                    InfoDisplay = $"{loc} · {size} · {time}",
                    TitleDisplay = $"{info.Name} · {size} · {time}"
                });
            }
            catch { /* skip inaccessible directories */ }
        }

        HasSaves = SaveEntries.Count > 0;
        SaveCount = SaveEntries.Count;
    }

    /// <summary>递归计算目录大小（字节）</summary>
    private static long CalculateDirectorySize(string path)
    {
        try
        {
            return Directory.GetFiles(path, "*", SearchOption.AllDirectories)
                .Sum(f =>
                {
                    try { return new FileInfo(f).Length; }
                    catch { return 0; }
                });
        }
        catch { return 0; }
    }

    /// <summary>格式化字节大小为人类可读字符串</summary>
    private static string FormatByteSize(long bytes)
    {
        if (bytes < 0) return "?";
        if (bytes < 1024) return $"{bytes} B";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int unitIdx = 0;
        double size = bytes;
        while (size >= 1024.0 && unitIdx < units.Length - 1)
        {
            size /= 1024.0;
            unitIdx++;
        }
        return $"{size:F1} {units[unitIdx]}";
    }

    /// <summary>删除存档（确认后）</summary>
    [RelayCommand]
    private async Task DeleteSaveAsync(SaveEntry? save)
    {
        if (save == null) return;

        if (!await _dialogService.ShowConfirmAsync(
            $"确定要删除存档「{save.Name}」吗？\n该操作不可恢复！",
            "确认删除"))
            return;

        try
        {
            await Task.Run(() =>
            {
                if (Directory.Exists(save.FullPath))
                    Directory.Delete(save.FullPath, true);
            });
            _notificationService.Show($"存档「{save.Name}」已删除。", NotificationType.Success);
        }
        catch (Exception ex)
        {
            _notificationService.Show($"删除失败：{ex.Message}", NotificationType.Error);
        }
        finally
        {
            LoadSaves();
        }
    }

    /// <summary>打包存档为 zip 并导出</summary>
    [RelayCommand]
    private async Task ArchiveSaveAsync(SaveEntry? save)
    {
        if (save == null) return;

        var savePath = await _dialogService.ShowSaveFileDialogAsync(
            $"{save.Name}.zip", "ZIP 压缩包|*.zip", "导出存档");
        if (savePath == null) return;

        try
        {
            await Task.Run(() =>
            {
                if (File.Exists(savePath)) File.Delete(savePath);
                ZipFile.CreateFromDirectory(save.FullPath, savePath,
                    CompressionLevel.Optimal, includeBaseDirectory: true);
            });
            _notificationService.Show($"存档「{save.Name}」已导出至 {savePath}", NotificationType.Success);
        }
        catch (Exception ex)
        {
            _notificationService.Show($"导出失败：{ex.Message}", NotificationType.Error);
        }
    }

    /// <summary>导入存档：选择 zip → 验证 → 解压到 saves 目录</summary>
    [RelayCommand]
    private async Task ImportSaveAsync()
    {
        var filePath = await _dialogService.ShowOpenFileDialogAsync(
            "选择存档文件", "ZIP 压缩包|*.zip");
        if (filePath == null) return;

        try
        {
            string? error = await Task.Run(() => ImportSaveZip(filePath));
            if (error != null)
            {
                _notificationService.Show(error, NotificationType.Warning);
                return;
            }
            _notificationService.Show("存档导入成功！", NotificationType.Success);
            LoadSaves();
        }
        catch (Exception ex)
        {
            _notificationService.Show($"导入失败：{ex.Message}", NotificationType.Error);
        }
    }

    /// <summary>
    /// 验证并解压存档 zip 到 saves 目录。
    /// 返回 null 表示成功，返回非 null 字符串表示验证失败原因。
    /// </summary>
    private string? ImportSaveZip(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);

        // 1. 整合包（manifest.json）
        if (archive.Entries.Any(e => e.Name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)))
            return "这不是存档文件，而是一个整合包。请在管理页面使用「导入整合包」功能。";

        // 2. Mod JAR（根目录的 .jar 文件）
        if (archive.Entries.Any(e => e.Name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
            && !e.FullName.Contains('/')))
            return "这不是存档文件，而是一个 Mod 文件（.jar）。";

        // 3. 资源包（pack.mcmeta）
        if (archive.Entries.Any(e => e.Name.Equals("pack.mcmeta", StringComparison.OrdinalIgnoreCase)
            && (e.FullName.IndexOf('/') < 0 || e.FullName.StartsWith("assets/") || e.FullName.StartsWith("pack."))))
            return "这不是存档文件，而是一个资源包。";

        // 4. 存档标识文件 level.dat
        var levelDatEntry = archive.Entries.FirstOrDefault(
            e => e.Name.Equals("level.dat", StringComparison.OrdinalIgnoreCase)
                && !e.FullName.EndsWith("/"));

        if (levelDatEntry == null)
            return "无效的存档文件：未找到 level.dat（世界数据文件）。";

        // === 验证通过，开始解压 ===

        string saveFolderName;
        string stripPrefix = "";

        if (levelDatEntry.FullName.Contains('/'))
        {
            string parentDir = levelDatEntry.FullName.Split('/')[0];
            saveFolderName = parentDir;
            stripPrefix = parentDir + "/";
        }
        else
        {
            saveFolderName = Path.GetFileNameWithoutExtension(zipPath);
        }

        saveFolderName = PathSafetyHelper.SanitizeNameComponent(saveFolderName);

        string destDir = Path.Combine(SavesFolder, saveFolderName);
        if (Directory.Exists(destDir))
        {
            int counter = 1;
            while (Directory.Exists(Path.Combine(SavesFolder, $"{saveFolderName}_{counter}")))
                counter++;
            destDir = Path.Combine(SavesFolder, $"{saveFolderName}_{counter}");
        }

        Directory.CreateDirectory(SavesFolder);

        foreach (var entry in archive.Entries)
        {
            if (!string.IsNullOrEmpty(stripPrefix)
                && !entry.FullName.StartsWith(stripPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            string relativePath = string.IsNullOrEmpty(stripPrefix)
                ? entry.FullName
                : entry.FullName.Substring(stripPrefix.Length);
            if (string.IsNullOrEmpty(relativePath)) continue;

            string destPath = Path.GetFullPath(Path.Combine(destDir, relativePath));
            string fullBase = Path.GetFullPath(destDir).TrimEnd(Path.DirectorySeparatorChar);
            if (!destPath.StartsWith(fullBase + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(destPath, fullBase, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("路径穿越检测");

            if (entry.FullName.EndsWith("/"))
            {
                Directory.CreateDirectory(destPath);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                entry.ExtractToFile(destPath, overwrite: true);
            }
        }

        return null; // 成功
    }

    // ==================== 存档锁 ====================

    /// <summary>扫描 saves 文件夹和 *.savenc 文件，刷新存档锁列表</summary>
    private void LoadSaveLockEntries()
    {
        SaveLockEntries.Clear();
        if (!Directory.Exists(SavesFolder))
        {
            HasSaveLockEntries = false;
            SaveLockCount = 0;
            return;
        }

        string loc = UseIsolatedDir
            ? $"隔离存档 . {IsolatedDir}"
            : ".minecraft\\saves";

        // 1. 未加密的文件夹存档
        var dirs = Directory.GetDirectories(SavesFolder)
            .OrderByDescending(d => Directory.GetLastWriteTime(d))
            .ToList();

        foreach (var dir in dirs)
        {
            try
            {
                var info = new DirectoryInfo(dir);
                string size = FormatByteSize(CalculateDirectorySize(dir));
                string time = Directory.GetLastWriteTime(dir).ToString("yyyy-MM-dd HH:mm");

                SaveLockEntries.Add(new SaveEntry
                {
                    Name = info.Name,
                    FullPath = dir,
                    LocationDisplay = loc,
                    SizeDisplay = size,
                    InfoDisplay = $"{size} . {time} . {loc}",
                    TitleDisplay = info.Name,
                    IsLocked = false,
                    SaveType = "folder"
                });
            }
            catch { /* skip inaccessible */ }
        }

        // 2. 已加密的 .savenc 文件
        var savencFiles = Directory.GetFiles(SavesFolder, "*.savenc")
            .OrderByDescending(f => File.GetLastWriteTime(f))
            .ToList();

        foreach (var savencFile in savencFiles)
        {
            try
            {
                var meta = _saveLockService.GetSaveLockMetadata(savencFile);
                string saveName = meta.SaveName;
                if (string.IsNullOrEmpty(saveName))
                    saveName = Path.GetFileNameWithoutExtension(savencFile);

                string size = FormatByteSize(meta.OriginalSize > 0 ? meta.OriginalSize : new FileInfo(savencFile).Length);
                string time = meta.CreatedAt != DateTimeOffset.MinValue
                    ? meta.CreatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm")
                    : File.GetLastWriteTime(savencFile).ToString("yyyy-MM-dd HH:mm");

                string modeDisplay = meta.LockMode == SaveLockMode.Permanent ? "永久" : "一次性";
                string infoLine = meta.OneDriveBackedUp
                    ? $"{size} . {time} . 已锁定 . {modeDisplay} . 已备份"
                    : $"{size} . {time} . 已锁定 . {modeDisplay} . 未云备份";

                SaveLockEntries.Add(new SaveEntry
                {
                    Name = saveName,
                    FullPath = savencFile,
                    LocationDisplay = loc,
                    SizeDisplay = size,
                    InfoDisplay = infoLine,
                    TitleDisplay = saveName,
                    IsLocked = true,
                    LockModeDisplay = modeDisplay,
                    LockedAtDisplay = time,
                    BoundMsAccount = !string.IsNullOrEmpty(meta.BoundMsUuidHash) ? "已绑定" : null,
                    PasswordHint = meta.PasswordHint,
                    SavencPath = savencFile,
                    OneDriveBackedUp = meta.OneDriveBackedUp,
                    SaveType = "savenc"
                });
            }
            catch { /* skip corrupted .savenc */ }
        }

        HasSaveLockEntries = SaveLockEntries.Count > 0;
        SaveLockCount = SaveLockEntries.Count;
    }

    [RelayCommand]
    private void OpenSaveLockDetail(SaveEntry? entry)
    {
        if (entry == null) return;
        bool isLocked = entry.SaveType == "savenc";
        string path = isLocked ? (entry.SavencPath ?? entry.FullPath) : entry.FullPath;
        NavigateToSaveLockDetail?.Invoke(InstanceName, entry.Name, path, isLocked);
    }
}
