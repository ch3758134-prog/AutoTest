using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AutoTest.Specialists.Scan.Lab;

// LAB is explicitly NOT a factory release authority. All origin fields are evidence facts.
public enum LabOrigin { Unknown, Mock, Replay, Physical }
public enum LabMatchStatus { Matched, Empty, Invalid, NoMatch, Ambiguous, StaleCatalog, InvalidCatalog }
public enum LabRunStatus { MockPass, MockFail, Interrupted, Rejected, Error }
public enum LabMockScenario { Pass, Fail, Disconnect, PowerLoss, Abort, Exception, SlowPass }

public sealed record LabIdentityRule(string RuleId, string Pattern, string IdentityFieldsCsv,
    bool Enabled, string OverlapReviewId = "");

public sealed record LabArtifact(string Key, string Revision, string Sha256,
    LabOrigin Source, bool Resolved);

public sealed record LabRecipe(string RecipeId, string Revision, string ModelField,
    string CustomerField, LabArtifact TestProgram, LabArtifact StationProfile,
    LabArtifact? FlashImage, bool RequiresFlash, bool Enabled,
    string EffectiveFromUtc = "", string EffectiveUntilUtc = "");

public sealed record LabRuleSample(string RawBarcode, string ExpectedRuleId, string ExpectedRecipeId,
    string ObservedAtUtc = "");

// A catalog snapshot is immutable by convention: consumers always Copy() before editing it.
public sealed record LabCatalog(string Revision, bool Published,
    IReadOnlyList<LabIdentityRule> IdentityRules,
    IReadOnlyList<LabRecipe> Recipes,
    IReadOnlyList<LabRuleSample> Samples)
{
    public LabCatalog Copy() => this with
    {
        IdentityRules = new List<LabIdentityRule>(IdentityRules ?? Array.Empty<LabIdentityRule>()),
        Recipes = new List<LabRecipe>(Recipes ?? Array.Empty<LabRecipe>()),
        Samples = new List<LabRuleSample>(Samples ?? Array.Empty<LabRuleSample>())
    };
}

public sealed record LabIdentity(string UnitKey, string RuleId,
    IReadOnlyDictionary<string, string> Fields);

public sealed record LabMatch(LabMatchStatus Status, string Code,
    LabIdentity? Identity, LabRecipe? Recipe,
    IReadOnlyList<string> CandidateIds, string CatalogRevision = "", string RawScan = "")
{
    public bool Accepted => Status == LabMatchStatus.Matched && Identity is not null && Recipe is not null;
}

public sealed record LabPreflight(bool Ready, IReadOnlyList<string> Blockers);

public sealed record LabRunRequest(Guid RunId, Guid OperationId, string ScanValue,
    string UnitKey, string RuleId, string CatalogRevision, string RecipeId,
    string RecipeRevision, LabOrigin RequestedOrigin, DateTimeOffset StartedUtc,
    Guid CorrelationId, long? SessionGeneration, string ProjectRevision);

public sealed record LabTestResult(bool Passed, LabOrigin Source, string Summary,
    IReadOnlyList<string> StepFacts, bool ConnectionHealthy, bool EvidenceDurable,
    bool HasPhysicalIdentity, bool HasMeasurementEvidence, string TraceReference);

public sealed record LabScanHistoryEvent(DateTimeOffset CapturedUtc, string RawText,
    LabMatchStatus MatchStatus, string Code, LabOrigin Source);

public sealed record LabRunRecord(LabRunRequest Request, LabRunStatus Status,
    LabOrigin ActualOrigin, string ResultCode, DateTimeOffset FinishedUtc,
    IReadOnlyList<string> StepFacts, bool FactoryReleased, string TraceReference);

public interface ILabTestRunner
{
    Task<LabTestResult> RunAsync(LabRunRequest request, LabMockScenario scenario,
        CancellationToken cancellationToken);
}

public interface ILabRunLedger
{
    Task AppendAsync(LabRunRecord record, CancellationToken cancellationToken);
    IReadOnlyList<LabRunRecord> Snapshot();
}

// Host-owned bridge: TEST and RECORD must supply real implementations during I6 integration.
// Physical release is denied here regardless of any UI control or mock response.
public interface ILabProductionPhysicalGate
{
    LabPreflight Check(LabRunRequest request, LabRecipe recipe);
}

public sealed class DenyPhysicalGate : ILabProductionPhysicalGate
{
    public LabPreflight Check(LabRunRequest request, LabRecipe recipe) =>
        new(false, new[] { "PHYSICAL_PRODUCTION_NOT_AUTHORIZED" });
}

