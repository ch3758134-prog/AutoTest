using System.Collections.ObjectModel;
using AutoTest.Application.G4;
using AutoTest.Core;
using AutoTest.Records.Queries;
using Prism.Commands;
using Prism.Mvvm;

namespace AutoTest.Desktop.G4;

/// <summary>Every command calls a real Application service. No hardware actions are registered.</summary>
public sealed class G4ToolsViewModel : BindableBase
{
    private readonly G4IntegrationHost _host;
    private string _scan = "EPB-00001";
    private string _scanResult = "等待模拟扫码";
    private string _udsResult = "等待 Mock CAN 诊断";
    private string _flashResult = "等待 Mock Bootloader";
    private string _historyResult = "等待 SQLite 查询";
    private bool _busy;
    public G4ToolsViewModel(G4IntegrationHost host)
    {
        _host = host;
        ScanCommand = new DelegateCommand(() => _ = RunAsync(ScanWorkAsync), () => !_busy);
        HistoryCommand = new DelegateCommand(() => _ = RunAsync(LoadHistoryAsync), () => !_busy);
        UdsCommand = new DelegateCommand(() => _ = RunAsync(UdsWorkAsync), () => !_busy);
        FlashCommand = new DelegateCommand(() => _ = RunAsync(FlashWorkAsync), () => !_busy);
    }
    public string ScanInput { get => _scan; set => SetProperty(ref _scan, value); }
    public string ScanResult { get => _scanResult; private set => SetProperty(ref _scanResult, value); }
    public string UdsResult { get => _udsResult; private set => SetProperty(ref _udsResult, value); }
    public string FlashResult { get => _flashResult; private set => SetProperty(ref _flashResult, value); }
    public string HistoryResult { get => _historyResult; private set => SetProperty(ref _historyResult, value); }
    public ObservableCollection<string> History { get; } = new();
    public DelegateCommand ScanCommand { get; }
    public DelegateCommand HistoryCommand { get; }
    public DelegateCommand UdsCommand { get; }
    public DelegateCommand FlashCommand { get; }
    private async Task RunAsync(Func<Task> job)
    {
        if (_busy) return;
        _busy = true;
        RaiseCommands();
        try { await job(); }
        catch (Exception ex)
        {
            var error = "操作失败：" + ex.GetType().Name + ": " + ex.Message;
            ScanResult = error; UdsResult = error; FlashResult = error; HistoryResult = error;
        }
        finally { _busy = false; RaiseCommands(); }
    }
    private async Task ScanWorkAsync()
    {
        ScanResult = "正在匹配扫码身份、锁定配方并执行正式 TEST 步骤…";
        var result = await _host.Production.SimulateScanAsync(ScanInput);
        ScanResult = result.Code + " | Identity=" + (result.Identity ?? "<blocked>") +
            " | RunId=" + (result.Run?.ToString() ?? "<none>") +
            " | SQLiteDurable=" + result.Durable + " | PhysicalRelease=false" +
            (result.Record is null ? "" : " | TEST=" + result.Record.TestReport.Verdict +
             " | StoredVerdict=" + result.Record.Stored.Verdict +
             " | StepFacts=" + result.Record.Stored.StepFactCount);
        if (result.Durable) await LoadHistoryAsync();
    }
    private async Task LoadHistoryAsync()
    {
        var entries = await _host.Records.HistoryAsync();
        History.Clear();
        foreach (var e in entries)
            History.Add($"{e.StartedUtc:yyyy-MM-dd HH:mm:ss} | {e.DutId} | {e.Run} | {e.Mode} | " +
                $"{e.Persistence} / {e.Verdict} | StepFacts={e.StepFactCount}");
        HistoryResult = $"SQLite 实际读取：{entries.Length} 条；Development / Simulated 不计生产 PASS";
    }
    private async Task UdsWorkAsync()
    {
        UdsResult = "ISO-TP / shared CAN Hub · Mock 会话运行中…";
        var result = await _host.Diagnostics.ReadDtcMockAsync();
        UdsResult = result.Code + " | Outcome=" + result.Outcome +
            " | PDU=" + Convert.ToHexString(result.Response) +
            " | CAN Hub TX=" + result.SharedHubRequests + " | RealDevice=false";
    }
    private async Task FlashWorkAsync()
    {
        FlashResult = "Mock Bootloader · 预检/受控传输/校验中…";
        var result = await _host.Flash.DemoAsync();
        FlashResult = result.Code + " | job=" + result.Job?.Job.ToString() +
            " | stages=" + result.Job?.Facts.Length +
            " | blockers=" + string.Join(";", result.Blockers) +
            " | PhysicalWrite=false";
    }
    private void RaiseCommands()
    {
        ScanCommand.RaiseCanExecuteChanged(); HistoryCommand.RaiseCanExecuteChanged();
        UdsCommand.RaiseCanExecuteChanged(); FlashCommand.RaiseCanExecuteChanged();
    }
}
