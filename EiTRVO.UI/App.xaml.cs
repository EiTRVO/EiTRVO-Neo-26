using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using EiTRVO.ProEngine.Services;
using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.ViewModels;
using EiTRVO.ProEngine.Orchestrators;
using EiTRVO.UI.Platforms;
using EiTRVO.UI.Services;
using EiTRVO.UI.ViewModels;

namespace EiTRVO.UI
{
    public partial class App : Application
    {
        public static IServiceProvider Services { get; private set; } = null!;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var services = new ServiceCollection();
            ConfigureServices(services);
            Services = services.BuildServiceProvider();

            // === Windows 10 1903+ 版本检查（与 TFM net8.0-windows10.0.18362.0 对齐） ===
            if (Environment.OSVersion.Version.Build < 18362)
            {
                MessageBox.Show(
                    "EiTRVO Neo 需要 Windows 10 1903（Build 18362）或更高版本。\n\n" +
                    "您的系统版本过低，请升级操作系统后再次运行。",
                    "EiTRVO Neo — 系统要求",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                Shutdown();
                return;
            }

            // === 全局异常处理 ===
            RegisterGlobalExceptionHandlers();

            var window = Services.GetRequiredService<MainWindow>();
            window.Show();
        }

        private static void ConfigureServices(IServiceCollection services)
        {
            // === HttpClient (Singleton) ===
            services.AddSingleton<System.Net.Http.HttpClient>(_ =>
            {
                var handler = new System.Net.Http.SocketsHttpHandler
                {
                    MaxConnectionsPerServer = 64,
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5)
                };
                var client = new System.Net.Http.HttpClient(handler)
                {
                    Timeout = TimeSpan.FromMinutes(10)
                };
                client.DefaultRequestHeaders.Add("User-Agent", $"EiTRVONeo/{AppInfo.Version} (EiTRVO)");
                return client;
            });

            // === Platform ===
            services.AddSingleton<IDispatcherService, WpfDispatcherService>();
            services.AddSingleton<IWindowsHelloService, WindowsHelloService>();
            services.AddSingleton<IGameProcessSecurityService, WindowsGameProcessSecurityService>();

            // === Services ===
            services.AddSingleton<IAuthService, AuthService>();
            services.AddSingleton<IDownloadService, DownloadService>();
            services.AddSingleton<IModLoaderService, ModLoaderService>();
            services.AddSingleton<IPackService, PackService>();
            services.AddSingleton<IModrinthService, ModrinthService>();
            services.AddSingleton<IDialogService, WpfDialogService>();
            services.AddSingleton<IClipboardService, WpfClipboardService>();
            services.AddSingleton<IProcessService, ProcessService>();
            services.AddSingleton<ISettingsService, AppSettingsService>();
            services.AddSingleton<SkinService>();

            // === Save Lock Services ===
            services.AddSingleton<SaveLockService>();
            services.AddSingleton<LocalKeyStore>();
            services.AddSingleton<SaveRecoveryFile>();

            // === Orchestrators ===
            services.AddSingleton<IGameFolderService, GameFolderService>();
            services.AddSingleton<INotificationService, NotificationService>();
            services.AddSingleton<IThemeService, ThemeService>();
            services.AddSingleton<JavaDetectionService>();
            services.AddSingleton<AccountManager>();
            services.AddSingleton<InstanceManager>();
            services.AddSingleton<LaunchOrchestrator>();
            services.AddSingleton<BackupService>();

            // === ViewModels ===
            services.AddTransient<HomeViewModel>();
            services.AddTransient<DownloadViewModel>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<ManageViewModel>();
            services.AddTransient<AccountViewModel>();
            services.AddTransient<AboutViewModel>();
            services.AddTransient<InstanceDetailViewModel>();
            services.AddTransient<ModManagementViewModel>();
            services.AddTransient<ResourcePackViewModel>();
            services.AddTransient<SaveLockDetailViewModel>();
            services.AddTransient<ModpackDownloadViewModel>();
            services.AddTransient<AccountSkinViewModel>();
            services.AddTransient<SchematicManagementViewModel>();

            // === MainWindow ===
            services.AddSingleton<MainWindow>();
        }

        // ==================== 全局异常处理 ====================

        private static void RegisterGlobalExceptionHandlers()
        {
            // 1) 后台线程未处理异常 → 写诊断日志
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                var ex = args.ExceptionObject as Exception
                         ?? new Exception(args.ExceptionObject?.ToString() ?? "未知错误");
                WriteCrashLog("未处理的后台线程异常", ex);
            };

            // 2) UI 线程未处理异常 → 写诊断日志 + 阻止 WPF 默认崩溃对话框
            Application.Current.DispatcherUnhandledException += (sender, args) =>
            {
                WriteCrashLog("未处理的 UI 线程异常", args.Exception);
                args.Handled = true;
                try
                {
                    MessageBox.Show(
                        $"发生未预期错误，启动器需要关闭。\n\n{args.Exception.Message}\n\n" +
                        "详细信息已写入诊断日志。",
                        "EiTRVO Neo — 错误",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                catch { /* MessageBox 本身也失败时放弃 */ }
                Application.Current.Shutdown();
            };

            // 3) Fire-and-forget Task 中未观察到的异常
            TaskScheduler.UnobservedTaskException += (sender, args) =>
            {
                WriteCrashLog("未观察到的 Task 异常", args.Exception);
                args.SetObserved();
            };
        }

        private static void WriteCrashLog(string title, Exception ex)
        {
            try
            {
                var notify = Services?.GetService<INotificationService>();
                if (notify == null) return;

                var detail = new System.Text.StringBuilder();
                detail.AppendLine($"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                detail.AppendLine($"版本：{AppInfo.Version}");
                detail.AppendLine($"OS：{Environment.OSVersion}");
                detail.AppendLine($"64位：{Environment.Is64BitProcess}");
                detail.AppendLine();
                detail.AppendLine("--- 异常详情 ---");
                var current = ex;
                int depth = 0;
                while (current != null && depth < 10)
                {
                    detail.AppendLine($"[{depth}] {current.GetType().FullName}: {current.Message}");
                    detail.AppendLine(current.StackTrace ?? "(无堆栈跟踪)");
                    detail.AppendLine();
                    current = current.InnerException;
                    depth++;
                }
                if (ex is AggregateException agg)
                {
                    foreach (var inner in agg.InnerExceptions)
                    {
                        detail.AppendLine("--- Aggregate 内部异常 ---");
                        detail.AppendLine($"{inner.GetType().FullName}: {inner.Message}");
                        detail.AppendLine(inner.StackTrace ?? "(无堆栈跟踪)");
                        detail.AppendLine();
                    }
                }

                notify.WriteDiagnosticLog(title, detail.ToString(), autoOpen: false);
            }
            catch { /* 日志记录本身失败则放弃 */ }
        }
    }
}
