using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;

namespace AutoTest.Specialists.Flash;

public sealed record FlashAddressRange(uint Start, ulong EndExclusive)
{
    public bool Contains(FirmwareSegment segment) => segment.Address >= Start && segment.EndExclusive <= EndExclusive;
}

// OEM process details are explicit configuration. This R4 profile is mock-only:
// no OEM erase routine, seed/key, verification routine or real flash map is implied.
public sealed record ProgrammingProfile(string Target, string ExpectedDeviceIdentity,
    UdsEndpointProfile Endpoint, ImmutableArray<FlashAddressRange> AllowedRanges,
    int MaxTransferDataBytes, double MinVoltage, double MaxVoltage,
    RevisionFingerprint Revision, bool MockOnly = true)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Target) || string.IsNullOrWhiteSpace(ExpectedDeviceIdentity))
            throw new ArgumentException("Target and expected ECU identity required.");
        AutoTest.Specialists.Uds.UdsEndpointProfileValidator.ValidateAddressing(Endpoint);
        if (MaxTransferDataBytes is < 1 or > 2048) throw new ArgumentOutOfRangeException(nameof(MaxTransferDataBytes));
        if (!double.IsFinite(MinVoltage) || !double.IsFinite(MaxVoltage) ||
            MinVoltage <= 0 || MaxVoltage <= MinVoltage)
            throw new ArgumentException("Invalid nominal voltage window.");
        if (AllowedRanges.IsDefaultOrEmpty) throw new ArgumentException("No permitted memory region.");
        ulong priorEnd = 0;
        foreach (var range in AllowedRanges.OrderBy(r=>r.Start))
        {
            if (range.EndExclusive <= range.Start || range.EndExclusive > 0x100000000UL
                || range.Start < priorEnd) throw new ArgumentException("Invalid/overlapping memory regions.");
            priorEnd = range.EndExclusive;
        }
        if (string.IsNullOrEmpty(Revision.Sha256)) throw new ArgumentException("Profile revision required.");
        if (!MockOnly) throw new NotSupportedException("Physical programming profile not authorized in G4.");
    }
    public string DescribeTarget() => $"{Target} ({ExpectedDeviceIdentity}), MockOnly={MockOnly}";
    public void ValidateImage(FirmwareImage image)
    {
        FirmwareImageParser.ValidateSegments(image.Segments);
        foreach (var segment in image.Segments)
            if (!AllowedRanges.Any(r=>r.Contains(segment)))
                throw new ArgumentException($"Firmware segment 0x{segment.Address:X8} is outside target map.");
    }
}
