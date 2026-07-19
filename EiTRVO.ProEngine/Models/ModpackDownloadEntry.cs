using CommunityToolkit.Mvvm.ComponentModel;

namespace EiTRVO.ProEngine.Models;

/// <summary>
/// 整合包搜索结果的 UI 绑定模型。
/// 参考 ModDownloadEntry.cs，增加 MC 版本和加载器字段。
/// </summary>
public partial class ModpackDownloadEntry : ObservableObject
{
    // === 搜索结果字段 ===

    public string ProjectId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Author { get; set; } = "";
    public string IconUrl { get; set; } = "";
    public long Downloads { get; set; }
    public string LatestVersionId { get; set; } = "";

    public string DownloadsDisplay => Downloads switch
    {
        >= 1_000_000 => $"{Downloads / 1_000_000.0:F1}M",
        >= 1_000 => $"{Downloads / 1_000.0:F1}K",
        _ => $"{Downloads}"
    };

    // === 版本和加载器信息（从 ModrinthHit 解析） ===

    [ObservableProperty]
    private string _mcVersion = "";

    [ObservableProperty]
    private string _loader = "";

    /// <summary>是否有 MC 版本（控制徽章可见性）</summary>
    public bool HasMcVersion => !string.IsNullOrEmpty(McVersion);

    /// <summary>是否有加载器（控制徽章可见性）</summary>
    public bool HasLoader => !string.IsNullOrEmpty(Loader);

    // === 下载状态 ===

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private string _downloadStatus = "";

    // === 每文件进度 ===

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileProgressDisplay))]
    [NotifyPropertyChangedFor(nameof(HasFileProgress))]
    private string _currentFileName = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileProgressDisplay))]
    [NotifyPropertyChangedFor(nameof(HasFileProgress))]
    private long _currentFileDownloadedBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileProgressDisplay))]
    [NotifyPropertyChangedFor(nameof(HasFileProgress))]
    private long _currentFileTotalBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileProgressDisplay))]
    private double _currentFileSpeed;

    // === 多文件进度（安装流水线） ===

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileProgressDisplay))]
    private int _completedFiles;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileProgressDisplay))]
    private int _totalFiles;

    public bool HasFileProgress => !string.IsNullOrEmpty(CurrentFileName) && CurrentFileTotalBytes > 0;

    /// <summary>
    /// 格式化的进度行。
    /// 单文件: "mod.jar  2.3 MB / 5.1 MB  ·  1.2 MB/s"
    /// 多文件: "文件 2/5: fabric-api.jar  8.5 / 12.1 MB  ·  4.1 MB/s"
    /// </summary>
    public string FileProgressDisplay
    {
        get
        {
            if (string.IsNullOrEmpty(CurrentFileName))
                return DownloadStatus;

            string fileCount = TotalFiles > 1
                ? $"文件 {CompletedFiles}/{TotalFiles}: "
                : "";
            string sizePart = CurrentFileTotalBytes > 0
                ? $"{FormatByteSize(CurrentFileDownloadedBytes)} / {FormatByteSize(CurrentFileTotalBytes)}"
                : FormatByteSize(CurrentFileDownloadedBytes);
            string speedPart = CurrentFileSpeed > 0
                ? $"  ·  {FormatByteSize((long)CurrentFileSpeed)}/s"
                : "";
            return $"{fileCount}{CurrentFileName}  {sizePart}{speedPart}";
        }
    }

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
}
