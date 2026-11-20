using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace AutoTest.UI.Design;

/// <summary>Single light, dense Windows product design vocabulary.</summary>
public static class UiKit
{
    public static readonly Brush Canvas = Brush("#F4F7FB");
    public static readonly Brush White = Brushes.White;
    public static readonly Brush Ink = Brush("#213044");
    public static readonly Brush Muted = Brush("#62758B");
    public static readonly Brush Accent = Brush("#2064AD");
    public static readonly Brush Outline = Brush("#DAE3EF");
    public static readonly Brush Soft = Brush("#EAF2FB");
    public static Brush Brush(string value) =>
        (Brush)new BrushConverter().ConvertFromString(value)!;
    public static TextBlock Text(string value, double size = 13, bool strong = false, Brush? color = null) =>
        new() { Text = value, FontSize = size, FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = color ?? Ink, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    public static Button Button(string title, Action action, bool primary = false)
    {
        var b = new Button
        {
            Content = title, Padding = new Thickness(13, 7, 13, 7),
            Background = primary ? Accent : White, Foreground = primary ? White : Ink,
            BorderBrush = primary ? Accent : Outline, BorderThickness = new Thickness(1),
            MinHeight = 34, Cursor = System.Windows.Input.Cursors.Hand,
            Margin = new Thickness(0, 0, 7, 6)
        };
        b.Click += (_, _) => action();
        return b;
    }
    public static Border Card(UIElement child, Thickness? padding = null) => new()
    {
        Background = White, BorderBrush = Outline, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(7), Padding = padding ?? new Thickness(15), Child = child
    };
    public static StackPanel Stack(Orientation o = Orientation.Vertical, double gap = 0)
    {
        return new StackPanel { Orientation = o };
    }
    public static DockPanel Header(string title, string detail)
    {
        var p = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var s = new StackPanel();
        s.Children.Add(Text(title, 22, true));
        s.Children.Add(Text(detail, 12, false, Muted));
        p.Children.Add(s);
        return p;
    }
    public static WrapPanel Actions(params Button[] buttons)
    {
        var p = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        foreach (var b in buttons) p.Children.Add(b);
        return p;
    }
    public static Grid Columns(params double[] widths)
    {
        var g = new Grid();
        foreach (var width in widths) g.ColumnDefinitions.Add(new ColumnDefinition
        { Width = width <= 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(width) });
        return g;
    }
    public static Grid Rows(params double[] heights)
    {
        var g = new Grid();
        foreach (var height in heights) g.RowDefinitions.Add(new RowDefinition
        { Height = height <= 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(height) });
        return g;
    }
    public static TextBox Input(string text = "", bool multiline = false) => new()
    {
        Text = text, Padding = new Thickness(9, 7, 9, 7),
        Background = Brushes.White, BorderBrush = Outline,
        MinHeight = multiline ? 72 : 34,
        AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
        VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled
    };
    public static FrameworkElement Field(string caption, UIElement editor)
    {
        var p = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        p.Children.Add(Text(caption, 12, false, Muted));
        if (editor is FrameworkElement e) e.Margin = new Thickness(0, 5, 0, 0);
        p.Children.Add(editor);
        return p;
    }
    public static DataGrid Table(object data, params (string Header, string Property, double Width)[] columns)
    {
        var dg = new DataGrid
        {
            ItemsSource = (System.Collections.IEnumerable)data,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            IsReadOnly = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            RowHeight = 35,
            ColumnHeaderHeight = 36,
            FontSize = 12,
            Background = White,
            BorderBrush = Outline,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush = Brush("#EEF2F7"),
            EnableRowVirtualization = true,
            EnableColumnVirtualization = true
        };
        foreach (var (title, prop, width) in columns)
        {
            dg.Columns.Add(new DataGridTextColumn
            {
                Header = title,
                Binding = new Binding(prop) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus },
                Width = width <= 0 ? new DataGridLength(1, DataGridLengthUnitType.Star)
                    : new DataGridLength(width)
            });
        }
        return dg;
    }
    public static Border Empty(string title, string explanation)
    {
        var s = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, MaxWidth = 500, Margin = new Thickness(32) };
        s.Children.Add(Text(title, 19, true));
        var tip = Text(explanation, 13, false, Muted);
        tip.Margin = new Thickness(0, 10, 0, 0);
        s.Children.Add(tip);
        return Card(s, new Thickness(24));
    }
    public static Border Note(string content, bool warning = false) => new()
    {
        Background = warning ? Brush("#FFF4E8") : Soft,
        BorderBrush = warning ? Brush("#F2D5B3") : Brush("#D5E5F8"),
        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
        Padding = new Thickness(12, 9, 12, 9),
        Margin = new Thickness(0, 0, 0, 12),
        Child = Text(content, 12, false, warning ? Brush("#865B25") : Brush("#35608E"))
    };
    public static ScrollViewer Scroll(UIElement child) => new()
    {
        Content = child, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
    };
    public static void Disabled(Button button, string reason)
    {
        button.IsEnabled = false;
        button.ToolTip = reason;
    }
}
