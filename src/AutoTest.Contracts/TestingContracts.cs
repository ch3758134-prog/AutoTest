using System.Collections.Immutable;
using AutoTest.Core;

namespace AutoTest.Contracts;

public enum EditAction { Insert, Delete, Rename, Undo, Redo }
public sealed record EditCommand(EditAction Action, StepId Step, string? Value,
    Guid TransactionId);
public sealed record EditResult(bool Applied, string? Diagnostic,
    ImmutableArray<StepId> AffectedSteps);
public sealed record CompileRequest(TestDocumentId Document,
    ProjectSnapshot Project, RevisionFingerprint DocumentFingerprint);
public sealed record CompileResult(bool Ready, PreparedPlan? Plan,
    ImmutableArray<string> Diagnostics);
public sealed record PreparedStep(StepId Id, string NodeType, string ParametersFingerprint);
public sealed record PreparedPlan(TestDocumentId Document, RevisionFingerprint PlanFingerprint,
    ImmutableArray<PreparedStep> Steps, ImmutableArray<AssetRef> LockedAssets);
public sealed record RunSnapshot(RunId Run, ProjectSnapshot Project, StationProfile Station,
    RevisionFingerprint PlanFingerprint, ImmutableArray<AssetRef> LockedAssets);
public enum RunState { NotStarted, Preparing, Running, PausePending, StopPending,
    Completed, Failed, Aborted, Unknown }
public enum StepState { Pending, Running, Passed, Failed, Skipped, Inconclusive }
public sealed record RunControlRequest(RunId Run, string Transition);
public interface ITestAuthoringService
{
    EditResult Apply(TestDocumentId documentId, EditCommand command);
}
public interface IPlanCompiler
{
    ValueTask<CompileResult> PrepareAsync(CompileRequest request, CancellationToken ct);
}
public interface IRunController
{
    ValueTask<OperationResult<RunId>> StartAsync(PreparedPlan plan, RunSnapshot snapshot,
        CancellationToken ct);
    ValueTask<OperationResult<Unit>> RequestStopAsync(RunId id, CancellationToken ct);
}
