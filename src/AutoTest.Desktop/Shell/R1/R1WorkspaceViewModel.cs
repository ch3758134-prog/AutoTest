using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Prism.Commands;
using Prism.Mvvm;

namespace AutoTest.Desktop.Shell.R1;

// Shell UI projection ONLY. The actual business View/VM is supplied by its specialist owner.
// An unavailable integration cannot be opened as a fake functioning document.
public enum WorkspaceAvailability { Unwired, Available, Busy, Faulted }
public enum WorkspaceSlot { Document, Tool }
public enum R1Theme { Dark, Light }
public enum R1Density { Compact, Comfortable }

public sealed record WorkspaceDescriptor(
    string Id, string Label, string Owner, WorkspaceSlot Slot,
    string RegionKey, string ContextRequirements);

public sealed class WorkspaceTab : BindableBase
{
    private bool _dirty;
    private bool _busy;
    private string _contextRevision = "UNKNOWN";
    private bool _contextStale;
    public WorkspaceTab(WorkspaceDescriptor descriptor)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
    }
    public WorkspaceDescriptor Descriptor { get; }
    public string Id => Descriptor.Id;
    public string Header => Descriptor.Label;
    public string Owner => Descriptor.Owner;
    public bool IsDirty { get => _dirty; set { if (SetProperty(ref _dirty, value)) RaisePropertyChanged(nameof(StateLabel)); } }
    public bool IsBusy { get => _busy; set { if (SetProperty(ref _busy, value)) RaisePropertyChanged(nameof(StateLabel)); } }
    public string ContextRevision { get => _contextRevision; set => SetProperty(ref _contextRevision, value); }
    public bool ContextStale { get => _contextStale; set { if (SetProperty(ref _contextStale, value)) RaisePropertyChanged(nameof(StateLabel)); } }
    public string StateLabel => IsBusy ? "BUSY" : IsDirty ? "DIRTY" : ContextStale ? "STALE" : "READY";
}

public interface IWorkspaceDocumentHost
{
    // INTEGRATION provides the one Prism/DI composition and real professional view lifetime.
    // This UI model does not create or dispose of device sessions.
    bool TryActivate(string workspaceId, out string failureReason);
    bool TryClose(string workspaceId, out string failureReason);
}

public sealed class R1WorkspaceViewModel : BindableBase
{
    private readonly IWorkspaceDocumentHost _host;
    private WorkspaceTab? _activeTab;
    private string _filter = "";
    private string _notice = "工作台就绪；未接入的专业页面不会冒充实际功能";
    private string _projectLabel = "未选择工程";
    private string _resourceRevision = "UNKNOWN";
    private string _stationIdentity = "未连接";
    private string _sessionGeneration = "UNKNOWN";
    private string _origin = "UNKNOWN";
    private R1Theme _theme = R1Theme.Dark;
    private R1Density _density = R1Density.Compact;

    public R1WorkspaceViewModel(IWorkspaceDocumentHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        OpenCommand = new DelegateCommand<WorkspaceDescriptor>(Open);
        CloseCommand = new DelegateCommand<WorkspaceTab>(Close);
        ReopenCommand = new DelegateCommand<WorkspaceDescriptor>(Open);
        NextTabCommand = new DelegateCommand(() => CycleTabs(1));
        PreviousTabCommand = new DelegateCommand(() => CycleTabs(-1));
        CloseActiveTabCommand = new DelegateCommand(() => Close(ActiveTab));
        ToggleThemeCommand = new DelegateCommand(() => Theme = Theme == R1Theme.Dark ? R1Theme.Light : R1Theme.Dark);
        ToggleDensityCommand = new DelegateCommand(() => Density = Density == R1Density.Compact ? R1Density.Comfortable : R1Density.Compact);
        foreach (var descriptor in Catalogue)
        {
            Navigation.Add(descriptor);
            _availability.Add(descriptor.Id, WorkspaceAvailability.Unwired);
        }
        RefreshNavigation();
    }

