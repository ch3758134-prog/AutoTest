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
    private TextBlock? _inlineDisplay;
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
        var display = FindDescendant<TextBlock>(container,
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
        AddBitCell("Byte", "#E7EDF5", false);
        for (var bit = 7; bit >= 0; bit--) AddBitCell(bit.ToString(), "#E7EDF5", false);
        if (_viewModel.SelectedMessage is not { } selected)
        {
            for (var i = 0; i < 8; i++)
            {
                AddBitCell(i.ToString(), "#E7EDF5", false);
                for (var j = 0; j < 8; j++) AddBitCell("—", "#F3F5F8", false);
            }
            return;
        }
        var message = selected.Message;
        var occupied = message.Signals.ToDictionary(s => s.Name,
            s => DbcSignalCodec.BitPositions(s).ToHashSet(), StringComparer.Ordinal);
        for (var b = 0; b < 8; b++)
        {
            AddBitCell(b.ToString(), "#E7EDF5", false);
            for (var bit = 7; bit >= 0; bit--)
            {
                var physical = b * 8 + bit;
                var owners = message.Signals.Where(signal =>
                    occupied[signal.Name].Contains(physical)).ToArray();
                var selectedName = _viewModel.SelectedSignal?.Name;
                var containsSelected = owners.Any(signal => signal.Name == selectedName);
                var background = b >= message.Dlc ? "#EEF0F3" :
                    containsSelected ? "#B8D7FA" :
                    owners.Length > 1 ? "#F7DDB8" :
                    owners.Length > 0 ? "#DCEBFA" : "#FFFFFF";
                var label = owners.Length == 0 ? "" :
                    owners.Length > 1 ? "×" :
                    owners[0].Name.Length <= 6 ? owners[0].Name : owners[0].Name[..5] + "…";
                var button = AddBitCell(label, background, owners.Length != 0);
                button.ToolTip = "Byte " + b + " · bit " + bit + " · " +
                    (owners.Length == 0 ? "未使用" :
                    string.Join("、", owners.Select(signal => signal.Name)));
                if (owners.Length == 1)
                {
                    var name = owners[0].Name;
                    button.Click += (_, _) => _viewModel.SelectedSignal =
                        _viewModel.Signals.FirstOrDefault(signal => signal.Name == name);
                }
            }
        }
    }

    private Button AddBitCell(string caption, string background, bool active)
    {
        var cell = new Button
        {
            Content = caption,
            FontSize = 10,
            MinHeight = 22,
            Margin = new Thickness(1),
            Padding = new Thickness(1, 0, 1, 0),
            Background = (Brush)new BrushConverter().ConvertFromString(background)!,
            Foreground = active ? Brushes.DarkSlateBlue : Brushes.SlateGray,
            BorderBrush = new SolidColorBrush(Color.FromRgb(214, 222, 232)),
            BorderThickness = new Thickness(1),
            IsEnabled = active
        };
        BitGrid.Children.Add(cell);
        return cell;
    }
}
