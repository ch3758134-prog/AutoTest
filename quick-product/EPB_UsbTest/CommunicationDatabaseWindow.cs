using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using EPB.Core.Communication;

namespace EPB_UsbTest;

/// <summary>
/// 通信数据库的独立编辑窗口。这里仅编辑报文/信号协议定义；运行时生成值和解析值
/// 仍由测试步骤负责，避免把一次测试的临时状态写回通信数据库。
/// </summary>
internal sealed class CommunicationDatabaseWindow : Window
{
    private readonly CanDatabase _database;
    private readonly Action _commit;
    private readonly Action _importDbc;
    private readonly Action _exportDbc;
    private readonly Action _importParseCsv;
    private readonly Action _exportParseCsv;
    private readonly Action _saveProject;
    private readonly Action _refreshProject;
    private readonly ListBox _messages = new();
    private readonly DataGrid _signals = new();
    private readonly TextBox _search = new();
    private readonly TextBox _name = new();
    private readonly TextBox _id = new();
    private readonly CheckBox _extended = new() { Content = "扩展帧" };
    private readonly TextBox _dlc = new();
    private readonly TextBox _cycle = new();
    private readonly ComboBox _sendType = new();
    private readonly TextBlock _messageEmptyState = new();
    private readonly TextBlock _editorEmptyState = new();
    private bool _loading;

    public CommunicationDatabaseWindow(
        Window owner,
        CanDatabase database,
        Action commit,
        Action importDbc,
        Action exportDbc,
        Action importParseCsv,
        Action exportParseCsv,
        Action saveProject,
        Action refreshProject)
    {
        Owner = owner;
        _database = database;
        _commit = commit;
        _importDbc = importDbc;
        _exportDbc = exportDbc;
        _importParseCsv = importParseCsv;
        _exportParseCsv = exportParseCsv;
        _saveProject = saveProject;
        _refreshProject = refreshProject;

        Title = "通信数据库 · 报文与信号定义";
        Width = 1440;
        Height = 900;
        MinWidth = 1180;
        MinHeight = 720;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = PaletteBrush("BgBrush", "#F5F7FA");
        FontFamily = new FontFamily("Microsoft YaHei UI, Microsoft YaHei");
        FontSize = 13;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);

