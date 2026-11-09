using System.Globalization;
using AutoTest.Devices.Diagnostics;
using AutoTest.Devices.Usb;

// A genuinely independent read-only hardware communication entrance. Windows terminal only.
// Firmware and user output wiring remain untouched. No on-connect automatic commands.
if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("This tool only runs on a Windows PC with the WinUSB device interface installed.");
    return 2;
}

Console.WriteLine("AutoTest Hardware Communications · B0/B1/B2 · READ-ONLY");
Console.WriteLine("DANGER: Firmware initializes IGN/ECU_PWR/KEY_PWR/relay HIGH. Isolate DUT and actuators first.");
Console.WriteLine("Commands: devices | connect INDEX | ping | version | send CMD | status | reconnect |");
Console.WriteLine("          disconnect | trace | help | quit");
Console.WriteLine("Allowed CMD: F0 F1 11 13 15 17 1F (hex, empty payload only). CAN TX always disabled here.");

await using var bench = new HardwareBenchController();
var outputLock = new object();
void WriteLine(string s) { lock (outputLock) Console.WriteLine(s); }
bench.UsbTrace += evt =>
{
    string raw = evt.RawHex.Length > 280 ? evt.RawHex[..280] + "…" : evt.RawHex;
    WriteLine($"[{evt.Utc:HH:mm:ss.fff}Z] USB {evt.Kind} g={evt.Generation} " +
        $"SEQ={evt.Sequence?.ToString() ?? "-"} CMD={(evt.Command.HasValue ? evt.Command.Value.ToString("X2") : "-")} " +
        $"STS={(evt.Status.HasValue ? evt.Status.Value.ToString("X2") : "-")} " +
        $"RAW={raw} {evt.Detail}");
};
bench.CanTrace += evt =>
    WriteLine($"[{evt.HostUtc:HH:mm:ss.fff}Z] CAN-RX PORT={evt.PhysicalPort} ROLE={evt.Role} " +
        $"VALID={evt.Valid} RAW={evt.RawUsbPayloadHex}");
bench.ConnectionChanged += state => WriteLine($"[USB state] {state}");

static string NewLogPath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
    "AutoTest", "HardwareTrace", $"hardware-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fffffff}-{Guid.NewGuid():N}.jsonl");
static string Hex(byte c) => c.ToString("X2", CultureInfo.InvariantCulture);

while (true)
{
    Console.Write("hw> ");
    string? commandLine = Console.ReadLine();
    if (commandLine is null) break;
    var parts = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (parts.Length == 0) continue;
    try
    {
        string command = parts[0].ToLowerInvariant();
        if (command is "quit" or "exit") break;
        if (command is "devices" or "list")
        {
            var devices = bench.Enumerate();
            if (devices.Count == 0) WriteLine("No eligible VID_1183&PID_5850 controller interface; check driver/USB cable.");
            for (int i = 0; i < devices.Count; i++)
                WriteLine($"[{i}] {devices[i].DeviceInstanceId}\n    {devices[i].DevicePath}");
        }
        else if (command == "connect" && parts.Length == 2)
        {
            if (!int.TryParse(parts[1], out int index)) throw new ArgumentException("Use: connect INDEX");
            var devices = bench.Enumerate();
            if (index < 0 || index >= devices.Count) throw new ArgumentOutOfRangeException(nameof(index));
            Console.Write("B0 confirm: DUT, air valve and external power output harness ISOLATED. Type B0 ISOLATED: ");
            if (Console.ReadLine() != "B0 ISOLATED") throw new UnauthorizedAccessException("B0 isolation confirmation not given.");
            string path = NewLogPath();
            await bench.ConnectAsync(devices[index].DeviceInstanceId, path);
            WriteLine("Connected without sending a command. Device identity unverified until PING and GET_VERSION.");
            WriteLine("Trace: " + path);
        }
        else if (command == "reconnect" && parts.Length == 1)
        {
            Console.Write("Reconnect can reset MCU GPIO HIGH. Confirm B0 isolation again; type B0 ISOLATED: ");
            if (Console.ReadLine() != "B0 ISOLATED") throw new UnauthorizedAccessException("B0 isolation confirmation not given.");
            await bench.ReconnectAsync(NewLogPath());
            WriteLine("Reconnected to same Windows device instance. No automatic output commands sent.");
        }
        else if (command == "disconnect" && parts.Length == 1)
        {
            await bench.DisconnectAsync();
            WriteLine("Disconnected. The MCU may keep driving its physical outputs: disconnect is not power-off.");
        }
        else if (command == "ping" || command == "version" || command == "send")
        {
            byte cmd;
            if (command == "ping" && parts.Length == 1) cmd = 0xF0;
            else if (command == "version" && parts.Length == 1) cmd = 0xF1;
            else if (command == "send" && parts.Length == 2 &&
                     byte.TryParse(parts[1].Replace("0x", "", StringComparison.OrdinalIgnoreCase),
                         NumberStyles.HexNumber, CultureInfo.InvariantCulture, out cmd)) { }
            else throw new ArgumentException("Use ping | version | send F0/F1/11/13/15/17/1F");
            if (!PhysicalCommandFence.IsReadOnly(cmd, ReadOnlySpan<byte>.Empty))
                throw new UnauthorizedAccessException("Physical output command blocked in B0/B1/B2.");
            var response = await bench.ReadOnlyCommandAsync(cmd, TimeSpan.FromSeconds(2));
            WriteLine($"PASS protocol request CMD=0x{Hex(cmd)} SEQ matched, STS=0x{Hex(response.Status)} " +
                $"payload={Convert.ToHexString(response.Payload)}");
            WriteLine("Decoded: " + FirmwareReadOnlyProtocol.Explain(cmd, response.Payload));
            if (cmd == 0x1F && response.Payload.Length == 1)
                WriteLine($"GET_ALL_PWR MCU pin readback: 0x{Hex(response.Payload[0])} " +
                    "[bit0 ECU_PWR, bit1 IGN, bit2 KEY_PWR]; not a measured voltage.");
            if (cmd == 0x13 && response.Payload.Length == 1)
                WriteLine($"GET_IGN MCU pin readback: {response.Payload[0]} (1=HIGH); not an electrical measurement.");
        }
        else if (command == "status")
        {
            WriteLine($"State={bench.State} Instance={bench.SelectedInstanceId ?? "-"} Path={bench.DevicePath ?? "-"} " +
                $"Generation={bench.Generation} ProtocolVersionVerified={bench.ProtocolVersionVerified}");
            WriteLine($"CAN published={bench.CanPublished} ingressDropped={bench.CanDropped} " +
                $"malformed={bench.CanMalformed} hubDropped={bench.HubDropped}; 0 frames is NOT a CAN failure.");
            WriteLine($"Trace={bench.TraceFile ?? "-"} logDropped={bench.TraceDropped} " +
                $"traceError={bench.TraceError ?? "-"}");
        }
        else if (command == "trace") WriteLine(bench.TraceFile ?? "No active trace; connect first.");
        else if (command == "help")
            WriteLine("devices -> connect INDEX -> ping -> version -> send 13 -> send 1F -> status -> disconnect. " +
                "CAN RX 0x41 appears automatically (passive only). STOP on firmware mismatch, unexpected voltage or loss.");
        else WriteLine("Unknown command; type help. No arbitrary USB payloads are accepted.");
    }
    catch (Exception error)
    {
        WriteLine($"ERROR: {error.GetType().Name}: {error.Message}");
        WriteLine("No automatic retry or physical state change. Inspect wiring/driver/trace first.");
    }
}
await bench.DisconnectAsync();
return 0;
