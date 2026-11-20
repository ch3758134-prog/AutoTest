using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AutoTest.Specialists.Uds.Workbench;

namespace AutoTest.UdsLab;

/// <summary>
/// The UDS Lab manages ordered diagnostic SERVICE CALLS only, not its own
/// TEST DSL. Each service executes through the existing physical UDS client,
/// remains bounded by the DEVICE fence, and records a real exchange.
/// </summary>
public partial class MainWindow
{
    private readonly ObservableCollection<DiagnosticCase> _cases = [];
    private readonly ObservableCollection<DiagnosticCaseStep> _steps = [];
    private readonly Dictionary<string, byte[]> _caseVariables =
        new(StringComparer.OrdinalIgnoreCase);
    private DiagnosticCase? _loadedCase;
    private DiagnosticCaseStep? _loadedStep;
    private bool _editorDirty;
    private bool _workspaceDirty;
    private bool _suppressCaseSelection;
    private bool _suppressStepSelection;
    private bool _loadingWorkspace;
    private bool _caseRunning;
    private int _nextStepIndex;

    private void InitializeCases()
    {
        CasesList.ItemsSource = _cases;
        StepsList.ItemsSource = _steps;
        var diagnosticCase = DiagnosticStepPresets.CreateWelcomeCase();
        _cases.Add(diagnosticCase);
        LoadCase(diagnosticCase);
        _editorDirty = false;
        _workspaceDirty = false;
        UpdateLibraryStatus();
    }

    private void UpdateLibraryStatus()
    {
        if (LibraryStatus is null) return;
        LibraryStatus.Text = _cases.Count + " 个用例 · " +
            (_editorDirty ? "步骤编辑尚未保存" :
             _workspaceDirty ? "文件有未保存修改" :
             _workspacePath is null ? "尚未建立配置文件" : "已保存到文件");
        Title = (_workspaceDirty || _editorDirty ? "* " : "") + "AutoTest · UDS 诊断用例";
        StepCountStatus.Text = _steps.Count + " 个步骤 · " +
            (_loadedCase?.Name ?? "未选择用例");
    }

    private void MarkDraftEdited()
    {
        if (!_ready || _settingEditor || _loadingWorkspace) return;
        _editorDirty = true;
        DraftStatus.Text = "步骤修改未保存 · 可先发送或保存";
        UpdateLibraryStatus();
    }

    private void EditorDraftChanged(object sender, TextChangedEventArgs e) => MarkDraftEdited();
    private void CaseStepDraftChanged(object sender, TextChangedEventArgs e) => MarkDraftEdited();
    private void CaseStepFlagChanged(object sender, RoutedEventArgs e) => MarkDraftEdited();