        Content = Build();
        Loaded += (_, _) =>
        {
            FitToWorkArea();
            RefreshMessages();
            _messages.SelectedIndex = _database.Messages.Count > 0 ? 0 : -1;
        };
    }

    private UIElement Build()
    {
        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        toolbar.Children.Add(Button("导入 DBC…", (_, _) => ReopenAfter(_importDbc)));
        toolbar.Children.Add(Button("导出 DBC…", (_, _) => _exportDbc()));
        toolbar.Children.Add(Button("导入解析 CSV…", (_, _) => ReopenAfter(_importParseCsv)));
        toolbar.Children.Add(Button("导出解析 CSV…", (_, _) => _exportParseCsv()));
        toolbar.Children.Add(Button("保存项目", (_, _) => _saveProject(), primary: true));
        toolbar.Children.Add(Button("刷新配置", (_, _) => ReopenAfter(_refreshProject)));
        Grid.SetRow(toolbar, 0);
        root.Children.Add(toolbar);

        var hint = new TextBlock
        {
            Text = $"{_database.Name} · {_database.Messages.Count} 个报文 · {_database.Messages.Sum(x => x.Signals.Count)} 个信号",
            Foreground = PaletteBrush("Text2Brush", "#687787"),
            Margin = new Thickness(2, 0, 0, 14),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(hint, 1);
        root.Children.Add(hint);

        var workspace = new Grid();
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280), MinWidth = 220 });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(7) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 600 });
        Grid.SetRow(workspace, 2);
        root.Children.Add(workspace);

        var browser = BuildBrowser();
        Grid.SetColumn(browser, 0);
        workspace.Children.Add(browser);

        var splitter = new GridSplitter
        {
            Width = 7,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = PaletteBrush("LineSoftBrush", "#EEF1F6")
        };
        Grid.SetColumn(splitter, 1);
        workspace.Children.Add(splitter);

        var editor = BuildEditor();
        Grid.SetColumn(editor, 2);
        workspace.Children.Add(editor);
        return root;
    }

    private UIElement BuildBrowser()
    {
        var root = new Grid { Margin = new Thickness(0, 0, 12, 0) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        StyleInput(_search);
        _search.Height = 32;
        _search.ToolTip = "搜索报文或信号定义：报文 ID、名称、信号名称、别名或描述";
        _search.TextChanged += (_, _) => RefreshMessages();
        Grid.SetRow(_search, 0);
        root.Children.Add(_search);

        _messages.Margin = new Thickness(0, 8, 0, 8);
        _messages.Background = PaletteBrush("CardBrush", "#FFFFFF");
        _messages.Foreground = PaletteBrush("TextBrush", "#34414F");
        _messages.BorderBrush = PaletteBrush("LineBrush", "#E4E8EF");
        _messages.BorderThickness = new Thickness(1);
        ScrollViewer.SetVerticalScrollBarVisibility(_messages, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(_messages, ScrollBarVisibility.Disabled);
        ScrollViewer.SetCanContentScroll(_messages, true);
        _messages.ItemContainerStyle = MessageItemStyle();
        _messages.SelectionChanged += (_, _) => LoadMessage((_messages.SelectedItem as DatabaseSearchResult)?.Message);
        _messages.ItemTemplate = MessageItemTemplate();

        var messageHost = new Grid();
        messageHost.Children.Add(_messages);
        _messageEmptyState.TextAlignment = TextAlignment.Center;
        _messageEmptyState.VerticalAlignment = VerticalAlignment.Center;
        _messageEmptyState.HorizontalAlignment = HorizontalAlignment.Center;
        _messageEmptyState.Margin = new Thickness(20);
        _messageEmptyState.TextWrapping = TextWrapping.Wrap;
        _messageEmptyState.Foreground = PaletteBrush("Text2Brush", "#687787");
        _messageEmptyState.IsHitTestVisible = false;
        messageHost.Children.Add(_messageEmptyState);
        Grid.SetRow(_messages, 1);
        Grid.SetRow(messageHost, 1);
        root.Children.Add(messageHost);

        var actions = new WrapPanel();
        actions.Children.Add(Button("新增报文", (_, _) => AddMessage(), primary: true));
        actions.Children.Add(Button("删除报文", (_, _) => DeleteMessage(), danger: true));
        Grid.SetRow(actions, 2);
        root.Children.Add(actions);
        return root;
    }

    private UIElement BuildEditor()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(160), MinHeight = 130 });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(7) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 150 });

        var title = new TextBlock
        {
            Text = "报文定义",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = PaletteBrush("TextBrush", "#34414F"),
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(title, 0);
        root.Children.Add(title);

        var metaCard = new Border
        {
            Background = PaletteBrush("CardBrush", "#FFFFFF"),
            BorderBrush = PaletteBrush("LineBrush", "#E4E8EF"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 8)
        };
        var fields = new Grid();
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 3; i++) fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        StyleInput(_name);
        StyleInput(_id);
        StyleInput(_dlc);
        StyleInput(_cycle);
        StyleInput(_extended);
        StyleInput(_sendType);
        AddField(fields, "报文名称", _name, 0, 0);
        AddField(fields, "报文 ID", _id, 0, 2);
        AddField(fields, "帧类型", _extended, 1, 0);
        AddField(fields, "DLC", _dlc, 1, 2);
        _sendType.ItemsSource = new[] { "仅发送一次", "开始周期发送", "停止周期发送" };
        AddField(fields, "默认周期", _cycle, 2, 0);
        AddField(fields, "发送方式", _sendType, 2, 2);
        var metaContent = new Grid();
        metaContent.Children.Add(fields);
        _editorEmptyState.Text = "暂无选中的报文\n请从左侧选择报文，或点击“新增报文”开始定义";
        _editorEmptyState.TextAlignment = TextAlignment.Center;
        _editorEmptyState.VerticalAlignment = VerticalAlignment.Center;
        _editorEmptyState.HorizontalAlignment = HorizontalAlignment.Center;
        _editorEmptyState.Margin = new Thickness(20);
        _editorEmptyState.TextWrapping = TextWrapping.Wrap;
        _editorEmptyState.Foreground = PaletteBrush("Text2Brush", "#687787");
        _editorEmptyState.IsHitTestVisible = false;
        Panel.SetZIndex(_editorEmptyState, 1);
        metaContent.Children.Add(_editorEmptyState);
        metaCard.Child = metaContent;
        Grid.SetRow(metaCard, 1);
        root.Children.Add(metaCard);

        var splitter = new GridSplitter
        {
            Height = 7,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = PaletteBrush("LineSoftBrush", "#EEF1F6")
        };
        Grid.SetRow(splitter, 2);
        root.Children.Add(splitter);

        var signalsRoot = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        signalsRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        signalsRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        signalsRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var bar = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new TextBlock
        {
            Text = "信号定义",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = PaletteBrush("TextBrush", "#34414F"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(label, 0);
        bar.Children.Add(label);
        var signalActions = new WrapPanel();
        signalActions.Children.Add(Button("新增信号", (_, _) => AddSignal(), primary: true));
        signalActions.Children.Add(Button("删除信号", (_, _) => DeleteSignal(), danger: true));
        signalActions.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(signalActions, 2);
        bar.Children.Add(signalActions);
        Grid.SetRow(bar, 0);
        signalsRoot.Children.Add(bar);

        ConfigureSignalGrid();
        Grid.SetRow(_signals, 1);
        signalsRoot.Children.Add(_signals);

        Grid.SetRow(signalsRoot, 3);
        root.Children.Add(signalsRoot);

        foreach (var input in new Control[] { _name, _id, _dlc, _cycle }) input.LostFocus += (_, _) => SaveMessage();
        _extended.Checked += (_, _) => SaveMessage();
        _extended.Unchecked += (_, _) => SaveMessage();
        _sendType.SelectionChanged += (_, _) => SaveMessage();
        return root;
    }

    private void ConfigureSignalGrid()
    {
        _signals.AutoGenerateColumns = false;
        _signals.CanUserAddRows = false;
        _signals.CanUserReorderColumns = true;
        _signals.CanUserResizeColumns = true;
        _signals.IsReadOnly = false;
        _signals.RowHeight = 30;
        _signals.ColumnHeaderHeight = 32;
        _signals.HeadersVisibility = DataGridHeadersVisibility.Column;
        _signals.GridLinesVisibility = DataGridGridLinesVisibility.All;
        _signals.Background = PaletteBrush("CardBrush", "#FFFFFF");
        _signals.Foreground = PaletteBrush("TextBrush", "#34414F");
        _signals.BorderBrush = PaletteBrush("LineBrush", "#E4E8EF");
        _signals.AlternatingRowBackground = PaletteBrush("AccentSoftBrush", "#EEF7FD");
        _signals.RowBackground = PaletteBrush("CardBrush", "#FFFFFF");
        ScrollViewer.SetVerticalScrollBarVisibility(_signals, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(_signals, ScrollBarVisibility.Auto);
        _signals.Resources[SystemColors.ControlBrushKey] = PaletteBrush("CardBrush", "#FFFFFF");
        _signals.Resources[SystemColors.WindowBrushKey] = PaletteBrush("CardBrush", "#FFFFFF");
        _signals.Resources[SystemColors.HighlightBrushKey] = PaletteBrush("AccentSoftBrush", "#EEF7FD");
        _signals.Resources[SystemColors.HighlightTextBrushKey] = PaletteBrush("TextBrush", "#34414F");

        TextColumn("名称", nameof(CanSignalDefinition.Name), 150);
        TextColumn("别名", nameof(CanSignalDefinition.DisplayName), 150);
        TextColumn("起始字节", nameof(CanSignalDefinition.StartByte), 72);
        TextColumn("起始位", nameof(CanSignalDefinition.StartBitInByte), 62);
        TextColumn("位宽", nameof(CanSignalDefinition.BitLength), 58);
        ComboColumn("字节序", nameof(CanSignalDefinition.ByteOrderDisplay), new[] { "小端序", "大端序" }, 88);
        TextColumn("系数", nameof(CanSignalDefinition.Factor), 70);
        TextColumn("偏移", nameof(CanSignalDefinition.Offset), 70);
        TextColumn("单位", nameof(CanSignalDefinition.Unit), 70);
        ComboColumn("显示方式", nameof(CanSignalDefinition.DisplayMode), new[] { "数值", "布尔", "枚举", "十六进制", "文本" }, 92);
        TextColumn("报警类型", nameof(CanSignalDefinition.AlarmType), 92);
        TextColumn("说明", nameof(CanSignalDefinition.Description), 220);
        _signals.Columns.Add(RuntimeValueColumn());
        _signals.CellEditEnding += (_, _) => Dispatcher.BeginInvoke(Commit, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void TextColumn(string title, string property, double width)
    {
        _signals.Columns.Add(new DataGridTextColumn
        {
            Header = title,
            Binding = new Binding(property)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.LostFocus
            },
            Width = new DataGridLength(width, DataGridLengthUnitType.Pixel)
        });
    }

    private void ComboColumn(string title, string property, IEnumerable<string> values, double width)
    {
        _signals.Columns.Add(new DataGridComboBoxColumn
        {
            Header = title,
            ItemsSource = values.ToArray(),
            SelectedItemBinding = new Binding(property)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            },
            Width = new DataGridLength(width, DataGridLengthUnitType.Pixel)
        });
    }

    private DataGridTemplateColumn RuntimeValueColumn()
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetValue(TextBlock.TextProperty, "—（未生成）");
        text.SetValue(TextBlock.ForegroundProperty, PaletteBrush("Text3Brush", "#9AA7B3"));
        text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        text.SetValue(TextBlock.MarginProperty, new Thickness(6, 0, 6, 0));

        return new DataGridTemplateColumn
        {
            Header = "当前值 / 生成数据",
            CellTemplate = new DataTemplate { VisualTree = text },
            IsReadOnly = true,
            Width = new DataGridLength(145, DataGridLengthUnitType.Pixel)
        };
    }

    private void AddField(Grid grid, string title, Control field, int row, int column)
    {
        var label = new TextBlock
        {
            Text = title,
            Foreground = PaletteBrush("Text2Brush", "#687787"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 8, 4)
        };
        Grid.SetRow(label, row);
        Grid.SetColumn(label, column);
        grid.Children.Add(label);

        field.MinWidth = 120;
        field.Height = 30;
        field.Margin = new Thickness(0, 4, 18, 4);
        field.Padding = new Thickness(7, 3, 7, 3);
        Grid.SetRow(field, row);
        Grid.SetColumn(field, column + 1);
        grid.Children.Add(field);
    }

    private void RefreshMessages(CanMessageDefinition? preserve = null)
    {
        var selected = preserve ?? (_messages.SelectedItem as DatabaseSearchResult)?.Message;
        var query = _search.Text.Trim();
        _messages.ItemsSource = _database.Messages
            .OrderBy(x => x.Id)
            .Where(x => CanDefinitionDisplay.Matches(x, query))
            .Select(x => new DatabaseSearchResult(x))
            .ToList();

        var hasMessages = _messages.Items.Count > 0;
        _messageEmptyState.Text = _database.Messages.Count == 0
            ? "暂无报文定义\n请点击“新增报文”或导入 DBC"
            : "没有匹配的报文\n请清除筛选条件或调整名称 / ID";
        _messageEmptyState.Visibility = hasMessages ? Visibility.Collapsed : Visibility.Visible;

        var selectedResult = _messages.Items.Cast<DatabaseSearchResult>()
            .FirstOrDefault(x => ReferenceEquals(x.Message, selected));
        if (selectedResult is not null)
            _messages.SelectedItem = selectedResult;
        else if (_messages.Items.Count > 0)
            _messages.SelectedIndex = 0;
        else
            LoadMessage(null);
    }

    private void LoadMessage(CanMessageDefinition? message)
    {
        _loading = true;
        try
        {
            var enabled = message is not null;
            _editorEmptyState.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
            foreach (var control in new Control[] { _name, _id, _extended, _dlc, _cycle, _sendType, _signals })
                control.IsEnabled = enabled;

            if (message is null)
            {
                _name.Text = _id.Text = _dlc.Text = _cycle.Text = "";
                _sendType.SelectedItem = null;
                _signals.ItemsSource = null;
                return;
            }

            _name.Text = message.Name;
            _id.Text = $"0x{message.Id:X}";
            _extended.IsChecked = message.Extended;
            _dlc.Text = message.Dlc.ToString(CultureInfo.InvariantCulture);
            _cycle.Text = message.CycleTimeMs?.ToString(CultureInfo.InvariantCulture) ?? "";
            _sendType.SelectedItem = message.SendType;
            _signals.ItemsSource = message.Signals;
        }
        finally
        {
            _loading = false;
        }
    }

    private void SaveMessage()
    {
        var message = (_messages.SelectedItem as DatabaseSearchResult)?.Message;
        if (_loading || message is null) return;

        message.Name = string.IsNullOrWhiteSpace(_name.Text) ? "未命名报文" : _name.Text.Trim();
        if (TryParseMessageId(_id.Text, out var id)) message.Id = id;
        message.Extended = _extended.IsChecked == true;
        if (int.TryParse(_dlc.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dlc))
            message.Dlc = Math.Clamp(dlc, 0, 64);
        message.CycleTimeMs = int.TryParse(_cycle.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cycle) && cycle > 0 ? cycle : null;
        message.SendType = _sendType.SelectedItem?.ToString() ?? "仅发送一次";
        Commit();
        RefreshMessages(message);
    }

    private static bool TryParseMessageId(string text, out uint id)
    {
        var value = text.Trim();
        var isHex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        if (isHex) value = value[2..];
        return uint.TryParse(value, isHex ? NumberStyles.HexNumber : NumberStyles.Integer, CultureInfo.InvariantCulture, out id);
    }

    private void AddMessage()
    {
        var count = _database.Messages.Count + 1;
        var message = new CanMessageDefinition { Name = $"新报文 {count}", Dlc = 8, SendType = "仅发送一次" };
        _database.Messages.Add(message);
        Commit();
        RefreshMessages(message);
    }

    private void DeleteMessage()
    {
        var message = (_messages.SelectedItem as DatabaseSearchResult)?.Message;
        if (message is null) return;
        if (MessageBox.Show(this, $"删除报文“{message.Name}”及其 {message.Signals.Count} 个信号？", "删除报文", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        _database.Messages.Remove(message);
        Commit();
        RefreshMessages();
    }

    private void AddSignal()
    {
        var message = (_messages.SelectedItem as DatabaseSearchResult)?.Message;
        if (message is null) return;
        var signal = new CanSignalDefinition
        {
            Name = "新信号",
            DisplayName = "",
            BitLength = 1,
            DisplayMode = "数值",
            AlarmType = ""
        };
        message.Signals.Add(signal);
        _signals.ItemsSource = null;
        _signals.ItemsSource = message.Signals;
        _signals.SelectedItem = signal;
        Commit();
    }

    private void DeleteSignal()
    {
        var message = (_messages.SelectedItem as DatabaseSearchResult)?.Message;
        if (message is null || _signals.SelectedItem is not CanSignalDefinition signal) return;
        message.Signals.Remove(signal);
        _signals.ItemsSource = null;
        _signals.ItemsSource = message.Signals;
        Commit();
    }

    private void Commit()
    {
        var invalid = _database.ValidateSignalNames();
        if (invalid.Count > 0)
        {
            MessageBox.Show(this, string.Join("\n", invalid.Take(8)), "无法保存通信数据库", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _database.Normalize();
        _commit();
    }

    private void ReopenAfter(Action action)
    {
        Close();
        action();
    }

    private Button Button(string text, RoutedEventHandler click, bool primary = false, bool danger = false)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 0, 8, 0),
            MinHeight = 30,
            Background = primary ? PaletteBrush("AccentSoftBrush", "#EEF7FD") : danger ? PaletteBrush("RedSoftBrush", "#FFEBEE") : PaletteBrush("CardBrush", "#FFFFFF"),
            BorderBrush = primary ? PaletteBrush("AccentBrush", "#5A9FD0") : danger ? PaletteBrush("RedBrush", "#C62828") : PaletteBrush("LineBrush", "#E4E8EF"),
            Foreground = danger ? PaletteBrush("RedBrush", "#C62828") : PaletteBrush("TextBrush", "#34414F")
        };
        ApplyStyle(button, danger ? "BtnDanger" : primary ? "BtnPrimary" : "BtnBase");
        button.Click += click;
        return button;
    }

    private Style MessageItemStyle()
    {
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(4)));
        style.Setters.Add(new Setter(Control.ForegroundProperty, PaletteBrush("TextBrush", "#34414F")));
        style.Setters.Add(new Setter(Control.BackgroundProperty, PaletteBrush("CardBrush", "#FFFFFF")));
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, PaletteBrush("AccentSoftBrush", "#EEF7FD")));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, PaletteBrush("TextBrush", "#34414F")));
        style.Triggers.Add(selected);
        return style;
    }

    private DataTemplate MessageItemTemplate()
    {
        var stack = new FrameworkElementFactory(typeof(StackPanel));
        stack.SetValue(StackPanel.MarginProperty, new Thickness(7, 5, 7, 5));

        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        name.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        name.SetBinding(TextBlock.TextProperty, new Binding(nameof(DatabaseSearchResult.DisplayName)));
        stack.AppendChild(name);

        var identity = new FrameworkElementFactory(typeof(TextBlock));
        identity.SetValue(TextBlock.ForegroundProperty, PaletteBrush("Text2Brush", "#687787"));
        identity.SetValue(TextBlock.FontSizeProperty, 11d);
        identity.SetBinding(TextBlock.TextProperty, new Binding(nameof(DatabaseSearchResult.Identity)));
        stack.AppendChild(identity);

        var signals = new FrameworkElementFactory(typeof(TextBlock));
        signals.SetValue(TextBlock.ForegroundProperty, PaletteBrush("Text3Brush", "#9AA7B3"));
        signals.SetValue(TextBlock.FontSizeProperty, 11d);
        signals.SetBinding(TextBlock.TextProperty, new Binding(nameof(DatabaseSearchResult.SignalSummary)));
        signals.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        stack.AppendChild(signals);

        return new DataTemplate { VisualTree = stack };
    }

    private sealed record DatabaseSearchResult(CanMessageDefinition Message)
    {
        public string DisplayName => CanDefinitionDisplay.MessagePrimaryName(Message);
        public string Identity => $"0x{Message.Id:X} · {(Message.Extended ? "扩展帧" : "标准帧")}";
        public string SignalSummary => $"{Message.Signals.Count} 个信号";
    }

    private void StyleInput(Control control)
    {
        control.Background = PaletteBrush("CardBrush", "#FFFFFF");
        control.Foreground = PaletteBrush("TextBrush", "#34414F");
        control.BorderBrush = PaletteBrush("LineBrush", "#E4E8EF");
        control.BorderThickness = new Thickness(1);
        control.VerticalContentAlignment = VerticalAlignment.Center;
    }

    private void ApplyStyle(Control control, string key)
    {
        if (TryFindResource(key) is Style style) control.Style = style;
    }

    private void FitToWorkArea()
    {
        var workArea = SystemParameters.WorkArea;
        var availableWidth = Math.Max(880, workArea.Width - 32);
        var availableHeight = Math.Max(500, workArea.Height - 48);

        // Width/Height are device-independent units, so clamp them against the
        // logical work area rather than physical screen pixels at 125%/150% DPI.
        MinWidth = Math.Min(MinWidth, availableWidth);
        MinHeight = Math.Min(MinHeight, availableHeight);
        Width = Math.Min(Width, availableWidth);
        Height = Math.Min(Height, availableHeight);

        var left = double.IsNaN(Left) ? workArea.Left + (workArea.Width - Width) / 2 : Left;
        var top = double.IsNaN(Top) ? workArea.Top + (workArea.Height - Height) / 2 : Top;
        Left = Math.Max(workArea.Left, Math.Min(left, workArea.Right - Width));
        Top = Math.Max(workArea.Top, Math.Min(top, workArea.Bottom - Height));
    }

    private Brush PaletteBrush(string key, string fallback) =>
        TryFindResource(key) as Brush ?? FallbackBrush(fallback);

    private static Brush FallbackBrush(string color) =>
        new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
}
