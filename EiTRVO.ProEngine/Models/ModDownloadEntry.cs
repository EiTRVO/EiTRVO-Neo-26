using CommunityToolkit.Mvvm.ComponentModel;

namespace EiTRVO.ProEngine.Models;

public partial class ModDownloadEntry : ObservableObject
{
    public string ProjectId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Author { get; set; } = "";
    public string IconUrl { get; set; } = "";
    public long Downloads { get; set; }

    public string DownloadsDisplay => Downloads switch
    {
        >= 1_000_000 => $"{Downloads / 1_000_000.0:F1}M",
        >= 1_000 => $"{Downloads / 1_000.0:F1}K",
        _ => $"{Downloads}"
    };

    [ObservableProperty]
    private bool _isCompatible;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private string _downloadStatus = "";

    // === Per-file progress ===

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

    // === Multi-file dependency chain progress ===

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileProgressDisplay))]
    private int _completedFiles;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileProgressDisplay))]
    private int _totalFiles;

    public bool HasFileProgress => !string.IsNullOrEmpty(CurrentFileName) && CurrentFileTotalBytes > 0;

    /// <summary>
    /// Formatted progress line.
    /// Single file: "jei.jar  2.3 MB / 5.1 MB  ·  1.2 MB/s"
    /// With deps:    "文件 2/5: fabric-api.jar  8.5 / 12.1 MB  ·  4.1 MB/s"
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

    /// <summary>
    /// File size estimate shown before download starts.
    /// "jei.jar (12.5 MB)"
    /// </summary>
    public string FileSizeHint
    {
        get
        {
            if (CurrentFileTotalBytes <= 0 || !string.IsNullOrEmpty(CurrentFileName))
                return "";
            return $"{FormatByteSize(CurrentFileTotalBytes)}";
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
