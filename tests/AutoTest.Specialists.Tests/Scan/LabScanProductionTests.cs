using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoTest.Specialists.Scan.Lab;
using Xunit;

namespace AutoTest.Specialists.Tests.Scan;

public sealed class LabScanProductionTests
{
    private static LabCatalog Demo() => LabDemoCatalog.Create();
    private static LabMatch Scan(LabCatalog catalog, string text = "M000001") =>
        LabIdentityMatcher.Match(catalog, text, catalog.Revision);

    [Fact]
    public void DemoBarcodePreservesLeadingZeroesAndMatchesUniqueRecipe()
    {
        var result = Scan(Demo());
        Assert.True(result.Accepted);
        Assert.Equal("M000001", result.Identity!.Fields["serial"]);
        Assert.Equal("MOCK-RECIPE", result.Recipe!.RecipeId);
        Assert.Equal("MOCK-SERIAL", result.Identity.RuleId);
    }

    [Fact]
    public void InvalidAndEmptyScanFailClosed()
    {
        Assert.Equal(LabMatchStatus.Empty, Scan(Demo(), "\r\n").Status);
        Assert.Equal(LabMatchStatus.NoMatch, Scan(Demo(), "wrong").Status);
        Assert.Equal(LabMatchStatus.Invalid, Scan(Demo(), "M000001\0").Status);
    }

    [Fact]
    public void ScannerRequiresDedicatedFocusAndRejectsBusyInput()
    {
        Assert.Equal("SCANNER_FOCUS_REQUIRED", LabScannerAdmission.Admit("M000001", false, false).Code);
        Assert.Equal("STATION_BUSY_SCAN_REJECTED", LabScannerAdmission.Admit("M000001", true, true).Code);
        Assert.Equal("M000001", LabScannerAdmission.Admit("M000001\r\n", true, false).NormalizedText);
    }

    [Fact]
    public void StaleRevisionRejectsScan()
    {
        var result = LabIdentityMatcher.Match(Demo(), "M000001", "stale");
        Assert.Equal(LabMatchStatus.StaleCatalog, result.Status);
    }

    [Fact]
    public void DuplicateRegexPatternsRefusePublication()
    {
        var c = Demo();
        var next = c with { IdentityRules = c.IdentityRules.Concat(new[] {
            new LabIdentityRule("OTHER", @"(?<serial>M[0-9]{6})", "serial", true, "REVIEW-A")
        }).ToArray() };
        Assert.Contains(LabCatalogValidator.Validate(next, true),
            x => x.StartsWith("IDENTITY_PATTERN_DUPLICATE", StringComparison.Ordinal));
    }

    [Fact]
    public void UnknownRegexOverlapRequiresExplicitReview()
    {
        var c = Demo();
        var next = c with { IdentityRules = c.IdentityRules.Concat(new[] {
            new LabIdentityRule("OTHER", @"(?<serial>M[0-9]+)", "serial", true)
        }).ToArray() };
        Assert.Contains(LabCatalogValidator.Validate(next, true),
            x => x.StartsWith("IDENTITY_OVERLAP_REVIEW_REQUIRED", StringComparison.Ordinal));
    }

    [Fact]
    public void RuntimeMultipleIdentityMatchesAlwaysBlockedEvenAfterReview()
    {
        var c = Demo();
        var next = c with { IdentityRules = new[] {
            new LabIdentityRule("A", @"(?<serial>M[0-9]{6})", "serial", true, "REVIEW"),
            new LabIdentityRule("B", @"(?<serial>M[0-9]+)", "serial", true, "REVIEW")
        }, Samples = Array.Empty<LabRuleSample>() };
        var result = LabIdentityMatcher.Match(next, "M000001", next.Revision);
        Assert.Equal(LabMatchStatus.Ambiguous, result.Status);
        Assert.Equal(2, result.CandidateIds.Count);
    }

    [Fact]
    public void RecipeConditionOverlapBlocksPublication()
    {
        var c = Demo();
        var next = c with { Recipes = c.Recipes.Concat(new[] {
            c.Recipes[0] with { RecipeId = "OTHER" }
        }).ToArray() };
        Assert.Contains(LabCatalogValidator.Validate(next, true),
            x => x.StartsWith("RECIPE_CONDITION_OVERLAP", StringComparison.Ordinal));
    }

