using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;
namespace AutoTest.Projects.Dbc;
public sealed class DecodedSignalPublisher(AssetEntryWithDbc dbc)
{
    public SignalObservation ObserveSignal(SignalRef signal, CanObservation frame)
    {
        var valid = frame.Frame.Role == signal.Role && frame.Frame.Id == signal.FrameId &&
            frame.Frame.Dlc <= frame.Frame.Data.Length && frame.EvidenceQuality != EvidenceQuality.Unknown &&
            frame.LostFrames == 0;
        double? value = null;
        if (valid)
        {
            try
            {
                var definition = DbcSignalResolver.Resolve(signal, dbc);
                value = DbcSignalCodec.DecodePhysical(definition, frame.Frame.Data.AsSpan().Slice(0, frame.Frame.Dlc));
            }
            catch (Exception e) when (e is AutoTest.Projects.Project.ProjectDataException or ArgumentException) { valid = false; }
        }
        return new SignalObservation(signal, value, signal.Unit, frame.Time,
            valid ? EvidenceQuality.Derived : EvidenceQuality.Unknown, frame.Evidence, !valid);
    }
}
