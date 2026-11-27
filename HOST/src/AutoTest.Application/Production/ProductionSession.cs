using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;

namespace AutoTest.Application.Production;

public enum ProductionState
{
    Empty, Scanned, Matched, Ready, Running, AwaitingCommit, Completed, Blocked,
    Failed, Aborted, Unknown
}

/// <summary>
 /// One immutable imported project package owns its FLASH and ordered TEST.
 /// The production station may select it, never assemble a separate recipe.
 /// </summary>
public sealed record ProductionProjectExecution(
    ProjectId Project, ProjectRevision Revision,
    RevisionFingerprint PackageFingerprint, bool RequiresFlash,
    string ExpectedSupply, ImmutableArray<AssetRef> Assets);

// Physical identity is locked to one attempt. A retry always creates a new RunId.
public sealed record ProductionJobSnapshot(RunId Run, string DutIdentity, RawScan Scan,
    ProductionProjectExecution ProjectExecution, RuleRevision RuleRevision,
    ProjectRevision ProjectRevision, StationId Station, bool IsMock);

public sealed class ProductionSession
{
    private readonly object _sync = new();
    public ProductionState State { get; private set; } = ProductionState.Empty;
    public RawScan? CurrentScan { get; private set; }
    public MatchDecision? Match { get; private set; }
    public ProductionJobSnapshot? Job { get; private set; }
    public string? FailureCode { get; private set; }

    public OperationResult<Unit> StartDraft(RawScan scan)
    {
        lock (_sync)
        {
            if (State is not (ProductionState.Empty or ProductionState.Scanned or
                ProductionState.Matched or ProductionState.Blocked))
                return OperationResult.Rejected("STATION_BUSY", "Current unit requires completion or explicit reset.");
            if (string.IsNullOrWhiteSpace(scan.Text))
                return OperationResult.Rejected("SCAN_EMPTY", "Scan is blank.");
            CurrentScan = scan; Match = null; Job = null; FailureCode = null;
            State = ProductionState.Scanned;
            return OperationResult.Accepted();
        }
    }

    public OperationResult<Unit> SetMatch(MatchDecision result)
    {
        lock (_sync)
        {
            if (State != ProductionState.Scanned)
                return OperationResult.Rejected("SESSION_NOT_SCANNED", "No editable draft.");
            Match = result;
            State = result.Allowed && !string.IsNullOrWhiteSpace(result.DutIdentity)
                ? ProductionState.Matched : ProductionState.Blocked;
            FailureCode = State == ProductionState.Blocked ? result.RejectionCode ?? "INVALID_IDENTITY" : null;
            return State == ProductionState.Matched ? OperationResult.Accepted() :
                OperationResult.Rejected(FailureCode!, "Identity match blocked.");
        }
    }

    public OperationResult<ProductionJobSnapshot> Confirm(ProductionProjectExecution execution,
        RuleRevision ruleRevision, ProjectRevision projectRevision, StationId station, bool isMock)
    {
        lock (_sync)
        {
            if (State != ProductionState.Matched || Match?.Allowed != true || CurrentScan is null ||
                string.IsNullOrWhiteSpace(Match.DutIdentity))
                return OperationResult<ProductionJobSnapshot>.Rejected("SESSION_NOT_MATCHED", "No valid candidate.");
            if (execution.Project.Value == Guid.Empty ||
                execution.Revision.Value <= 0 ||
                execution.Revision != projectRevision ||
                execution.PackageFingerprint.Sha256 is not { Length: 64 })
                return OperationResult<ProductionJobSnapshot>.Rejected(
                    "PROJECT_VERSION_INVALID", "Installed project version is not frozen.");
            if (execution.Assets.IsDefaultOrEmpty ||
                !execution.Assets.SequenceEqual(Match.MatchedAssets))
                return OperationResult<ProductionJobSnapshot>.Rejected(
                    "PROJECT_ASSET_SNAPSHOT_MISMATCH", "Project assets changed after matching.");
            Job = new ProductionJobSnapshot(RunId.New(), Match.DutIdentity, CurrentScan,
                execution, ruleRevision, projectRevision, station, isMock);
            State = ProductionState.Ready;
            return OperationResult<ProductionJobSnapshot>.Accepted(Job);
        }
    }

    public OperationResult<Unit> BeginExecution()
    {
        lock (_sync)
        {
            if (State != ProductionState.Ready || Job is null)
                return OperationResult.Rejected("NOT_READY", "Production job has not passed preflight.");
            State = ProductionState.Running;
            return OperationResult.Accepted();
        }
    }

    public void Mark(ProductionState target, string? failureCode = null)
    {
        lock (_sync)
        {
            if (State != ProductionState.Running && State != ProductionState.AwaitingCommit)
                throw new InvalidOperationException("INVALID_RUN_TRANSITION");
            if (target is not (ProductionState.AwaitingCommit or ProductionState.Completed or
                ProductionState.Blocked or ProductionState.Failed or ProductionState.Aborted or ProductionState.Unknown))
                throw new InvalidOperationException("INVALID_RUN_TARGET");
            State = target; FailureCode = failureCode;
        }
    }

    public OperationResult<Unit> Reset()
    {
        lock (_sync)
        {
            if (State is ProductionState.Running or ProductionState.AwaitingCommit or
                ProductionState.Unknown or ProductionState.Aborted)
                return OperationResult.Rejected("RECOVERY_REQUIRED", "Run is active or its side effects require reconciliation.");
            CurrentScan = null; Match = null; Job = null; FailureCode = null;
            State = ProductionState.Empty;
            return OperationResult.Accepted();
        }
    }
}
