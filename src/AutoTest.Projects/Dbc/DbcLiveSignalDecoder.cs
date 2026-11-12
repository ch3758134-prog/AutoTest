using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Projects.Resources;

namespace AutoTest.Projects.Dbc;

/// <summary>
/// Decode one actual CAN observation using the revision-locked DBC asset.
/// Frame format, length and missing frames are checked before physical conversion.
/// </summary>
public sealed class DbcLiveSignalDecoder
{
    private readonly AssetEntryWithDbc _source;
    public DbcLiveSignalDecoder(AssetEntry asset, DbcDatabase database) =>
        _source = new AssetEntryWithDbc(asset.Validate(), database.Validate());

    public SignalObservation Decode(SignalRef reference, CanObservation frame)
    {
        var invalid = new SignalObservation(reference, null, reference.Unit, frame.Time,
            EvidenceQuality.Unknown, frame.Evidence, true);
        if (frame.Frame.Role != reference.Role || frame.Frame.Id != reference.FrameId ||
            frame.LostFrames != 0 || frame.EvidenceQuality == EvidenceQuality.Unknown)
            return invalid;
        try
        {
            var message = _source.Database.FindMessage(reference.FrameId);
            if (message.IsExtended != frame.Frame.ExtendedId ||
                message.ArbitrationId != frame.Frame.Id || message.Dlc != frame.Frame.Dlc ||
                message.Dlc != frame.Frame.Data.Length)
                return invalid;
            var signal = DbcSignalResolver.Resolve(reference, _source);
            if (!string.IsNullOrWhiteSpace(signal.Multiplexer))
            {
                // Never interpret a multiplexed branch without a selector evaluation.
                return invalid;
            }
            var value = DbcSignalCodec.DecodePhysical(signal, frame.Frame.Data.AsSpan());
            if (!double.IsFinite(value)) return invalid;
            return new SignalObservation(reference, value, signal.Unit, frame.Time,
                EvidenceQuality.Derived, frame.Evidence, false);
        }
        catch (Exception ex) when (ex is AutoTest.Projects.Project.ProjectDataException or ArgumentException)
        {
            return invalid;
        }
    }
}
