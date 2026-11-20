using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace AutoTest.UdsLab;

/// <summary>
/// Small, case-scoped editing affordances. Clipboard remains in memory and
/// never touches Windows clipboard (the steps may contain ECU proprietary
/// diagnostic parameters). Undo/redo is restricted to the current case's
/// step document; WPF TextBox handles its own text undo and paste.
/// </summary>
public partial class MainWindow
{
    private sealed record StepDocumentSnapshot(
        Guid CaseId, Guid? SelectedStepId, DiagnosticCaseStep[] Steps);

    private readonly Stack<StepDocumentSnapshot> _stepUndo = new();
    private readonly Stack<StepDocumentSnapshot> _stepRedo = new();
    private readonly List<DiagnosticCaseStep> _stepClipboard = [];
    private bool _restoringStepSnapshot;

    private StepDocumentSnapshot TakeStepSnapshot() => new(
        _loadedCase?.Id ?? Guid.Empty,
        _loadedStep?.Id,
        _steps.Select(step => step.Clone(newIdentity: false)).ToArray());

    private void RememberStepUndo()
    {
        if (_loadedCase is null || _caseRunning || _restoringStepSnapshot) return;
        _stepUndo.Push(TakeStepSnapshot());
        // Bounded document undo, keep the most recent 40 edits.
        if (_stepUndo.Count > 40)
        {
            var recent = _stepUndo.Take(40).Reverse().ToArray();
            _stepUndo.Clear();
            foreach (var snapshot in recent) _stepUndo.Push(snapshot);
        }
        _stepRedo.Clear();
    }

    private void RestoreStepDocument(StepDocumentSnapshot snapshot)
    {
        if (_loadedCase is null || _loadedCase.Id != snapshot.CaseId) return;
        _restoringStepSnapshot = true;
        _suppressStepSelection = true;
        try
        {
            _steps.Clear();
            foreach (var item in snapshot.Steps)
                _steps.Add(item.Clone(newIdentity: false));
            SyncCaseSteps();
            _loadedStep = null;
            StepsList.SelectedItem = null;
        }
        finally
        {
            _suppressStepSelection = false;
            _restoringStepSnapshot = false;
        }
        var target = _steps.FirstOrDefault(s => s.Id == snapshot.SelectedStepId)
                     ?? _steps.FirstOrDefault();
        if (target is null) ClearStepEditor();
        else LoadStep(target);
        _nextStepIndex = Math.Clamp(_nextStepIndex, 0, _steps.Count);
        _editorDirty = false;
        _workspaceDirty = true;
        UpdateLibraryStatus();
    }

    private void UndoSteps_Click(object sender, RoutedEventArgs e)
    {
        if (_caseRunning || _loadedCase is null) return;
        if (_editorDirty && !SaveCurrentStepCore()) return;
        if (_stepUndo.Count == 0)
        {
            CaseRunStatus.Text = "当前用例没有可撤销的步骤修改。";
            return;
        }
        var previous = _stepUndo.Pop();
        _stepRedo.Push(TakeStepSnapshot());
        RestoreStepDocument(previous);
        CaseRunStatus.Text = "已撤销一次步骤编辑（尚未写入文件）。";
    }

    private void RedoSteps_Click(object sender, RoutedEventArgs e)
    {
        if (_caseRunning || _loadedCase is null) return;
        if (_editorDirty && !SaveCurrentStepCore()) return;
        if (_stepRedo.Count == 0)
        {
            CaseRunStatus.Text = "没有可以重做的步骤修改。";
            return;
        }
        var next = _stepRedo.Pop();
        _stepUndo.Push(TakeStepSnapshot());
        RestoreStepDocument(next);
        CaseRunStatus.Text = "已重做一次步骤编辑（尚未写入文件）。";
    }

    private void ClearCaseUndoHistory()
    {
        _stepUndo.Clear();
        _stepRedo.Clear();
    }

