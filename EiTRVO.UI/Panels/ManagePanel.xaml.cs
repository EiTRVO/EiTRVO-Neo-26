using System.Linq;
using System.Windows;
using EiTRVO.ProEngine.ViewModels;

namespace EiTRVO.UI.Panels;

public partial class ManagePanel
{
    public ManagePanel()
    {
        InitializeComponent();
    }

    private void LstManagedInstances_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void LstManagedInstances_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            var zipFile = files.FirstOrDefault(f => f.EndsWith(".zip", System.StringComparison.OrdinalIgnoreCase));
            if (zipFile != null && DataContext is ManageViewModel vm)
            {
                try { await vm.ImportPackAsync(zipFile); }
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"导入整合包失败: {ex}"); }
            }
        }
    }
}
