using AutoTest.Contracts;
using AutoTest.Projects.Resources;

namespace AutoTest.Projects.Dbc;

/// <summary>
/// Version-locked live adapter to the ONE project DBC decoding implementation.
/// CAN Monitor, TEST Read and Wave record share DecodedSignalPublisher semantics.
/// </summary>
public sealed class DbcLiveSignalDecoder
{
    private readonly DecodedSignalPublisher _decoder;
    public DbcLiveSignalDecoder(AssetEntry asset, DbcDatabase database) =>
        _decoder = new DecodedSignalPublisher(
            new AssetEntryWithDbc(asset.Validate(), database.Validate()));

    public SignalObservation Decode(SignalRef reference, CanObservation frame) =>
        _decoder.ObserveSignal(reference, frame);
}
