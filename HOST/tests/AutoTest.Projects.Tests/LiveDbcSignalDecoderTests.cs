using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Projects.Dbc;
using AutoTest.Projects.Resources;
using Xunit;

namespace AutoTest.Projects.Tests;

public sealed class LiveDbcSignalDecoderTests
{
    [Fact] public void UsesPinnedDbcAndRejectsWrongFrameFormat()
    {
        var signal = new DbcSignal("Status", 0, 8, true, false, 0.5, 1, 0, 128, "V", "ECU");
        var db = new DbcDatabase("ECU", 1, ImmutableArray<string>.Empty,
            ImmutableArray.Create(new DbcMessage(0x123, "State", 1, "ECU",
                ImmutableArray.Create(signal))));
        var asset = AssetEntry.Create("dbc", "ECU", "dbcs/ecu.dbc", [1,2,3]);
        var reference = new SignalRef(asset.ToReference(), BusRole.DutCan, 0x123, "Status", "V");
        var decoder = new DbcLiveSignalDecoder(asset, db);
        var observation = new CanObservation(CanFrame.Create(BusRole.DutCan, false, 0x123, [20]),
            TimePoint.FromHostArrival(DateTimeOffset.UtcNow, 100), EvidenceQuality.HostObserved,
            1, 0, EvidenceRef.New());
        Assert.Equal(11, decoder.Decode(reference, observation).Value);
        Assert.Null(decoder.Decode(reference,
            observation with { Frame = CanFrame.Create(BusRole.DutCan, true, 0x123, [20]) }).Value);
        Assert.True(decoder.Decode(reference, observation with { LostFrames = 1 }).HasGap);
    }
}
