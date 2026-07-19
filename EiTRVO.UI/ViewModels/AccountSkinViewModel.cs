using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Orchestrators;
using EiTRVO.UI.Rendering;
using EiTRVO.ProEngine.Services;
using EiTRVO.ProEngine.ViewModels;

namespace EiTRVO.UI.ViewModels;

public partial class AccountSkinViewModel : BaseViewModel
{
    private readonly SkinService _skinService;
    private readonly INotificationService _notificationService;
    private readonly IDialogService _dialogService;

    private Account? _account;
    private readonly object _loadLock = new();
    private CancellationTokenSource? _loadCts;

    // === Observable Properties ===

    [ObservableProperty]
    private string _playerName = "";

    [ObservableProperty]
    private ImageSource? _avatarSource;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _selectedVariant = "classic";

    [ObservableProperty]
    private string _selectedFilePath = "";

    [ObservableProperty]
    private bool _isUploading;

    [ObservableProperty]
    private string _statusText = "";

    public event Action? BackRequested;

    public AccountSkinViewModel(
        SkinService skinService,
        INotificationService notificationService,
        IDialogService dialogService)
    {
        _skinService = skinService;
        _notificationService = notificationService;
        _dialogService = dialogService;
    }

    public void LoadAccount(Account account)
    {
        _account = account;
        PlayerName = account.Username;
        SelectedFilePath = "";
        StatusText = "";
        _ = LoadSkinPreviewAsync();
    }

    [RelayCommand]
    private void GoBack() => BackRequested?.Invoke();

    private async Task LoadSkinPreviewAsync()
    {
        if (_account == null) return;

        lock (_loadLock)
        {
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = new CancellationTokenSource();
        }
        var ct = _loadCts.Token;

        IsLoading = true;
        StatusText = "正在加载皮肤...";

        try
        {
            ct.ThrowIfCancellationRequested();

            var info = await _skinService.GetSkinTextureInfoAsync(_account.UUID, ct);
            if (info == null)
            {
                AvatarSource = null;
                StatusText = "该玩家未设置皮肤或服务器无响应。";
                return;
            }

            SelectedVariant = info.Model;

            byte[]? pngData = await _skinService.DownloadSkinPngAsync(info.Url, ct);
            if (pngData == null || pngData.Length == 0)
            {
                AvatarSource = null;
                StatusText = "无法下载皮肤文件。";
                return;
            }

            ct.ThrowIfCancellationRequested();

            var avatar = await Application.Current.Dispatcher.InvokeAsync(
                () => SkinRenderer.RenderAvatar(pngData));

            AvatarSource = avatar;
            StatusText = $"皮肤已加载 — 模型: {(info.Model == "slim" ? "Slim (Alex)" : "Classic (Steve)")}";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusText = $"加载失败：{ex.Message}";
            _notificationService.Show($"皮肤加载失败：{ex.Message}", NotificationType.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task BrowseSkinFile()
    {
        var path = await _dialogService.ShowOpenFileDialogAsync(
            "选择皮肤文件", "PNG 图片|*.png|所有文件|*.*");
        if (path != null)
            SelectedFilePath = path;
    }

    [RelayCommand]
    private async Task UploadSkin()
    {
        if (_account == null) return;
        if (string.IsNullOrEmpty(SelectedFilePath))
        {
            _notificationService.Show("请先选择皮肤文件。", NotificationType.Warning);
            return;
        }
        if (!File.Exists(SelectedFilePath))
        {
            _notificationService.Show("所选文件不存在。", NotificationType.Warning);
            return;
        }

        byte[] pngData;
        try { pngData = await File.ReadAllBytesAsync(SelectedFilePath); }
        catch (Exception ex)
        {
            _notificationService.Show($"无法读取文件：{ex.Message}", NotificationType.Error);
            return;
        }

        if (pngData.Length < 8 || pngData[0] != 0x89 || pngData[1] != 'P'
            || pngData[2] != 'N' || pngData[3] != 'G')
        {
            _notificationService.Show("所选文件不是有效的 PNG 图片。", NotificationType.Warning);
            return;
        }

        IsUploading = true;
        StatusText = "正在上传皮肤...";

        try
        {
            var result = await _skinService.UploadSkinAsync(
                _account, pngData, SelectedVariant, () => { });

            if (result.Success)
            {
                _notificationService.Show("皮肤上传成功！", NotificationType.Success);
                StatusText = "皮肤已更换，正在刷新预览...";
                await Task.Delay(1500, _loadCts?.Token ?? CancellationToken.None);
                await LoadSkinPreviewAsync();
            }
            else
            {
                _notificationService.Show($"皮肤上传失败：{result.ErrorMessage}", NotificationType.Error);
                StatusText = $"上传失败：{result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            _notificationService.Show($"皮肤上传失败：{ex.Message}", NotificationType.Error);
            StatusText = $"上传失败：{ex.Message}";
        }
        finally
        {
            IsUploading = false;
        }
    }

    [RelayCommand]
    private async Task ResetSkin()
    {
        if (_account == null) return;

        if (!await _dialogService.ShowConfirmAsync(
            "确定要重置皮肤为默认吗？\n重置后将恢复为 Steve 或 Alex 默认外观。", "确认重置"))
            return;

        IsUploading = true;
        StatusText = "正在重置皮肤...";

        try
        {
            var result = await _skinService.ResetSkinAsync(_account, () => { });

            if (result.Success)
            {
                _notificationService.Show("皮肤已重置为默认。", NotificationType.Success);
                StatusText = "皮肤已重置，正在刷新预览...";
                await Task.Delay(1500, _loadCts?.Token ?? CancellationToken.None);
                await LoadSkinPreviewAsync();
            }
            else
            {
                _notificationService.Show($"重置失败：{result.ErrorMessage}", NotificationType.Error);
                StatusText = $"重置失败：{result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            _notificationService.Show($"重置失败：{ex.Message}", NotificationType.Error);
            StatusText = $"重置失败：{ex.Message}";
        }
        finally
        {
            IsUploading = false;
        }
    }
}
