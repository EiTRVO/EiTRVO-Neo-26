using System;
using System.Runtime.InteropServices;

namespace EiTRVO.ProEngine.Helpers;

/// <summary>
/// Detects system physical memory via <c>GlobalMemoryStatusEx</c> (kernel32).
/// Provides recommended Minecraft memory bounds based on total installed RAM.
/// </summary>
public static class SystemMemoryInfo
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    /// <summary>Total installed physical memory, in megabytes.</summary>
    public static long TotalPhysicalMB { get; }

    /// <summary>Currently available physical memory (free + standby), in megabytes.</summary>
    public static long AvailablePhysicalMB { get; }

    /// <summary>System memory load percentage (0–100).</summary>
    public static uint MemoryLoadPercent { get; }

    /// <summary>
    /// Recommended maximum Minecraft memory allocation: 70% of total RAM,
    /// clamped to [2048, 32768] MB.
    /// </summary>
    public static int RecommendedMaxMemoryMB =>
        Math.Clamp((int)(TotalPhysicalMB * 0.70), 2048, 32768);

    /// <summary>
    /// Slider soft cap: 70% of total RAM capped at 16 GB (16384 MB).
    /// Prevents the slider's rightmost ticks from bunching up at
    /// unnecessarily high values while still allowing manual input
    /// beyond the cap.
    /// </summary>
    public static int SliderMaxMemoryMB =>
        Math.Min(RecommendedMaxMemoryMB, 16384);

    /// <summary>
    /// Recommended default Minecraft memory allocation: 20% of total RAM,
    /// clamped to [2048, 8192] MB.
    /// </summary>
    public static int RecommendedDefaultMemoryMB =>
        Math.Clamp((int)(TotalPhysicalMB * 0.20), 2048, 8192);

    /// <summary>
    /// Safe zone threshold: 50% of total RAM (green zone).
    /// </summary>
    public static int SafeZoneMB =>
        Math.Max(1024, (int)(TotalPhysicalMB * 0.50));

    /// <summary>
    /// Warning zone threshold: 65% of total RAM (yellow zone).
    /// </summary>
    public static int WarnZoneMB =>
        Math.Max(1024, (int)(TotalPhysicalMB * 0.65));

    static SystemMemoryInfo()
    {
        var memStatus = new MEMORYSTATUSEX();
        memStatus.dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();

        if (GlobalMemoryStatusEx(ref memStatus))
        {
            TotalPhysicalMB = (long)(memStatus.ullTotalPhys / (1024 * 1024));
            AvailablePhysicalMB = (long)(memStatus.ullAvailPhys / (1024 * 1024));
            MemoryLoadPercent = memStatus.dwMemoryLoad;
        }
        else
        {
            // Fallback: assume 8 GB total, 4 GB available
            TotalPhysicalMB = 8192;
            AvailablePhysicalMB = 4096;
            MemoryLoadPercent = 50;
        }
    }

    /// <summary>
    /// Snap a memory value (in MB) to the nearest valid step based on the
    /// segmented step-size table. Lower ranges use finer granularity.
    /// </summary>
    public static int SnapToStep(int mb)
    {
        int step = GetStepSize(mb);
        return (int)(Math.Round((double)mb / step) * step);
    }

    /// <summary>
    /// Get the step size for the segmented slider at a given memory value.
    /// </summary>
    public static int GetStepSize(int mb)
    {
        return mb switch
        {
            <= 1024  => 128,
            <= 4096  => 256,
            <= 8192  => 512,
            <= 16384 => 1024,
            _        => 2048
        };
    }

    /// <summary>Human-readable total RAM string (e.g. "16 GB").</summary>
    public static string TotalRamDisplay
    {
        get
        {
            double gb = TotalPhysicalMB / 1024.0;
            return gb >= 1.0 ? $"{gb:F0} GB" : $"{TotalPhysicalMB} MB";
        }
    }
}
