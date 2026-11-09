using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using Microsoft.Win32;
using Prism.Commands;
using Prism.Mvvm;

namespace AutoTest.LabLinkGui.LabLink;

/// <summary>Only UI state and rendering; USB implementation belongs to DEVICE.
/// Register ILabLinkUiPort in INTEGRATION as a façade over the existing UsbSessionService.</summary>
public sealed class LabLinkViewModel : BindableBase, IDisposable
{
    private readonly ILabLinkUiPort _port;
    private readonly Dispatcher _dispatcher;
    private bool _busy;
    private bool _connected;
    private string _status = "未连接";
    private string _identity = "未选择主控板";
    private string _error = "";
    private string _searchText = "";
    private LabLogFilter _filter;
    private LabBoardIdentity? _selectedBoard;
    private LabLinkLogEntry? _selectedLog;
    private bool _awaitingSelection;
    private bool _disposed;

    public LabLinkViewModel(ILabLinkUiPort port)
    {
        _port = port;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _connected = port.IsConnected;
        _identity = port.ConnectedBoard?.DisplayName ?? "未选择主控板";
        _status = _connected ? "已连接" : "未连接";
        ConnectionCommand = new DelegateCommand(() => _ = ToggleAsync(), () => !IsBusy && (!AwaitingSelection || SelectedBoard is not null));
        CopyCommand = new DelegateCommand(CopyLog);
        ExportCommand = new DelegateCommand(ExportLog);
        LogsView = CollectionViewSource.GetDefaultView(Logs);
        LogsView.Filter = o => o is LabLinkLogEntry e && LabLinkLogUtilities.Matches(e, Filter, SearchText);
        _port.Activity += OnActivity;
    }

    public ObservableCollection<LabLinkLogEntry> Logs { get; } = new();
    public ObservableCollection<LabBoardIdentity> Candidates { get; } = new();
    public ICollectionView LogsView { get; }
    public DelegateCommand ConnectionCommand { get; }
    public DelegateCommand CopyCommand { get; }
    public DelegateCommand ExportCommand { get; }
    public Array Filters => Enum.GetValues(typeof(LabLogFilter));
    public bool IsBusy { get => _busy; private set { if (SetProperty(ref _busy, value)) { ConnectionCommand.RaiseCanExecuteChanged(); RaisePropertyChanged(nameof(ConnectionLabel)); } } }
    public bool IsConnected { get => _connected; private set { if (SetProperty(ref _connected, value)) RaisePropertyChanged(nameof(ConnectionLabel)); } }
    public string ConnectionLabel => IsBusy ? "正在处理…" : IsConnected ? "断开" : AwaitingSelection ? "连接所选主控板" : "连接主控板";
    public string ConnectionStatus { get => _status; private set => SetProperty(ref _status, value); }
    public string DeviceIdentity { get => _identity; private set => SetProperty(ref _identity, value); }
    public string LastError { get => _error; private set => SetProperty(ref _error, value); }
    public bool AwaitingSelection { get => _awaitingSelection; private set { if (SetProperty(ref _awaitingSelection, value)) { RaisePropertyChanged(nameof(ConnectionLabel)); ConnectionCommand.RaiseCanExecuteChanged(); } } }
    public LabBoardIdentity? SelectedBoard { get => _selectedBoard; set { if (SetProperty(ref _selectedBoard, value)) ConnectionCommand.RaiseCanExecuteChanged(); } }
    public LabLinkLogEntry? SelectedLog { get => _selectedLog; set => SetProperty(ref _selectedLog, value); }
    public string SearchText { get => _searchText; set { if (SetProperty(ref _searchText, value)) LogsView.Refresh(); } }
    public LabLogFilter Filter { get => _filter; set { if (SetProperty(ref _filter, value)) LogsView.Refresh(); } }

