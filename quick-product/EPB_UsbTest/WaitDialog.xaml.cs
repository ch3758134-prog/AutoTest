using System.Windows;

namespace EPB_UsbTest;

public partial class WaitDialog : Window
{
    public int DurationMs { get; private set; }

    public WaitDialog(int durationMs)
    {
        InitializeComponent();
        DurationBox.Text = Math.Max(0, durationMs).ToString();
        Loaded += (_, _) => { DurationBox.Focus(); DurationBox.SelectAll(); };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(DurationBox.Text, out var value) || value < 0 || value > 3_600_000)
        {
            MessageBox.Show(this, "请输入 0 到 3600000 之间的毫秒数。", "用例间等待");
            return;
        }
        DurationMs = value;
        DialogResult = true;
    }
}