    [Fact]
    public void BadRuleDraftCanBeSavedButNotPublished()
    {
        var c = Demo() with { Published = false,
            IdentityRules = new[] { new LabIdentityRule("bad", "([", "serial", true) } };
        Assert.Contains(LabCatalogValidator.Validate(c, true), x => x.Contains("REGEX_INVALID"));
    }

    [Fact]
    public async Task DraftSavingDoesNotReplacePublishedCatalog()
    {
        var path = Path.Combine(Path.GetTempPath(), "autotest-lab-" + Guid.NewGuid().ToString("N"));
        var store = new LabCatalogStore(path);
        try
        {
            var original = await store.PublishAsync(Demo());
            var draft = await store.SaveDraftAsync(original with { IdentityRules = new[] {
                new LabIdentityRule("BAD", "([", "serial", true) } });
            Assert.False(draft.Published);
            Assert.Equal(original.Revision, (await store.LoadAsync())!.Revision);
            Assert.Equal(draft.Revision, (await store.LoadDraftAsync())!.Revision);
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.PublishAsync(draft));
            Assert.Equal(original.Revision, (await store.LoadAsync())!.Revision);
        }
        finally
        {
            foreach (var suffix in new[] { ".published.json", ".draft.json" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    [Fact]
    public async Task MalformedTimeWindowIsStorableDraftButPublicationIsRejected()
    {
        var path = Path.Combine(Path.GetTempPath(), "invalid-time-" + Guid.NewGuid().ToString("N"));
        var store = new LabCatalogStore(path);
        try
        {
            var catalog = Demo() with { Recipes = new[] {
                Demo().Recipes[0] with { EffectiveFromUtc = "not-a-UTC-timestamp" }
            } };
            var draft = await store.SaveDraftAsync(catalog);
            Assert.False(draft.Published);
            Assert.Contains(LabCatalogValidator.Validate(draft, true),
                x => x.StartsWith("RECIPE_TIME_WINDOW_INVALID", StringComparison.Ordinal));
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.PublishAsync(draft));
        }
        finally
        {
            foreach (var suffix in new[] { ".published.json", ".draft.json" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    [Fact]
    public async Task MockPassNeverReleasesFactoryUnit()
    {
        var engine = NewEngine();
        var result = await engine.RunAsync(Demo(), Scan(Demo()), "M000001", LabOrigin.Mock,
            LabMockScenario.Pass, false);
        Assert.Equal(LabRunStatus.MockPass, result.Status);
        Assert.False(result.FactoryReleased);
        Assert.Equal(0, engine.FactoryReleasedUnits);
        Assert.Equal(1, engine.DistinctDemoUnits);
    }

    [Fact]
    public async Task RetestRequiresExplicitConsentAndKeepsUniqueUnitCount()
    {
        var engine = NewEngine();
        await engine.RunAsync(Demo(), Scan(Demo()), "M000001", LabOrigin.Mock,
            LabMockScenario.Pass, false);
        var preflight = engine.Preflight(Demo(), Scan(Demo()), LabOrigin.Mock, false);
        Assert.Contains("RETEST_CONFIRMATION_REQUIRED", preflight.Blockers);
        await engine.RunAsync(Demo(), Scan(Demo()), "M000001", LabOrigin.Mock,
            LabMockScenario.Fail, true);
        Assert.Equal(2, engine.History.Count);
        Assert.Equal(1, engine.DistinctDemoUnits);
    }

    [Theory]
    [InlineData(LabMockScenario.Disconnect, LabRunStatus.MockFail)]
    [InlineData(LabMockScenario.PowerLoss, LabRunStatus.MockFail)]
    [InlineData(LabMockScenario.Abort, LabRunStatus.Interrupted)]
    [InlineData(LabMockScenario.Exception, LabRunStatus.Error)]
    public async Task FailureModesAreRecordedAndNotReleased(LabMockScenario scenario, LabRunStatus expected)
    {
        var engine = NewEngine();
        var result = await engine.RunAsync(Demo(), Scan(Demo()), "M000001", LabOrigin.Mock,
            scenario, false);
        Assert.Equal(expected, result.Status);
        Assert.Single(engine.History);
        Assert.False(result.FactoryReleased);
    }

    [Fact]
    public void PhysicalModeIsAlwaysBlockedWithoutSafetyApproval()
    {
        var gate = NewEngine().Preflight(Demo(), Scan(Demo()), LabOrigin.Physical, false);
        Assert.False(gate.Ready);
        Assert.Contains("PHYSICAL_PRODUCTION_NOT_AUTHORIZED", gate.Blockers);
    }

    [Fact]
    public async Task ChangedScanCannotBorrowPreviousMatchedIdentity()
    {
        var engine = NewEngine();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.RunAsync(
            Demo(), Scan(Demo()), "M999999", LabOrigin.Mock, LabMockScenario.Pass, false));
        Assert.Contains("SCAN_CHANGED_AFTER_MATCH", error.Message);
        Assert.Empty(engine.History);
    }

    [Fact]
    public async Task CancellationDuringSlowMockIsRecordedWithoutFactoryRelease()
    {
        using var cancel = new CancellationTokenSource();
        var engine = NewEngine();
        var running = engine.RunAsync(Demo(), Scan(Demo()), "M000001", LabOrigin.Mock,
            LabMockScenario.SlowPass, false, cancel.Token);
        Assert.True(engine.IsBusy);
        cancel.Cancel();
        var result = await running;
        Assert.Equal(LabRunStatus.Interrupted, result.Status);
        Assert.False(result.FactoryReleased);
        Assert.Single(engine.History);
    }

    [Fact]
    public async Task RunnerMustNotUpgradeMockOriginToPhysical()
    {
        var engine = new LabProductionOrchestrator(new ForgedPhysicalRunner(), new LabMemoryLedger());
        var result = await engine.RunAsync(Demo(), Scan(Demo()), "M000001", LabOrigin.Mock,
            LabMockScenario.Pass, false);
        Assert.Equal(LabRunStatus.Rejected, result.Status);
        Assert.Equal(LabOrigin.Unknown, result.ActualOrigin);
        Assert.False(result.FactoryReleased);
    }

    [Fact]
    public async Task SimultaneousRunCannotOverwriteLockedIdentity()
    {
        var pending = new TaskCompletionSource<LabTestResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var engine = new LabProductionOrchestrator(new PausedRunner(pending), new LabMemoryLedger());
        var first = engine.RunAsync(Demo(), Scan(Demo()), "M000001", LabOrigin.Mock,
            LabMockScenario.Pass, false);
        Assert.True(engine.IsBusy);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.RunAsync(
            Demo(), Scan(Demo(), "M000002"), "M000002", LabOrigin.Mock,
            LabMockScenario.Pass, false));
        Assert.Contains("STATION_BUSY", error.Message);
        pending.SetResult(new LabTestResult(true, LabOrigin.Mock, "MOCK_PASS",
            Array.Empty<string>(), true, false, false, false, ""));
        Assert.Equal("M000001", (await first).Request.ScanValue);
        Assert.Single(engine.History);
    }

    private static LabProductionOrchestrator NewEngine() =>
        new(new LabDeterministicMockRunner(), new LabMemoryLedger());

    private sealed class ForgedPhysicalRunner : ILabTestRunner
    {
        public Task<LabTestResult> RunAsync(LabRunRequest request, LabMockScenario scenario, CancellationToken ct) =>
            Task.FromResult(new LabTestResult(true, LabOrigin.Physical, "fake physical pass",
                Array.Empty<string>(), true, true, true, true, "fake trace"));
    }

    private sealed class PausedRunner : ILabTestRunner
    {
        private readonly TaskCompletionSource<LabTestResult> _result;
        public PausedRunner(TaskCompletionSource<LabTestResult> result) => _result = result;
        public Task<LabTestResult> RunAsync(LabRunRequest request, LabMockScenario scenario, CancellationToken ct) =>
            _result.Task;
    }
}
