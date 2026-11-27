using System.Windows;

namespace AutoTest.Desktop.Projects;

public partial class ResourceNameDialog : Window
{
    public string ResourceName => ResourceNameInput.Text.Trim();

    public ResourceNameDialog(string title, string initialName)
    {
        InitializeComponent();
        Loaded += (_, _) => AutoTest.Desktop.Shell.DesktopWindowSizing.FitToVisibleWorkspace(this);
        Title = title;
        Prompt.Text = title;
        ResourceNameCaption.Text = title.Contains("工程", StringComparison.Ordinal) ? "工程名称" :
            title.Contains("DBC", StringComparison.OrdinalIgnoreCase) ? "DBC 名称" : "名称";
        ResourceNameInput.Text = initialName;
        Loaded += (_, _) => { ResourceNameInput.Focus(); ResourceNameInput.SelectAll(); };
    }


    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        var text = ResourceNameInput.Text;
        if (string.IsNullOrWhiteSpace(text) || text != text.Trim() ||
            text.Length > 100 || text.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
        {
            ErrorText.Text = "请输入有效名称，不能包含 Windows 文件名非法字符。";
            return;
        }
        DialogResult = true;
    }
}
