using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AutoTest.UiPreview;

/// <summary>Small modal prompt for names only; all actual validation stays in Project services.</summary>
public static class EditorTextPrompt
{
    public static string? Ask(Window owner, string title, string label)
    {
        var box = new TextBox { MinHeight = 28, Margin = new Thickness(0, 7, 0, 13) };
        var dialog = new Window
        {
            Title = title, Owner = owner, Width = 380, Height = 168,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = System.Windows.Media.Brushes.White, ShowInTaskbar = false
        };
        var stack = new StackPanel { Margin = new Thickness(18, 12, 18, 12) };
        stack.Children.Add(new TextBlock { Text = label });
        stack.Children.Add(box);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "取消", Width = 74, Height = 27, IsCancel = true,
            Margin = new Thickness(0, 0, 7, 0) };
        var ok = new Button { Content = "确定", Width = 74, Height = 27, IsDefault = true };
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(box.Text)) { box.Focus(); return; }
            dialog.DialogResult = true;
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        stack.Children.Add(buttons);
        dialog.Content = stack;
        dialog.Loaded += (_, _) => box.Focus();
        return dialog.ShowDialog() == true ? box.Text.Trim() : null;
    }
}
