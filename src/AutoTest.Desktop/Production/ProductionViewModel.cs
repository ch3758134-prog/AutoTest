using AutoTest.Application.Production;
using AutoTest.Contracts;
using AutoTest.Core;
using Prism.Commands;
using Prism.Mvvm;

namespace AutoTest.Desktop.Production;

// This view model is UI-only. Input focus must be attached by the eventual ProductionView;
// Shell and prism registration belong exclusively to INTEGRATION/UI.
public sealed class ProductionViewModel : BindableBase
{
    private readonly ProductionCommandHandler _commands;
    private string _scanText = string.Empty;
    private string _status = "SCAN_READY";
    private string _identity = "";
    private string _runId = "";
    private string _verdict = "Inconclusive";
    private bool _busy;
    private ProductionStationReadiness? _station;

    public ProductionViewModel(ProductionCommandHandler commands)
    {
        _commands = commands;
        StartCommand = new DelegateCommand(() => _ = StartWorkAsync(), () => !IsBusy);
        StopCommand = new DelegateCommand(() => _ = StopWorkAsync(), () => IsBusy);
        ResetCommand = new DelegateCommand(Reset, () => !IsBusy);
    }

    public string ScanText { get => _scanText; set => SetProperty(ref _scanText, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string Identity { get => _identity; private set => SetProperty(ref _identity, value); }
    public string RunId { get => _runId; private set => SetProperty(ref _runId, value); }
    public string Verdict { get => _verdict; private set => SetProperty(ref _verdict, value); }
    public bool IsBusy { get => _busy; private set
        {
            if (SetProperty(ref _busy, value))
            { StartCommand.RaiseCanExecuteChanged(); StopCommand.RaiseCanExecuteChanged();
                ResetCommand.RaiseCanExecuteChanged(); }
        }
    }
    public DelegateCommand StartCommand { get; }
    public DelegateCommand StopCommand { get; }
    public DelegateCommand ResetCommand { get; }

    public void AttachStationReadiness(ProductionStationReadiness readiness) => _station = readiness;

    public async Task CaptureScanAsync(RawScan scan, bool autoStart = true,
        CancellationToken ct = default)
    {
        // Do not allow a second scan to mutate the ViewModel while running.
        if (IsBusy) { Status = "STATION_BUSY"; return; }
        if (_station is null) { Status = "STATION_NOT_SELECTED"; return; }
        IsBusy = true;
        Status = "MATCHING";
        try
        {
            var response = await _commands.ScanAsync(scan, _station, autoStart, ct);
            ShowResult(response);
        }
        catch (OperationCanceledException) { Status = "SCAN_CANCELLED"; }
        catch (Exception) { Status = "SCAN_UNEXPECTED_ERROR"; }
        finally { IsBusy = false; ScanText = string.Empty; }
    }

    public async Task StartWorkAsync(CancellationToken ct = default)
    {
        if (IsBusy) return;
        IsBusy = true;
        try { ShowResult(await _commands.StartAsync(ct)); }
        finally { IsBusy = false; }
    }

    public async Task StopWorkAsync(CancellationToken ct = default) =>
        ShowResult(await _commands.StopAsync(ct));

    public void Reset()
    {
        var result = _commands.RetryWithNewRun();
        Status = result.Outcome == Outcome.Accepted ? "SCAN_READY" : result.FailureCode ?? "RESET_DENIED";
        if (result.Outcome == Outcome.Accepted) { Identity = string.Empty; RunId = string.Empty; Verdict = "Inconclusive"; }
    }

    public void ShowResult(ProductionCommandFeedback feedback)
    {
        Status = feedback.Code;
        Identity = feedback.DutIdentity ?? "";
        RunId = feedback.Run?.ToString() ?? "";
        Verdict = feedback.Verdict.ToString();
    }
}
