using System.Collections.Immutable;
using AutoTest.Projects.Project;

namespace AutoTest.Projects.ControllerConfig;

public sealed record IncompatibleKeyPair(uint A, uint B);
public sealed record HardwareKeyPolicy(Guid HardwareProfileId, uint SupportedMask,
    ImmutableArray<IncompatibleKeyPair> ForbiddenPairs)
{
    public HardwareKeyPolicy Validate()
    {
        if (HardwareProfileId == Guid.Empty || SupportedMask > 0xFFFFF || ForbiddenPairs.IsDefault)
            throw new ProjectDataException("CTRL_HARDWARE", "Invalid hardware key resources.");
        if (ForbiddenPairs.Any(p => p is null || p.A == 0 || p.B == 0 ||
            ((p.A | p.B) & ~SupportedMask) != 0))
            throw new ProjectDataException("CTRL_KEY_CONFLICT", "Invalid forbidden key pair.");
        return this;
    }
    public uint ValidateCombinedMask(EcuControlProfile profile, IEnumerable<StateReference> activeStates)
    {
        Validate(); profile.Validate();
        if (HardwareProfileId != profile.HardwareProfileId)
            throw new ProjectDataException("CTRL_HARDWARE_STALE", "Configured key hardware does not match profile.");
        var all = activeStates.ToArray();
        var mask = 0u;
        var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in all)
        {
            var state = reference.Resolve(profile);
            if ((state.KeyMask & ~SupportedMask) != 0)
                throw new ProjectDataException("CTRL_KEY_UNSUPPORTED", "Mask requests an unavailable key.");
            if (state.Interaction == ControlInteraction.Exclusive &&
                !groups.Add(state.ExclusiveGroup ?? ""))
                throw new ProjectDataException("CTRL_EXCLUSIVE", "Multiple states in the same exclusive group.");
            mask |= state.KeyMask;
        }
        foreach (var conflict in ForbiddenPairs)
            if ((mask & conflict.A) == conflict.A && (mask & conflict.B) == conflict.B)
                throw new ProjectDataException("CTRL_KEY_CONFLICT", "Combined mask activates incompatible keys.");
        return mask;
    }
}
