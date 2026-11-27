using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Desktop.Hardware;
using Xunit;

namespace AutoTest.Desktop.Tests.Projects;

public sealed class EcuCanFixtureFeedbackTests
{
    [Fact]
    public void OnlyFreshObservedEcuCanFrameCanConfirmFunctionalFixtureReadiness()
    {
        var afterPress = DateTimeOffset.UtcNow;
        var ecu = CanFrame.Create(BusRole.DutCan, false, 0x120,
            new byte[] { 0x01, 0x02 });
        var observation = new CanObservation(ecu,
            TimePoint.FromHostArrival(afterPress.AddMilliseconds(1), 12000),
            EvidenceQuality.HostObserved, 1, 0, EvidenceRef.New());

        Assert.True(EcuCanFixtureFeedback.IsEligibleEcuFrame(observation, afterPress));
        Assert.False(EcuCanFixtureFeedback.IsEligibleEcuFrame(observation,
            afterPress.AddSeconds(1)));
        Assert.False(EcuCanFixtureFeedback.IsEligibleEcuFrame(observation with
        {
            Frame = ecu with { Role = BusRole.KeypadCan }
        }, afterPress));
        Assert.False(EcuCanFixtureFeedback.IsEligibleEcuFrame(observation with
        {
            EvidenceQuality = EvidenceQuality.Unknown
        }, afterPress));
        Assert.False(EcuCanFixtureFeedback.IsEligibleEcuFrame(observation with
        {
            Evidence = new EvidenceRef(Guid.Empty)
        }, afterPress));
    }
}
