using System.Threading.Tasks;

namespace EiTRVO.ProEngine.Services;

/// <summary>
/// 平台无关的对话框服务接口。
/// 所有方法均为异步，WPF 实现内部包装同步 API，WinUI 3 实现使用原生异步 API。
/// </summary>
public interface IDialogService
{
    Task<bool> ShowConfirmAsync(string message, string title);
    Task<string?> ShowOpenFileDialogAsync(string title, string filter);
    Task<string?> ShowSaveFileDialogAsync(string defaultName, string filter, string title);
    Task<string?> ShowFolderBrowserDialogAsync(string title);
}
