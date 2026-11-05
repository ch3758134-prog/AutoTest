using System.Collections.Immutable;
using AutoTest.Core;

namespace AutoTest.Contracts;

public enum ApplicationCommandKind { MockNormal, MockTimeout, MockDisconnect,
    MockUnauthorized, PhysicalOutput, RealProgramming }
public sealed record ApplicationCommand(ApplicationCommandKind Kind, Guid CorrelationId);
public sealed record UiSelection(ProjectId? Project, TestDocumentId? Document, StepId? Step);
public sealed record RunProjection(RunId? Run, RunState RunState, StepId? HighlightedStep,
    Verdict Verdict);
public sealed record StationProjection(StationId Station, StationConnectionState Connection,
    long Generation, bool IsSimulation, PhysicalState PhysicalState);
public sealed record WorkbenchProjection(
    string ModeLabel, bool PhysicalOutputsEnabled, string RequestStatus,
    string AckStatus, string ObservationStatus, Verdict Verdict,
    string FailureReason, RunProjection Run, StationProjection Station,
    UiSelection Selection, ImmutableArray<RunFact> Facts);
public sealed record ApplicationCommandResult(Outcome Outcome,
    WorkbenchProjection Projection, string? FailureCode);
public interface IApplicationCommandGateway
{
    ValueTask<ApplicationCommandResult> ExecuteAsync(ApplicationCommand command,
        CancellationToken ct);
    WorkbenchProjection Observe();
    string? GetCanExecuteReason(ApplicationCommand command);
}
public sealed record ProductionReadiness(bool Ready, ImmutableArray<string> Blockers);
public sealed record ProductionPlan(string DutIdentity, ProjectSnapshot Project,
    StationProfile Station, RunId ReservedRun);
public readonly record struct ProductionJobId(Guid Value);
public interface IProductionService
{
    ValueTask<ProductionReadiness> PrepareAsync(MatchDecision match,
        ProjectSnapshot project, StationProfile station);
    ValueTask<ProductionJobId> StartAsync(ProductionPlan plan, Authorization authorization);
}
