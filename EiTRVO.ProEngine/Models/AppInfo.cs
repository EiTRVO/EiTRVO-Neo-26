namespace EiTRVO.ProEngine.Models;

/// <summary>
/// Single source of truth for all app-level display strings.
/// When bumping the version, change it here and all UI spots update.
/// </summary>
public static class AppInfo
{
    public const string Title = "EiTRVO Neo";
    public const string Version = "1006";
    public const string VersionShort = "26";
    public const string BuildDate = "2026年7月";
    public const string Runtime = ".NET";
    public const string Architecture = "Windows Desktop";
    public const string Platform = "x86_64";

    // Pack export/import
    public const int PackVersion = 1;
    public const string PackFormat = "eitrvo-pack:1";
}
