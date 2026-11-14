using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AutoTest.Desktop.Testing;

public partial class TestEditorView : UserControl
{
    private readonly TestEditorViewModel _viewModel;
    private TestStepInspector? _inspector;
    private bool _revertingSelection;


    public TestEditorView(TestEditorViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.InspectorRequested += ShowInspector;
        _viewModel.AllowEditorAction = () =>
        {
            if (_inspector?.HasPendingEdits != true) return true;
            _inspector.ShowPendingWarning();
            return false;
        };
        Loaded += (_, _) => _viewModel.ResetInspector();
    }

    private void ShowInspector(StepInspectorContext context)
    {
        var editor = new TestStepInspector(context.Choices, context.ExistingMappings,
            context.Original, context.InitialActionKey);
        editor.ApplyRequested = _viewModel.ApplyInspector;
        editor.Cancelled += _viewModel.ResetInspector;
        _inspector = editor;
        InspectorHost.Content = editor;
    }


    private static T? FindAncestor<T>(DependencyObject? from) where T : DependencyObject
    {
        var item = from;
        while (item is not null)
        {
            if (item is T match) return match;
            item = item is Visual || item is System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(item) : LogicalTreeHelper.GetParent(item);
        }
        return null;
    }

    private void StepsTree_SelectedItemChanged(object sender,
        RoutedPropertyChangedEventArgs<object> e)
    {
        if (_revertingSelection) return;
        if (_inspector?.HasPendingEdits == true && e.OldValue is StepTreeItem previous)
        {
            _inspector.ShowPendingWarning();
            _revertingSelection = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                previous.IsSelected = true;
                _revertingSelection = false;
            }), System.Windows.Threading.DispatcherPriority.Input);
            return;
        }
        _viewModel.SelectItem(e.NewValue as StepTreeItem);
    }

    private void StepsTree_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject) is { } item)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }

    private void StepsTree_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject) is not null &&
            _viewModel.EditParametersCommand.CanExecute())
        {
            _viewModel.EditParametersCommand.Execute();
            e.Handled = true;
        }
    }
}
