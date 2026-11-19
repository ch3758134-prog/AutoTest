using System.Globalization;
using AutoTest.Projects.Project;

namespace AutoTest.Projects.Dbc;

/// <summary>
/// Build a complete classic CAN payload from a pinned DBC message. No unassigned
/// signals are silently assumed to be zero. Multiplexed frames are intentionally
/// rejected until the selected mux branch can be proven and validated.
/// </summary>
public static class DbcCanMessageEncoder
{
    /// <summary>
    /// Payload input: "HEX:001122..." for exact raw bytes, or
    /// "SignalA=12.5;SignalB=-3" for a complete physical-value message.
    /// The legacy unprefixed full HEX payload is accepted for existing workflows.
    /// </summary>
    public static byte[] Encode(DbcMessage message, string specification)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(specification);
        message.Validate();
        string input = specification.Trim();
        if (input.StartsWith("HEX:", StringComparison.OrdinalIgnoreCase))
            return Raw(message, input[4..]);
        if (!input.Contains('='))
            return Raw(message, input);
        if (message.Signals.IsDefaultOrEmpty)
            throw new ProjectDataException("DBC_TX_NO_SIGNALS",
                "A physical-value message needs DBC signal definitions.");
        if (message.Signals.Any(s => !string.IsNullOrEmpty(s.Multiplexer)))
            throw new ProjectDataException("DBC_TX_MULTIPLEXED",
                "Multiplexed CAN messages require an explicitly validated branch; use a complete HEX payload instead.");
        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var item in input.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int equals = item.IndexOf('=');
            if (equals <= 0 || equals == item.Length - 1)
                throw new ProjectDataException("DBC_TX_SYNTAX", "Expected signalName=physicalValue.");
            string name = item[..equals].Trim();
            string value = item[(equals + 1)..].Trim();
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
                !double.IsFinite(number) || !values.TryAdd(name, number))
                throw new ProjectDataException("DBC_TX_VALUE", "Invalid or duplicated CAN signal assignment: " + name);
        }
        if (values.Count != message.Signals.Length ||
            message.Signals.Any(signal => !values.ContainsKey(signal.Name)))
            throw new ProjectDataException("DBC_TX_INCOMPLETE",
                "Specify exactly every non-multiplexed DBC signal before physical transmission.");
        var bytes = new byte[message.Dlc];
        foreach (var signal in message.Signals)
        {
            var value = values[signal.Name];
            if (value < signal.Minimum || value > signal.Maximum)
                throw new ProjectDataException("DBC_TX_RANGE",
                    "Physical value outside DBC limits: " + signal.Name);
            DbcSignalCodec.EncodePhysical(signal, value, bytes);
        }
        return bytes;
    }

    private static byte[] Raw(DbcMessage message, string value)
    {
        byte[] payload;
        try
        {
            payload = Convert.FromHexString(string.Concat(value.Where(c => !char.IsWhiteSpace(c))));
        }
        catch (FormatException ex)
        {
            throw new ProjectDataException("DBC_TX_HEX", "Invalid CAN HEX payload: " + ex.Message);
        }
        if (payload.Length != message.Dlc)
            throw new ProjectDataException("DBC_TX_DLC", "CAN HEX payload must match the DBC message DLC.");
        return payload;
    }
}