    public static IReadOnlyList<WorkspaceDescriptor> Catalogue { get; } = new[]
    {
        new WorkspaceDescriptor("P01", "工程中心", "PROJECT", WorkspaceSlot.Document, "Project", "Project/Revision"),
        new WorkspaceDescriptor("P02", "Station 连接", "DEVICE", WorkspaceSlot.Tool, "Station", "Station/Session"),
        new WorkspaceDescriptor("P03", "CAN Monitor", "DEVICE", WorkspaceSlot.Tool, "CanMonitor", "Station/BusRole"),
        new WorkspaceDescriptor("P04", "DBC Editor", "PROJECT", WorkspaceSlot.Document, "DbcEditor", "Project/DBC Revision"),
        new WorkspaceDescriptor("P05", "Controller Designer", "PROJECT", WorkspaceSlot.Document, "ControllerDesigner", "Project/Controller Revision"),
        new WorkspaceDescriptor("P06", "Manual Bench", "UI/BENCH", WorkspaceSlot.Tool, "ManualBench", "Project/Controller/Station"),
        new WorkspaceDescriptor("P07", "Wave Studio", "WAVE", WorkspaceSlot.Tool, "WaveStudio", "Project/DBC/Station"),
        new WorkspaceDescriptor("P08", "UDS Console", "UDS", WorkspaceSlot.Tool, "UdsConsole", "Profile/Session/Mode"),
        new WorkspaceDescriptor("P09", "CAN Flash", "FLASH", WorkspaceSlot.Tool, "CanFlash", "Profile/Job/Session"),
        new WorkspaceDescriptor("P10", "Test Case Editor", "TEST", WorkspaceSlot.Document, "TestCaseEditor", "Project/TestDocument"),
        new WorkspaceDescriptor("P11", "Test Run & Debugger", "TEST", WorkspaceSlot.Tool, "TestDebugger", "Project/RunId"),
        new WorkspaceDescriptor("P12", "Production / Scan", "SCAN", WorkspaceSlot.Tool, "ProductionScan", "Station/Identity/Recipe"),
        new WorkspaceDescriptor("P13", "Records & Reports", "RECORD", WorkspaceSlot.Document, "RecordsReports", "RunId/RecordContext")
    };

    private readonly Dictionary<string, WorkspaceAvailability> _availability = new(StringComparer.Ordinal);

    public ObservableCollection<WorkspaceDescriptor> Navigation { get; } = new();
    public ObservableCollection<WorkspaceDescriptor> FilteredNavigation { get; } = new();
    public ObservableCollection<WorkspaceTab> OpenTabs { get; } = new();
    public DelegateCommand<WorkspaceDescriptor> OpenCommand { get; }
    public DelegateCommand<WorkspaceDescriptor> ReopenCommand { get; }
    public DelegateCommand<WorkspaceTab> CloseCommand { get; }
    public DelegateCommand NextTabCommand { get; }
    public DelegateCommand PreviousTabCommand { get; }
    public DelegateCommand CloseActiveTabCommand { get; }
    public DelegateCommand ToggleThemeCommand { get; }
    public DelegateCommand ToggleDensityCommand { get; }
    public R1Theme Theme { get => _theme; set { if (SetProperty(ref _theme, value)) RaisePropertyChanged(nameof(ThemeLabel)); } }
    public R1Density Density { get => _density; set { if (SetProperty(ref _density, value)) RaisePropertyChanged(nameof(DensityLabel)); } }
    public string ThemeLabel => Theme == R1Theme.Dark ? "深色" : "浅色";
    public string DensityLabel => Density == R1Density.Compact ? "紧凑" : "舒适";

    public WorkspaceTab? ActiveTab
    {
        get => _activeTab;
        set { if (value is null || OpenTabs.Contains(value)) SetProperty(ref _activeTab, value); }
    }
    public string Filter
    {
        get => _filter;
        set { if (SetProperty(ref _filter, value)) RefreshNavigation(); }
    }
    public string Notice { get => _notice; private set => SetProperty(ref _notice, value); }
    public string ProjectLabel { get => _projectLabel; private set => SetProperty(ref _projectLabel, value); }
    public string ResourceRevision { get => _resourceRevision; private set => SetProperty(ref _resourceRevision, value); }
    public string StationIdentity { get => _stationIdentity; private set => SetProperty(ref _stationIdentity, value); }
    public string SessionGeneration { get => _sessionGeneration; private set => SetProperty(ref _sessionGeneration, value); }
    public string Origin { get => _origin; private set => SetProperty(ref _origin, value); }

