using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using AutoTest.Contracts;

namespace AutoTest.TestFixtures;

public sealed class ReplayCanStream
{
    private readonly ImmutableArray<CanObservation> _events;
    public ReplayCanStream(IEnumerable<CanObservation> events) => _events = events.ToImmutableArray();
    public async IAsyncEnumerable<CanObservation> PlayAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var observation in _events)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return observation;
        }
    }
}
