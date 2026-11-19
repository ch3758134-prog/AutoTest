using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Projects.Project;
namespace AutoTest.Projects.Dbc;
public static class DbcSignalResolver
{
    public static DbcSignal Resolve(SignalRef reference, AssetEntryWithDbc source)
    {
        if (source.Asset.Id != reference.DbcAsset.Id || source.Asset.Revision != reference.DbcAsset.Revision.Value ||
            !string.Equals(source.Asset.Sha256, reference.DbcAsset.Fingerprint.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new ProjectDataException("REFERENCE_STALE", "DBC asset identity, revision or hash mismatch.");
        var signal = source.Database.FindSignal(reference.FrameId, reference.SignalName);
        if (!string.Equals(signal.Unit, reference.Unit, StringComparison.Ordinal))
            throw new ProjectDataException("REFERENCE_STALE", "DBC signal unit changed.");
        return signal;
    }
    public static bool DetectStale(SignalRef reference, AssetEntryWithDbc source)
    {
        try { Resolve(reference, source); return false; } catch (ProjectDataException) { return true; }
    }
}
public sealed record AssetEntryWithDbc(AutoTest.Projects.Resources.AssetEntry Asset, DbcDatabase Database);
