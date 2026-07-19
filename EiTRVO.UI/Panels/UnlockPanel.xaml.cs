using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using EiTRVO.ProEngine.Models;

namespace EiTRVO.UI.Panels;

public partial class UnlockPanel : Window
{
    public string Password => txtPassword.Password;
    public bool ForgotPasswordClicked { get; private set; }

    private readonly List<(string Path, string SaveName, SaveLockMetadata Meta)> _metaList;

    public UnlockPanel(
        List<(string Path, string SaveName, SaveLockMetadata Meta)> metaList,
        bool hasMsAccount, string msAccountEmail)
    {
        InitializeComponent();

        _metaList = metaList;

        if (metaList.Count == 1)
        {
            // 单存档模式
            var (_, saveName, meta) = metaList[0];
            lblSaveName.Text = saveName;
            lblInstanceName.Text = meta.InstanceName;
            lblLockMode.Text = meta.LockMode == SaveLockMode.Permanent
                ? "永久加密" : "一次性加密";
            lblLockedAt.Text = meta.CreatedAt != System.DateTimeOffset.MinValue
                ? meta.CreatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm")
                : "";
            lblBoundAccount.Text = !string.IsNullOrEmpty(msAccountEmail)
                ? msAccountEmail : "未绑定";
            pnlSingleSave.Visibility = Visibility.Visible;
            lstMultipleSaves.Visibility = Visibility.Collapsed;

            // 密码提示
            if (!string.IsNullOrEmpty(meta.PasswordHint))
            {
                lblPasswordHint.Text = $"密码提示: {meta.PasswordHint}";
                lblPasswordHint.Visibility = Visibility.Visible;
            }
        }
        else
        {
            // 多存档模式
            pnlSingleSave.Visibility = Visibility.Collapsed;
            lstMultipleSaves.Visibility = Visibility.Visible;

            Title = $"存档已锁定 ({metaList.Count} 个存档)";
            var items = metaList.Select(m => new SaveSelectItem
            {
                Path = m.Path,
                SaveName = m.SaveName,
                DisplayText = m.SaveName,
                DetailText = $"{m.Meta.InstanceName}  ·  {m.Meta.LockMode switch
                {
                    SaveLockMode.Permanent => "永久",
                    _ => "一次性"
                }}  ·  {m.Meta.CreatedAt.LocalDateTime:yyyy-MM-dd HH:mm}",
                IsSelected = true
            }).ToList();

            lstMultipleSaves.ItemsSource = new ObservableCollection<SaveSelectItem>(items);
        }

        // 忘记密码按钮：仅在有 MS 账户时显示
        btnForgotPassword.Visibility = hasMsAccount
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BtnForgotPassword_Click(object sender, RoutedEventArgs e)
    {
        ForgotPasswordClicked = true;
        DialogResult = false;
        Close();
    }

    private void BtnUnlock_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(Password))
        {
            lblError.Text = "密码不能为空。";
            lblError.Visibility = Visibility.Visible;
            return;
        }

        lblError.Visibility = Visibility.Collapsed;
        ForgotPasswordClicked = false;
        DialogResult = true;
        Close();
    }

    /// <summary>获取多存档模式中选中的存档路径</summary>
    public string[] GetSelectedPaths()
    {
        if (lstMultipleSaves.Visibility != Visibility.Visible)
            return _metaList.Select(m => m.Path).ToArray();

        var items = lstMultipleSaves.ItemsSource as ObservableCollection<SaveSelectItem>;
        return items?.Where(i => i.IsSelected).Select(i => i.Path).ToArray() ?? [];
    }
}

/// <summary>多存档列表项</summary>
public class SaveSelectItem : INotifyPropertyChanged
{
    public string Path { get; set; } = "";
    public string SaveName { get; set; } = "";
    public string DisplayText { get; set; } = "";
    public string DetailText { get; set; } = "";

    private bool _isSelected = true;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
