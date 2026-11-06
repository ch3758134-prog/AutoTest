using AutoTest.Contracts;

namespace AutoTest.Specialists.Uds;

public static class UdsEndpointProfileValidator
{
    public static void ValidateIds(UdsEndpointProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.RequestCanId > 0x1FFFFFFF || profile.ResponseCanId > 0x1FFFFFFF
            || profile.RequestCanId == profile.ResponseCanId)
            throw new ArgumentException("Request and response CAN IDs must be distinct valid IDs.");
        if (profile.Role != BusRole.DutCan) throw new ArgumentException("UDS DUT endpoint must use DutCan.");
        if (profile.P2 <= TimeSpan.Zero || profile.P2Star < profile.P2)
            throw new ArgumentException("Require P2 > 0 and P2* >= P2.");
    }
    public static void ValidateAddressing(UdsEndpointProfile profile)
    {
        ValidateIds(profile);
        if (!string.Equals(profile.Addressing, "Normal", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("This release supports only ISO-TP Normal addressing.");
        if ((profile.RequestCanId > 0x7FF) != (profile.ResponseCanId > 0x7FF))
            throw new NotSupportedException("Mixed 11-bit/29-bit ID formats are unsupported.");
    }
}
