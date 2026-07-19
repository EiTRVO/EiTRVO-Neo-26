using System.Windows;
using EiTRVO.ProEngine.Models;

namespace EiTRVO.UI.Panels;

public partial class SaveLockDialog : Window
{
    public string Password => txtPassword.Password;
    public string ConfirmPassword => txtConfirmPassword.Password;
    public SaveLockMode LockMode => rbPermanent.IsChecked == true
        ? SaveLockMode.Permanent : SaveLockMode.OneTime;
    public string PasswordHint => txtPasswordHint.Text.Trim();
    public bool SaveLocalBackup => chkLocalBackup.IsChecked == true;
    public bool ExportRecoveryFile => chkRecovery.IsChecked == true;

    public SaveLockDialog(string saveName, string? msAccountEmail, bool hasMsAccount)
    {
        InitializeComponent();
        lblTitle.Text = $"锁定存档: \"{saveName}\"";

        if (hasMsAccount && !string.IsNullOrEmpty(msAccountEmail))
        {
            pnlHasMsAccount.Visibility = Visibility.Visible;
            pnlNoMsAccount.Visibility = Visibility.Collapsed;
            chkRecovery.IsEnabled = true;
        }
        else
        {
            pnlHasMsAccount.Visibility = Visibility.Collapsed;
            pnlNoMsAccount.Visibility = Visibility.Visible;
            chkRecovery.IsChecked = false;
            chkRecovery.IsEnabled = false;
        }
    }

    private void BtnConfirm_Click(object sender, RoutedEventArgs e)
    {
        // Validate password length
        if (Password.Length < 6)
        {
            lblError.Text = "密码至少需要 6 个字符。";
            lblError.Visibility = Visibility.Visible;
            return;
        }

        // Validate passwords match
        if (Password != ConfirmPassword)
        {
            lblError.Text = "两次输入的密码不一致。";
            lblError.Visibility = Visibility.Visible;
            return;
        }

        lblError.Visibility = Visibility.Collapsed;
        DialogResult = true;
        Close();
    }
}
