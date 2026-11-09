using AutoTest.Application.Composition;
using AutoTest.Contracts;
using AutoTest.Core;
using Xunit;

namespace AutoTest.Integration.Tests;

public sealed class MockRunSmokeTests
{
    [Fact]
    public async Task V0_Smoke_RequestAckObservationAndInconclusiveJudgementAreDistinct()
    {
        var mock = ApplicationComposition.RegisterMock();
        var result = await mock.Gateway.ExecuteAsync(
            new ApplicationCommand(ApplicationCommandKind.MockNormal, Guid.NewGuid()),
            CancellationToken.None);
        Assert.Equal(Outcome.Accepted, result.Outcome);
        var view = result.Projection;
        Assert.Equal("SIMULATOR_ACK_ONLY", view.AckStatus);
        Assert.Equal("SIMULATED_INDICATOR_READY", view.ObservationStatus);
        Assert.Equal(Verdict.Inconclusive, view.Verdict);
        Assert.Equal(PhysicalState.Unknown, view.Station.PhysicalState);
        Assert.False(view.PhysicalOutputsEnabled);
        Assert.Equal("SIMULATION_ONLY", (await mock.Station.ConnectAsync(
            StationId.Create("V0-SIM-STATION"), CancellationToken.None)).Value!.Transport);
        Assert.Contains(view.Facts, fact => fact.Kind == RunFactKind.CommandAck &&
            fact.Quality == EvidenceQuality.AcknowledgedOnly);
        Assert.Contains(view.Facts, fact => fact.Kind == RunFactKind.MockObservation &&
            fact.Quality == EvidenceQuality.Derived);
        Assert.Contains(view.Facts, fact => fact.Kind == RunFactKind.Judgement &&
            fact.Description.StartsWith("INCONCLUSIVE"));
        var commit = await mock.Facts.CommitAsync(view.Run.Run!.Value, CancellationToken.None);
        Assert.False(commit.Durable);
    }

    [Theory]
    [InlineData(ApplicationCommandKind.MockTimeout, "MOCK_TIMEOUT", Outcome.Uncertain)]
    [InlineData(ApplicationCommandKind.MockDisconnect, "MOCK_DISCONNECTED", Outcome.Uncertain)]
    [InlineData(ApplicationCommandKind.MockUnauthorized, "AUTH_MISSING", Outcome.Rejected)]
    public async Task V0_NegativePathsHaveNoFalseAckOrObservation(
        ApplicationCommandKind scenario, string expectedCode, Outcome expected)
    {
        var mock = ApplicationComposition.RegisterMock();
        var result = await mock.Gateway.ExecuteAsync(new ApplicationCommand(scenario,
            Guid.NewGuid()), CancellationToken.None);
        Assert.Equal(expected, result.Outcome);
        Assert.Equal(expectedCode, result.FailureCode);
        Assert.Equal("NO_ACK", result.Projection.AckStatus);
        Assert.Equal("NO_OBSERVATION", result.Projection.ObservationStatus);
        Assert.Equal(Verdict.Inconclusive, result.Projection.Verdict);
        Assert.DoesNotContain(result.Projection.Facts, f =>
            f.Kind == RunFactKind.CommandAck || f.Kind == RunFactKind.MockObservation);
    }

    [Fact]
    public async Task V0_PhysicalCommandIsRejectedAtApplicationBoundary()
    {
        var mock = ApplicationComposition.RegisterMock();
        var result = await mock.Gateway.ExecuteAsync(
            new ApplicationCommand(ApplicationCommandKind.PhysicalOutput, Guid.NewGuid()),
            CancellationToken.None);
        Assert.Equal(Outcome.Rejected, result.Outcome);
        Assert.Equal("V0_SCOPE_DENIED", result.FailureCode);
        Assert.Equal(0, mock.Station.CurrentGeneration);
    }

    [Fact]
    public void V0_RealCompositionCannotBeStarted()
    {
        Assert.Throws<NotSupportedException>(() => ApplicationComposition.RegisterReal());
    }
}
