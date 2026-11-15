using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using AutoTest.Desktop.Projects;
using AutoTest.Projects.Resources;
using AutoTest.Testing.Authoring;
using Prism.Commands;
using Prism.Mvvm;

namespace AutoTest.Desktop.Testing;

public sealed record TestProjectPackageChoice(AssetEntry Asset)
{
    public string Label => Asset.Name + " · 修订 " + Asset.Revision;
}

public sealed class TestProjectPackageRow : BindableBase
{
    private bool _enabled;
    private bool _runSelected;
    private int _order;
    public int Order { get => _order; set => SetProperty(ref _order, value); }
    public Guid ItemId { get; }
    public Guid AssetId { get; }
    public int Revision { get; }
    public string Sha256 { get; }
    public string Name { get; }
    public TestProjectPackageRow(TestProjectEntry item)
    {
        ItemId = item.ItemId;
        AssetId = item.PackageAssetId;
        Revision = item.PackageRevision;
        Sha256 = item.PackageSha256;
        Name = item.DisplayName;
        _enabled = item.Enabled;
        _runSelected = item.Enabled;
    }
    /// <summary>Engineer-only selection: never serialized as production Enabled.</summary>
    public bool RunSelected
    {
        get => _runSelected;
        set => SetProperty(ref _runSelected, value);
    }
    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }
    public TestProjectEntry ToEntry() => new(ItemId, AssetId, Revision, Sha256, Name, Enabled);
}

public sealed class TestProjectWorkbenchViewModel : BindableBase
{
    private readonly ProjectWorkspaceService _workspace;
    private readonly TestEditorDialogs _dialogs;
    private TestProjectEditorSession _session;
    private bool _busy;
    private string _name;
    private string _status = "从右侧选择测试用例并加入执行列表。";
    private TestProjectPackageChoice? _selectedAvailable;
    private TestProjectPackageRow? _selectedPackage;

    public TestProjectWorkbenchViewModel(ProjectWorkspaceService workspace,
        TestEditorDialogs dialogs, TestProjectEditorSession session)
    {
        _workspace = workspace; _dialogs = dialogs; _session = session;
        _name = session.Document.Name;
        AddCommand = new DelegateCommand(Add, () => SelectedAvailable is not null && !IsBusy);
        RemoveCommand = new DelegateCommand(Remove, () => SelectedPackage is not null && !IsBusy);
        UpCommand = new DelegateCommand(() => Move(-1), () => CanMove(-1));
        DownCommand = new DelegateCommand(() => Move(1), () => CanMove(1));
        SaveCommand = new DelegateCommand(async () => await SaveAsync(), () => !IsBusy && IsDirty);
        ReloadCommand = new DelegateCommand(async () => await ReloadAsync(), () => !IsBusy);
        SelectAllCommand = new DelegateCommand(() => SetSelection(true));
        ClearSelectionCommand = new DelegateCommand(() => SetSelection(false));
        PreflightSelectedCommand = new DelegateCommand(PreflightSelected);
        LoadRows(session.Document);
        RefreshAvailable();
    }

    public ObservableCollection<TestProjectPackageChoice> AvailablePackages { get; } = new();
    public ObservableCollection<TestProjectPackageRow> Packages { get; } = new();
    public string Name
    {
        get => _name;
        set { if (SetProperty(ref _name, value)) RefreshCommands(); }
    }
    public string AssetName => _session.AssetName;
    public string FilePath => _session.FilePath;
    public int Revision => _session.AssetRevision;
    public string StatusMessage { get => _status; private set => SetProperty(ref _status, value); }
    public bool IsBusy
    {
        get => _busy;
        private set { if (SetProperty(ref _busy, value)) RefreshCommands(); }
    }
    // Dirty detection is valid even while the user is midway through typing a name.
    // Never serialize+validate on each TextBox keystroke.
    public bool IsDirty => !string.Equals(Name, _session.Document.Name, StringComparison.Ordinal) ||
        !Packages.Select(x => x.ToEntry()).SequenceEqual(_session.Document.Packages);
    public TestProjectPackageChoice? SelectedAvailable
    {
        get => _selectedAvailable;
        set { if (SetProperty(ref _selectedAvailable, value)) RefreshCommands(); }
    }
    public TestProjectPackageRow? SelectedPackage
    {
        get => _selectedPackage;
        set { if (SetProperty(ref _selectedPackage, value)) RefreshCommands(); }
    }

