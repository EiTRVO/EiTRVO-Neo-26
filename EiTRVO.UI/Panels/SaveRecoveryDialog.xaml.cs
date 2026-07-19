using System.Windows;

namespace EiTRVO.UI.Panels;

public enum RecoveryMethod
{
    KeyBackup,
    RecoveryFile
}

public partial class SaveRecoveryDialog : Window
{
    public RecoveryMethod SelectedMethod =>
        rbRecoveryFile.IsChecked == true ? RecoveryMethod.RecoveryFile : RecoveryMethod.KeyBackup;

    public SaveRecoveryDialog(string? msAccountEmail)
    {
        InitializeComponent();
        if (!string.IsNullOrEmpty(msAccountEmail))
            lblMsAccount.Text = $"当前已登录: {msAccountEmail}";
        else
            rbRecoveryFile.IsEnabled = false;
    }

    private void BtnContinue_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
