using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Threading;

namespace AutoTest.Frontend;

/// <summary>
/// A UI evaluation session, NOT an AutoTest project/session/run.
/// This draft is only stored under LocalAppData/AutoTest/FrontendPreview.
/// Integrators must replace it with the approved PROJECT/TEST presentation adapters.
/// </summary>
public sealed class UiWorkbenchState : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private readonly HashSet<object> _observed = new(ReferenceEqualityComparer.Instance);
    private bool _hydrating;
    private string _saveStatus = "界面草稿 · 未保存";

    public string PreviewLocation { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AutoTest", "FrontendPreview", "ui-draft.json");

    public UiDraftFile Document { get; private set; } = new();
    public ObservableCollection<CaseDraft> Cases => Document.Cases;
    public ObservableCollection<CanDraft> CanRows => Document.CanRows;
    public ObservableCollection<KeyActionDraft> KeyActions => Document.KeyActions;
    public ObservableCollection<UdsRequestDraft> UdsRequests => Document.UdsRequests;
    public ObservableCollection<DbcMessageDraft> DbcMessages => Document.DbcMessages;
    public ObservableCollection<WaveSignalDraft> WaveSignals => Document.WaveSignals;
    public ObservableCollection<InputStateDraft> InputStates => Document.InputStates;

    // Explorer-only projections: filtering the project tree never changes the
    // run selection or the original UI draft collections.
    public ObservableCollection<CaseDraft> ExplorerCases { get; } = [];
    public ObservableCollection<DbcMessageDraft> ExplorerDbc { get; } = [];
    public ObservableCollection<WaveSignalDraft> ExplorerWaves { get; } = [];
    public ObservableCollection<InputStateDraft> ExplorerInputs { get; } = [];
    private string _resourceFilter = "";
    public void FilterResources(string value)
    {
        _resourceFilter = value?.Trim() ?? "";
        RefreshResourceViews();
    }
    private void RefreshResourceViews()
    {
        static void Apply<T>(ObservableCollection<T> target, IEnumerable<T> source)
        {
            target.Clear();
            foreach (var entry in source) target.Add(entry);
        }
        bool Match(string? name) => string.IsNullOrWhiteSpace(_resourceFilter) ||
            (name?.Contains(_resourceFilter, StringComparison.OrdinalIgnoreCase) ?? false);
        Apply(ExplorerCases, Cases.Where(x => Match(x.Name)));
        Apply(ExplorerDbc, DbcMessages.Where(x => Match(x.Name)));
        Apply(ExplorerWaves, WaveSignals.Where(x => Match(x.Name)));
        Apply(ExplorerInputs, InputStates.Where(x => Match(x.Name)));
    }

    public string ProjectLabel
    {
        get => Document.ProjectLabel;
        set { Document.ProjectLabel = value; Changed(); MarkChanged(); }
    }
    public string FlashFilePath
    {
        get => Document.FlashFilePath;
        set { Document.FlashFilePath = value; Changed(); MarkChanged(); }
    }
    public string FlashProfile
    {
        get => Document.FlashProfile;
        set { Document.FlashProfile = value; Changed(); MarkChanged(); }
    }
    public string WaveReferenceName
    {
        get => Document.WaveReferenceName;
        set { Document.WaveReferenceName = value; Changed(); MarkChanged(); }
    }

    public string SaveStatus
    {
        get => _saveStatus;
        private set { if (_saveStatus != value) { _saveStatus = value; Changed(); } }
    }
    public string RunSelectionSummary => $"{Cases.Count(x => x.SelectedForRun)} / {Cases.Count} 个用例已选";
    public string StepCountSummary => SelectedCase is null
        ? "未选择用例" : $"{SelectedCase.Steps.Count} 个步骤 · 前端样例";
    public string PreviewFlag => "仅 UI 草稿 · 未接正式工程和设备";

    private CaseDraft? _selectedCase;
    public CaseDraft? SelectedCase
    {
        get => _selectedCase;
        set
        {
            if (ReferenceEquals(_selectedCase, value)) return;
            _selectedCase = value; Changed();
            SelectedStep = _selectedCase?.Steps.FirstOrDefault();
            Changed(nameof(StepCountSummary));
        }
    }
    private StepDraft? _selectedStep;
    public StepDraft? SelectedStep
    {
        get => _selectedStep;
        set { if (ReferenceEquals(_selectedStep, value)) return; _selectedStep = value; Changed(); }
    }
    private CanDraft? _selectedCan;
    public CanDraft? SelectedCan
    {
        get => _selectedCan;
        set { if (ReferenceEquals(_selectedCan, value)) return; _selectedCan = value; Changed(); }
    }
    private KeyActionDraft? _selectedAction;
    public KeyActionDraft? SelectedAction
    {
        get => _selectedAction;
        set
        {
            if (ReferenceEquals(_selectedAction, value)) return;
            _selectedAction = value; Changed();
            SelectedKeyStep = _selectedAction?.Steps.FirstOrDefault();
        }
    }
    private KeyStepDraft? _selectedKeyStep;
    public KeyStepDraft? SelectedKeyStep
    {
        get => _selectedKeyStep;
        set { if (ReferenceEquals(_selectedKeyStep, value)) return; _selectedKeyStep = value; Changed(); }
    }
    private UdsRequestDraft? _selectedUds;
    public UdsRequestDraft? SelectedUds
    {
        get => _selectedUds;
        set { if (ReferenceEquals(_selectedUds, value)) return; _selectedUds = value; Changed(); }
    }
    private DbcMessageDraft? _selectedDbcMessage;
    public DbcMessageDraft? SelectedDbcMessage
    {
        get => _selectedDbcMessage;
        set
        {
            if (ReferenceEquals(_selectedDbcMessage, value)) return;
            _selectedDbcMessage = value; Changed();
            SelectedDbcSignal = _selectedDbcMessage?.Signals.FirstOrDefault();
        }
    }
    private DbcSignalDraft? _selectedDbcSignal;
    public DbcSignalDraft? SelectedDbcSignal
    {
        get => _selectedDbcSignal;
        set { if (ReferenceEquals(_selectedDbcSignal, value)) return; _selectedDbcSignal = value; Changed(); }
    }
    private WaveSignalDraft? _selectedWave;
    public WaveSignalDraft? SelectedWave
    {
        get => _selectedWave;
        set { if (ReferenceEquals(_selectedWave, value)) return; _selectedWave = value; Changed(); }
    }
    private InputStateDraft? _selectedInputState;
    public InputStateDraft? SelectedInputState
    {
        get => _selectedInputState;
        set { if (ReferenceEquals(_selectedInputState, value)) return; _selectedInputState = value; Changed(); }
    }

    private UiWorkbenchState()
    {
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveNow();
        };
    }

    public static UiWorkbenchState Load()
    {
        var state = new UiWorkbenchState();
        state._hydrating = true;
        try
        {
            if (File.Exists(state.PreviewLocation))
            {
                state.Document = JsonSerializer.Deserialize<UiDraftFile>(
                    File.ReadAllText(state.PreviewLocation)) ?? MakeExample();
                state.SaveStatus = "已恢复上次界面草稿";
            }
            else
            {
                state.Document = MakeExample();
                state.SaveStatus = "首次打开 · 界面样例";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            state.Document = MakeExample();
            state.SaveStatus = "草稿恢复失败 · 请检查本地文件";
        }
        state.NormalizeCollections();
        state.ObserveAll();
        state.RefreshResourceViews();
        state.SelectedCase = state.Cases.FirstOrDefault();
        state.SelectedCan = state.CanRows.FirstOrDefault();
        state.SelectedAction = state.KeyActions.FirstOrDefault();
        state.SelectedUds = state.UdsRequests.FirstOrDefault();
        state.SelectedDbcMessage = state.DbcMessages.FirstOrDefault();
        state.SelectedWave = state.WaveSignals.FirstOrDefault();
        state.SelectedInputState = state.InputStates.FirstOrDefault();
        state._hydrating = false;
        return state;
    }

    private void NormalizeCollections()
    {
        Document.ProjectLabel ??= "界面示例工程";
        Document.Cases ??= [];
        Document.CanRows ??= [];
        Document.KeyActions ??= [];
        Document.UdsRequests ??= [];
        Document.DbcMessages ??= [];
        Document.WaveSignals ??= [];
        Document.InputStates ??= [];
        foreach (var item in Document.Cases) item.Steps ??= [];
        foreach (var item in Document.KeyActions)
        {
            item.Steps ??= [];
            foreach (var step in item.Steps) step.Keys ??= [];
        }
        foreach (var item in Document.DbcMessages) item.Signals ??= [];
        foreach (var item in Document.InputStates) item.Keys ??= [];
    }

    public void Import(string path)
    {
        var file = JsonSerializer.Deserialize<UiDraftFile>(File.ReadAllText(path))
                   ?? throw new InvalidDataException("文件为空或不是 UI 草稿。");
        _saveTimer.Stop();
        _hydrating = true;
        Document = file;
        NormalizeCollections();
        _observed.Clear();
        ObserveAll();
        RefreshResourceViews();
        foreach (var name in new[] { nameof(Cases), nameof(CanRows), nameof(KeyActions),
                     nameof(UdsRequests), nameof(DbcMessages), nameof(WaveSignals),
                     nameof(InputStates), nameof(ProjectLabel), nameof(FlashFilePath),
                     nameof(FlashProfile), nameof(WaveReferenceName) })
            Changed(name);
        SelectedCase = Cases.FirstOrDefault();
        SelectedCan = CanRows.FirstOrDefault();
        SelectedAction = KeyActions.FirstOrDefault();
        SelectedUds = UdsRequests.FirstOrDefault();
        SelectedDbcMessage = DbcMessages.FirstOrDefault();
        SelectedWave = WaveSignals.FirstOrDefault();
        SelectedInputState = InputStates.FirstOrDefault();
        _hydrating = false;
        MarkChanged();
    }

    public void Export(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(Document, SaveOptions));

    private static JsonSerializerOptions SaveOptions => new() { WriteIndented = true };

    public void MarkChanged()
    {
        if (_hydrating) return;
        Changed(nameof(RunSelectionSummary));
        Changed(nameof(StepCountSummary));
        RefreshResourceViews();
        SaveStatus = "界面草稿正在自动保存…";
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void SaveNow()
    {
        if (_hydrating) return;
        _saveTimer.Stop();
        try
        {
            var directory = Path.GetDirectoryName(PreviewLocation)!;
            Directory.CreateDirectory(directory);
            var temp = PreviewLocation + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Document, SaveOptions));
            File.Move(temp, PreviewLocation, overwrite: true);
            SaveStatus = "界面草稿已自动保存";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            SaveStatus = "界面草稿保存失败 · 修改仍保留在当前窗口";
        }
    }

    private void WatchObject(DraftEntity? item)
    {
        if (item is null || !_observed.Add(item)) return;
        item.PropertyChanged += (_, _) => MarkChanged();
    }
    private void WatchCollection<T>(ObservableCollection<T> collection, Action<T> add)
    {
        if (!_observed.Add(collection)) return;
        foreach (var item in collection) add(item);
        collection.CollectionChanged += (_, args) =>
        {
            if (args.NewItems is not null)
                foreach (T item in args.NewItems) add(item);
            MarkChanged();
        };
    }
    private void ObserveAll()
    {
        WatchCollection(Cases, c =>
        {
            WatchObject(c);
            WatchCollection(c.Steps, WatchObject);
        });
        WatchCollection(CanRows, WatchObject);
        WatchCollection(KeyActions, a =>
        {
            WatchObject(a);
            WatchCollection(a.Steps, step =>
            {
                WatchObject(step);
                WatchCollection(step.Keys, WatchObject);
            });
        });
        WatchCollection(UdsRequests, WatchObject);
        WatchCollection(DbcMessages, m =>
        {
            WatchObject(m);
            WatchCollection(m.Signals, WatchObject);
        });
        WatchCollection(WaveSignals, WatchObject);
        WatchCollection(InputStates, s =>
        {
            WatchObject(s);
            WatchCollection(s.Keys, WatchObject);
        });
    }

    private static UiDraftFile MakeExample()
    {
        var file = new UiDraftFile { ProjectLabel = "界面样例工程（非真实 ECU 工程）" };
        file.Cases.Add(new CaseDraft
        {
            Name = "通信状态检查（界面样例）",
            Steps = new ObservableCollection<StepDraft>
            {
                new() { Phase = "准备", Kind = "等待", Name = "等待工位准备" },
                new() { Phase = "测试", Kind = "CAN 读取", Name = "观察指定报文" },
                new() { Phase = "测试", Kind = "比较判定", Name = "检查响应条件" },
                new() { Phase = "清理", Kind = "等待", Name = "结束检查" }
            }
        });
        file.Cases.Add(new CaseDraft
        {
            Name = "外部输入响应（界面样例）",
            Steps = new ObservableCollection<StepDraft>
            {
                new() { Phase = "准备", Kind = "IGN 控制", Name = "设置输入条件" },
                new() { Phase = "测试", Kind = "输入模拟", Name = "施加工程定义输入" },
                new() { Phase = "测试", Kind = "CAN 读取", Name = "观察 ECU 响应" },
                new() { Phase = "清理", Kind = "等待", Name = "完成当前测试" }
            }
        });
        file.Cases.Add(new CaseDraft { Name = "诊断服务检查（界面样例）" });
        file.UdsRequests.Add(new UdsRequestDraft
        {
            Name = "读取数据（界面样例）", Service = "22",
            Payload = "F1 90", Description = "仅演示请求编辑，不执行 UDS"
        });
        file.DbcMessages.Add(new DbcMessageDraft
        {
            Name = "SampleMessage", IdHex = "180",
            Signals = new ObservableCollection<DbcSignalDraft>
            {
                new() { Name = "SampleSignal", StartBit = 0, BitLength = 8, Factor = 1 }
            }
        });
        return file;
    }
}
