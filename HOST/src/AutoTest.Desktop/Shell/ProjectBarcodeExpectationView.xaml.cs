using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using AutoTest.Desktop.Projects;
using AutoTest.Projects.Project;
using Prism.Mvvm;

namespace AutoTest.Desktop.Shell;

public sealed class BarcodeExpectationEditorRow : BindableBase
{
    private string _name, _purpose, _expected, _actual = "";
    private int _position;
    public BarcodeExpectationEditorRow(ProjectBarcodeField field)
    { _name = field.Name; _purpose = field.Purpose; _expected = field.Expected; }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string Purpose
    {
        get => _purpose;
        set
        {
            if (SetProperty(ref _purpose, value) && value != ProjectBarcodeExpectation.Match)
                Expected = "";
        }
    }
    public string Expected { get => _expected; set => SetProperty(ref _expected, value); }
    public string Actual { get => _actual; set => SetProperty(ref _actual, value); }
    public int Position { get => _position; set => SetProperty(ref _position, value); }
    public ProjectBarcodeField ToField() => new(Name, Purpose, Expected);
}

/// <summary>Project-owned editor. Uses the same autosave/close gate as TEST
/// and the same matcher as the production barcode preflight.</summary>
public partial class ProjectBarcodeExpectationView : UserControl
{
    private readonly ProjectWorkspaceService _workspace;
    private readonly MainWindowViewModel _shell;
    private readonly Guid _projectId;
    private readonly string _manifestPath;
    private readonly ObservableCollection<BarcodeExpectationEditorRow> _rows = new();
    private bool _initializing = true;
    private long _editRevision;
    private string? _savedExpectation;
    public bool IsDirty { get; private set; }
    public bool IsBusy { get; private set; }

    public ProjectBarcodeExpectationView(MainWindowViewModel shell, ProjectWorkspaceService workspace)
    {
        _shell = shell;
        _workspace = workspace;
        var current = workspace.Current ?? throw new InvalidOperationException("请先打开 ECU 工程。");
        _projectId = current.Definition.Id.Value;
        _manifestPath = current.ManifestPath;
        _savedExpectation = Serialize(current.Definition.BarcodeExpectation);
        InitializeComponent();
        ProjectLabel.Text = current.Definition.Name + " · 工程检测准备";
        var draft = current.Definition.BarcodeExpectation ??
            ProjectBarcodeExpectation.CreateDraft(current.Definition.EcuModelCode);
        SeparatorBox.Text = draft.Separator;
        foreach (var field in draft.Fields) AddRow(field);
        FieldGrid.ItemsSource = _rows;
        Renumber();
        _initializing = false;
        SaveStatus.Text = current.Definition.BarcodeExpectation is null
            ? "填写型号、硬件版本和字段顺序后自动保存；无需先扫码。"
            : "已读取当前工程保存的扫码期望。";
    }

    private static string? Serialize(ProjectBarcodeExpectation? rule) =>
        rule is null ? null : System.Text.Json.JsonSerializer.Serialize(rule);

