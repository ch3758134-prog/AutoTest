using System.Windows;
using System.Windows.Controls;

namespace AutoTest.UdsLab;

/// <summary>
/// Saved diagnostic operations are actual editable project resources, not
/// one-way request "favorites". Edits remain a draft until saved to the
/// operation library; the library persists to the workspace JSON file.
/// </summary>
public partial class MainWindow
{
    private DiagnosticAction? _loadedOperation;
    private bool _editorDirty;
    private bool _workspaceDirty;
    private bool _suppressOperationSelection;
    private bool _loadingWorkspace;

    private void UpdateLibraryStatus()
    {
        if (LibraryStatus is null) return;
        LibraryStatus.Text = _favorites.Count + " 个操作 · " +
            (_editorDirty ? "当前编辑未保存" :
             _workspaceDirty ? "配置有未写盘修改" :
             _workspacePath is null ? "尚未建立配置文件" : "已保存到配置文件");
        Title = (_workspaceDirty || _editorDirty ? "* " : "") + "AutoTest · UDS 诊断";
    }

    private void MarkDraftEdited()
    {
        if (!_ready || _settingEditor) return;
        _editorDirty = true;
        DraftStatus.Text = _loadedOperation is null
            ? "新操作 · 尚未加入操作库"
            : "修改未保存 · " + _loadedOperation.Name;
        UpdateLibraryStatus();
    }

