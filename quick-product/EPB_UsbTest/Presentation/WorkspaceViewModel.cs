using System.Collections.ObjectModel;
using System.Windows.Input;
using EPB.Core.Configuration;
using EPB.Core.Testing;

namespace EPB_UsbTest.Presentation;

/// <summary>
/// 主工作区的纯状态投影。暂不替换旧窗口事件链，先作为新布局的唯一绑定入口逐步接管。
/// </summary>
public sealed class WorkspaceViewModel : ObservableObject
{
    private string _projectName = "未加载配置";
    private string _connectionText = "主控板未连接";
    private string _runStatus = "就绪";
    private bool _isRunning;
    private bool _isDirty;
    private TestCaseDefinition? _selectedTestCase;
    private TestFlowNode? _selectedNode;

    public string ProjectName { get => _projectName; set => SetProperty(ref _projectName, value); }
    public string ConnectionText { get => _connectionText; set => SetProperty(ref _connectionText, value); }
    public string RunStatus { get => _runStatus; set => SetProperty(ref _runStatus, value); }
    public bool IsRunning { get => _isRunning; set { if (SetProperty(ref _isRunning, value)) { (RunCommand as RelayCommand)?.Refresh(); (StopCommand as RelayCommand)?.Refresh(); } } }
    public bool IsDirty { get => _isDirty; set => SetProperty(ref _isDirty, value); }
    public ObservableCollection<TestCaseDefinition> TestCases { get; } = [];
    public ObservableCollection<LogItem> Logs { get; } = [];
    public ObservableCollection<CanFrameRow> CanFrames { get; } = [];
    public TestCaseDefinition? SelectedTestCase { get => _selectedTestCase; set => SetProperty(ref _selectedTestCase, value); }
    public TestFlowNode? SelectedNode { get => _selectedNode; set => SetProperty(ref _selectedNode, value); }

    public ICommand RunCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand AddStepCommand { get; }

    public WorkspaceViewModel()
    {
        RunCommand = new RelayCommand(_ => RunRequested?.Invoke(this, EventArgs.Empty), _ => !IsRunning && SelectedTestCase is not null);
        StopCommand = new RelayCommand(_ => StopRequested?.Invoke(this, EventArgs.Empty), _ => IsRunning);
        SaveCommand = new RelayCommand(_ => SaveRequested?.Invoke(this, EventArgs.Empty), _ => IsDirty && !IsRunning);
        AddStepCommand = new RelayCommand(_ => AddStepRequested?.Invoke(this, EventArgs.Empty), _ => SelectedTestCase is not null && !IsRunning);
    }

    public event EventHandler? RunRequested;
    public event EventHandler? StopRequested;
    public event EventHandler? SaveRequested;
    public event EventHandler? AddStepRequested;

    public void Load(EcuConfiguration configuration)
    {
        TestCases.Clear();
        foreach (var testCase in configuration.TestPlan.Cases.OrderBy(x => x.Order)) TestCases.Add(testCase);
        ProjectName = configuration.Name;
        SelectedTestCase = TestCases.FirstOrDefault();
        SelectedNode = SelectedTestCase?.Flow.FirstOrDefault();
        IsDirty = false;
    }
}

public sealed record LogItem(string Time, string Level, string Source, string Message);
public sealed record CanFrameRow(string Id, string Data, string Period);
