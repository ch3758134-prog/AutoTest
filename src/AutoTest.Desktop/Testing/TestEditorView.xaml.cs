using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace AutoTest.Desktop.Testing;

/// <summary>Native high-density TEST rows; inspector is hosted in the ONE Shell right rail.</summary>
public partial class TestEditorView : UserControl
{
    private readonly TestEditorViewModel _viewModel;
    private TestStepInspector? _inspector;
    private Point _dragStart;
    private StepRowProjection? _dragSource;
    public ContentControl InspectorHost { get; } = new()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch
    };

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

    private void StepsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(StepsList);
        var origin = e.OriginalSource as DependencyObject;
        var container = origin is null ? null :
            ItemsControl.ContainerFromElement(StepsList, origin) as ListBoxItem;
        _dragSource = (container?.DataContext as StepRowProjection) is { IsStep: true } row
            ? row : null;
    }

    private void StepsList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragSource is null || e.LeftButton != MouseButtonState.Pressed) return;
        var now = e.GetPosition(StepsList);
        if (Math.Abs(now.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(now.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var source = _dragSource;
        _dragSource = null;
        DragDrop.DoDragDrop(StepsList, new DataObject("AutoTest.StepRow", source),
            DragDropEffects.Move);
    }

    private void StepsList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent("AutoTest.StepRow") ?
            DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void StepsList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData("AutoTest.StepRow") is not StepRowProjection moving) return;
        var origin = e.OriginalSource as DependencyObject;
        var container = origin is null ? null :
            ItemsControl.ContainerFromElement(StepsList, origin) as ListBoxItem;
        if (container?.DataContext is StepRowProjection target)
        {
            _viewModel.MoveStepAfter(moving, target);
            e.Handled = true;
        }
    }

    private void StepsList_RightClick(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        var container = source is null ? null :
            ItemsControl.ContainerFromElement(StepsList, source) as ListBoxItem;
        if (container is null) return;
        StepsList.SelectedItem = container.DataContext;
        var menu = new ContextMenu();
        void Item(string title, ICommand command)
        {
            menu.Items.Add(new MenuItem { Header = title, Command = command });
        }
        Item("＋ 添加步骤 / 子步骤", _viewModel.AddChildCommand);
        Item("在该步骤下方插入", _viewModel.AddAfterCommand);
        menu.Items.Add(new Separator());
        Item("编辑步骤属性", _viewModel.EditParametersCommand);
        Item("复制步骤及子分支", _viewModel.DuplicateCommand);
        Item("上移", _viewModel.MoveUpCommand);
        Item("下移", _viewModel.MoveDownCommand);
        Item("删除", _viewModel.DeleteCommand);
        menu.Items.Add(new Separator());
        Item("撤销", _viewModel.UndoCommand);
        Item("重做", _viewModel.RedoCommand);
        Item("保存", _viewModel.SaveCommand);
        container.ContextMenu = menu;
    }

    private void StepsList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.SelectedRow?.IsStep != true || e.ChangedButton != MouseButton.Left) return;
        _viewModel.EditParametersCommand.Execute();
        _inspector?.FocusName();
        e.Handled = true;
    }

    private void Editor_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        var focused = Keyboard.FocusedElement;
        bool typing = focused is TextBoxBase or PasswordBox or ComboBox;
        bool ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        if (ctrl && e.Key == Key.F && !typing)
        {
            StepSearch.Focus(); StepSearch.SelectAll(); e.Handled = true; return;
        }
        if (typing) return; // text editing owns Delete and text Undo/Redo
        ICommand? command = null;
        if (ctrl && e.Key == Key.S) command = _viewModel.SaveCommand;
        else if (ctrl && e.Key == Key.Z) command = _viewModel.UndoCommand;
        else if (ctrl && e.Key == Key.Y) command = _viewModel.RedoCommand;
        else if (ctrl && e.Key == Key.Space) command = _viewModel.AddChildCommand;
        else if (e.Key == Key.Delete) command = _viewModel.DeleteCommand;
        else if (e.Key == Key.F9)
        {
            _viewModel.ToggleBreakpoint(); e.Handled = true; return;
        }
        else if (e.Key == Key.F2)
        {
            _viewModel.EditParametersCommand.Execute(); _inspector?.FocusName();
            e.Handled = true; return;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Alt && e.Key == Key.Up)
            command = _viewModel.MoveUpCommand;
        else if (Keyboard.Modifiers == ModifierKeys.Alt && e.Key == Key.Down)
            command = _viewModel.MoveDownCommand;
        if (command?.CanExecute(null) == true)
        {
            command.Execute(null); e.Handled = true;
        }
    }
}
