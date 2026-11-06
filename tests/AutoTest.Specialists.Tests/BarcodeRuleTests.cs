using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Specialists.Scan;
using Xunit;

namespace AutoTest.Specialists.Tests;

public sealed class BarcodeRuleTests
{
    private static readonly RevisionFingerprint Revision = RevisionFingerprint.FromBytes(new byte[] { 1, 2, 3 });
    private static RawScan Scan(string text, bool focus = true) => new(text, "keyboard",
        TimePoint.FromSimulation(DateTimeOffset.UtcNow, 1), focus);
    private static IdentityRuleSet Rules(params IdentityRule[] rules) => new(rules, Revision, true);
    private static IdentityRule Rule(string id = "A", string pattern = @"(?<model>EPB)-(?<serial>\d{5})") =>
        new(id, pattern, ImmutableArray.Create("model", "serial"), RulePublication.Published);
    private static ProductionRecipe Recipe() => new("Recipe-EPB", 1, "EPB", null,
        ImmutableArray.Create(new AssetRef(AssetId.New(), AssetRevision.Create(1), Revision)), false, true);

    [Fact] // E1-T02: focused capture only; no global keyboard stealing.
    public void E1_T02_UnfocusedKeyboardInputIsRejected()
    {
        var router = new BarcodeInputRouter();
        router.AttachMode(ScannerCaptureMode.KeyboardWedge, new InputPolicy(true, 32, "keyboard"));
        var capture = new KeyboardWedgeCapture(router);
        Assert.Equal("SCAN_FOCUS_DENIED", capture.OnKeyInput('X', false)!.Code);
        Assert.Equal("SCAN_EMPTY", capture.OnKeyInput('\r', true)!.Code);
        Assert.Equal("SCAN_FOCUS_DENIED", router.PublishRaw(Scan("EPB-00001", false),
            ScannerCaptureMode.KeyboardWedge).Code);
    }

    [Fact] // E1-T03: empty input, truncated scan and no whitespace identity.
    public void E1_T03_EmptyOrShortCodeNeverMatches()
    {
        var router = new BarcodeInputRouter();
        router.AttachMode(ScannerCaptureMode.KeyboardWedge, new InputPolicy(true, 12, "keyboard"));
        Assert.Equal("SCAN_EMPTY", router.PublishRaw(Scan(""), ScannerCaptureMode.KeyboardWedge).Code);
        Assert.Equal("IDENTITY_NO_MATCH", Rules(Rule()).Match(Scan("EPB-1")).Code);
    }

    [Fact] // E1-T04: duplicate scans yield same identity, never count as distinct ECU twice.
    public void E1_T04_RepeatedScansKeepTheSameIdentityKey()
    {
        var rules = Rules(Rule());
        var a = rules.Match(Scan("EPB-00001"));
        var b = rules.Match(Scan("EPB-00001"));
        Assert.Equal(a.Identity!.IdentityKey, b.Identity!.IdentityKey);
        Assert.Contains("00001", a.Identity.IdentityKey);
    }

    [Fact] // E1-T05: two successful parsing rules must reject, not first-match.
    public void E1_T05_OverlappingRulesAreAmbiguous()
    {
        var rules = Rules(Rule("A"), Rule("B"));
        var result = rules.Match(Scan("EPB-00001"));
        Assert.Equal("IDENTITY_AMBIGUOUS", result.Code);
        Assert.Null(result.Identity);
        Assert.Equal(2, result.MatchedRuleIds.Length);
    }

    [Fact]
    public void E1_RuleRevisionMismatchRejectsMatching()
    {
        var resolver = new ConfigMatchService(Rules(Rule()), new ProjectRevision(1), new[] { Recipe() });
        var decision = resolver.Match(Scan("EPB-00001"),
            new RuleRevision(RevisionFingerprint.FromBytes(new byte[] { 7 })), new ProjectRevision(1));
        Assert.False(decision.Allowed);
        Assert.Equal("MATCH_REVISION_STALE", decision.RejectionCode);
    }

    [Fact]
    public void E1_RecipeAmbiguityRejectsMatching()
    {
        var r = Recipe();
        var resolver = new ConfigMatchService(Rules(Rule()), new ProjectRevision(1),
            new[] { r, r with { RecipeId = "Other" } });
        var decision = resolver.Match(Scan("EPB-00001"), new RuleRevision(Revision), new ProjectRevision(1));
        Assert.Equal("CONFIG_AMBIGUOUS", decision.RejectionCode);
    }

    [Fact]
    public void E1_DraftConfirmationLocksIdentity()
    {
        var resolver = new ConfigMatchService(Rules(Rule()), new ProjectRevision(1), new[] { Recipe() });
        var drafts = new ScanDraftService();
        var d = drafts.Capture(Scan("EPB-00001"));
        drafts.Evaluate(d.DraftId, resolver, new RuleRevision(Revision), new ProjectRevision(1));
        drafts.Confirm(d.DraftId);
        Assert.Throws<InvalidOperationException>(() => drafts.Capture(Scan("EPB-00002")));
    }

    [Fact]
    public void E1_KeyboardWedgeAssemblesUntilTerminator()
    {
        var router = new BarcodeInputRouter();
        router.AttachMode(ScannerCaptureMode.KeyboardWedge, new InputPolicy(true, 32, "keyboard"));
        var capture = new KeyboardWedgeCapture(router);
        foreach (var c in "EPB-00001") Assert.Null(capture.OnKeyInput(c, true));
        var result = capture.OnKeyInput('\r', true);
        Assert.True(result!.Accepted);
        Assert.Equal("EPB-00001", result.Event!.Scan.Text);
        Assert.Null(capture.OnKeyInput('\n', true)); // CRLF terminator is one scan.
    }
}
