using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using AutoTest.Projects.Project;
using Prism.Commands;
using Prism.Mvvm;

namespace AutoTest.Desktop.Projects;

/// <summary>
/// Edits the unique project manifest-pinned station procedure. This is an
/// authored DRAFT only; it cannot send USB commands or certify a fixture.
/// Production must explicitly freeze and execute the same pinned revision.
/// </summary>
public sealed class InspectionStationProcedureEditorViewModel : BindableBase
{
    public sealed class ActionEdit : BindableBase
    {
        public Guid ActionId { get; }
        private string _kind;
        private int _waitAfterMilliseconds;
        public ActionEdit(InspectionStationAction action)
        {
            ActionId = action.ActionId;
            _kind = action.Kind;
            _waitAfterMilliseconds = action.WaitAfterMilliseconds;
        }
        public string Kind { get => _kind; set => SetProperty(ref _kind, value); }
        public int WaitAfterMilliseconds
        {
            get => _waitAfterMilliseconds;
            set => SetProperty(ref _waitAfterMilliseconds, value);
        }
        public InspectionStationAction ToDefinition() =>
            new(ActionId, Kind, WaitAfterMilliseconds);
    }

    private readonly ProjectWorkspaceService _workspace;
    private ProjectConfigurationSession _session;
    private readonly System.Windows.Threading.DispatcherTimer _saveTimer =
        new() { Interval = TimeSpan.FromMilliseconds(750) };
    private string _saveStatus = "已加载工程工位动作草稿。";
    private bool _dirty;
    private bool _saving;
    private bool _closed;
    private long _editGeneration;
    public ObservableCollection<ActionEdit> BeforeStart { get; } = new();
    public ObservableCollection<ActionEdit> AfterFinish { get; } = new();
    public sealed record ActionChoice(string Kind, string Label);
    public IReadOnlyList<ActionChoice> ActionOptions { get; } =
    [
        new("Wait", "仅等待"),
        // Standard fixture control is station-owned and frozen at release.
        // Do not offer conflicting Clamp1/2 actions in project-special stages.
        new("IgnOn", "IGN · ON"),
        new("IgnOff", "IGN · OFF")
    ];
    public string SaveStatus
    {
        get => _saveStatus;
        private set => SetProperty(ref _saveStatus, value);
    }
    public bool IsDirty => _dirty;
    public bool IsBusy => _saving;
    public string Name => _session.AssetName;

    public DelegateCommand AddBeforeCommand { get; }
    public DelegateCommand AddAfterCommand { get; }
    public DelegateCommand<ActionEdit> RemoveActionCommand { get; }
    public DelegateCommand<ActionEdit> MoveUpCommand { get; }
    public DelegateCommand<ActionEdit> MoveDownCommand { get; }
    public DelegateCommand SaveCommand { get; }

    public InspectionStationProcedureEditorViewModel(ProjectWorkspaceService workspace,
        ProjectConfigurationSession session)
    {
        _workspace = workspace;
        _session = session;
        var document = (InspectionStationProcedureDocument)session.Document;
        foreach (var action in document.BeforeStart) AddInternal(BeforeStart, new(action));
        foreach (var action in document.AfterFinish) AddInternal(AfterFinish, new(action));
        AddBeforeCommand = new DelegateCommand(() => Add(BeforeStart));
        AddAfterCommand = new DelegateCommand(() => Add(AfterFinish));
        RemoveActionCommand = new DelegateCommand<ActionEdit>(Remove);
        MoveUpCommand = new DelegateCommand<ActionEdit>(row => Move(row, -1));
        MoveDownCommand = new DelegateCommand<ActionEdit>(row => Move(row, 1));
        SaveCommand = new DelegateCommand(async () => await SaveAsync());
        _saveTimer.Tick += async (_, _) =>
        {
            _saveTimer.Stop();
            if (_dirty) await SaveAsync();
        };
    }

    private void AddInternal(ObservableCollection<ActionEdit> list, ActionEdit row)
    {
        row.PropertyChanged += OnRowChanged;
        list.Add(row);
    }
    private void OnRowChanged(object? sender, PropertyChangedEventArgs e) => MarkDirty();
    private void MarkDirty()
    {
        if (_closed) return;
        _dirty = true;
        _editGeneration++;
        RaisePropertyChanged(nameof(IsDirty));
        SaveStatus = "草稿已修改，等待自动保存…";
        _saveTimer.Stop();
        _saveTimer.Start();
    }
    private void Add(ObservableCollection<ActionEdit> list)
    {
        // Wait is deliberately inert until an engineer selects real hardware
        // operation or gives a positive delay. Empty defaults never energize.
        AddInternal(list, new(new InspectionStationAction(Guid.NewGuid(), "Wait", 1000)));
        MarkDirty();
    }
    private void Remove(ActionEdit? row)
    {
        if (row is null) return;
        row.PropertyChanged -= OnRowChanged;
        if (BeforeStart.Remove(row) || AfterFinish.Remove(row)) MarkDirty();
    }
    private void Move(ActionEdit? row, int offset)
    {
        if (row is null) return;
        var list = BeforeStart.Contains(row) ? BeforeStart :
            AfterFinish.Contains(row) ? AfterFinish : null;
        if (list is null) return;
        var index = list.IndexOf(row);
        var next = index + offset;
        if (next < 0 || next >= list.Count) return;
        list.Move(index, next);
        MarkDirty();
    }

    public async Task<bool> SaveAsync()
    {
        if (_closed) return false;
        if (_saving) return false;
        if (!_dirty) return true;
        _saveTimer.Stop();
        _saving = true;
        try
        {
            var savedGeneration = _editGeneration;
            var draft = new InspectionStationProcedureDocument(1, _session.AssetName,
                BeforeStart.Select(x => x.ToDefinition()).ToList(),
                AfterFinish.Select(x => x.ToDefinition()).ToList());
            draft.Validate();
            var before = _session;
            _session = await _workspace.SaveConfigurationAsync(before, draft);
            if (savedGeneration != _editGeneration)
            {
                // The user edited a field while disk I/O was in progress.
                // Never dismiss that newer draft or claim it was persisted.
                _dirty = true;
                _saveTimer.Stop();
                _saveTimer.Start();
                SaveStatus = "上一修订已保存；有更新的草稿，正在继续保存。";
                return false;
            }
            _dirty = false;
            RaisePropertyChanged(nameof(IsDirty));
            SaveStatus = "已保存到当前工程（资源版本 " +
                _session.AssetRevision + "）；正式工位流程尚未完成真机验收。";
            return true;
        }
        catch (Exception error) when (error is IOException or ArgumentException or
            InvalidDataException or ProjectDataException or UnauthorizedAccessException or
            FormatException or InvalidOperationException)
        {
            SaveStatus = "工程动作草稿保存失败，保留编辑内容：" + error.Message;
            return false;
        }
        finally { _saving = false; }
    }

    public async Task<bool> SaveForCloseAsync()
    {
        if (_saving) return false;
        return !_dirty || await SaveAsync();
    }

    public bool ConfirmClose() => !_dirty && !_saving;
    public void Close()
    {
        _closed = true;
        _saveTimer.Stop();
        foreach (var row in BeforeStart.Concat(AfterFinish))
            row.PropertyChanged -= OnRowChanged;
    }
}
