using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;

namespace AutoTest.Testing.Authoring;

/// <summary>Names used by a TEST program; hardware IDs remain in version-pinned mappings.</summary>
public enum TestTargetKind { Signal, Button, CanMessage, Ignition, WaveProfile, RawCan, RawKey }

/// <summary>
/// A mapping belongs to the TEST document and can be rebound without rewriting
/// its step tree. It does not authorize any physical write.
/// Signal and CAN IDs are wire arbitration IDs; DBC extendedness is explicit.
/// </summary>
public sealed record TestTargetMapping(
    string Name, TestTargetKind Kind, AssetId Asset,
    int AssetRevision, string AssetSha256,
    BusRole Role = BusRole.DutCan, uint CanId = 0, bool Extended = false,
    string SignalName = "", string Unit = "",
    Guid ControllerId = default, Guid StateId = default)
{
    public TestTargetMapping Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 128 ||
            !Enum.IsDefined(Kind) || !Enum.IsDefined(Role))
            throw new ArgumentException("Invalid target mapping identity.");
        if (Kind == TestTargetKind.Ignition) return this;
        if (Kind == TestTargetKind.RawCan)
        {
            if (Role != BusRole.DutCan || CanId > (Extended ? 0x1FFFFFFFu : 0x7FFu) ||
                Asset.Value != Guid.Empty)
                throw new ArgumentException("Raw CAN target must identify a valid DUT CAN1 frame without a DBC asset.");
            return this;
        }
        if (Kind == TestTargetKind.RawKey)
        {
            if (Role != BusRole.KeypadCan || Asset.Value != Guid.Empty)
                throw new ArgumentException("Raw KEY_SIM operation must use CAN0 without an ECU profile asset.");
            return this;
        }
        if (Asset.Value == Guid.Empty || AssetRevision < 1 || AssetSha256 is not { Length: 64 } ||
            !AssetSha256.All(Uri.IsHexDigit))
            throw new ArgumentException("Target mapping must pin an existing project asset revision and SHA256.");
        if (Kind == TestTargetKind.Button &&
            (ControllerId == Guid.Empty || StateId == Guid.Empty))
            throw new ArgumentException("Mapped button must name one configured controller and state.");
        if (Kind is TestTargetKind.Signal or TestTargetKind.CanMessage)
        {
            if (CanId > (Extended ? 0x1FFFFFFFu : 0x7FFu))
                throw new ArgumentException("Mapped CAN ID exceeds frame format.");
            if (Kind == TestTargetKind.Signal && string.IsNullOrWhiteSpace(SignalName))
                throw new ArgumentException("Mapped signal name is required.");
        }
        return this;
    }
}

public static class TestTargetMappings
{
    public static ImmutableArray<string> Validate(ImmutableArray<TestTargetMapping> mappings)
    {
        var errors = ImmutableArray.CreateBuilder<string>();
        if (mappings.IsDefault) { errors.Add("Mapping list missing"); return errors.ToImmutable(); }
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in mappings)
        {
            if (m is null) { errors.Add("Null mapping"); continue; }
            if (!names.Add(m.Name)) errors.Add("Duplicate mapping " + m.Name);
            try { m.Validate(); }
            catch (ArgumentException ex) { errors.Add(m.Name + ": " + ex.Message); }
        }
        return errors.ToImmutable();
    }
}
