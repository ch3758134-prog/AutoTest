using AutoTest.Testing.Authoring;
using AutoTest.Testing.Preparation;
using AutoTest.Testing.Runtime;
using Xunit;

namespace AutoTest.Testing.Tests.Diagnostics;

public sealed class FormalTypedConditionsR4Tests
{
    private static StepNode N(string type, params (string key, string value)[] properties)
    {
        var node = StepNode.Create(type);
        foreach (var (key, value) in properties) node = node.WithProperty(key, value);
        return node;
    }
    private static StepNode Observe(string value, string quality = "Observed") => N("MockObserve",
        ("variable", "signal"), ("value", value), ("quality", quality));
    private static StepNode Check(string expected, string operation = "==", string valueType = "String") =>
        N("Assert", ("variable", "signal"), ("equals", expected),
            ("operator", operation), ("valueType", valueType));
    private static TestDocument Doc(params StepNode[] nodes)
    {
        var edit = new StepEditTransaction(TestDocument.Create("R4 typed-condition test"));
        foreach (var node in nodes)
            edit.Insert(TestPhase.Test, null, null, edit.Current.Get(TestPhase.Test).Length, node);
        return edit.Current;
    }
    private static async Task<MockRunReport> Run(params StepNode[] nodes)
    {
        var plan = new PlanCompiler().Prepare(Doc(nodes));
        Assert.Empty(plan.Diagnostics);
        return await new RunCoordinator().StartAsync(plan, new MockStation());
    }

    [Theory]
    [InlineData("12.50", "12.5", "==", MockStepVerdict.Pass)]
    [InlineData("12.50", "12.6", "<", MockStepVerdict.Pass)]
    [InlineData("12.50", "12.5", ">", MockStepVerdict.Fail)]
    [InlineData("-2", "-1", ">=", MockStepVerdict.Fail)]
    public async Task NumericAssertUsesInvariantDecimalAndEmitsCorrectVerdict(
        string actual, string expected, string op, MockStepVerdict expectedVerdict)
    {
        var report = await Run(Observe(actual), Check(expected, op, "Number"));
        Assert.Equal(expectedVerdict, report.Verdict);
        Assert.Equal(expectedVerdict, report.Facts.Last().Verdict);
    }

