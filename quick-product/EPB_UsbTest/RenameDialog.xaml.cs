using System.Windows;

namespace EPB_UsbTest;

public partial class RenameDialog : Window
{
    public string Value => NameBox.Text;

    public RenameDialog(string value)
    {
        InitializeComponent();
        NameBox.Text = value;
        NameBox.SelectAll();
        Loaded += (_, _) => NameBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            MessageBox.Show(this, "名称不能为空。", "重命名");
            return;
        }
        DialogResult = true;
    }
}