    public DelegateCommand AddCommand { get; }
    public DelegateCommand RemoveCommand { get; }
    public DelegateCommand UpCommand { get; }
    public DelegateCommand DownCommand { get; }
    public DelegateCommand SaveCommand { get; }
    public DelegateCommand ReloadCommand { get; }
    public DelegateCommand SelectAllCommand { get; }
    public DelegateCommand ClearSelectionCommand { get; }
    public DelegateCommand PreflightSelectedCommand { get; }
    public int SelectedCount => Packages.Count(x => x.RunSelected);
    public int TotalCount => Packages.Count;
    public string SelectionSummary => SelectedCount + " / " + TotalCount + " 个用例（只作用于本次检查）";

    private void Renumber()
    {
        for (var i = 0; i < Packages.Count; i++) Packages[i].Order = i + 1;
    }
    private void LoadRows(TestProjectDocument doc)
    {
        foreach (var row in Packages) row.PropertyChanged -= RowChanged;
        Packages.Clear();
        foreach (var item in doc.Packages)
        {
            var row = new TestProjectPackageRow(item);
            row.PropertyChanged += RowChanged;
            Packages.Add(row);
        }
        Renumber();
        SelectedPackage = null;
        RaisePropertyChanged(nameof(SelectedCount));
        RaisePropertyChanged(nameof(TotalCount));
        RaisePropertyChanged(nameof(SelectionSummary));
        RaisePropertyChanged(nameof(IsDirty));
    }
    private void RowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TestProjectPackageRow.RunSelected))
        {
            RaisePropertyChanged(nameof(SelectedCount));
            RaisePropertyChanged(nameof(SelectionSummary));
        }
        RefreshCommands();
    }
    private void SetSelection(bool select)
    {
        foreach (var row in Packages) row.RunSelected = select;
        StatusMessage = select ? "已选择本项目所有用例，仅影响本次检查。" :
            "已清空本次检查选择；工程默认未修改。";
    }
    private void PreflightSelected()
    {
        var selected = Packages.Where(x => x.RunSelected).ToArray();
        if (selected.Length == 0)
        {
            StatusMessage = "无法检查：请先勾选至少一个测试用例。";
            return;
        }
        var assets = _workspace.Current?.Definition.Assets;
        if (assets is null) { StatusMessage = "无法检查：未打开真实工程。"; return; }
        var missing = selected.Where(row => !assets.Any(a =>
            a.Id.Value == row.AssetId && a.Revision == row.Revision &&
            a.Sha256.Equals(row.Sha256, StringComparison.OrdinalIgnoreCase))).ToArray();
        StatusMessage = missing.Length > 0
            ? "BLOCKED_RESOURCE: " + missing.Length + " 条用例的固定资产修订或 SHA 已失效。"
            : "BLOCKED_BY_BACKEND: 当前只有真实资源身份检查，尚无项目级 Run/设备租约预检；未启动 ECU，未生成测试结论。";
    }

    private void RefreshAvailable()
    {
        AvailablePackages.Clear();
        var assets = _workspace.Current?.Definition.Assets
            .Where(x => x.Kind == ProjectWorkspaceService.TestProgramKind)
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray() ?? Array.Empty<AssetEntry>();
        foreach (var asset in assets)
            AvailablePackages.Add(new TestProjectPackageChoice(asset));
        SelectedAvailable = AvailablePackages.FirstOrDefault();
    }

    private TestProjectDocument Compose() =>
        new(_session.Document.SchemaVersion, _session.Document.Id, (Name ?? "").Trim(),
            Packages.Select(x => x.ToEntry()).ToList());

    private void Add()
    {
        if (SelectedAvailable is not { } choice || IsBusy) return;
        var asset = choice.Asset;
        var row = new TestProjectPackageRow(new TestProjectEntry(
            Guid.NewGuid(), asset.Id.Value, asset.Revision, asset.Sha256, asset.Name, true));
        row.PropertyChanged += RowChanged;
        Packages.Add(row);
        Renumber();
        SelectedPackage = row;
        RaisePropertyChanged(nameof(SelectionSummary));
        RaisePropertyChanged(nameof(TotalCount));
        StatusMessage = "已加入测试用例：" + asset.Name + "。重复引用会形成独立的执行条目。";
        RefreshCommands();
    }
    private void Remove()
    {
        if (SelectedPackage is not { } row || IsBusy) return;
        row.PropertyChanged -= RowChanged;
        var index = Packages.IndexOf(row);
        Packages.Remove(row);
        Renumber();
        RaisePropertyChanged(nameof(SelectedCount));
        RaisePropertyChanged(nameof(TotalCount));
        RaisePropertyChanged(nameof(SelectionSummary));
        SelectedPackage = Packages.Count == 0 ? null : Packages[Math.Min(index, Packages.Count - 1)];
        RefreshCommands();
    }
    private bool CanMove(int direction) => !IsBusy && SelectedPackage is { } row &&
        Packages.IndexOf(row) + direction >= 0 &&
        Packages.IndexOf(row) + direction < Packages.Count;
    private void Move(int direction)
    {
        if (!CanMove(direction) || SelectedPackage is not { } row) return;
        var index = Packages.IndexOf(row);
        Packages.Move(index, index + direction);
        Renumber();
        SelectedPackage = row;
        RefreshCommands();
    }
    private void RefreshCommands()
    {
        RaisePropertyChanged(nameof(IsDirty));
        AddCommand?.RaiseCanExecuteChanged(); RemoveCommand?.RaiseCanExecuteChanged();
        UpCommand?.RaiseCanExecuteChanged(); DownCommand?.RaiseCanExecuteChanged();
        SaveCommand?.RaiseCanExecuteChanged(); ReloadCommand?.RaiseCanExecuteChanged();
    }
    public bool ConfirmClose() =>
        !IsBusy && (!IsDirty || _dialogs.Confirm("测试项目有未保存的用例顺序修改。确定放弃吗？"));

    private async Task SaveAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var changed = Compose();
            changed.Validate();
            _session = await _workspace.SaveTestProjectEditorAsync(_session, changed);
            Name = _session.Document.Name;
            LoadRows(_session.Document);
            RefreshAvailable();
            RaisePropertyChanged(nameof(Revision));
            StatusMessage = "测试项目已保存。用例列表引用当前修订；不会自动运行或连接台架。";
        }
        catch (Exception error) when (error is IOException or InvalidDataException or
                                      InvalidOperationException or ArgumentException or
                                      AutoTest.Projects.Project.ProjectDataException)
        {
            StatusMessage = "保存测试项目失败：" + error.Message;
            _dialogs.ShowError(StatusMessage);
        }
        finally { IsBusy = false; }
    }
    private async Task ReloadAsync()
    {
        if (IsBusy || !ConfirmClose()) return;
        IsBusy = true;
        try
        {
            _session = await _workspace.OpenTestProjectEditorAsync(_session.AssetId);
            Name = _session.Document.Name;
            LoadRows(_session.Document);
            RefreshAvailable();
            RaisePropertyChanged(nameof(Revision));
            StatusMessage = "测试项目已从工程重新读取。";
        }
        catch (Exception error) when (error is IOException or InvalidDataException or
                                      InvalidOperationException or ArgumentException or
                                      AutoTest.Projects.Project.ProjectDataException)
        {
            StatusMessage = "重新加载失败：" + error.Message;
            _dialogs.ShowError(StatusMessage);
        }
        finally { IsBusy = false; }
    }
}
