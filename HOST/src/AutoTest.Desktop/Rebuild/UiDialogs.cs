using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AutoTest.Desktop.Rebuild;

public static class UiDialogs
{
    public static string? Text(Window owner, string title, string label, string initial = "")
    {
        var dialog = new Window
        {
            Title = title, Width = 450, Height = 228, MinWidth = 360, MinHeight = 210,
            Background = UiVisual.Back, FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 13, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize, Owner = owner, ShowInTaskbar = false
        };
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(UiVisual.Text(label, 15, true));
        var input = UiVisual.Input(initial);
        input.Margin = new Thickness(0, 15, 0, 16);
        panel.Children.Add(input);
        var status = UiVisual.Note("");
        panel.Children.Add(status);
        var actions = UiVisual.Stack(true);
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        actions.Margin = new Thickness(0, 15, 0, 0);
        actions.Children.Add(UiVisual.Button("取消", () => dialog.DialogResult = false));
        actions.Children.Add(UiVisual.Button("确定", () =>
        {
            if (string.IsNullOrWhiteSpace(input.Text))
            {
                status.Text = "名称不能为空。";
                input.Focus();
                return;
            }
            dialog.DialogResult = true;
        }, true));
        panel.Children.Add(actions);
        dialog.Content = panel;
        dialog.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        return dialog.ShowDialog() == true ? input.Text.Trim() : null;
    }
    public static bool Confirm(Window owner, string title, string message)
        => MessageBox.Show(owner, message, title, MessageBoxButton.YesNo,
            MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
}
