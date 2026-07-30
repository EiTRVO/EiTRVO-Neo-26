using System.ComponentModel;
using System.Windows;
using EiTRVO.ProEngine.Orchestrators;
using EiTRVO.UI.ViewModels;

namespace EiTRVO.UI.Panels;

public partial class WizardWindow : Window
{
    private readonly WizardViewModel _viewModel;
    private bool _wizardCompleted;

    public WizardWindow(WizardViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        // Wire page DataContexts (they inherit from parent, but explicit is safer)
        page1.DataContext = viewModel;
        page2.DataContext = viewModel;
        page3.DataContext = viewModel;
        page4.DataContext = viewModel;

        // Listen for step changes to switch pages
        viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // ViewModel requests window close
        viewModel.RequestClose += completed =>
        {
            _wizardCompleted = completed;
            DialogResult = completed;
            Close();
        };

        // Prevent accidental close without save
        Closing += OnWizardClosing;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WizardViewModel.CurrentStepIndex))
        {
            SwitchToPage(_viewModel.CurrentStepIndex);
        }
    }

    private void SwitchToPage(int index)
    {
        page1.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        page2.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        page3.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        page4.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// When user clicks the window close button (X), skip without saving.
    /// The wizard will reappear on next launch.
    /// </summary>
    private void OnWizardClosing(object? sender, CancelEventArgs e)
    {
        // If the user explicitly chose Skip or Finish, DialogResult is already set.
        // If they clicked X, cancel closing without marking wizard as completed.
        if (DialogResult != true)
        {
            var result = MessageBox.Show(
                "确定要退出向导吗？\n\n您可以随时通过「跳过」按钮退出，下次启动时向导将不再显示。\n直接关闭窗口则下次启动时向导会再次出现。",
                "EiTRVO Neo — 退出向导",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                DialogResult = false;
            }
            else
            {
                e.Cancel = true;
            }
        }
    }
}
