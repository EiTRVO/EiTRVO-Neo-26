using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using EiTRVO.ProEngine.Services;

namespace EiTRVO.UI.Platforms;

/// <summary>
/// WPF 平台的对话框服务实现。
/// 所有对话框操作通过 IDispatcherService 调度到 UI 线程，确保线程安全。
/// </summary>
public class WpfDialogService : IDialogService
{
    private readonly IDispatcherService _dispatcher;

    public WpfDialogService(IDispatcherService dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public Task<bool> ShowConfirmAsync(string message, string title)
    {
        bool result = false;
        _dispatcher.Invoke(() =>
        {
            result = MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning)
                     == MessageBoxResult.Yes;
        });
        return Task.FromResult(result);
    }

    public Task<string?> ShowOpenFileDialogAsync(string title, string filter)
    {
        string? result = null;
        _dispatcher.Invoke(() =>
        {
            var dialog = new OpenFileDialog
            {
                Title = title,
                Filter = filter,
                CheckFileExists = true
            };
            result = dialog.ShowDialog() == true ? dialog.FileName : null;
        });
        return Task.FromResult(result);
    }

    public Task<string?> ShowSaveFileDialogAsync(string defaultName, string filter, string title)
    {
        string? result = null;
        _dispatcher.Invoke(() =>
        {
            var dialog = new SaveFileDialog
            {
                FileName = defaultName,
                Filter = filter,
                Title = title
            };
            result = dialog.ShowDialog() == true ? dialog.FileName : null;
        });
        return Task.FromResult(result);
    }

    public Task<string?> ShowFolderBrowserDialogAsync(string title)
    {
        string? result = null;
        _dispatcher.Invoke(() =>
        {
            try
            {
                // 使用 Windows 原生 COM IFileOpenDialog + FOS_PICKFOLDERS
                var dialogType = Type.GetTypeFromCLSID(new Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7"));
                if (dialogType == null)
                {
                    result = null;
                    return;
                }

                var dialog = (IFileOpenDialog)Activator.CreateInstance(dialogType)!;

                // 设置 FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM
                const uint FOS_PICKFOLDERS = 0x00000020;
                const uint FOS_FORCEFILESYSTEM = 0x00000040;
                dialog.SetOptions(FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM);

                if (!string.IsNullOrEmpty(title))
                    dialog.SetTitle(title);

                // 获取主窗口句柄作为 owner
                var mainWindow = Application.Current.MainWindow;
                var owner = mainWindow != null
                    ? new System.Windows.Interop.WindowInteropHelper(mainWindow).Handle
                    : IntPtr.Zero;

                int hr = dialog.Show(owner);
                if (hr >= 0)
                {
                    dialog.GetResult(out var shellItem);
                    shellItem.GetDisplayName(SIGDN_FILESYSPATH, out var path);
                    result = path;
                }
            }
            catch
            {
                result = null;
            }
        });
        return Task.FromResult(result);
    }

    // ==================== COM 接口定义 ====================

    private const uint SIGDN_FILESYSPATH = 0x80058000;

    [ComImport, Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
    private class NativeFileOpenDialog { }

    [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        [PreserveSig] int Show(IntPtr parent);
        void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise(IntPtr pfde, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOptions(uint fos);
        void GetOptions(out uint pfos);
        void SetDefaultFolder(IShellItem psi);
        void SetFolder(IShellItem psi);
        void GetFolder(out IShellItem ppsi);
        void GetCurrentSelection(out IShellItem ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult(out IShellItem ppsi);
        void AddPlace(IShellItem psi, uint fdap);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        void Close(int hr);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr pFilter);
    }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }
}
