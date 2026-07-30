using System;
using System.Threading;
using System.Threading.Tasks;
using EiTRVO.ProEngine.Helpers;
using EiTRVO.ProEngine.Models;

namespace EiTRVO.ProEngine.Services;

/// <summary>
/// .mrpack 整合包安装管线接口。
/// 处理从本地 .mrpack 文件到完整实例的安装流程。
/// </summary>
public interface IMrpackInstallService
{
    /// <summary>
    /// 解析并验证 .mrpack 文件的清单。不执行安装。
    /// </summary>
    /// <param name="mrpackPath">.mrpack 文件路径</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>解析出的清单信息，包含 manifest、MC 版本和 loader 信息</returns>
    Task<MrpackInfo> ParseMrpackAsync(string mrpackPath, CancellationToken ct = default);

    /// <summary>
    /// 从 .mrpack 文件安装整合包。
    /// </summary>
    /// <param name="mrpackPath">.mrpack 文件路径</param>
    /// <param name="instanceName">目标实例名称</param>
    /// <param name="targetDir">目标实例目录</param>
    /// <param name="gameDir">游戏根目录</param>
    /// <param name="mcVersion">Minecraft 版本</param>
    /// <param name="loaderType">Mod 加载器类型（可为 null）</param>
    /// <param name="loaderVersion">Mod 加载器版本（可为 null）</param>
    /// <param name="javaPath">Java 路径（Forge/NeoForge 需要）</param>
    /// <param name="settings">启动器设置</param>
    /// <param name="progress">进度报告</param>
    /// <param name="log">日志输出</param>
    /// <param name="ct">取消令牌</param>
    Task InstallMrpackAsync(
        string mrpackPath,
        string instanceName,
        string targetDir,
        string gameDir,
        string mcVersion,
        string? loaderType,
        string? loaderVersion,
        string? javaPath,
        LauncherSettings settings,
        IProgress<DownloadProgress> progress,
        Action<string, NotificationType, int> log,
        CancellationToken ct = default);

    /// <summary>
    /// Java 兼容性检查委托（由调用方注入，用于 Forge/NeoForge 安装）。
    /// </summary>
    Func<string, string, Task<string?>>? JavaCompatibilityHandler { get; set; }
}

/// <summary>
/// .mrpack 解析结果，包含安装所需的全部元数据。
/// </summary>
public class MrpackInfo
{
    public ModpackManifest Manifest { get; init; } = new();
    public string McVersion { get; init; } = "";
    public string? LoaderType { get; init; }
    public string? LoaderVersionSpec { get; init; }
}
