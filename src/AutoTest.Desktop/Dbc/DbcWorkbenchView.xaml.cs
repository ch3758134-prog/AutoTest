using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AutoTest.Projects.Dbc;

namespace AutoTest.Desktop.Dbc;

/// <summary>
/// Native Windows editor conventions: one click selects, F2/double-click on a
/// displayed name edits the Chinese alias in-place; complex DBC identifiers
/// and shape updates use the revision-safe model's full parameter editor.
/// </summary>
public partial class DbcWorkbenchView : UserControl
{
    private readonly DbcWorkbenchViewModel _viewModel;
    private TextBox? _inlineEditor;
    private FrameworkElement? _inlineDisplay;
    private bool _inlineIsMessage;

    public DbcWorkbenchView(DbcWorkbenchViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        Loaded += (_, _) => RebuildBitLayout();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DbcWorkbenchViewModel.SelectedMessage) or
            nameof(DbcWorkbenchViewModel.SelectedSignal))
            RebuildBitLayout();
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T matched) return matched;
            current = current is Visual || current is System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        return null;
    }

    private static T? FindDescendant<T>(DependencyObject parent, string name)
        where T : FrameworkElement
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match && match.Name == name) return match;
            var nested = FindDescendant<T>(child, name);
            if (nested is not null) return nested;
        }
        return null;
    }

    private void BeginInlineRename(DependencyObject container, bool message)
    {
        if (_viewModel.IsBusy) return;
        if (_inlineEditor is not null) return;
        if (message && _viewModel.SelectedMessage is null ||
            !message && _viewModel.SelectedSignal is null) return;
        var textbox = FindDescendant<TextBox>(container,
            message ? "MessageInlineEditor" : "SignalInlineEditor");
        var display = FindDescendant<FrameworkElement>(container,
            message ? "MessageInlineDisplay" : "SignalInlineDisplay");
        if (textbox is null || display is null) return;
        _inlineEditor = textbox;
        _inlineDisplay = display;
        _inlineIsMessage = message;
        textbox.Text = message
            ? _viewModel.SelectedMessage!.DisplayNames.MessageAlias(
                _viewModel.SelectedMessage.Message.Id)
            : _viewModel.SelectedMessage!.DisplayNames.SignalAlias(
                _viewModel.SelectedMessage.Message.Id, _viewModel.SelectedSignal!.Name);
        display.Visibility = Visibility.Collapsed;
        textbox.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(() =>
        {
            if (!ReferenceEquals(_inlineEditor, textbox)) return;
            textbox.Focus();
            textbox.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void EndInlineRename(bool commit)
    {
        var editor = _inlineEditor;
        var display = _inlineDisplay;
        if (editor is null) return;
        // Reset first: rebuilding a message/signal list removes the template
        // and can raise another LostKeyboardFocus event recursively.
        _inlineEditor = null;
        _inlineDisplay = null;
        var message = _inlineIsMessage;
        var value = editor.Text;
        editor.Visibility = Visibility.Collapsed;
        if (display is not null) display.Visibility = Visibility.Visible;
        if (commit)
        {
            if (message) _viewModel.RenameSelectedMessage(value);
            else _viewModel.RenameSelectedSignal(value);
        }
    }

    private void InlineEditor_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Escape)) return;
        var wasMessage = _inlineIsMessage;
        EndInlineRename(e.Key == Key.Enter);
        e.Handled = true;
        Dispatcher.BeginInvoke(() =>
        {
            if (wasMessage) MessageList.Focus();
            else SignalGrid.Focus();
        }, DispatcherPriority.Input);
    }

    private void InlineEditor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (ReferenceEquals(sender, _inlineEditor)) EndInlineRename(true);
    }

    private void MessageList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item is not null && !_viewModel.IsBusy)
        {
            MessageList.SelectedItem = item.DataContext;
            BeginInlineRename(item, message: true);
            e.Handled = true;
        }
    }

    private void SignalGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row is null || _viewModel.IsBusy) return;
        SignalGrid.SelectedItem = row.Item;
        var cell = FindAncestor<DataGridCell>(e.OriginalSource as DependencyObject);
        if (cell is null) return; // double-clicking a scrollbar/empty area is not Edit
        if (cell.Column.DisplayIndex == 0)
            BeginInlineRename(row, message: false);
        else _viewModel.EditSignal();
        e.Handled = true;
    }

    private void Workbench_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F2 || _inlineEditor is not null || _viewModel.IsBusy) return;
        if (MessageList.IsKeyboardFocusWithin &&
            MessageList.ItemContainerGenerator.ContainerFromItem(MessageList.SelectedItem)
                is ListBoxItem messageItem)
        {
            BeginInlineRename(messageItem, message: true);
            e.Handled = true;
        }
        else if (SignalGrid.IsKeyboardFocusWithin &&
                 SignalGrid.ItemContainerGenerator.ContainerFromItem(SignalGrid.SelectedItem)
                     is DataGridRow row)
        {
            BeginInlineRename(row, message: false);
            e.Handled = true;
        }
    }

    private void MessageList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item is not null) MessageList.SelectedItem = item.DataContext;
    }

    private void SignalGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row is not null) SignalGrid.SelectedItem = row.Item;
    }

    private void RenameMessageMenu_Click(object sender, RoutedEventArgs e)
    {
        if (MessageList.ItemContainerGenerator.ContainerFromItem(MessageList.SelectedItem)
            is ListBoxItem item) BeginInlineRename(item, message: true);
    }

    private void RenameSignalMenu_Click(object sender, RoutedEventArgs e)
    {
        if (SignalGrid.ItemContainerGenerator.ContainerFromItem(SignalGrid.SelectedItem)
            is DataGridRow row) BeginInlineRename(row, message: false);
    }

    private void EditMessageMenu_Click(object sender, RoutedEventArgs e) => _viewModel.EditMessage();
    private void EditSignalMenu_Click(object sender, RoutedEventArgs e) => _viewModel.EditSignal();

    private void RebuildBitLayout()
    {
        if (BitGrid is null) return;
        BitGrid.Children.Clear();
        BitGrid.RowDefinitions.Clear();
        BitGrid.ColumnDefinitions.Clear();

        // Physical bits are arranged by byte and bit index, not as a wall of
        // repeated truncated signal names. The label at the right is shown once
        // per byte, and individual bit cells carry exact, inspectable tooltips.
        BitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
        for (var bit = 0; bit < 8; bit++)
            BitGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
        BitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        for (var row = 0; row <= 8; row++)
            BitGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });

        AddBitLabel(0, 0, "字节", true);
        for (var bit = 7; bit >= 0; bit--)
            AddBitLabel(0, 8 - bit, bit.ToString(), true);
        AddBitLabel(0, 9, "信号", true);

        var message = _viewModel.SelectedMessage?.Message;
        var occupied = message is null
            ? new Dictionary<string, HashSet<int>>(StringComparer.Ordinal)
            : message.Signals.ToDictionary(signal => signal.Name,
                signal => DbcSignalCodec.BitPositions(signal).ToHashSet(),
                StringComparer.Ordinal);

        for (var byteIndex = 0; byteIndex < 8; byteIndex++)
        {
            var row = byteIndex + 1;
            AddBitLabel(row, 0, "Byte " + byteIndex, false);
            var namesInByte = new HashSet<string>(StringComparer.Ordinal);
            for (var bit = 7; bit >= 0; bit--)
            {
                var physical = byteIndex * 8 + bit;
                var matches = message is null ? [] : message.Signals
                    .Where(signal => occupied[signal.Name].Contains(physical))
                    .ToArray();
                foreach (var owner in matches) namesInByte.Add(owner.Name);
                var selected = matches.Any(signal =>
                    signal.Name == _viewModel.SelectedSignal?.Name);
                var color = message is null || byteIndex >= message.Dlc ? "#EFF2F6" :
                    matches.Length > 1 ? "#F4CD96" :
                    selected ? "#83B8EC" :
                    matches.Length == 1 ? "#D4E6F8" : "#FFFFFF";
                var cell = new Border
                {
                    Background = (Brush)new BrushConverter().ConvertFromString(color)!,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(217, 226, 237)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(2, 2, 2, 2),
                    ToolTip = "Byte " + byteIndex + " · bit " + bit + " · " +
                        (matches.Length == 0 ? "未使用" :
                            string.Join(" / ", matches.Select(signal => signal.Name)))
                };
                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, 8 - bit);
                BitGrid.Children.Add(cell);
                if (matches.Length == 1)
                {
                    var name = matches[0].Name;
                    cell.Cursor = Cursors.Hand;
                    cell.MouseLeftButtonUp += (_, _) => SelectBitSignal(name);
                }
                else if (matches.Length > 1)
                {
                    cell.Child = new TextBlock
                    {
                        Text = "×",
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = Brushes.SaddleBrown
                    };
                }
            }
            var signalNames = namesInByte.Select(name =>
            {
                var signal = _viewModel.Signals.FirstOrDefault(s => s.Name == name);
                return signal?.NavTitle ?? name;
            }).ToArray();
            var label = signalNames.Length switch
            {
                0 => "未使用",
                1 => signalNames[0],
                _ => signalNames.Length + " 个信号"
            };
            var labelBlock = AddBitLabel(row, 9, label, false);
            labelBlock.ToolTip = namesInByte.Count == 0 ? "未使用" :
                string.Join("、", namesInByte);
            if (namesInByte.Count == 1)
            {
                var name = namesInByte.First();
                labelBlock.Cursor = Cursors.Hand;
                labelBlock.MouseLeftButtonUp += (_, _) => SelectBitSignal(name);
            }
        }
    }

    private TextBlock AddBitLabel(int row, int column, string text, bool heading)
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = heading ? 11 : 12,
            FontWeight = heading ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = heading ? new SolidColorBrush(Color.FromRgb(80, 103, 131))
                : new SolidColorBrush(Color.FromRgb(57, 73, 93)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = column == 9 ? HorizontalAlignment.Left :
                HorizontalAlignment.Center,
            Margin = column == 9 ? new Thickness(10, 0, 2, 0) :
                new Thickness(1, 0, 1, 0)
        };
        Grid.SetRow(label, row);
        Grid.SetColumn(label, column);
        BitGrid.Children.Add(label);
        return label;
    }

    private void SelectBitSignal(string signalName)
    {
        var signal = _viewModel.Signals.FirstOrDefault(s => s.Name == signalName);
        if (signal is null) return;
        _viewModel.SelectedSignal = signal;
        SignalGrid.ScrollIntoView(signal);
    }

}
