namespace EiTRVO.ProEngine.Models;

public class DownloadProgress
{
    /// <summary>Overall progress — file count completed (backward compatible).</summary>
    public long BytesDownloaded { get; set; }
    /// <summary>Overall progress — total file count.</summary>
    public long TotalBytes { get; set; }

    // ---- Per-file byte-level progress (null/empty = no per-file update) ----

    /// <summary>Name of the file currently being downloaded.</summary>
    public string? CurrentFileName { get; set; }
    /// <summary>Bytes downloaded for the current file so far.</summary>
    public long CurrentFileDownloadedBytes { get; set; }
    /// <summary>Total size of the current file in bytes (-1 if unknown).</summary>
    public long CurrentFileTotalBytes { get; set; }
    /// <summary>Current download speed in bytes per second.</summary>
    public double DownloadSpeedBytesPerSecond { get; set; }

    /// <summary>Create a simple overall-progress report (file-count based).</summary>
    public static DownloadProgress Overall(long completed, long total) =>
        new() { BytesDownloaded = completed, TotalBytes = total };

    /// <summary>Create a per-file progress report.</summary>
    public static DownloadProgress FileProgress(
        string fileName, long downloadedBytes, long totalBytes, double speedBps,
        long overallCompleted = 0, long overallTotal = 0) =>
        new()
        {
            BytesDownloaded = overallCompleted,
            TotalBytes = overallTotal,
            CurrentFileName = fileName,
            CurrentFileDownloadedBytes = downloadedBytes,
            CurrentFileTotalBytes = totalBytes,
            DownloadSpeedBytesPerSecond = speedBps
        };
}
