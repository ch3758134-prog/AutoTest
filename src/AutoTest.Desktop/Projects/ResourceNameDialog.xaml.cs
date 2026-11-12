using System.Windows;
using System.Windows.Input;

namespace AutoTest.Desktop.Projects;

public partial class ResourceNameDialog : Window
{
    public string ResourceName => ResourceNameInput.Text.Trim();

    public ResourceNameDialog(string title, string initialName)
    {
        InitializeComponent();
        Title = title;
        Prompt.Text = title;
        ResourceNameInput.Text = initialName;
        Loaded += (_, _) => { ResourceNameInput.Focus(); ResourceNameInput.SelectAll(); };
    }

    private void NameInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Confirm_Click(sender, new RoutedEventArgs()); e.Handled = true; }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        var text = ResourceNameInput.Text;
        if (string.IsNullOrWhiteSpace(text) || text != text.Trim() ||
            text.Length > 100 || text.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
        {
            ErrorText.Text = "请输入有效的资源名称，不能包含 Windows 文件名非法字符。";
            return;
        }
        DialogResult = true;
    }
}