    [Theory]
    [InlineData("TRUE", "true", MockStepVerdict.Pass)]
    [InlineData("false", "true", MockStepVerdict.Fail)]
    [InlineData("enabled", "true", MockStepVerdict.Inconclusive)]
    public async Task BooleanComparisonIsStrictAndNeverTreatsGarbageAsFalse(
        string actual, string expected, MockStepVerdict expectedVerdict)
    {
        var report = await Run(Observe(actual), Check(expected, "==", "Boolean"));
        Assert.Equal(expectedVerdict, report.Verdict);
        if (expectedVerdict == MockStepVerdict.Inconclusive)
            Assert.Contains(report.Facts, f => f.Detail is not null &&
                f.Detail.Contains("MOCK_CONDITION_INVALID_BOOLEAN", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MissingOrInvalidIfInputMustNotFallThroughToElse()
    {
        var thenArm = Observe("then");
        var elseArm = Observe("else");
        var invalid = N("SetVariable", ("variable", "x"), ("value", "not-a-number"));
        var branch = N("If", ("variable", "x"), ("equals", "10"),
                ("operator", ">"), ("valueType", "Number"))
            .WithSlot("then", thenArm).WithSlot("else", elseArm);
        var report = await Run(invalid, branch);
        Assert.Equal(MockStepVerdict.Inconclusive, report.Verdict);
        Assert.DoesNotContain(report.Facts, f => f.StepId == thenArm.Id || f.StepId == elseArm.Id);
        Assert.Contains(report.Facts, f => f.StepId == branch.Id && f.Detail is not null &&
            f.Detail.Contains("MOCK_CONDITION_INVALID_NUMBER", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NumericIfFalseRunsElseWithVerifiedAssertion()
    {
        var thenArm = Observe("bad");
        var elseArm = Observe("good");
        var branch = N("If", ("variable", "speed"), ("equals", "11"),
                ("operator", ">"), ("valueType", "Number"))
            .WithSlot("then", thenArm).WithSlot("else", elseArm, Check("good"));
        var report = await Run(N("SetVariable", ("variable", "speed"), ("value", "10")), branch);
        Assert.Equal(MockStepVerdict.Pass, report.Verdict);
        Assert.DoesNotContain(report.Facts, f => f.StepId == thenArm.Id);
        Assert.Contains(report.Facts, f => f.StepId == elseArm.Id);
    }

    [Fact]
    public async Task TypedWhenMustWaitForNumericConditionAndThenRunChild()
    {
        var station = new MockStation();
        var when = N("When", ("variable", "signal"), ("equals", "11"),
            ("operator", ">="), ("valueType", "Number"), ("timeoutMs", "400"))
            .WithSlot("body", Check("11", ">=", "Number"));
        var plan = new PlanCompiler().Prepare(Doc(when));
        Assert.Empty(plan.Diagnostics);
        station.Set("signal", "10.9", MockQuality.Observed);
        var running = new RunCoordinator().StartAsync(plan, station);
        await Task.Delay(30);
        Assert.False(running.IsCompleted);
        station.Set("signal", "11.5", MockQuality.Observed);
        var report = await running;
        Assert.Equal(MockStepVerdict.Pass, report.Verdict);
        Assert.Contains(report.Facts, f => f.Type == "Assert" && f.Verdict == MockStepVerdict.Pass);
    }

    [Fact]
    public async Task InvalidNumericWhenObservationTimesOutAsInconclusive()
    {
        var station = new MockStation();
        station.Set("signal", "11,5", MockQuality.Observed);
        var when = N("When", ("variable", "signal"), ("equals", "11"),
                ("operator", ">="), ("valueType", "Number"), ("timeoutMs", "0"))
            .WithSlot("body", Observe("invalidly-executed"));
        var plan = new PlanCompiler().Prepare(Doc(when));
        Assert.Empty(plan.Diagnostics);
        var report = await new RunCoordinator().StartAsync(plan, station);
        Assert.Equal(MockStepVerdict.Inconclusive, report.Verdict);
        Assert.Single(report.Facts);
        Assert.Contains("MOCK_CONDITION_INVALID_NUMBER", report.Facts[0].Detail);
        var exported = AutoTest.Testing.Diagnostics.TestDiagnosticArchive.FromFact(
            report.Run.ToString(), "Test", report.Facts[0]);
        Assert.Equal("MOCK_CONDITION_INVALID_NUMBER", exported.ErrorCode);
    }

    [Theory]
    [InlineData("String", ">", "a", "CONDITION_OPERATOR_TYPE_MISMATCH")]
    [InlineData("Number", "==", "1,2", "CONDITION_EXPECTED_NUMBER_INVALID")]
    [InlineData("Boolean", "==", "perhaps", "CONDITION_EXPECTED_BOOLEAN_INVALID")]
    [InlineData("Number", "xor", "10", "CONDITION_OPERATOR_INVALID")]
    [InlineData("Float32", "==", "10", "CONDITION_TYPE_INVALID")]
    public void InvalidConditionConfigurationsAreRejectedBeforeRun(
        string type, string op, string expected, string code)
    {
        var check = Check(expected, op, type);
        var plan = new PlanCompiler().Prepare(Doc(check));
        Assert.Contains(plan.Diagnostics, d => d.Contains(code, StringComparison.Ordinal));
    }

    [Fact]
    public async Task LegacyStringEqualsAndTypedMetadataSurviveSaveReload()
    {
        var legacy = N("Assert", ("variable", "signal"), ("equals", "exact"));
        var typed = Check("exact", "!=", "String");
        var original = Doc(Observe("exact"), legacy);
        var encoded = TestDocumentCodec.Serialize(original);
        Assert.Equal(MockStepVerdict.Pass,
            (await new RunCoordinator().StartAsync(new PlanCompiler().Prepare(
                TestDocumentCodec.Deserialize(encoded)), new MockStation())).Verdict);
        var roundTrip = TestDocumentCodec.Deserialize(TestDocumentCodec.Serialize(Doc(typed)));
        Assert.Equal("!=", roundTrip.Walk().Single().Properties["operator"]);
        Assert.Equal("String", roundTrip.Walk().Single().Properties["valueType"]);
    }
}
