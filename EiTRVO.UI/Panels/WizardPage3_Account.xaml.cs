using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EiTRVO.UI.ViewModels;

namespace EiTRVO.UI.Panels;

public partial class WizardPage3_Account : UserControl
{
    public WizardPage3_Account()
    {
        InitializeComponent();

        Loaded += (s, e) =>
        {
            if (DataContext is WizardViewModel vm)
            {
                vm.OnAccountPageActivated();
            }
        };
    }

    /// <summary>Copy user code to clipboard on click.</summary>
    private void UserCode_Click(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is WizardViewModel vm && vm.CopyCodeCommand.CanExecute(null))
        {
            vm.CopyCodeCommand.Execute(null);
        }
    }

    /// <summary>PasswordBox can't bind TwoWay in WPF — sync manually.</summary>
    private void TxtYggdrasilPassword_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is WizardViewModel vm)
        {
            vm.SetYggdrasilPassword(txtYggdrasilPassword.Password);
        }
    }
}