    private void AddRow(ProjectBarcodeField field)
    {
        var row = new BarcodeExpectationEditorRow(field);
        row.PropertyChanged += RowChanged;
        _rows.Add(row);
    }
    private void RowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BarcodeExpectationEditorRow.Name) or
            nameof(BarcodeExpectationEditorRow.Purpose) or nameof(BarcodeExpectationEditorRow.Expected))
            MarkDirty();
    }
    private void Renumber()
    {
        for (var i = 0; i < _rows.Count; i++) _rows[i].Position = i + 1;
    }
    private void MarkDirty()
    {
        if (_initializing) return;
        IsDirty = true;
        _editRevision++;
        SaveStatus.Text = "配置已修改，等待自动保存。";
        MatchStatus.Text = "规则已修改，请重新验证。";
        foreach (var row in _rows) row.Actual = "";
    }
    private void SeparatorChanged(object sender, TextChangedEventArgs e) => MarkDirty();
    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (_rows.Count >= 32) { SaveStatus.Text = "最多 32 个字段。"; return; }
        var n = 1;
        while (_rows.Any(r => string.Equals(r.Name, "Custom" + n, StringComparison.OrdinalIgnoreCase))) n++;
        AddRow(new("Custom" + n, ProjectBarcodeExpectation.Record));
        Renumber(); FieldGrid.SelectedItem = _rows.Last(); MarkDirty();
    }
    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (FieldGrid.SelectedItem is not BarcodeExpectationEditorRow row) return;
        if (row.Name is ProjectBarcodeExpectation.Model or ProjectBarcodeExpectation.Hardware or ProjectBarcodeExpectation.Serial)
        { SaveStatus.Text = "产品型号、硬件版本和 SN 是必需字段，不可删除。"; return; }
        row.PropertyChanged -= RowChanged;
        _rows.Remove(row); Renumber(); MarkDirty();
    }
    private void Move(int delta)
    {
        if (FieldGrid.SelectedItem is not BarcodeExpectationEditorRow row) return;
        var old = _rows.IndexOf(row);
        var next = old + delta;
        if (next < 0 || next >= _rows.Count) return;
        _rows.Move(old, next); Renumber(); FieldGrid.SelectedItem = row; MarkDirty();
    }
    private void Up_Click(object sender, RoutedEventArgs e) => Move(-1);
    private void Down_Click(object sender, RoutedEventArgs e) => Move(1);
    private void OpenDevice_Click(object sender, RoutedEventArgs e) => _shell.OpenSerialScannerCommand.Execute();
    private void UseLast_Click(object sender, RoutedEventArgs e) =>
        SampleBox.Text = _shell.ScannerSetupModel.LastRaw;

    private ProjectBarcodeExpectation Draft() => new(SeparatorBox.Text,
        _rows.Select(x => x.ToField()).ToImmutableArray());

    private void CommitGrid()
    {
        FieldGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        FieldGrid.CommitEdit(DataGridEditingUnit.Row, true);
    }

    private void Verify_Click(object sender, RoutedEventArgs e)
    {
        CommitGrid();
        var draft = Draft();
        var model = draft.Fields.FirstOrDefault(x => x.Name == ProjectBarcodeExpectation.Model)?.Expected;
        var result = draft.Evaluate(SampleBox.Text, model);
        foreach (var row in _rows) row.Actual = result.Fields.GetValueOrDefault(row.Name) ?? "";
        MatchStatus.Text = (result.Accepted ? "匹配通过｜" : "匹配未通过｜") + result.Explanation;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        // Explicit Save can validate a default/new draft even before its first edit.
        CommitGrid();
        IsDirty = true;
        _editRevision++;
        await SaveForCloseAsync();
    }

    public async Task<bool> SaveForCloseAsync()
    {
        if (IsBusy) return false;
        if (!IsDirty) return true;
        // All editable bindings update the source as the user types; an idle
        // save must not end the active DataGrid edit or steal keyboard focus.
        IsBusy = true;
        var revision = _editRevision;
        try
        {
            var current = _workspace.Current;
            if (current is null || current.Definition.Id.Value != _projectId ||
                !string.Equals(current.ManifestPath, _manifestPath, StringComparison.OrdinalIgnoreCase) ||
                Serialize(current.Definition.BarcodeExpectation) != _savedExpectation)
                throw new InvalidOperationException("工程或已保存扫码期望已经改变，请重新打开检测准备。");
            var draft = Draft();
            await _workspace.SaveBarcodeExpectationAsync(_projectId, _manifestPath, _savedExpectation, draft);
            _savedExpectation = Serialize(draft);
            IsDirty = revision != _editRevision;
            SaveStatus.Text = IsDirty ? "较新的编辑尚未保存，将在下次自动保存处理。"
                : "已保存至工程。用于正式生产前请重新部署工程；不需要试扫。";
            _ = _shell.RefreshEngineeringSetupAsync();
            return !IsDirty;
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or
            ArgumentException or ProjectDataException or UnauthorizedAccessException or FormatException)
        {
            SaveStatus.Text = "尚未保存：" + error.Message;
            return false;
        }
        finally { IsBusy = false; }
    }
}
