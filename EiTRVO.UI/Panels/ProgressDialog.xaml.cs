using System.Windows;

namespace EiTRVO.UI.Panels;

public partial class ProgressDialog : Window
{
    public ProgressDialog(string title)
    {
        InitializeComponent();
        lblTitle.Text = title;
    }

    public void Report(int done, int total)
    {
        if (total > 0)
        {
            progressBar.IsIndeterminate = false;
            progressBar.Maximum = total;
            progressBar.Value = done;
            lblDetail.Text = $"正在处理 {done} / {total} 个文件...";
        }
        else
        {
            progressBar.IsIndeterminate = true;
            lblDetail.Text = "正在处理...";
        }
    }
}