    private void WorkspaceParameterChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready || _loadingWorkspace) return;
        _workspaceDirty = true;
        UpdateLibraryStatus();
    }

    private void CaseMetadataChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready || _loadingWorkspace || _suppressCaseSelection || _loadedCase is null) return;
        _loadedCase.Name = string.IsNullOrWhiteSpace(CaseNameBox.Text)
            ? "未命名诊断用例" : CaseNameBox.Text.Trim();
        _loadedCase.Description = CaseDescriptionBox.Text;
        _workspaceDirty = true;
        UpdateLibraryStatus();
    }

    private void CaseSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready || CasesList.ItemsSource is null) return;
        string term = CaseSearchBox.Text.Trim();
        var view = CollectionViewSource.GetDefaultView(_cases);
        view.Filter = x => x is DiagnosticCase c &&
            (term.Length == 0 || c.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
             c.Description.Contains(term, StringComparison.OrdinalIgnoreCase));
        _suppressCaseSelection = true;
        try { view.Refresh(); }
        finally { _suppressCaseSelection = false; }
    }

    private DiagnosticCaseStep CaptureCurrentStepDraft()
    {
        if (_loadedStep is null)
            throw new InvalidOperationException("请先选择一条诊断步骤。");
        var action = CaptureEditor(allowEmpty: true);
        var candidate = _loadedStep.Clone(newIdentity: false);
        candidate.Name = string.IsNullOrWhiteSpace(OperationNameBox.Text)
            ? action.Name : OperationNameBox.Text.Trim();
        action.Id = _loadedStep.Action.Id;
        candidate.Action = action;
        candidate.Purpose = PurposeBox.Text.Trim();
        candidate.PauseBefore = PauseBeforeCheck.IsChecked == true;
        candidate.RequestTemplate = RequestTemplateBox.Text.Trim();
        candidate.CaptureAs = CaptureNameBox.Text.Trim();
        if (!int.TryParse(CaptureOffsetBox.Text, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int offset) ||
            !int.TryParse(CaptureLengthBox.Text, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int length))
            throw new FormatException("变量偏移和长度必须为有效整数。");
        candidate.CaptureOffset = offset;
        candidate.CaptureLength = length;
        DiagnosticCaseVariables.Validate(candidate);
        return candidate;
    }

    private bool SaveCurrentStepCore()
    {
        if (_loadedStep is null) return true;
        try
        {
            DiagnosticCaseStep candidate = CaptureCurrentStepDraft();
            _loadedStep.Name = candidate.Name;
            _loadedStep.Action = candidate.Action;
            _loadedStep.Purpose = candidate.Purpose;
            _loadedStep.PauseBefore = candidate.PauseBefore;
            _loadedStep.RequestTemplate = candidate.RequestTemplate;
            _loadedStep.CaptureAs = candidate.CaptureAs;
            _loadedStep.CaptureOffset = candidate.CaptureOffset;
            _loadedStep.CaptureLength = candidate.CaptureLength;
            _workspaceDirty = true;
            _editorDirty = false;
            _suppressStepSelection = true;
            try
            {
                StepsList.Items.Refresh();
                StepsList.SelectedItem = _loadedStep;
            }
            finally { _suppressStepSelection = false; }
            DraftStatus.Text = "步骤已保存到当前用例 · Ctrl+S 写入文件";
            UpdateLibraryStatus();
            AppendAudit("CASE_STEP_SAVE", (_loadedCase?.Name ?? "") + "/" + _loadedStep.Name);
            return true;
        }
        catch (Exception ex)
        {
            ShowError("步骤编辑内容无效", ex);
            return false;
        }
    }

    private bool ConfirmUnsavedBeforeLeave()
    {
        // Switching services applies edits in the in-memory document; only
        // Ctrl+S / 保存用例文件 writes to disk.
        return !_editorDirty || SaveCurrentStepCore();
    }

    private void SelectCaseQuietly(DiagnosticCase? item)
    {
        _suppressCaseSelection = true;
        try { CasesList.SelectedItem = item; }
        finally { _suppressCaseSelection = false; }
    }

    private void SelectStepQuietly(DiagnosticCaseStep? item)
    {
        _suppressStepSelection = true;
        try { StepsList.SelectedItem = item; }
        finally { _suppressStepSelection = false; }
    }

    private void SyncCaseSteps()
    {
        if (_loadedCase is not null)
            _loadedCase.Steps = _steps.ToList();
    }

    private void LoadCase(DiagnosticCase diagnosticCase)
    {
        _loadedCase = diagnosticCase;
        SelectCaseQuietly(diagnosticCase);
        _suppressCaseSelection = true;
        try
        {
            CaseNameBox.Text = diagnosticCase.Name;
            CaseDescriptionBox.Text = diagnosticCase.Description;
        }
        finally { _suppressCaseSelection = false; }
        _suppressStepSelection = true;
        try
        {
            _steps.Clear();
            foreach (var step in diagnosticCase.Steps)
            {
                step.LastState = "未运行";
                _steps.Add(step);
            }
            StepsList.SelectedItem = null;
        }
        finally { _suppressStepSelection = false; }
        _loadedStep = null;
        _caseVariables.Clear();
        _nextStepIndex = 0;
        RefreshCaseVariables();
        if (_steps.Count > 0)
            LoadStep(_steps[0]);
        else ClearStepEditor();
        UpdateLibraryStatus();
    }

    private void LoadStep(DiagnosticCaseStep step)
    {
        _loadedStep = step;
        SelectStepQuietly(step);
        LoadAction(step.Action);
        _settingEditor = true;
        try
        {
            OperationNameBox.Text = step.Name;
            PurposeBox.Text = step.Purpose;
            RequestTemplateBox.Text = step.RequestTemplate;
            PauseBeforeCheck.IsChecked = step.PauseBefore;
            CaptureNameBox.Text = step.CaptureAs;
            CaptureOffsetBox.Text = step.CaptureOffset.ToString(CultureInfo.InvariantCulture);
            CaptureLengthBox.Text = step.CaptureLength.ToString(CultureInfo.InvariantCulture);
        }
        finally { _settingEditor = false; }
        _editorDirty = false;
        DraftStatus.Text = "编辑步骤：" + step.Name;
        UpdateLibraryStatus();
    }

    private void ClearStepEditor()
    {
        _loadedStep = null;
        SelectStepQuietly(null);
        _settingEditor = true;
        try
        {
            OperationNameBox.Text = "";
            OperationGroupBox.Text = "";
            PurposeBox.Text = "";
            RawModeCheck.IsChecked = false;
            ServiceCombo.SelectedItem = UdsOperationTemplates.Find(0x22);
            PrimaryBox.Text = AuxiliaryBox.Text = RequestBox.Text = ExpectedBox.Text = "";
            SelectDecoder("Hex");
            OffsetBox.Text = "3";
            ScaleBox.Text = "1";
            UnitBox.Text = "";
            RequestTemplateBox.Text = CaptureNameBox.Text = "";
            PauseBeforeCheck.IsChecked = false;
            CaptureOffsetBox.Text = "3";
            CaptureLengthBox.Text = "0";
        }
        finally { _settingEditor = false; }
        RefreshTemplate();
        RefreshButtons();
        _editorDirty = false;
        DraftStatus.Text = "点击“添加服务步骤”后开始编辑";
    }

    private void CaseSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _loadingWorkspace || _suppressCaseSelection) return;
        var selected = CasesList.SelectedItem as DiagnosticCase;
        if (selected is null || ReferenceEquals(selected, _loadedCase)) return;
        if (_caseRunning || !ConfirmUnsavedBeforeLeave())
        {
            SelectCaseQuietly(_loadedCase);
            return;
        }
        SyncCaseSteps();
        LoadCase(selected);
    }

    private void CaseStepSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _loadingWorkspace || _suppressStepSelection) return;
        var selected = StepsList.SelectedItem as DiagnosticCaseStep;
        if (selected is null || ReferenceEquals(selected, _loadedStep)) return;
        if (_caseRunning || !ConfirmUnsavedBeforeLeave())
        {
            SelectStepQuietly(_loadedStep);
            return;
        }
        LoadStep(selected);
        _nextStepIndex = Math.Max(0, _steps.IndexOf(selected));
    }

    private void NewCase_Click(object sender, RoutedEventArgs e)
    {
        if (_caseRunning || !ConfirmUnsavedBeforeLeave()) return;
        SyncCaseSteps();
        if (_cases.Count >= 100) { MessageBox.Show(this, "最多 100 个诊断用例。"); return; }
        var c = new DiagnosticCase { Name = "未命名诊断用例 " + (_cases.Count + 1) };
        _cases.Add(c);
        CaseSearchBox.Text = "";
        LoadCase(c);
        _workspaceDirty = true;
        UpdateLibraryStatus();
    }

    private void CopyCase_Click(object sender, RoutedEventArgs e)
    {
        if (_caseRunning || _loadedCase is null || !ConfirmUnsavedBeforeLeave()) return;
        SyncCaseSteps();
        if (_cases.Count >= 100) { MessageBox.Show(this, "最多 100 个诊断用例。"); return; }
        var copy = _loadedCase.Clone();
        _cases.Add(copy);
        CaseSearchBox.Text = "";
        LoadCase(copy);
        _workspaceDirty = true;
        UpdateLibraryStatus();
    }

    private void DeleteCase_Click(object sender, RoutedEventArgs e)
    {
        if (_caseRunning || _loadedCase is null) return;
        if (MessageBox.Show(this, "删除用例“" + _loadedCase.Name + "”及其全部步骤？",
                "删除用例", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        _cases.Remove(_loadedCase);
        _workspaceDirty = true;
        if (_cases.Count == 0)
        {
            var empty = new DiagnosticCase { Name = "未命名诊断用例" };
            _cases.Add(empty);
        }
        LoadCase(_cases[0]);
    }

    // The user chooses WHAT to diagnose before editing SID/DID/PDU parameters.
    private void AddStep_Click(object sender, RoutedEventArgs e)
    {
        if (_caseRunning || _loadedCase is null) return;
        if (AddServiceButton.ContextMenu is { } menu)
        {
            menu.PlacementTarget = AddServiceButton;
            menu.IsOpen = true;
        }
    }

    private void AddStepChoice_Click(object sender, RoutedEventArgs e)
    {
        if (_caseRunning || _loadedCase is null || !ConfirmUnsavedBeforeLeave()) return;
        if (sender is not MenuItem { Tag: string key }) return;
        if (_steps.Count >= 150)
        {
            MessageBox.Show(this, "每个诊断用例最多 150 个诊断动作。");
            return;
        }
        DiagnosticCaseStep step;
        try { step = DiagnosticStepPresets.Create(key); }
        catch (Exception ex) { ShowError("添加诊断动作失败", ex); return; }
        int selectedIndex = StepsList.SelectedIndex;
        int index = selectedIndex < 0 ? _steps.Count :
            Math.Clamp(selectedIndex + 1, 0, _steps.Count);
        _steps.Insert(index, step);
        SyncCaseSteps();
        LoadStep(step);
        _workspaceDirty = true;
        UpdateLibraryStatus();
        AppendAudit("CASE_STEP_ADD", (_loadedCase?.Name ?? "") + " · " + step.Name);
    }

    private void CopyStep_Click(object sender, RoutedEventArgs e)
    {
        if (_caseRunning || _loadedStep is null || !ConfirmUnsavedBeforeLeave()) return;
        if (_steps.Count >= 150) { MessageBox.Show(this, "每个用例最多 150 个步骤。"); return; }
        int index = _steps.IndexOf(_loadedStep);
        var copy = _loadedStep.Clone(newIdentity: true);
        copy.Name += " · 副本";
        _steps.Insert(index + 1, copy);
        SyncCaseSteps();
        LoadStep(copy);
        _workspaceDirty = true;
        UpdateLibraryStatus();
    }

    private void DeleteStep_Click(object sender, RoutedEventArgs e)
    {
        if (_caseRunning || _loadedStep is null) return;
        int index = _steps.IndexOf(_loadedStep);
        if (index < 0) return;
        _steps.RemoveAt(index);
        SyncCaseSteps();
        if (_steps.Count > 0) LoadStep(_steps[Math.Min(index, _steps.Count - 1)]);
        else ClearStepEditor();
        _workspaceDirty = true;
        UpdateLibraryStatus();
    }

    private void MoveStepUp_Click(object sender, RoutedEventArgs e) => MoveStep(-1);
    private void MoveStepDown_Click(object sender, RoutedEventArgs e) => MoveStep(1);
    private void MoveStep(int delta)
    {
        if (_caseRunning || _loadedStep is null || !ConfirmUnsavedBeforeLeave()) return;
        int oldIndex = _steps.IndexOf(_loadedStep), newIndex = oldIndex + delta;
        if (newIndex < 0 || newIndex >= _steps.Count) return;
        _steps.Move(oldIndex, newIndex);
        SyncCaseSteps();
        _workspaceDirty = true;
        UpdateLibraryStatus();
    }

    private void StepEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready || _loadingWorkspace || _suppressStepSelection || _caseRunning) return;
        if (sender is FrameworkElement { IsLoaded: true, DataContext: DiagnosticCaseStep })
        {
            _workspaceDirty = true;
            UpdateLibraryStatus();
        }
    }

    private void SaveStep_Click(object sender, RoutedEventArgs e) => SaveCurrentStepCore();

    private void RefreshCaseVariables()
    {
        if (CaseVariableStatus is null) return;
        CaseVariableStatus.Text = _caseVariables.Count == 0 ? "本次执行变量：无" :
            "本次执行变量：" + string.Join(" · ",
                _caseVariables.Select(v => v.Key + "=" + UdsHex.Format(v.Value)));
    }

    private void ResetCaseVariables()
    {
        _caseVariables.Clear();
        _nextStepIndex = 0;
        RefreshCaseVariables();
    }

    private void RefreshCaseControls()
    {
        if (!_ready || CasesList is null) return;
        CasesList.IsEnabled = !_caseRunning;
        StepsList.IsEnabled = !_caseRunning;
        // No editing inputs should appear usable until the user has selected
        // an actual diagnosis action from the steps list.
        // Keep the editor surface active during a running request so STOP
        // remains clickable. In-flight request parameters are already frozen.
        StepEditorPanel.IsEnabled = _loadedStep is not null;
        RunNextButton.IsEnabled = !_caseRunning && _steps.Count > 0 &&
            !_needsReconnect && _pendingCancel is null && _hardware?.IsReady == true;
        RunFromButton.IsEnabled = RunNextButton.IsEnabled;
        RunCaseButton.IsEnabled = RunNextButton.IsEnabled;
    }
}
