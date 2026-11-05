using System.Collections.ObjectModel;
using AutoTest.Contracts;
using Prism.Commands;
using Prism.Mvvm;

namespace AutoTest.Desktop.Shell;

public sealed class MainWindowViewModel : BindableBase
{
    private readonly IApplicationCommandGateway _gateway;
    private bool _busy;
    private string _requestStatus = "NOT_STARTED";
    private string _ackStatus = "NO_ACK";
    private string _observationStatus = "NO_OBSERVATION";
    private string _verdict = "Inconclusive";
    private string _failureReason = "";

    public MainWindowViewModel(IApplicationCommandGateway gateway)
    {
        _gateway = gateway;
        NormalCommand = Make(ApplicationCommandKind.MockNormal);
        TimeoutCommand = Make(ApplicationCommandKind.MockTimeout);
        DisconnectCommand = Make(ApplicationCommandKind.MockDisconnect);
        UnauthorizedCommand = Make(ApplicationCommandKind.MockUnauthorized);
        UpdateProjection(_gateway.Observe());
    }

    public DelegateCommand NormalCommand { get; }
    public DelegateCommand TimeoutCommand { get; }
    public DelegateCommand DisconnectCommand { get; }
    public DelegateCommand UnauthorizedCommand { get; }
    public ObservableCollection<string> FactLines { get; } = new();
    public bool IsBusy { get => _busy; private set
        { if (SetProperty(ref _busy, value)) { RaisePropertyChanged(nameof(BusyLabel));
            NormalCommand.RaiseCanExecuteChanged(); TimeoutCommand.RaiseCanExecuteChanged();
            DisconnectCommand.RaiseCanExecuteChanged(); UnauthorizedCommand.RaiseCanExecuteChanged(); } } }
    public string BusyLabel => IsBusy ? "Executing mock request…" : "Ready";
    public string RequestStatus { get => _requestStatus; private set => SetProperty(ref _requestStatus, value); }
    public string AckStatus { get => _ackStatus; private set => SetProperty(ref _ackStatus, value); }
    public string ObservationStatus { get => _observationStatus; private set => SetProperty(ref _observationStatus, value); }
    public string Verdict { get => _verdict; private set => SetProperty(ref _verdict, value); }
    public string FailureReason { get => _failureReason; private set => SetProperty(ref _failureReason, value); }

    private DelegateCommand Make(ApplicationCommandKind kind) =>
        new(() => _ = ExecuteScenarioAsync(kind),
            () => !IsBusy && _gateway.GetCanExecuteReason(new ApplicationCommand(kind, Guid.Empty)) is null);

    public async Task ExecuteScenarioAsync(ApplicationCommandKind kind)
    {
        if (IsBusy) return;
        var command = new ApplicationCommand(kind, Guid.NewGuid());
        var denial = _gateway.GetCanExecuteReason(command);
        if (denial is not null) { FailureReason = denial; return; }
        IsBusy = true;
        RequestStatus = "RUNNING_MOCK";
        AckStatus = "WAITING_FOR_ACK";
        ObservationStatus = "NO_OBSERVATION";
        try
        {
            var result = await _gateway.ExecuteAsync(command, CancellationToken.None);
            UpdateProjection(result.Projection);
        }
        catch (Exception exception)
        {
            RequestStatus = "ERROR";
            AckStatus = "NO_ACK";
            ObservationStatus = "NO_OBSERVATION";
            Verdict = "Inconclusive";
            FailureReason = "HOST_MOCK_EXCEPTION: " + exception.GetType().Name;
        }
        finally { IsBusy = false; }
    }

    private void UpdateProjection(WorkbenchProjection projection)
    {
        RequestStatus = projection.RequestStatus;
        AckStatus = projection.AckStatus;
        ObservationStatus = projection.ObservationStatus;
        Verdict = projection.Verdict.ToString();
        FailureReason = projection.FailureReason;
        FactLines.Clear();
        foreach (var fact in projection.Facts)
            FactLines.Add($"{fact.Kind,-16} [{fact.Quality}] {fact.Description}");
    }
}