    private void CaseItemRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: DiagnosticCase item } &&
            !ReferenceEquals(CasesList.SelectedItem, item))
            CasesList.SelectedItem = item;
    }

    private void StepItemRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem { DataContext: DiagnosticCaseStep item }) return;
        // Preserve Ctrl/Shift multi-selection when right-clicking one of the
        // selected items, so copy/delete operate on the intended selection.
        if (!StepsList.SelectedItems.Contains(item))
            StepsList.SelectedItem = item;
    }

    private void RenameCase_Click(object sender, RoutedEventArgs e)
    {
        if (_caseRunning || _loadedCase is null) return;
        CaseInfoExpander.IsExpanded = true;
        CaseNameBox.Focus();
        CaseNameBox.SelectAll();
    }

    private void RenameStep_Click(object sender, RoutedEventArgs e)
    {
        if (_caseRunning || _loadedStep is null) return;
        OperationNameBox.Focus();
        OperationNameBox.SelectAll();
    }

    private IReadOnlyList<DiagnosticCaseStep> SelectedStepsForEdit()
    {
        var selection = StepsList.SelectedItems.Cast<DiagnosticCaseStep>().ToHashSet();
        if (selection.Count == 0 && _loadedStep is not null) selection.Add(_loadedStep);
        return _steps.Where(selection.Contains).ToArray();
    }

    private void CopyStepsToClipboard_Click(object sender, RoutedEventArgs e)
    {
        if (_caseRunning) return;
        if (!ConfirmUnsavedBeforeLeave()) return;
        _stepClipboard.Clear();
        foreach (var step in SelectedStepsForEdit())
            _stepClipboard.Add(step.Clone(newIdentity: false));
        if (_stepClipboard.Count > 0)
            CaseRunStatus.Text = "已复制 " + _stepClipboard.Count +
                                 " 条步骤到本窗口的步骤剪贴板，可粘贴进其他用例。";
    }

    private void PasteSteps_Click(object sender, RoutedEventArgs e)
    {
        if (_caseRunning || _loadedCase is null || !ConfirmUnsavedBeforeLeave()) return;
        if (_stepClipboard.Count == 0)
        {
            CaseRunStatus.Text = "尚未复制步骤。先选中步骤并按 Ctrl+C。";
            return;
        }
        if (_steps.Count + _stepClipboard.Count > 150)
        {
            MessageBox.Show(this, "一个诊断用例最多包含 150 个步骤。");
            return;
        }
        RememberStepUndo();
        int after = StepsList.SelectedIndex;
        int insert = after < 0 ? _steps.Count : after + 1;
        DiagnosticCaseStep? first = null;
        foreach (var step in _stepClipboard)
        {
            var cloned = step.Clone(newIdentity: true);
            cloned.LastState = "未运行";
            _steps.Insert(insert++, cloned);
            first ??= cloned;
        }
        SyncCaseSteps();
        _workspaceDirty = true;
        if (first is not null) LoadStep(first);
        UpdateLibraryStatus();
        CaseRunStatus.Text = "已粘贴 " + _stepClipboard.Count + " 条步骤；Ctrl+S 保存用例文件。";
    }

    private void CopyStep_Click(object sender, RoutedEventArgs e)
    {
        if (_caseRunning || _loadedCase is null || !ConfirmUnsavedBeforeLeave()) return;
        var originals = SelectedStepsForEdit();
        if (originals.Count == 0) return;
        if (_steps.Count + originals.Count > 150)
        {
            MessageBox.Show(this, "一个诊断用例最多包含 150 个步骤。");
            return;
        }
        RememberStepUndo();
        int index = _steps.IndexOf(originals[^1]) + 1;
        DiagnosticCaseStep? first = null;
        foreach (var original in originals)
        {
            var copy = original.Clone(newIdentity: true);
            copy.Name += " · 副本";
            copy.LastState = "未运行";
            _steps.Insert(index++, copy);
            first ??= copy;
        }
        SyncCaseSteps();
        _workspaceDirty = true;
        if (first is not null) LoadStep(first);
        UpdateLibraryStatus();
    }

    private void DeleteStep_Click(object sender, RoutedEventArgs e)
    {
        if (_caseRunning || _loadedCase is null) return;
        var selected = SelectedStepsForEdit();
        if (selected.Count == 0) return;
        if (!ConfirmUnsavedBeforeLeave()) return;
        selected = SelectedStepsForEdit();
        if (MessageBox.Show(this,
                "确认删除选中的 " + selected.Count + " 条诊断步骤？可使用撤销恢复。",
                "删除步骤", MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        RememberStepUndo();
        int firstIndex = _steps.IndexOf(selected[0]);
        foreach (var step in selected) _steps.Remove(step);
        SyncCaseSteps();
        if (_steps.Count > 0)
            LoadStep(_steps[Math.Min(firstIndex, _steps.Count - 1)]);
        else ClearStepEditor();
        _workspaceDirty = true;
        UpdateLibraryStatus();
    }

    private void MoveStep(int delta)
    {
        if (_caseRunning || _loadedCase is null || !ConfirmUnsavedBeforeLeave()) return;
        var selected = SelectedStepsForEdit();
        if (selected.Count != 1)
        {
            CaseRunStatus.Text = "移动顺序时请只选中一条步骤。";
            return;
        }
        int from = _steps.IndexOf(selected[0]), to = from + delta;
        if (from < 0 || to < 0 || to >= _steps.Count) return;
        RememberStepUndo();
        _steps.Move(from, to);
        SyncCaseSteps();
        _workspaceDirty = true;
        UpdateLibraryStatus();
    }

    private bool HandleEditingShortcut(KeyEventArgs e)
    {
        // Do not steal normal TextBox undo/copy/paste/delete/arrow behavior.
        if (Keyboard.FocusedElement is TextBoxBase or PasswordBox) return false;
        var mods = Keyboard.Modifiers;
        bool ctrl = mods == ModifierKeys.Control;
        bool ctrlShift = mods == (ModifierKeys.Control | ModifierKeys.Shift);

        if (ctrlShift && e.Key == Key.N)
        {
            AddStep_Click(this, new RoutedEventArgs());
            return true;
        }
        if (ctrl && e.Key == Key.N)
        {
            NewCase_Click(this, new RoutedEventArgs());
            return true;
        }
        if (ctrl && e.Key == Key.D && StepsList.IsKeyboardFocusWithin)
        {
            CopyStep_Click(this, new RoutedEventArgs());
            return true;
        }
        if (ctrl && e.Key == Key.C && StepsList.IsKeyboardFocusWithin)
        {
            CopyStepsToClipboard_Click(this, new RoutedEventArgs());
            return true;
        }
        if (ctrl && e.Key == Key.V && StepsList.IsKeyboardFocusWithin)
        {
            PasteSteps_Click(this, new RoutedEventArgs());
            return true;
        }
        if (ctrl && e.Key == Key.Z && StepsList.IsKeyboardFocusWithin)
        {
            UndoSteps_Click(this, new RoutedEventArgs());
            return true;
        }
        if (ctrl && e.Key == Key.Y && StepsList.IsKeyboardFocusWithin)
        {
            RedoSteps_Click(this, new RoutedEventArgs());
            return true;
        }
        if (ctrl && e.Key == Key.Up && StepsList.IsKeyboardFocusWithin)
        {
            MoveStep(-1);
            return true;
        }
        if (ctrl && e.Key == Key.Down && StepsList.IsKeyboardFocusWithin)
        {
            MoveStep(1);
            return true;
        }
        if (mods == ModifierKeys.None && e.Key == Key.Delete)
        {
            if (StepsList.IsKeyboardFocusWithin)
            {
                DeleteStep_Click(this, new RoutedEventArgs());
                return true;
            }
            if (CasesList.IsKeyboardFocusWithin)
            {
                DeleteCase_Click(this, new RoutedEventArgs());
                return true;
            }
        }
        if (mods == ModifierKeys.None && e.Key == Key.F2)
        {
            if (StepsList.IsKeyboardFocusWithin)
            {
                RenameStep_Click(this, new RoutedEventArgs());
                return true;
            }
            if (CasesList.IsKeyboardFocusWithin)
            {
                RenameCase_Click(this, new RoutedEventArgs());
                return true;
            }
        }
        return false;
    }
}
