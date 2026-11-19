using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AutoTest.Desktop.Rebuild;

public static class UiVisual
{
    public static readonly Brush Back = Brush("#F3F6FB");
    public static readonly Brush Surface = Brush("#FFFFFF");
    public static readonly Brush Ink = Brush("#24364F");
    public static readonly Brush Muted = Brush("#718198");
    public static readonly Brush Line = Brush("#DFE6F0");
    public static readonly Brush Accent = Brush("#2469C9");
    public static readonly Brush SoftAccent = Brush("#ECF3FE");
    public static readonly Brush Pale = Brush("#F8FAFD");
    public static Brush Brush(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
    public static TextBlock Text(string value, double size = 13, bool strong = false, Brush? color = null)
        => new() { Text = value, FontSize = size, FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = color ?? Ink, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    public static TextBlock Title(string name, string detail = "")
        => Text(name, 20, true);

    public static Border Box(UIElement child, Thickness? padding = null, Brush? background = null,
        CornerRadius? radius = null) => new()
        {
            Child = child, Background = background ?? Surface, Padding = padding ?? new Thickness(14),
            CornerRadius = radius ?? new CornerRadius(8), BorderBrush = Line,
            BorderThickness = new Thickness(1)
        };
    public static StackPanel Stack(bool horizontal = false, double gap = 0)
        => new() { Orientation = horizontal ? Orientation.Horizontal : Orientation.Vertical };
    public static Button Button(string text, Action action, bool primary = false, string? tooltip = null)
    {
        var b = new Button { Content = text, Padding = new Thickness(13, 7, 13, 7),
            Margin = new Thickness(0, 0, 7, 6), MinHeight = 34, Cursor = System.Windows.Input.Cursors.Hand,
            Foreground = primary ? Brushes.White : Ink,
            Background = primary ? Accent : Surface, BorderBrush = primary ? Accent : Line,
            BorderThickness = new Thickness(1), ToolTip = tooltip ?? text };
        b.Click += (_, _) => action();
        return b;
    }
    public static TextBox Input(string text = "", double width = double.NaN)
        => new() { Text = text, MinHeight = 34, Padding = new Thickness(8, 6, 8, 6),
            Width = width, Margin = new Thickness(0, 2, 0, 8), Background = Surface,
            Foreground = Ink, BorderBrush = Line, BorderThickness = new Thickness(1),
            VerticalContentAlignment = VerticalAlignment.Center };
    public static ComboBox Combo(double width = double.NaN)
        => new() { MinHeight = 34, Width = width, Padding = new Thickness(7, 4, 7, 4),
            Margin = new Thickness(0, 2, 7, 8), Background = Surface, Foreground = Ink,
            BorderBrush = Line };
    public static StackPanel Field(string title, UIElement editor)
    {
        var stack = Stack();
        stack.Margin = new Thickness(0, 0, 0, 7);
        stack.Children.Add(Text(title, 12, false, Muted));
        stack.Children.Add(editor);
        return stack;
    }
    public static Grid Columns(params GridLength[] widths)
    {
        var g = new Grid();
        foreach (var w in widths) g.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        return g;
    }
    public static GridLength Star(double v = 1) => new(v, GridUnitType.Star);
    public static ScrollViewer Scroll(UIElement child)
        => new() { Content = child, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    public static void At(Grid g, UIElement child, int col = 0, int row = 0)
    {
        Grid.SetColumn(child, col); Grid.SetRow(child, row); g.Children.Add(child);
    }
    public static Separator Rule()
        => new() { Background = Line, Margin = new Thickness(0, 9, 0, 9) };
    public static TextBlock Note(string body) => Text(body, 12, false, Muted);
    public static TextBlock Alert(string body) => Text(body, 12, false, Brush("#9A6828"));
    public static Border Empty(string title, string description)
    {
        var s = Stack();
        s.HorizontalAlignment = HorizontalAlignment.Center;
        s.VerticalAlignment = VerticalAlignment.Center;
        s.MaxWidth = 470;
        s.Children.Add(Text(title, 17, true));
        var msg = Note(description);
        msg.Margin = new Thickness(0, 8, 0, 0);
        s.Children.Add(msg);
        return Box(s, new Thickness(25));
    }
    public static DataGrid Table(object items)
        => new() { ItemsSource = (System.Collections.IEnumerable)items,
            AutoGenerateColumns = true, HeadersVisibility = DataGridHeadersVisibility.Column,
            CanUserAddRows = false, CanUserDeleteRows = false, IsReadOnly = false,
            RowHeight = 34, ColumnHeaderHeight = 37, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush = Line, VerticalGridLinesBrush = Line, BorderBrush = Line,
            Background = Brushes.White, AlternatingRowBackground = Pale,
            RowHeaderWidth = 0, SelectionMode = DataGridSelectionMode.Single,
            EnableRowVirtualization = true, EnableColumnVirtualization = true,
            Margin = new Thickness(0, 7, 0, 5)
        };
    public static Border Section(string title, UIElement content, string? note = null)
    {
        var s = Stack();
        var header = Text(title, 15, true);
        header.Margin = new Thickness(0, 0, 0, 10);
        s.Children.Add(header);
        if (note is not null)
        {
            var n = Note(note);
            n.Margin = new Thickness(0, 0, 0, 10);
            s.Children.Add(n);
        }
        s.Children.Add(content);
        return Box(s);
    }
    public static StackPanel Toolbar(params UIElement[] items)
    {
        var s = Stack(true);
        s.Margin = new Thickness(0, 0, 0, 12);
        foreach (var e in items) s.Children.Add(e);
        return s;
    }
    public static void AddRow(Panel s, string title, string value)
    {
        var row = Stack(true);
        row.Children.Add(Text(title + "    ", 12, false, Muted));
        row.Children.Add(Text(value, 12));
        s.Children.Add(row);
    }
}
