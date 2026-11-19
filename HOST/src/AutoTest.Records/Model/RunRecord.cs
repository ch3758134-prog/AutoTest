using System.Collections.Immutable;
using System.Text.Json;
using AutoTest.Contracts;
using AutoTest.Core;

namespace AutoTest.Records.Model;

/// <summary>Production/Debug classification is explicit; it is never inferred from a PASS verdict.</summary>
public enum RecordRunMode { Production, Development, Diagnostic }
public enum RecordPersistenceState { Writing, Committed, Interrupted }

/// <summary>A normalized scanner identity. An unknown/unvalidated identity cannot enter production counts.</summary>
public sealed record SessionIdentity(string DutId, string IdentityRuleFingerprint, string Source)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DutId) || DutId != DutId.Trim() || DutId.Length > 256 ||
            DutId.Any(char.IsControl))
            throw new InvalidDataException("A normalized, nonempty DUT identity is required.");
        if (IdentityRuleFingerprint is not { Length: 64 } || !IdentityRuleFingerprint.All(Uri.IsHexDigit) ||
            string.IsNullOrWhiteSpace(Source))
            throw new InvalidDataException("Identity rule and provenance are required.");
    }
}

public sealed record RunAttempt(int Number, DateTimeOffset StartedUtc, DateTimeOffset? CompletedUtc,
    string Disposition);

/// <summary>Immutable run header. Facts, verdict and persistence receipt have separate lifecycles.</summary>
public sealed record RunRecord(RunSnapshot Snapshot, SessionIdentity Identity, RecordRunMode Mode,
    DateTimeOffset StartedUtc, string HostBuildIdentity, string? OperatorId = null)
{
    public RunId RunId => Snapshot.Run;

    public static RunRecord CreateFromSnapshot(RunSnapshot snapshot, SessionIdentity identity,
        RecordRunMode mode, DateTimeOffset startedUtc, string hostBuildIdentity, string? operatorId = null)
    {
        var result = new RunRecord(snapshot, identity, mode, startedUtc, hostBuildIdentity, operatorId);
        result.Validate();
        return result;
    }

    public void Validate()
    {
        if (Snapshot is null || Snapshot.Project is null || Snapshot.Station is null ||
            Snapshot.Run.Value == Guid.Empty || Snapshot.Project.Id.Value == Guid.Empty ||
            string.IsNullOrWhiteSpace(Snapshot.Station.Station.Value) ||
            Snapshot.Project.Fingerprint.Sha256 is not { Length: 64 } ||
            Snapshot.Station.Roles is null ||
            Snapshot.Station.Roles.Revision.Sha256 is not { Length: 64 } ||
            Snapshot.Station.ProfileRevision.Sha256 is not { Length: 64 } ||
            string.IsNullOrWhiteSpace(Snapshot.Station.VoltageProfile) ||
            Snapshot.PlanFingerprint.Sha256 is not { Length: 64 } ||
            Snapshot.LockedAssets.IsDefault || Snapshot.Project.Assets.IsDefault)
            throw new InvalidDataException("RunSnapshot is incomplete.");
        Identity?.Validate();
        if (Identity is null || StartedUtc == default || StartedUtc.Offset != TimeSpan.Zero ||
            string.IsNullOrWhiteSpace(HostBuildIdentity) || !Enum.IsDefined(Mode))
            throw new InvalidDataException("Run identity, UTC start or host build is invalid.");
        foreach (var asset in Snapshot.LockedAssets.Concat(Snapshot.Project.Assets))
            if (asset.Id.Value == Guid.Empty || asset.Revision.Value <= 0 || asset.Fingerprint.Sha256 is not { Length: 64 })
                throw new InvalidDataException("Locked asset fingerprint is missing.");
    }

    /// <summary>Primitive-only, canonical snapshot representation, independent of current workspace.</summary>
    public string CanonicalSnapshotJson()
    {
        Validate();
        return JsonSerializer.Serialize(new
        {
            Run = RunId.ToString(),
            Dut = Identity.DutId,
            IdentityRule = Identity.IdentityRuleFingerprint,
            IdentitySource = Identity.Source,
            Mode = Mode.ToString(),
            StartUtc = StartedUtc.ToString("O"),
            HostBuild = HostBuildIdentity,
            Operator = OperatorId,
            ProjectId = Snapshot.Project.Id.ToString(),
            ProjectRevision = Snapshot.Project.Revision.Value,
            ProjectFingerprint = Snapshot.Project.Fingerprint.Sha256,
            ProjectSchema = Snapshot.Project.SchemaVersion,
            ProjectAssets = Snapshot.Project.Assets.Select(a => new
                { Id = a.Id.ToString(), Revision = a.Revision.Value, Hash = a.Fingerprint.Sha256 }).ToArray(),
            PlanFingerprint = Snapshot.PlanFingerprint.Sha256,
            Station = new
            {
                Id = Snapshot.Station.Station.ToString(),
                Roles = new { Revision = Snapshot.Station.Roles.Revision.Sha256,
                    Snapshot.Station.Roles.DutPort, Snapshot.Station.Roles.KeypadPort },
                Snapshot.Station.VoltageProfile,
                ProfileRevision = Snapshot.Station.ProfileRevision.Sha256
            },
            LockedAssets = Snapshot.LockedAssets.Select(a => new
                { Id = a.Id.ToString(), Revision = a.Revision.Value, Hash = a.Fingerprint.Sha256 }).ToArray()
        });
    }
}

/// <summary>Finalization never changes the immutable run header or previously appended facts.</summary>
public sealed record RunCompletion(RunId Run, DateTimeOffset CompletedUtc, RunState TerminalState,
    Verdict? Verdict, string? FailureReason)
{
    public void Validate(DateTimeOffset startedUtc)
    {
        if (Run.Value == Guid.Empty || CompletedUtc.Offset != TimeSpan.Zero || CompletedUtc < startedUtc ||
            TerminalState is not (RunState.Completed or RunState.Failed or RunState.Aborted or RunState.Unknown))
            throw new InvalidDataException("Invalid terminal run completion.");
        if (Verdict == AutoTest.Core.Verdict.Pass && TerminalState != RunState.Completed)
            throw new InvalidDataException("PASS requires completed execution.");
    }
}
