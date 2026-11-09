using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using Prism.Mvvm;

namespace AutoTest.Desktop.Shell.R1;

// UI projection only. INTEGRATION supplies authoritative specialist events.
// This does not own Device, UDS, Test, Trace persistence, or the physical bus.
public enum WorkspaceEventKind { Output, Trace, Problem }
public enum WorkspaceEventLevel { Info, Warning, Error }

public sealed record WorkspaceDiagnosticEvent(
    DateTimeOffset TimestampUtc,
    WorkspaceEventKind Kind,
    WorkspaceEventLevel Level,
    string Source,
    string Layer,
    string Summary,
    string? WorkspaceId = null,
    string? CorrelationId = null,
    string? TraceReference = null,
    string? ErrorCode = null,
    string? RawText = null,
    string? SessionGeneration = null)
{
    public string TimestampDisplay => TimestampUtc.ToLocalTime()
        .ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
}

public sealed class R1DiagnosticsPanel : BindableBase
{
    // In-memory projection is deliberately finite. Truncated history is ALWAYS explicit.
    public const int DisplayCapacity = 2000;
    private readonly ObservableCollection<WorkspaceDiagnosticEvent> _all = new();
    private readonly ReadOnlyObservableCollection<WorkspaceDiagnosticEvent> _readOnly;
    private WorkspaceEventKind _activeKind = WorkspaceEventKind.Output;
    private string _search = "";
    private WorkspaceDiagnosticEvent? _selected;
    private long _dropped;
    private bool _visible = true;

    public R1DiagnosticsPanel()
    {
        _readOnly = new ReadOnlyObservableCollection<WorkspaceDiagnosticEvent>(_all);
    }

    public ReadOnlyObservableCollection<WorkspaceDiagnosticEvent> AllEvents => _readOnly;
    public ObservableCollection<WorkspaceDiagnosticEvent> VisibleEvents { get; } = new();
    public WorkspaceEventKind ActiveKind
    {
        get => _activeKind;
        set { if (SetProperty(ref _activeKind, value)) Refresh(); }
    }
    public string Search
    {
        get => _search;
        set { if (SetProperty(ref _search, value)) Refresh(); }
    }
    public WorkspaceDiagnosticEvent? SelectedEvent
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }
    public long DroppedCount
    {
        get => _dropped;
        private set { if (SetProperty(ref _dropped, value)) RaisePropertyChanged(nameof(IntegrityLabel)); }
    }
    public string IntegrityLabel => DroppedCount > 0
        ? $"仅当前内存窗口；已移出 {DroppedCount} 条，不能当完整原始日志"
        : "仅当前内存窗口；完整底层 Trace 请从专业工具获取";
    public bool IsVisible { get => _visible; set => SetProperty(ref _visible, value); }
    public int OutputCount => _all.Count(x => x.Kind == WorkspaceEventKind.Output);
    public int TraceCount => _all.Count(x => x.Kind == WorkspaceEventKind.Trace);
    public int ProblemCount => _all.Count(x => x.Kind == WorkspaceEventKind.Problem);

    public void Accept(WorkspaceDiagnosticEvent item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _all.Add(item);
        if (_all.Count > DisplayCapacity)
        {
            _all.RemoveAt(0);
            DroppedCount++;
        }
        RaisePropertyChanged(nameof(OutputCount));
        RaisePropertyChanged(nameof(TraceCount));
        RaisePropertyChanged(nameof(ProblemCount));
        Refresh();
    }

    public void SetTab(WorkspaceEventKind kind) => ActiveKind = kind;
    public void ToggleVisibility() => IsVisible = !IsVisible;

    private void Refresh()
    {
        var previous = SelectedEvent;
        VisibleEvents.Clear();
        var query = Search.Trim();
        foreach (var e in _all.Where(x => x.Kind == ActiveKind &&
            (query.Length == 0 || new[] {
                x.Source, x.Layer, x.Summary, x.WorkspaceId, x.CorrelationId,
                x.TraceReference, x.ErrorCode, x.RawText, x.SessionGeneration
            }.Any(y => y?.Contains(query, StringComparison.OrdinalIgnoreCase) == true))).Reverse())
        {
            VisibleEvents.Add(e);
        }
        SelectedEvent = previous is not null && VisibleEvents.Contains(previous) ? previous : null;
    }

    // Copy selected row only. Do not leak unrelated raw events or call this a persisted archive.
    public string CopySelected()
    {
        if (SelectedEvent is not { } e) return "";
        return string.Join("\t", new[] {
            e.TimestampUtc.ToString("O", CultureInfo.InvariantCulture), e.Kind.ToString(),
            e.Level.ToString(), e.Source, e.Layer, e.WorkspaceId ?? "",
            e.CorrelationId ?? "", e.TraceReference ?? "", e.ErrorCode ?? "",
            e.SessionGeneration ?? "", e.Summary, e.RawText ?? ""
        }.Select(Sanitize));
    }

    private static string Sanitize(string s) => s
        .Replace("\t", "\\t", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal);
}
