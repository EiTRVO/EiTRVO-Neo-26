using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Services;

namespace EiTRVO.ProEngine.ViewModels;

public partial class ProgressViewModel : BaseViewModel, IDisposable
{
    private readonly IDispatcherService _dispatcher;
    private IDisposable? _timer;
    private int _tipIndex;

    private static readonly string[] Tips =
    {
        "使用 EiTRVO Firewall 保护您的游戏进程",
        "使用 SaveLock 保护您的存档",
        "推荐开启自动备份以保障您的游戏体验",
        "您可以在存档管理-Mod管理内下载游戏版本对应的模组"
    };

    [ObservableProperty]
    private string _selectedTab = "download";

    [ObservableProperty]
    private object? _activeDownloadVm;

    [ObservableProperty]
    private string _versionDisplay = "";

    [ObservableProperty]
    private string _loaderDisplay = "";

    [ObservableProperty]
    private string _currentTip = "";

    // === 启动进度条 ===

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLaunchProgressVisible))]
    private bool _isLaunching;

    [ObservableProperty]
    private int _launchProgressPercent;

    public bool IsLaunchProgressVisible => IsLaunching;

    private IDisposable? _launchProgressTimer;

    // === 运行日志状态 ===

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGameLog))]
    private ObservableCollection<RuntimeLogEntry> _gameLogEntries = new();

    [ObservableProperty]
    private string _gameStatusText = "游戏未运行";

    [ObservableProperty]
    private bool _isGameRunning;

    public bool HasGameLog => GameLogEntries.Count > 0;

    private const int MaxRuntimeLogLines = 1000;

    // ==================== 启动进度 ====================

    /// <summary>开始启动进度动画：0 → 90% 快速递增。</summary>
    public void StartLaunchProgress()
    {
        _launchProgressTimer?.Dispose();
        IsLaunching = true;
        LaunchProgressPercent = 0;

        int step = 0;
        _launchProgressTimer = _dispatcher.StartTimer(TimeSpan.FromMilliseconds(120), () =>
        {
            step++;
            // 前 60 步 (~7 秒) 到 90%，速度逐渐放缓
            LaunchProgressPercent = Math.Min(90, step * 3 > 30 ? 30 + (step - 10) : step * 3);

            if (LaunchProgressPercent >= 90)
            {
                _launchProgressTimer?.Dispose();
                _launchProgressTimer = null;
            }
        });
    }

    /// <summary>完成启动进度：跳到 100%，短暂停留后隐藏。</summary>
    public void CompleteLaunchProgress()
    {
        _launchProgressTimer?.Dispose();
        _launchProgressTimer = null;

        LaunchProgressPercent = 100;

        _launchProgressTimer = _dispatcher.StartTimer(TimeSpan.FromMilliseconds(600), () =>
        {
            _launchProgressTimer?.Dispose();
            _launchProgressTimer = null;
            IsLaunching = false;
            LaunchProgressPercent = 0;
        });
    }

    /// <summary>新启动时清空旧日志和进度状态。</summary>
    public void ClearOnNewLaunch(string instanceName)
    {
        _launchProgressTimer?.Dispose();
        _launchProgressTimer = null;
        IsLaunching = false;
        LaunchProgressPercent = 0;
        GameLogEntries.Clear();
        GameStatusText = $"正在启动 {instanceName}...";
        IsGameRunning = false;
        OnPropertyChanged(nameof(HasGameLog));
        _launchingInstanceName = instanceName;
    }

    private string _launchingInstanceName = "";

    // === 下载状态 ===

    public bool HasActiveDownload => ActiveDownloadVm != null;

    public bool IsDownloadTabVisible => SelectedTab == "download";
    public bool IsLaunchTabVisible => SelectedTab == "launch";

    public ProgressViewModel(IDispatcherService dispatcher)
    {
        _dispatcher = dispatcher;
    }

    partial void OnSelectedTabChanged(string value)
    {
        OnPropertyChanged(nameof(IsDownloadTabVisible));
        OnPropertyChanged(nameof(IsLaunchTabVisible));
    }

    partial void OnActiveDownloadVmChanged(object? value)
    {
        OnPropertyChanged(nameof(HasActiveDownload));

        _timer?.Dispose();
        _timer = null;

        if (value != null)
        {
            _tipIndex = 0;
            CurrentTip = Tips[0];
            _timer = _dispatcher.StartTimer(TimeSpan.FromSeconds(5), OnTimerTick);
        }
        else
        {
            VersionDisplay = "";
            LoaderDisplay = "";
            CurrentTip = "";
        }
    }

    private void OnTimerTick()
    {
        _tipIndex = (_tipIndex + 1) % Tips.Length;
        CurrentTip = Tips[_tipIndex];
    }

    // ==================== 运行时日志 ====================

    /// <summary>由 MainWindow 调用— 将一行游戏输出追加到运行日志。</summary>
    public void AppendRuntimeLog(string line, bool isStderr)
    {
        _dispatcher.Invoke(() =>
        {
            var entry = new RuntimeLogEntry
            {
                Timestamp = DateTime.Now,
                Message = line,
                IsStderr = isStderr
            };
            GameLogEntries.Add(entry);

            // FIFO 上限：超出上限时移除最早条目
            while (GameLogEntries.Count > MaxRuntimeLogLines)
                GameLogEntries.RemoveAt(0);

            OnPropertyChanged(nameof(HasGameLog));
        });
    }

    /// <summary>由 MainWindow 调用— 游戏进程启动/退出通知。</summary>
    public void OnGameRunningChanged(bool isRunning)
    {
        _dispatcher.Invoke(() =>
        {
            if (isRunning)
            {
                AppendRuntimeLog(
                    $"══════ 游戏启动 ({DateTime.Now:yyyy-MM-dd HH:mm:ss}) ══════", false);
                GameStatusText = string.IsNullOrEmpty(_launchingInstanceName)
                    ? "游戏运行中"
                    : $"游戏运行中 — {_launchingInstanceName}";
                IsGameRunning = true;
            }
            else
            {
                GameStatusText = "游戏已退出";
                IsGameRunning = false;
                _launchingInstanceName = "";
            }
        });
    }

    [RelayCommand]
    private void ClearRuntimeLog()
    {
        GameLogEntries.Clear();
        OnPropertyChanged(nameof(HasGameLog));
    }

    [RelayCommand]
    private void SelectTab(string tab) => SelectedTab = tab;

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