    private void OperationSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (OperationsList is null || OperationsList.ItemsSource is null) return;
        string text = OperationSearchBox.Text.Trim();
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_favorites);
        view.Filter = value => value is DiagnosticAction a &&
            (text.Length == 0 ||
             a.Name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
             a.Group.Contains(text, StringComparison.OrdinalIgnoreCase) ||
             a.RequestHex.Contains(text, StringComparison.OrdinalIgnoreCase));
        // Filtering the list must not discard the draft currently being edited
        // merely because its saved item was temporarily hidden.
        _suppressOperationSelection = true;
        try { view.Refresh(); }
        finally { _suppressOperationSelection = false; }
    }

    private void EditorDraftChanged(object sender, TextChangedEventArgs e) => MarkDraftEdited();

    private void WorkspaceParameterChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready || _loadingWorkspace) return;
        _workspaceDirty = true;
        UpdateLibraryStatus();
    }

    private static void CopyActionFields(DiagnosticAction destination, DiagnosticAction source)
    {
        // Id is intentionally stable for updates, new for copies.
        destination.Name = source.Name;
        destination.Group = source.Group;
        destination.RequestHex = source.RequestHex;
        destination.ExpectedPrefixHex = source.ExpectedPrefixHex;
        destination.ServiceSidHex = source.ServiceSidHex;
        destination.PrimaryHex = source.PrimaryHex;
        destination.AuxiliaryHex = source.AuxiliaryHex;
        destination.RawMode = source.RawMode;
        destination.Decoder = source.Decoder;
        destination.DataOffset = source.DataOffset;
        destination.Scale = source.Scale;
        destination.Unit = source.Unit;
    }

    private void SelectOperationWithoutEvent(DiagnosticAction? operation)
    {
        _suppressOperationSelection = true;
        try { OperationsList.SelectedItem = operation; }
        finally { _suppressOperationSelection = false; }
    }

    private bool SaveOperationCore(bool asNew, bool selectAfter)
    {
        DiagnosticAction current;
        try { current = CaptureEditor(allowEmpty: true); }
        catch (Exception error)
        {
            ShowError("操作未保存，请先修正请求格式", error);
            return false;
        }
        if (_loadedOperation is not null && !asNew)
        {
            CopyActionFields(_loadedOperation, current);
            current = _loadedOperation;
        }
        else
        {
            if (_favorites.Count >= 500)
            {
                MessageBox.Show(this, "最多保存 500 个诊断操作。", "操作库已满");
                return false;
            }
            if (asNew && _favorites.Any(a => a.Name == current.Name))
                current.Name += " · 副本";
            _favorites.Add(current);
        }
        if (selectAfter)
        {
            OperationSearchBox.Text = "";
            SelectOperationWithoutEvent(current);
        }
        _loadedOperation = current;
        if (selectAfter)
        {
            _settingEditor = true;
            try
            {
                OperationNameBox.Text = current.Name;
                OperationGroupBox.Text = current.Group == "未分类" ? "" : current.Group;
            }
            finally { _settingEditor = false; }
        }
        _editorDirty = false;
        _workspaceDirty = true;
        DraftStatus.Text = "操作已更新到库 · 点击顶部保存配置即可写入文件";
        OperationsList.Items.Refresh();
        OperationsList.ScrollIntoView(current);
        UpdateLibraryStatus();
        AppendAudit("ACTION_SAVE", "保存或更新诊断操作 " + current.Name + " · " + current.RequestHex);
        return true;
    }

    private bool ConfirmUnsavedBeforeLeave()
    {
        if (!_editorDirty) return true;
        var result = MessageBox.Show(this,
            "当前操作有尚未保存的编辑。\n\n是：保存到操作库\n否：丢弃本次编辑\n取消：继续编辑",
            "离开当前操作", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (result == MessageBoxResult.Cancel) return false;
        if (result == MessageBoxResult.Yes && !SaveOperationCore(asNew: false, selectAfter: false))
            return false;
        _editorDirty = false;
        UpdateLibraryStatus();
        return true;
    }

    private void OperationSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _settingEditor || _suppressOperationSelection) return;
        var selected = OperationsList.SelectedItem as DiagnosticAction;
        if (ReferenceEquals(selected, _loadedOperation)) return;
        var old = _loadedOperation;
        if (!ConfirmUnsavedBeforeLeave())
        {
            SelectOperationWithoutEvent(old);
            return;
        }
        _loadedOperation = selected;
        if (selected is not null)
        {
            LoadAction(selected);
            DraftStatus.Text = "正在编辑：" + selected.Name + " · 保存修改后可以重新打开";
        }
        else DraftStatus.Text = "当前未选择已保存操作";
        _editorDirty = false;
        UpdateLibraryStatus();
    }

    private void ResetNewOperationEditor()
    {
        _loadedOperation = null;
        SelectOperationWithoutEvent(null);
        _settingEditor = true;
        try
        {
            OperationNameBox.Text = "";
            OperationGroupBox.Text = "";
            RawModeCheck.IsChecked = false;
            ServiceCombo.SelectedItem = UdsOperationTemplates.Find(0x22);
            PrimaryBox.Text = "";
            AuxiliaryBox.Text = "";
            RequestBox.Text = "";
            ExpectedBox.Text = "";
            SelectDecoder("Hex");
            OffsetBox.Text = "3";
            ScaleBox.Text = "1";
            UnitBox.Text = "";
        }
        finally { _settingEditor = false; }
        _editorDirty = false;
        RefreshTemplate();
        RefreshButtons();
        DraftStatus.Text = "空白草稿 · 可编辑、调试或保存为诊断操作";
        UpdateLibraryStatus();
    }

    private void NewOperation_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmUnsavedBeforeLeave()) return;
        ResetNewOperationEditor();
        OperationNameBox.Focus();
    }

    private void SaveAction_Click(object sender, RoutedEventArgs e)
        => SaveOperationCore(asNew: false, selectAfter: true);

    private void SaveAsAction_Click(object sender, RoutedEventArgs e)
        => SaveOperationCore(asNew: true, selectAfter: true);

    private void DuplicateAction_Click(object sender, RoutedEventArgs e)
        => SaveOperationCore(asNew: true, selectAfter: true);

    private void DeleteAction_Click(object sender, RoutedEventArgs e)
    {
        var item = OperationsList.SelectedItem as DiagnosticAction ?? _loadedOperation;
        if (item is null) return;
        if (MessageBox.Show(this, "删除诊断操作「" + item.Name + "」？",
                "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _suppressOperationSelection = true;
        try
        {
            _favorites.Remove(item);
            OperationsList.SelectedItem = null;
            _loadedOperation = null;
        }
        finally { _suppressOperationSelection = false; }
        _workspaceDirty = true;
        ResetNewOperationEditor();
        DraftStatus.Text = "操作已删除，可新建或选择其他操作";
        UpdateLibraryStatus();
        AppendAudit("ACTION_DELETE", "从操作库删除 " + item.Name);
    }

    private void MoveActionUp_Click(object sender, RoutedEventArgs e) => MoveOperation(-1);
    private void MoveActionDown_Click(object sender, RoutedEventArgs e) => MoveOperation(1);

    private void MoveOperation(int delta)
    {
        if (OperationsList.SelectedItem is not DiagnosticAction item) return;
        int oldIndex = _favorites.IndexOf(item), newIndex = oldIndex + delta;
        if (newIndex < 0 || newIndex >= _favorites.Count) return;
        _favorites.Move(oldIndex, newIndex);
        _workspaceDirty = true;
        UpdateLibraryStatus();
    }
}
