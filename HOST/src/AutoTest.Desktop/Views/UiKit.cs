using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace AutoTest.Desktop.Views;

public static class UiKit
{
    public static readonly Brush Canvas = new SolidColorBrush(Color.FromRgb(243,246,250));
    public static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(37,103,175));
    public static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(99,116,139));
    public static readonly Brush Line = new SolidColorBrush(Color.FromRgb(220,229,240));
    public static readonly Brush White = Brushes.White;
    public static readonly Brush Green = new SolidColorBrush(Color.FromRgb(35,123,98));

    public static Button Button(string label, RoutedEventHandler click, bool primary = false)
    {
        var b = new Button { Content = label, ToolTip = label };
        if (primary && Application.Current.TryFindResource("PrimaryButton") is Style style) b.Style = style;
        b.Click += click;
        return b;
    }
    public static Border Card(UIElement child, Thickness? margin = null) => new()
    {
        Child = child, Background = Brushes.White,
        BorderBrush = Line, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6), Margin = margin ?? new Thickness(0)
    };
    public static TextBlock Heading(string title, int size = 18, Thickness? margin = null) => new()
    {
        Text = title, FontSize = size, FontWeight = FontWeights.SemiBold,
        Foreground = new SolidColorBrush(Color.FromRgb(33,51,75)),
        Margin = margin ?? new Thickness(0,0,0,9)
    };
    public static TextBlock Note(string text, int size = 12, Thickness? margin = null) => new()
    {
        Text = text, FontSize = size, Foreground = Muted,
        VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap,
        Margin = margin ?? new Thickness(0,0,0,6)
    };
    public static StackPanel Vertical(params UIElement[] children)
    {
        var p = new StackPanel();
        foreach (var child in children) p.Children.Add(child);
        return p;
    }
    public static WrapPanel Actions(params UIElement[] children)
    {
        var p = new WrapPanel { Margin = new Thickness(0,0,0,12) };
        foreach (var child in children) p.Children.Add(child);
        return p;
    }
    public static Grid Split(UIElement left, UIElement right, int leftWidth = 330)
    {
        var grid = new Grid { Background = Canvas };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(leftWidth), MinWidth=210 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1,GridUnitType.Star) });
        Grid.SetColumn(left, 0); grid.Children.Add(left);
        var split = new GridSplitter { HorizontalAlignment = HorizontalAlignment.Stretch, Width = 5,
            Background = Line, ShowsPreview = true };
        Grid.SetColumn(split, 1); grid.Children.Add(split);
        Grid.SetColumn(right, 2); grid.Children.Add(right);
        return grid;
    }
    public static FrameworkElement Scroll(UIElement view)
    {
        return new ScrollViewer { Content = view, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Background = Canvas };
    }
    public static DockPanel Page(string title, string note, UIElement body, params UIElement[] actions)
    {
        var page = new DockPanel { LastChildFill=true, Margin = new Thickness(16, 14, 16, 15) };
        var top = Vertical(Heading(title,19), Note(note), Actions(actions));
        DockPanel.SetDock(top,Dock.Top);
        page.Children.Add(top);
        page.Children.Add(body);
        return page;
    }
    public static TextBox Text(string value = "", string? tip = null)
    {
        var box = new TextBox { Text = value };
        if (tip is not null) box.ToolTip = tip;
        return box;
    }
    public static FrameworkElement Field(string caption, FrameworkElement input)
    {
        var stack = Vertical(Note(caption, 12, new Thickness(0,8,0,4)), input);
        return stack;
    }
    public static TextBox BoundText(object source, string property, bool readOnly = false)
    {
        var box = new TextBox { IsReadOnly = readOnly };
        box.SetBinding(TextBox.TextProperty,new Binding(property)
        { Source=source, Mode=readOnly?BindingMode.OneWay:BindingMode.TwoWay,
          UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged,
          ValidatesOnDataErrors=true, NotifyOnValidationError=true });
        return box;
    }
    public static CheckBox BoundCheck(object source, string property, string caption)
    {
        var c = new CheckBox { Content=caption, Margin=new Thickness(4,7,9,9) };
        c.SetBinding(CheckBox.IsCheckedProperty,new Binding(property) { Source=source, Mode=BindingMode.TwoWay });
        return c;
    }
    public static DataGrid Table<T>(IEnumerable<T> source, params (string Caption,string Property,int Width,bool Editable)[] columns)
    {
        var dg = new DataGrid
        {
            ItemsSource=source,
            SelectionMode=DataGridSelectionMode.Single,
            SelectionUnit=DataGridSelectionUnit.FullRow,
            IsReadOnly=false, CanUserReorderColumns=true,
            HeadersVisibility=DataGridHeadersVisibility.Column,
            AlternatingRowBackground=new SolidColorBrush(Color.FromRgb(247,250,253)),
            VerticalScrollBarVisibility=ScrollBarVisibility.Auto
        };
        foreach (var (caption,property,width,editable) in columns)
        {
            var binding=new Binding(property) { Mode=editable?BindingMode.TwoWay:BindingMode.OneWay,
                UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged };
            dg.Columns.Add(new DataGridTextColumn
            {
                Header=caption, Binding=binding, IsReadOnly=!editable,
                Width=width==0?new DataGridLength(1,DataGridLengthUnitType.Star):new DataGridLength(width)
            });
        }
        return dg;
    }
    public static bool Confirm(Window? owner, string text)
    {
        return MessageBox.Show(owner, text,"确认 UI 操作",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes;
    }
    public static string? AskText(Window? owner,string title,string field,string initial="")
    {
        var dialog=new Window
        {
            Title=title, Width=420, Height=195, ResizeMode=ResizeMode.NoResize,
            WindowStartupLocation=WindowStartupLocation.CenterOwner, Owner=owner,
            Background=Canvas
        };
        var root=new DockPanel { Margin=new Thickness(18) };
        dialog.Content=root;
        var actions=Actions();
        DockPanel.SetDock(actions,Dock.Bottom);
        root.Children.Add(actions);
        var input=Text(initial);
        root.Children.Add(Vertical(Heading(field,14),input));
        var result=false;
        actions.Children.Add(Button("取消",(_,_)=>dialog.Close()));
        actions.Children.Add(Button("确定",(_,_)=>{ if (string.IsNullOrWhiteSpace(input.Text)) return; result=true;dialog.Close(); },true));
        dialog.Loaded+=(_,_)=>{input.Focus();input.SelectAll();};
        dialog.ShowDialog();
        return result?input.Text.Trim():null;
    }
    public static Grid ThreeColumns(UIElement a,UIElement b,UIElement c,int left=280,int right=300)
    {
        var root=new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(left),MinWidth=180});
        root.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(5)});
        root.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        root.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(5)});
        root.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(right),MinWidth=180});
        foreach(var (control,column) in new[]{(a,0),(b,2),(c,4)})
        { Grid.SetColumn(control,column);root.Children.Add(control); }
        foreach(var col in new[]{1,3})
        {
            var s=new GridSplitter{Width=5,HorizontalAlignment=HorizontalAlignment.Stretch,
                Background=Line,ShowsPreview=true};
            Grid.SetColumn(s,col);root.Children.Add(s);
        }
        return root;
    }
    public static Border Side(string title, UIElement content)
    {
        var p=new DockPanel();
        var h=Heading(title,14,new Thickness(12,12,6,10));
        DockPanel.SetDock(h,Dock.Top);p.Children.Add(h);
        p.Children.Add(content);
        return Card(p);
    }
    public static Border Empty(string heading,string description)
    {
        return Card(new StackPanel
        {
            HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,
            MaxWidth=540,Margin=new Thickness(30),
            Children={Heading(heading,18),Note(description,13)}
        });
    }
}
