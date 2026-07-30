using System.Collections.Specialized;
using System.Windows.Controls;
using EiTRVO.ProEngine.ViewModels;

namespace EiTRVO.UI.Panels;

public partial class ProgressPanel : UserControl
{
    public ProgressPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is ProgressViewModel vm)
        {
            vm.GameLogEntries.CollectionChanged += OnGameLogCollectionChanged;
        }
    }

    private void OnGameLogCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems?.Count > 0)
        {
            // 仅当用户位于或接近底部时才自动滚动
            if (runtimeLogScroller.VerticalOffset >= runtimeLogScroller.ScrollableHeight - 30)
            {
                runtimeLogScroller.ScrollToBottom();
            }
        }
    }
}
