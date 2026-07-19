using System.Windows;
using System.Windows.Controls;
using EiTRVO.ProEngine.ViewModels;

namespace EiTRVO.UI.Panels;

public partial class AccountPanel
{
    public AccountPanel()
    {
        InitializeComponent();
    }

    private void TxtYggdrasilPassword_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is AccountViewModel vm)
            vm.YggdrasilPassword = txtYggdrasilPassword.Password;
    }

    private void TxtUserCode_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var code = txtUserCode.Text;
        if (!string.IsNullOrWhiteSpace(code))
        {
            try { System.Windows.Clipboard.SetText(code); }
            catch { }
        }
    }
}
