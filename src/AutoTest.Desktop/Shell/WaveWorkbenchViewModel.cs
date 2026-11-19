using System.Collections.ObjectModel;
using System.IO;
using AutoTest.Core;
using AutoTest.Desktop.Projects;
using AutoTest.Specialists.Wave;
using Prism.Commands;
using Prism.Mvvm;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// Read-only real waveform evidence/reference browser. Uses the project evidence
/// codec, verified sidecars and waveform chart projection; no invented samples,
/// CAN subscriptions, UI-created reference files or simulated captures.
/// </summary>
public sealed class WaveWorkbenchViewModel : BindableBase
{
    public sealed record WaveItem(string Title, string Detail, Guid Id, bool IsReference,
        WaveReferenceEntry? Reference)
    {
        public string Display => Title + " · " + Detail;
    }

    private readonly ProjectWorkspaceService _project;
    private WaveItem? _selected;
    private WaveSeries? _series;
    private IReadOnlyList<WaveChartPoint> _points = Array.Empty<WaveChartPoint>();
    private string _status = "打开工程后刷新，查看已保存的真实波形证据与参考波形。";
    private string _info = "";
    private string _cursorInfo = "点击曲线两次测量时间差、数值差与区间极值。";
    public ObservableCollection<WaveItem> Items { get; } = new();
    public DelegateCommand RefreshCommand { get; }
    public WaveWorkbenchViewModel(ProjectWorkspaceService project)
    {
        _project = project;
        RefreshCommand = new DelegateCommand(Refresh);
        _project.CurrentChanged += ProjectChanged;
        Refresh();
    }
    public event Action? ChartChanged;
    public WaveItem? Selected
    {
        get => _selected;
        set { if (SetProperty(ref _selected, value)) LoadSelected(); }
    }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string Info { get => _info; private set => SetProperty(ref _info, value); }
    public string CursorInfo { get => _cursorInfo; set => SetProperty(ref _cursorInfo, value); }
    public IReadOnlyList<WaveChartPoint> Points => _points;
    public WaveSeries? Series => _series;

    private void ProjectChanged(object? sender, EventArgs e) =>
        System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(Refresh));

    public void Refresh()
    {
        Items.Clear();
        _selected = null;
        _series = null;
        _points = Array.Empty<WaveChartPoint>();
        RaisePropertyChanged(nameof(Selected));
        ChartChanged?.Invoke();
        Info = "";
        if (_project.Current is not { } project)
        {
            Status = "未打开工程；不会加载其他目录的波形记录。";
            return;
        }
        try
        {
            var root = Path.Combine(project.RootDirectory, "wave-records");
            var evidencePath = Path.Combine(root, "evidence");
            if (Directory.Exists(evidencePath))
            {
                var store = new WaveEvidenceStore(evidencePath);
                foreach (var file in Directory.EnumerateFiles(evidencePath, "*.wave.json")
                             .OrderByDescending(File.GetLastWriteTimeUtc).Take(500))
                {
                    var prefix = Path.GetFileName(file).Split('.')[0];
                    if (!Guid.TryParseExact(prefix, "N", out var id)) continue;
                    try
                    {
                        var actual = store.Read(new EvidenceRef(id));
                        Items.Add(new WaveItem(actual.Actual.Signal.SignalName,
                            actual.Kind + " · " + actual.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") +
                            " · " + actual.Actual.Samples.Length + " 点",
                            id, false, null));
                    }
                    catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException)
                    {
                        // A broken evidence file never becomes a selectable waveform.
                    }
                }
            }
            var catalog = new WaveReferenceCatalog(Path.Combine(root, "references"));
            foreach (var reference in catalog.List())
                Items.Add(new WaveItem(reference.Name,
                    "参考 · " + reference.Signal.SignalName + " · " +
                    reference.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                    reference.Reference.Asset.Id.Value, true, reference));
            Status = "已载入 " + Items.Count + " 条已验证的波形记录；请选择一条查看。";
            if (Items.Count > 0) Selected = Items[0];
        }
        catch (Exception ex) { Status = "读取真实波形记录失败：" + ex.Message; }
    }

    private void LoadSelected()
    {
        _series = null;
        _points = Array.Empty<WaveChartPoint>();
        Info = "";
        try
        {
            if (Selected is null || _project.Current is not { } project) return;
            var root = Path.Combine(project.RootDirectory, "wave-records");
            _series = Selected.IsReference
                ? new WaveReferenceCatalog(Path.Combine(root, "references"))
                    .Load(Selected.Reference!.Reference).Series
                : new WaveEvidenceStore(Path.Combine(root, "evidence"))
                    .Read(new EvidenceRef(Selected.Id)).Actual;
            _points = WaveChartAnalysis.FromSeries(_series);
            var values = _series.Samples.Select(x => x.Sample.Value).ToArray();
            var min = values.Length == 0 ? "" : values.Min().ToString("G6");
            var max = values.Length == 0 ? "" : values.Max().ToString("G6");
            Info = _series.Signal.SignalName + " · " + _series.Signal.Unit +
                " · CAN ID 0x" + _series.Signal.FrameId.ToString("X") +
                " · " + _series.Samples.Length + " 个样本" +
                " · 最小 " + min + " · 最大 " + max +
                " · 丢失标记 " + (_series.HasGap || _series.Overflow ? "存在" : "未标记");
            CursorInfo = "点击曲线两次，查看区间时间差、幅值差与极值。";
            Status = "显示真实样本；降采样仅用于绘图，不用于判定。";
        }
        catch (Exception ex)
        {
            _points = Array.Empty<WaveChartPoint>();
            _series = null;
            Info = "";
            Status = "波形证据无法读取：" + ex.Message;
        }
        finally { ChartChanged?.Invoke(); }
    }

    public void SetCursorMeasure(double first, double second)
    {
        try
        {
            var measurement = WaveChartAnalysis.Measure(_points, first, second);
            CursorInfo = measurement is null ? "选定范围没有可用样本。" :
                "Δt=" + measurement.DeltaSeconds.ToString("F4") + " s   " +
                "Δy=" + measurement.DeltaValue.ToString("G6") + " " +
                (_series?.Signal.Unit ?? "") + "   区间最小=" +
                measurement.Minimum.ToString("G6") + "   最大=" +
                measurement.Maximum.ToString("G6");
        }
        catch (Exception ex) { CursorInfo = "无法计算游标：" + ex.Message; }
    }

    public void Close() => _project.CurrentChanged -= ProjectChanged;
}