    public async Task ToggleAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy || _disposed) return;
        IsBusy = true;
        try
        {
            if (IsConnected || _port.IsConnected)
            {
                Log(LabLinkActivityKind.UserAction, "用户点击断开");
                await _port.DisconnectAsync(cancellationToken);
                IsConnected = _port.IsConnected;
                ConnectionStatus = IsConnected ? "断开未完成" : "未连接";
                AwaitingSelection = false;
                if (IsConnected) Fail("断开验证", "会话仍报告已连接", null, DeviceIdentity);
                else Log(LabLinkActivityKind.Success, "主控板已断开");
                return;
            }
            LastError = "";
            if (!AwaitingSelection)
            {
                Log(LabLinkActivityKind.UserAction, "用户点击连接主控板");
                ConnectionStatus = "正在发现设备";
                IReadOnlyList<LabBoardIdentity> discovered;
                try { discovered = await _port.DiscoverMatchingBoardsAsync(cancellationToken); }
                catch (Exception ex) { Fail("设备发现", ex); return; }
                Candidates.Clear();
                SelectedBoard = null; // never reuse a choice from a previous discovery
                foreach (var board in discovered) Candidates.Add(board);
                var decision = LabLinkConnectionPolicy.Decide(discovered);
                Log(LabLinkActivityKind.Discovery, $"发现 {Candidates.Count} 台匹配主控板");
                if (decision == LabConnectionDecision.None) { Fail("设备发现", "未发现匹配的主控板", null, "未识别设备"); return; }
                if (decision == LabConnectionDecision.RequireSelection)
                {
                    AwaitingSelection = true;
                    ConnectionStatus = $"发现 {Candidates.Count} 台匹配设备，请选择";
                    return;
                }
                SelectedBoard = Candidates[0];
            }
            if (SelectedBoard is null)
            {
                ConnectionStatus = "请选择一台主控板";
                return;
            }
            var boardToConnect = SelectedBoard;
            AwaitingSelection = false;
            DeviceIdentity = FormatIdentity(boardToConnect);
            ConnectionStatus = "正在连接";
            Log(LabLinkActivityKind.UserAction, "请求连接", DeviceIdentity);
            try
            {
                await _port.ConnectAsync(boardToConnect, cancellationToken);
                IsConnected = _port.IsConnected;
                ConnectionStatus = IsConnected ? "已连接" : "连接未完成";
                if (IsConnected) Log(LabLinkActivityKind.Success, "主控板连接成功", DeviceIdentity);
                else Fail("连接验证", "会话未报告连接成功", null, DeviceIdentity);
            }
            catch (Exception ex) { Fail("连接主控板", ex, DeviceIdentity); }
        }
        catch (OperationCanceledException) { ConnectionStatus = "操作已取消"; Log(LabLinkActivityKind.Status, ConnectionStatus); }
        catch (Exception ex) { Fail("界面操作", ex); }
        finally { IsBusy = false; }
    }

    private static string FormatIdentity(LabBoardIdentity d) =>
        $"{d.DisplayName} | {d.VidPid ?? "VID/PID未知"} | SN:{d.Serial ?? "未知"} | {d.DevicePath}";

    private void Fail(string stage, Exception exception, string? identity = null)
    {
        // DEVICE should supply a typed error event carrying its actual failed stage and Win32 code.
        // Do not invent a Win32 code from a generic exception or replace a more specific DEVICE event.
        var native = exception as Win32Exception ?? exception.InnerException as Win32Exception;
        Fail(stage, exception.Message, native?.NativeErrorCode, identity);
    }
    private void Fail(string stage, string message, int? win32, string? identity = null)
    {
        IsConnected = _port.IsConnected;
        ConnectionStatus = IsConnected ? "设备已连接（操作失败）" : "操作失败";
        LastError = $"失败阶段：{stage} | Win32：{(win32.HasValue ? $"{win32.Value} (0x{win32.Value:X8})" : "未提供")} | 设备：{identity ?? DeviceIdentity} | {message}";
        Log(new LabLinkUiEvent(DateTimeOffset.Now, LabLinkActivityKind.Failure, message,
            identity ?? DeviceIdentity, stage, win32));
    }

    private void OnActivity(object? sender, LabLinkUiEvent activity)
    {
        if (_disposed) return;
        if (_dispatcher.CheckAccess()) ApplyActivity(activity);
        else _dispatcher.BeginInvoke(() => { if (!_disposed) ApplyActivity(activity); });
    }
    private void ApplyActivity(LabLinkUiEvent activity)
    {
        Log(activity);
        if (activity.Kind == LabLinkActivityKind.Status)
        {
            IsConnected = _port.IsConnected;
            // Preserve a detailed failure description until a genuinely successful state transition.
            if (IsConnected)
            {
                ConnectionStatus = "已连接";
                LastError = "";
            }
            else if (!IsBusy)
            {
                ConnectionStatus = "未连接";
            }
            if (_port.ConnectedBoard is not null) DeviceIdentity = FormatIdentity(_port.ConnectedBoard);
            else if (!IsConnected) DeviceIdentity = "未连接主控板";
        }
        if (activity.Kind is LabLinkActivityKind.Failure or LabLinkActivityKind.Error)
        {
            LastError = $"失败阶段：{activity.Stage ?? "未提供"} | Win32：{(activity.Win32Error.HasValue ? $"{activity.Win32Error.Value} (0x{activity.Win32Error.Value:X8})" : "未提供")} | 设备：{activity.DeviceIdentity ?? DeviceIdentity} | {activity.Message}";
            if (!_port.IsConnected) { IsConnected = false; ConnectionStatus = "连接/通信失败"; }
        }
    }
    private void Log(LabLinkActivityKind kind, string message, string? identity = null) =>
        Log(new LabLinkUiEvent(DateTimeOffset.Now, kind, message, identity));
    private void Log(LabLinkUiEvent e)
    {
        var line = new LabLinkLogEntry(e);
        Logs.Add(line);
        SelectedLog = line;
    }
    private void CopyLog()
    {
        try { Clipboard.SetText(SelectedLog?.Detail ?? LabLinkLogUtilities.Export(LogsView.Cast<LabLinkLogEntry>())); }
        catch (Exception ex) { LastError = "复制失败：" + ex.Message; }
    }
    private void ExportLog()
    {
        try
        {
            var dialog = new SaveFileDialog { Title = "导出操作日志", Filter = "TSV 日志 (*.tsv)|*.tsv|文本文件 (*.txt)|*.txt", FileName = "AutoTest-LabLink-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".tsv" };
            if (dialog.ShowDialog() != true) return;
            File.WriteAllText(dialog.FileName, LabLinkLogUtilities.Export(LogsView.Cast<LabLinkLogEntry>()), new System.Text.UTF8Encoding(true));
            Log(LabLinkActivityKind.UserAction, "操作日志已导出：" + dialog.FileName);
        }
        catch (Exception ex) { LastError = "导出失败：" + ex.Message; Log(LabLinkActivityKind.Error, LastError); }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _port.Activity -= OnActivity;
    }
}