    public WorkspaceAvailability AvailabilityOf(string id) =>
        _availability.TryGetValue(id, out var state) ? state : WorkspaceAvailability.Unwired;

    public void UpdateAvailability(string id, WorkspaceAvailability state)
    {
        if (!_availability.ContainsKey(id)) throw new ArgumentOutOfRangeException(nameof(id));
        _availability[id] = state;
    }

    public void AcceptContext(string? project, string? revision, string? station,
        long? generation, string? origin)
    {
        ProjectLabel = string.IsNullOrWhiteSpace(project) ? "未选择工程" : project;
        ResourceRevision = string.IsNullOrWhiteSpace(revision) ? "UNKNOWN" : revision;
        StationIdentity = string.IsNullOrWhiteSpace(station) ? "未连接" : station;
        SessionGeneration = generation?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "UNKNOWN";
        Origin = origin is "Mock" or "Replay" or "Physical" ? origin : "UNKNOWN";
        // Existing tabs remain open. Mark stale instead of rewriting captured revision.
        foreach (var tab in OpenTabs)
            tab.ContextStale = !StringComparer.Ordinal.Equals(tab.ContextRevision, ResourceRevision);
        if (OpenTabs.Any(x => x.ContextStale))
            Notice = "工程 Revision 已改变，旧文档保持打开但标记 STALE；专业页须核验其引用有效性";
    }

    private void RefreshNavigation()
    {
        FilteredNavigation.Clear();
        foreach (var item in Navigation.Where(x =>
            string.IsNullOrWhiteSpace(Filter) ||
            x.Label.Contains(Filter.Trim(), StringComparison.OrdinalIgnoreCase) ||
            x.Id.Contains(Filter.Trim(), StringComparison.OrdinalIgnoreCase) ||
            x.Owner.Contains(Filter.Trim(), StringComparison.OrdinalIgnoreCase)))
            FilteredNavigation.Add(item);
    }

    public void Open(WorkspaceDescriptor? descriptor)
    {
        if (descriptor is null || !Catalogue.Contains(descriptor)) return;
        if (AvailabilityOf(descriptor.Id) != WorkspaceAvailability.Available)
        {
            Notice = $"{descriptor.Id} {descriptor.Label}：尚未接入正式专业 View；不能打开演示占位作为实际工具";
            return;
        }
        var tab = OpenTabs.FirstOrDefault(x => x.Id == descriptor.Id);
        if (tab is not null) { ActiveTab = tab; return; }
        if (!_host.TryActivate(descriptor.Id, out var reason))
        {
            Notice = $"{descriptor.Label}：{reason}";
            return;
        }
        tab = new WorkspaceTab(descriptor) { ContextRevision = ResourceRevision };
        OpenTabs.Add(tab);
        ActiveTab = tab;
        Notice = $"已激活 {descriptor.Label} · {descriptor.Owner}";
    }

    public void CycleTabs(int direction)
    {
        if (OpenTabs.Count == 0 || direction == 0) return;
        var currentIndex = ActiveTab is null ? -1 : OpenTabs.IndexOf(ActiveTab);
        var nextIndex = currentIndex < 0 ? 0 : ((currentIndex + (direction > 0 ? 1 : -1) + OpenTabs.Count) % OpenTabs.Count);
        ActiveTab = OpenTabs[nextIndex];
    }

    public void Close(WorkspaceTab? tab)
    {
        if (tab is null || !OpenTabs.Contains(tab)) return;
        if (tab.IsBusy || tab.IsDirty)
        {
            Notice = tab.IsBusy ? "工具正忙：须由专业页处理取消/关闭" : "文档有未保存修改：须由专业页确认保存或放弃";
            return;
        }
        if (!_host.TryClose(tab.Id, out var reason))
        {
            Notice = $"无法关闭 {tab.Header}：{reason}";
            return;
        }
        var wasActive = ReferenceEquals(ActiveTab, tab);
        OpenTabs.Remove(tab);
        if (wasActive) ActiveTab = OpenTabs.LastOrDefault();
        Notice = $"已关闭 {tab.Header}；未触碰共享 Station/USB/CAN 会话";
    }
}
