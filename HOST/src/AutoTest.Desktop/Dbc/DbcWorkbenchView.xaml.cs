using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using AutoTest.Projects.Dbc;

namespace AutoTest.Desktop.Dbc;

public partial class DbcWorkbenchView : UserControl
{
    private readonly DbcWorkbenchViewModel _viewModel;
    private string _overlayKind = "";
    private bool _messageNotes;
    private readonly ObservableCollection<EnumEditRow> _enums = new();
    private System.Reflection.PropertyInfo? _editingProperty;
    private object? _editingValue;
    private DbcSignalRow? _editingRow;

    public sealed class EnumEditRow
    {
        public string Raw { get; set; } = "";
        public string Label { get; set; } = "";
    }

    public DbcWorkbenchView(DbcWorkbenchViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        EnumGrid.ItemsSource = _enums;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(DbcWorkbenchViewModel.SelectedMessage) or
                nameof(DbcWorkbenchViewModel.SelectedSignal) or nameof(DbcWorkbenchViewModel.CurrentMessageModel))
                RebuildBitLayout();
        };
        Loaded += (_, _) => RebuildBitLayout();
        MessageGrid.PreviewMouseRightButtonDown += (_, e) => SelectContextRow(MessageGrid, e);
        SignalGrid.PreviewMouseRightButtonDown += (_, e) => SelectContextRow(SignalGrid, e);
    }


    private void NewDocument_Click(object sender, RoutedEventArgs e)
    {
        if (!TryFinishEditing()) return;
        if (global::System.Windows.Application.Current.MainWindow?.DataContext is
            AutoTest.Desktop.Shell.MainWindowViewModel shell)
            shell.NewIndependentDbcCommand.Execute();
    }

    private void OpenDocument_Click(object sender, RoutedEventArgs e)
    {
        if (!TryFinishEditing()) return;
        if (global::System.Windows.Application.Current.MainWindow?.DataContext is
            AutoTest.Desktop.Shell.MainWindowViewModel shell)
            shell.OpenIndependentDbcCommand.Execute();
    }

    private async void RenameDocument_Click(object sender, RoutedEventArgs e)
    {
        if (!TryFinishEditing()) return;
        await _viewModel.RenameDocumentAsync();
    }

    private async void SaveCopy_Click(object sender, RoutedEventArgs e)
    {
        if (!TryFinishEditing()) return;
        var owner = Window.GetWindow(this);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = _viewModel.IsIndependentFile ? "DBC 另存为" : "导出当前工程 DBC",
            Filter = "CAN 数据库 (*.dbc)|*.dbc",
            DefaultExt = ".dbc",
            AddExtension = true,
            FileName = _viewModel.AssetName + ".dbc",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(owner) == true)
            await _viewModel.SaveCopyAsync(dialog.FileName);
    }

    private static T? Ancestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match) return match;
            source = source is Visual || source is System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        return null;
    }

    private void SelectContextRow(DataGrid grid, MouseButtonEventArgs e)
    {
        var row = Ancestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row is null || !FinishGrid()) return;
        grid.SelectedItem = row.Item;
        if (grid == SignalGrid && row.Item is DbcSignalRow signal) _viewModel.SelectedSignal = signal;
    }

    private bool _advancedColumnsVisible;
    private void ToggleAdvancedColumns_Click(object sender, RoutedEventArgs e)
    {
        if (!FinishGrid()) return;
        _advancedColumnsVisible = !_advancedColumnsVisible;
        var state = _advancedColumnsVisible ? Visibility.Visible : Visibility.Collapsed;
        foreach (var column in new DataGridColumn[]
        {
            DbcMinimumColumn, DbcMaximumColumn, DbcReceiverColumn,
            DbcMultiplexerColumn, DbcEnumColumn, DbcDescriptionColumn
        })
            column.Visibility = state;
        AdvancedColumnsButton.Content = _advancedColumnsVisible ? "收起高级列" : "高级列";
    }

    private bool FinishGrid()
    {
        if (!SignalGrid.CommitEdit(DataGridEditingUnit.Cell, true)) return false;
        if (!SignalGrid.CommitEdit(DataGridEditingUnit.Row, true)) return false;
        _viewModel.ApplySignalParameters();
        return !_viewModel.HasPendingSignalDraft;
    }

    public bool TryFinishEditing() => FinishOverlay() && FinishGrid();

    private void Signal_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
    {
        if (!_viewModel.CanEdit || _viewModel.IsBusy || _viewModel.HasOpenEditor)
        { e.Cancel = true; return; }
        if (e.Row.Item is not DbcSignalRow row) { e.Cancel = true; return; }
        _viewModel.SelectedSignal = row;
        var path = (e.Column as DataGridBoundColumn)?.Binding is Binding b
            ? b.Path.Path : "Draft." + e.Column.SortMemberPath;
        _editingRow = row;
        _editingProperty = typeof(DbcSignalEditState).GetProperty(path.Split('.').Last());
        _editingValue = _editingProperty?.GetValue(row.Draft);
    }

    private void Signal_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Cancel && _editingRow is { } row &&
            ReferenceEquals(e.Row.Item, row) && _editingProperty is { } property)
            property.SetValue(row.Draft, _editingValue);
        _editingProperty = null; _editingRow = null; _editingValue = null;
    }

    private void Signal_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
    {
        if (e.Row.Item is not DbcSignalRow row) return;
        if (e.EditAction == DataGridEditAction.Cancel)
        {
            _viewModel.ResetSignalRow(row);
            return;
        }
        e.Cancel = !_viewModel.TryCommitSignalRow(row);
    }

    private void Message_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Ancestor<DataGridRow>(e.OriginalSource as DependencyObject) is { } row && FinishGrid())
        {
            MessageGrid.SelectedItem = row.Item;
            _viewModel.EditMessage(); e.Handled = true;
        }
    }

    private void Signal_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        var cell = Ancestor<DataGridCell>(e.OriginalSource as DependencyObject);
        var row = Ancestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (cell is null || row?.Item is not DbcSignalRow signal || _viewModel.IsBusy) return;
        if (!cell.Column.IsReadOnly) return; // native DataGrid inline editing
        if (!FinishGrid()) return;
        _viewModel.SelectedSignal = signal;
        if (cell.Column.Header?.ToString() == "枚举") OpenEnums();
        else OpenNotes(false);
        e.Handled = true;
    }

    private void Workbench_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel.HasOpenEditor)
        {
            if (e.Key == Key.Escape) { CloseOverlay(); e.Handled = true; }
            else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            { Save_Click(this, new RoutedEventArgs()); e.Handled = true; }
            return;
        }
        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        { Save_Click(this, new RoutedEventArgs()); e.Handled = true; return; }
        if (e.Key == Key.F5)
        { Reload_Click(this, new RoutedEventArgs()); e.Handled = true; return; }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.Z or Key.Y &&
            !SignalGrid.IsKeyboardFocusWithin && Keyboard.FocusedElement is not TextBox)
        {
            if (FinishGrid()) _viewModel.UndoEdit(e.Key == Key.Y);
            e.Handled = true; return;
        }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.Z or Key.Y &&
            SignalGrid.IsKeyboardFocusWithin && !_viewModel.HasActiveGridEdit)
        {
            _viewModel.UndoEdit(e.Key == Key.Y); e.Handled = true; return;
        }
        if (e.Key == Key.F2 && MessageGrid.IsKeyboardFocusWithin)
        { _viewModel.EditMessage(); e.Handled = true; }
        else if (e.Key == Key.F2 && SignalGrid.IsKeyboardFocusWithin &&
                 SignalGrid.CurrentColumn?.IsReadOnly == true)
        { OpenNotes(false); e.Handled = true; }
        else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control &&
                 SignalGrid.IsKeyboardFocusWithin && !_viewModel.HasActiveGridEdit &&
                 _viewModel.CanEdit && SignalGrid.CurrentItem is DbcSignalRow row &&
                 SignalGrid.CurrentColumn is DataGridBoundColumn { IsReadOnly: false, Binding: Binding binding })
        {
            try
            {
                var value = Clipboard.GetText().Trim();
                var property = typeof(DbcSignalEditState).GetProperty(binding.Path.Path.Split('.').Last());
                if (property?.PropertyType == typeof(string) && !value.Any(c => c is '\r' or '\n' or '\t'))
                {
                    var original = property.GetValue(row.Draft);
                    property.SetValue(row.Draft, value);
                    if (!_viewModel.TryCommitSignalRow(row))
                        property.SetValue(row.Draft, original);
                    e.Handled = true;
                }
            }
            catch (System.Runtime.InteropServices.ExternalException) { }
        }
    }

    private void CopyFilePath_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(_viewModel.FilePath); }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            MessageBox.Show(Window.GetWindow(this), "无法复制文件位置，请检查剪贴板是否被占用。",
                "复制失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryFinishEditing()) return;
        await _viewModel.SaveForCloseAsync();
    }
    private void Undo_Click(object sender, RoutedEventArgs e) { if (FinishGrid()) _viewModel.UndoEdit(); }
    private void Redo_Click(object sender, RoutedEventArgs e) { if (FinishGrid()) _viewModel.UndoEdit(true); }
    private void Reload_Click(object sender, RoutedEventArgs e)
    { if (FinishGrid() && _viewModel.ReloadCommand.CanExecute()) _viewModel.ReloadCommand.Execute(); }
    private void Check_Click(object sender, RoutedEventArgs e) { if (FinishGrid()) _viewModel.CheckDocument(); }
    private void AddMessage_Click(object sender, RoutedEventArgs e) { if (FinishGrid()) _viewModel.AddMessage(); }
    private void AddSignal_Click(object sender, RoutedEventArgs e) { if (FinishGrid()) _viewModel.AddSignal(); }
    private void DeleteMessage_Click(object sender, RoutedEventArgs e) { if (FinishGrid()) _viewModel.DeleteSelected(true); }
    private void DeleteSignal_Click(object sender, RoutedEventArgs e) { if (FinishGrid()) _viewModel.DeleteSelected(false); }
    private void EditMessage_Click(object sender, RoutedEventArgs e) { if (FinishGrid()) _viewModel.EditMessage(); }
    private void EditSignal_Click(object sender, RoutedEventArgs e) { if (FinishGrid()) _viewModel.EditSignal(); }
    private void MessageNotes_Click(object sender, RoutedEventArgs e) => OpenNotes(true);
    private void SignalNotes_Click(object sender, RoutedEventArgs e) => OpenNotes(false);
    private void Enums_Click(object sender, RoutedEventArgs e) => OpenEnums();

    private void ShowOverlay(string kind, string title)
    {
        _overlayKind = kind;
        OverlayTitle.Text = title;
        NotesPanel.Visibility = kind == "notes" ? Visibility.Visible : Visibility.Collapsed;
        EnumsPanel.Visibility = kind == "enums" ? Visibility.Visible : Visibility.Collapsed;
        OverlayError.Text = "";
        _viewModel.HasOpenEditor = true;
        PropertyOverlay.Visibility = Visibility.Visible;
    }

    private void OpenNotes(bool message)
    {
        if (!FinishGrid() || _viewModel.IsBusy || _viewModel.SelectedMessage is not { } m ||
            (!message && _viewModel.SelectedSignal is null)) return;
        _messageNotes = message;
        OriginalName.Text = "原始名称：" + (message ? m.Name : _viewModel.SelectedSignal!.Name);
        AliasBox.Text = message ? _viewModel.MessageAliasText : _viewModel.SignalAliasText;
        SummaryBox.Text = message ? _viewModel.MessageSummaryText : _viewModel.SignalSummaryText;
        CommentBox.Text = message ? _viewModel.MessageCommentText : _viewModel.SignalCommentText;
        ShowOverlay("notes", message ? "报文名称与注释" : "信号名称与注释");
        AliasBox.Focus(); AliasBox.SelectAll();
    }

    private void OpenEnums()
    {
        if (!FinishGrid() || _viewModel.IsBusy || _viewModel.SelectedSignal is not { } s) return;
        _enums.Clear();
        foreach (var entry in _viewModel.SelectedEnums.OrderBy(x => x.Key))
            _enums.Add(new EnumEditRow { Raw = entry.Key.ToString(CultureInfo.InvariantCulture), Label = entry.Value });
        var editable = _viewModel.CanEditValueTables;
        EnumGrid.IsReadOnly = !editable;
        AddEnumButton.IsEnabled = DeleteEnumButton.IsEnabled = editable;
        EnumPolicy.Text = editable ? "使用原始整数值；应用时检查位长、有符号范围和重复值。"
            : "当前 DBC 原文受保护：仅查看原始枚举，不修改 VAL_ 定义。";
        ShowOverlay("enums", s.Name + " · 枚举值");
    }

    private void AddEnum_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanEditValueTables) return;
        var n = 0L; while (_enums.Any(r => r.Raw == n.ToString(CultureInfo.InvariantCulture))) n++;
        var row = new EnumEditRow { Raw = n.ToString(CultureInfo.InvariantCulture) };
        _enums.Add(row); EnumGrid.SelectedItem = row; EnumGrid.ScrollIntoView(row);
    }
    private void DeleteEnum_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.CanEditValueTables && EnumGrid.SelectedItem is EnumEditRow row) _enums.Remove(row);
    }

    private void Overlay_Backdrop(object sender, MouseButtonEventArgs e)
    {
        // Clicking outside never applies or discards an in-progress edit.
        // The user must explicitly choose 完成 or 取消.
        if (ReferenceEquals(e.OriginalSource, PropertyOverlay))
            e.Handled = true;
    }

    private void FinishOverlay_Click(object sender, RoutedEventArgs e) => FinishOverlay();
    private void CancelOverlay_Click(object sender, RoutedEventArgs e) => CloseOverlay();

    private bool FinishOverlay()
    {
        if (!_viewModel.HasOpenEditor) return true;
        if (_overlayKind == "notes" &&
            !_viewModel.TrySaveNotes(_messageNotes, AliasBox.Text, SummaryBox.Text, CommentBox.Text))
        { OverlayError.Text = _viewModel.StatusMessage; return false; }
        if (_overlayKind == "enums" && _viewModel.CanEditValueTables)
        {
            if (!EnumGrid.CommitEdit(DataGridEditingUnit.Cell, true) ||
                !EnumGrid.CommitEdit(DataGridEditingUnit.Row, true)) return false;
            var values = new Dictionary<long, string>();
            foreach (var row in _enums)
                if (!long.TryParse(row.Raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var raw) ||
                    !values.TryAdd(raw, row.Label))
                { OverlayError.Text = "枚举原始值必须是唯一的有效整数。"; return false; }
            if (!_viewModel.TrySaveEnums(values))
            { OverlayError.Text = _viewModel.StatusMessage; return false; }
        }
        CloseOverlay(); return true;
    }

    private void CloseOverlay()
    {
        PropertyOverlay.Visibility = Visibility.Collapsed;
        _viewModel.HasOpenEditor = false;
        _overlayKind = "";
    }

    private static readonly string[] ColorsBySignal =
        ["#BAD7FA", "#BDE1D5", "#E1D1F5", "#F2DBAC", "#B9DDEB", "#D0E3B4", "#F2CAD8", "#C8D5E8"];

    private void RebuildBitLayout()
    {
        if (BitGrid is null) return;
        BitGrid.Children.Clear(); BitGrid.RowDefinitions.Clear(); BitGrid.ColumnDefinitions.Clear();
        BitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });
        for (var i = 0; i < 8; i++) BitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        BitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        BitGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(23) });
        AddBitLabel(0, 0, "字节");
        for (var b = 7; b >= 0; b--) AddBitLabel(0, 8 - b, b.ToString());
        AddBitLabel(0, 9, "信号");
        var message = _viewModel.CurrentMessageModel;
        if (message is null) { BitStatus.Text = "请选择报文。"; return; }
        var signals = message.Signals.ToArray();
        var positions = signals.Select(s => DbcSignalCodec.BitPositions(s).ToHashSet()).ToArray();
        var conflictBits = 0; var sharedBits = 0;
        for (var byteIndex = 0; byteIndex < message.Dlc; byteIndex++)
        {
            BitGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(25) });
            var row = byteIndex + 1;
            AddBitLabel(row, 0, "Byte " + byteIndex);
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (var bit = 7; bit >= 0; bit--)
            {
                var physical = byteIndex * 8 + bit;
                var owners = Enumerable.Range(0, signals.Length).Where(i => positions[i].Contains(physical)).ToArray();
                foreach (var i in owners) names.Add(signals[i].Name);
                var mux = owners.Length > 1 && owners.All(i => signals[i].Multiplexer?.StartsWith('m') == true) &&
                    owners.Select(i => signals[i].Multiplexer).Distinct().Count() == owners.Length;
                if (owners.Length > 1) { if (mux) sharedBits++; else conflictBits++; }
                var selected = owners.Any(i => signals[i].Name == _viewModel.SelectedSignal?.Name);
                var color = owners.Length == 0 ? "#FFFFFF" : owners.Length == 1
                    ? ColorsBySignal[owners[0] % ColorsBySignal.Length] : mux ? "#DBE6F5" : "#F8C5B8";
                var cell = new Border
                {
                    Background = (Brush)new BrushConverter().ConvertFromString(color)!,
                    BorderBrush = (Brush)new BrushConverter().ConvertFromString(selected ? "#3478D6" : "#D7E0E9")!,
                    BorderThickness = new Thickness(selected ? 2 : 1), CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(1),
                    ToolTip = "Byte " + byteIndex + " · bit " + bit + " · " +
                        (owners.Length == 0 ? "未使用" : string.Join(" / ", owners.Select(i => signals[i].Name)))
                        + (mux ? " · 不同复用分支" : owners.Length > 1 ? " · 位冲突" : "")
                };
                if (owners.Length > 1)
                    cell.Child = new TextBlock { Text = mux ? "M" : "!", FontSize = 11,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                if (owners.Length == 1)
                {
                    var name = signals[owners[0]].Name;
                    cell.Cursor = Cursors.Hand;
                    cell.MouseLeftButtonUp += (_, _) => SelectBitSignal(name);
                }
                else if (owners.Length > 1)
                {
                    var menu = new ContextMenu();
                    foreach (var i in owners)
                    {
                        var name = signals[i].Name;
                        var item = new MenuItem { Header = name };
                        item.Click += (_, _) => SelectBitSignal(name); menu.Items.Add(item);
                    }
                    cell.ContextMenu = menu;
                    cell.Cursor = Cursors.Hand;
                    cell.MouseLeftButtonUp += (_, _) => menu.IsOpen = true;
                }
                Grid.SetRow(cell, row); Grid.SetColumn(cell, 8 - bit); BitGrid.Children.Add(cell);
            }
            var label = AddBitLabel(row, 9, names.Count == 0 ? "未使用" : string.Join(" / ", names.Select(n =>
                _viewModel.Signals.FirstOrDefault(s => s.Name == n)?.NavTitle ?? n)));
            if (names.Count == 1)
            { var name = names.First(); label.Cursor = Cursors.Hand; label.MouseLeftButtonUp += (_, _) => SelectBitSignal(name); }
        }
        BitStatus.Text = "颜色区分信号 · 蓝框：当前信号 · M：不同复用分支 · !：位冲突" +
            (sharedBits > 0 ? " · 复用共享 " + sharedBits + " bit" : "") +
            (conflictBits > 0 ? " · 冲突 " + conflictBits + " bit，保存前须修复" : "");
    }

    private TextBlock AddBitLabel(int row, int col, string text)
    {
        var label = new TextBlock { Text = text, ToolTip = text, FontSize = 11,
            Foreground = Brushes.SlateGray, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = col == 9 ? HorizontalAlignment.Left : HorizontalAlignment.Center,
            Margin = new Thickness(col == 9 ? 8 : 1, 0, 1, 0) };
        Grid.SetRow(label, row); Grid.SetColumn(label, col); BitGrid.Children.Add(label); return label;
    }

    private void SelectBitSignal(string name)
    {
        if (!FinishGrid()) return;
        var row = _viewModel.Signals.FirstOrDefault(s => s.Name == name);
        if (row is null) return;
        SignalGrid.SelectedItem = row;
        _viewModel.SelectedSignal = row;
        SignalGrid.ScrollIntoView(row);
    }
}
