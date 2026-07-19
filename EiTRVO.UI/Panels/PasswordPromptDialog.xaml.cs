using System.Windows;

namespace EiTRVO.UI.Panels;

public partial class PasswordPromptDialog : Window
{
    public string Password => txtPassword.Password;

    /// <summary>
    /// 密码验证对话框（补传/解密/导出 .savrec 共用）
    /// </summary>
    /// <param name="actionTitle">操作标题（如 "解密存档"、"补传密钥到 OneDrive"）</param>
    /// <param name="saveName">存档名称</param>
    /// <param name="instanceName">实例名称</param>
    public PasswordPromptDialog(string actionTitle, string saveName, string instanceName)
    {
        InitializeComponent();
        Title = actionTitle;
        lblTitle.Text = actionTitle;
        lblInfo.Text = $"存档: {saveName}\n实例: {instanceName}";
        btnConfirm.Content = actionTitle switch
        {
            "解密存档" => "解锁",
            "补传密钥到 OneDrive" => "上传",
            "导出恢复文件" => "导出",
            _ => "确认"
        };
    }

    private void BtnConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(Password))
        {
            lblError.Text = "密码不能为空。";
            lblError.Visibility = Visibility.Visible;
            return;
        }

        lblError.Visibility = Visibility.Collapsed;
        DialogResult = true;
        Close();
    }
}
