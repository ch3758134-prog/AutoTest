using System.Collections.Immutable;
using AutoTest.Contracts;

namespace AutoTest.Specialists.Uds;

/// <summary>
/// Correlates a read-only ECU response with the selected request. This is
/// deliberately independent of transport ACK and refuses invalid DID/subfunction
/// echoes; a matching response SID alone is not enough evidence of a valid read.
/// </summary>
public static class UdsReadOnlyResponseValidator
{
    public static void Validate(ReadOnlySpan<byte> request, ReadOnlySpan<byte> response)
    {
        if (request.Length < 2 || response.Length < 2)
            throw new IsoTpProtocolException("Truncated UDS request/response.");
        if (response[0] != (byte)(request[0] + 0x40))
            throw new IsoTpProtocolException("UDS response SID does not match request.");
        switch (request[0])
        {
            case 0x22:
                if (request.Length < 3 || (request.Length - 1) % 2 != 0 ||
                    response.Length < 3 || request[1] != response[1] || request[2] != response[2])
                    throw new IsoTpProtocolException("ReadDID response DID echo missing or mismatched.");
                break;
            case 0x19:
                if (request[1] != response[1])
                    throw new IsoTpProtocolException("DTC response subfunction mismatch.");
                if (request[1] == 0x02 && (response.Length < 3 ||
                    (response.Length - 3) % 4 != 0))
                    throw new IsoTpProtocolException("DTC ReportByStatusMask response record length invalid.");
                break;
        }
    }
}
