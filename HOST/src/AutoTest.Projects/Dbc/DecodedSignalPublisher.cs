using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;
namespace AutoTest.Projects.Dbc;
public sealed class DecodedSignalPublisher(AssetEntryWithDbc dbc)
{
    public SignalObservation ObserveSignal(SignalRef signal, CanObservation frame)
    {
        var valid = frame.Frame.Role == signal.Role && frame.Frame.Id == signal.FrameId &&
            frame.Frame.Dlc == frame.Frame.Data.Length &&
            frame.EvidenceQuality != EvidenceQuality.Unknown && frame.LostFrames == 0;
        double? value = null;
        if (valid)
        {
            try
            {
                var message = dbc.Database.FindMessage(signal.FrameId);
                var definition = DbcSignalResolver.Resolve(signal, dbc);
                if (message.IsExtended != frame.Frame.ExtendedId ||
                    message.ArbitrationId != frame.Frame.Id || message.Dlc != frame.Frame.Dlc ||
                    !string.IsNullOrWhiteSpace(definition.Multiplexer))
                    valid = false;
                else
                {
                    value = DbcSignalCodec.DecodePhysical(definition, frame.Frame.Data.AsSpan());
                    if (!double.IsFinite(value.Value)) { value = null; valid = false; }
                }
            }
            catch (Exception e) when (e is AutoTest.Projects.Project.ProjectDataException or ArgumentException) { valid = false; }
        }
        return new SignalObservation(signal, value, signal.Unit, frame.Time,
            valid ? EvidenceQuality.Derived : EvidenceQuality.Unknown, frame.Evidence, !valid);
    }
}
