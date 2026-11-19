using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AutoTest.UdsLab;

/// <summary>Human must type the transport fence's exact 15-byte frame token.</summary>
public sealed class FrameConsentDialog : Window
{
    private readonly TextBox _entered = new() { FontFamily = new FontFamily("Consolas"), FontSize = 13 };
    private readonly string _expected;
    public string? Confirmation { get; private set; }

    public FrameConsentDialog(Window owner, string expected, string frameDescription)
    {
        Owner = owner;
        _expected = expected;
        Title = "实体 CAN 逐帧授权";
        Width = 800; Height = 310; MinWidth = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanMinimize;
        Background = Brushes.White;

        var content = new StackPanel { Margin = new Thickness(22) };
        content.Children.Add(new TextBlock
        {
            Text = "正在向真实 ECU 发送 CAN 帧。请核对地址、数据和操作目的。",
            FontSize = 16, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock
        {
            Text = "当前帧：" + frameDescription,
            Margin = new Thickness(0, 12, 0, 9), TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock
        {
            Text = "必须准确输入以下完整确认文本（区分大小写）。每一帧均需单独授权：",
            Foreground = Brushes.DarkSlateGray, TextWrapping = TextWrapping.Wrap
        });
        var expectedText = new TextBox
        {
            Text = _expected, IsReadOnly = true, FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 5)
        };
        content.Children.Add(expectedText);
        content.Children.Add(_entered);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 5, 0, 0)
        };
        var cancel = new Button { Content = "拒绝发送", IsCancel = true };
        var approve = new Button { Content = "确认此帧", IsDefault = true };
        cancel.Click += (_, _) => { DialogResult = false; };
        approve.Click += (_, _) =>
        {
            if (_entered.Text != _expected)
            {
                MessageBox.Show(this, "确认内容不匹配，实体帧未授权。", "授权被拒绝",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Confirmation = _entered.Text;
            DialogResult = true;
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(approve);
        content.Children.Add(buttons);
        Content = content;
        Loaded += (_, _) => _entered.Focus();
    }
}
