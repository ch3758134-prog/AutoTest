using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AutoTest.TestHiFi;

internal sealed class NameDialog : Window
{
    private readonly TextBox _text;
    private readonly ComboBox? _type;
    public string Value => _text.Text.Trim();
    public string Kind => _type?.SelectedItem is ComboBoxItem item && item.Tag is string tag ? tag : "NOTE";

    public NameDialog(Window owner, string title, string description,
                      string defaultValue, bool chooseKind = false)
    {
        Owner = owner;
        Title = title;
        Width = 440;
        Height = chooseKind ? 280 : 225;
        MinWidth = 400;
        MaxHeight = 310;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White;
        FontFamily = new FontFamily("Microsoft YaHei UI");
        var grid = new Grid { Margin = new Thickness(22, 18, 22, 18) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (chooseKind) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap,
            FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(35, 50, 70)),
            Margin = new Thickness(0, 0, 0, 14) };
        Grid.SetRow(heading, 0);
        grid.Children.Add(heading);
        _text = new TextBox { Text = defaultValue, FontSize = 13, MinHeight = 35,
            Padding = new Thickness(10, 7, 10, 7),
            BorderBrush = new SolidColorBrush(Color.FromRgb(194, 208, 225)),
            Margin = new Thickness(0, 0, 0, 12) };
        Grid.SetRow(_text, 1); grid.Children.Add(_text);

        if (chooseKind)
        {
            _type = new ComboBox { Height = 35, Margin = new Thickness(0, 0, 0, 10) };
            foreach (var kind in WorkspaceStorage.AssetKinds)
            {
                var asset = new UiAsset { Kind = kind };
                _type.Items.Add(new ComboBoxItem { Content = asset.KindLabel, Tag = kind });
            }
            _type.SelectedIndex = 0;
            Grid.SetRow(_type, 2); grid.Children.Add(_type);
        }
        var actions = new StackPanel { Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "取消", Width = 83, Height = 34,
            Margin = new Thickness(0, 0, 9, 0), IsCancel = true };
        var ok = new Button { Content = "确定", Width = 88, Height = 34, IsDefault = true,
            Background = new SolidColorBrush(Color.FromRgb(23, 105, 210)),
            Foreground = Brushes.White, BorderThickness = new Thickness(0) };
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(Value) ||
                Value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Value.Length > 100)
            {
                MessageBox.Show(this, "名称不能为空、超过 100 个字符或包含非法文件名字符。",
                    "输入无效", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            DialogResult = true;
        };
        actions.Children.Add(cancel); actions.Children.Add(ok);
        Grid.SetRow(actions, grid.RowDefinitions.Count - 1);
        grid.Children.Add(actions);
        Content = grid;
        Loaded += (_, _) => { _text.Focus(); _text.SelectAll(); };
    }
}
