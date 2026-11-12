using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoTest.Devices.Diagnostics;
using AutoTest.Devices.Can;
using AutoTest.Devices.Usb;
using Xunit;

namespace AutoTest.Devices.Tests;

public sealed class DeviceEvidenceArchiveWriterTests
{
    private static DeviceEvidenceContext Context(bool disconnected = true, long lost = 0) => new(
        "0c63b660896b1d287dfc3cfe24efa7b728e47767", "USB\\VID_1183&PID_5850\\BOARD",
        "WinUSB", 7, lost, 0, 0, 0, 9, null, null, disconnected);

    [Fact]
    public async Task Archive_preserves_both_original_jsonl_byte_for_byte_and_hashes()
    {
        string dir = NewDir();
        try
        {
            string transport = Path.Combine(dir, "t.jsonl"), audit = Path.Combine(dir, "a.jsonl");
            string output = Path.Combine(dir, "result.zip");
            byte[] raw = Encoding.UTF8.GetBytes("{\"RawHex\":\"A500\",\"Generation\":7}\n");
            byte[] ops = Encoding.UTF8.GetBytes("{\"Action\":\"CONNECT\",\"Outcome\":\"OK\"}\n");
            await File.WriteAllBytesAsync(transport, raw);
            await File.WriteAllBytesAsync(audit, ops);
            var result = await DeviceEvidenceArchiveWriter.ExportAsync(transport, audit, output, Context());
            using var zip = ZipFile.OpenRead(output);
            Assert.Equal(raw, ReadEntry(zip, "raw/transport.jsonl"));
            Assert.Equal(ops, ReadEntry(zip, "raw/operations.jsonl"));
            using var manifest = JsonDocument.Parse(ReadEntry(zip, "manifest.json"));
            var root = manifest.RootElement;
            Assert.False(root.GetProperty("PhysicalTestPassed").GetBoolean());
            Assert.Equal("PHYSICAL_READONLY_OBSERVATION_UNVERIFIED", root.GetProperty("Source").GetString());
            Assert.Equal(7, root.GetProperty("SessionGeneration").GetInt64());
            Assert.Equal(Convert.ToHexString(SHA256.HashData(raw)), result.Files[0].Sha256);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(ops)), result.Files[1].Sha256);
            Assert.Equal(64, result.Sha256.Length);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Rejects_active_physical_connection_before_reading_any_source()
    {
        string dir = NewDir();
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                DeviceEvidenceArchiveWriter.ExportAsync("missing", "missing", Path.Combine(dir, "out.zip"), Context(false)));
            Assert.Empty(Directory.GetFiles(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Missing_source_and_existing_destination_never_get_overwritten()
    {
        string dir = NewDir();
        try
        {
            var tx = Path.Combine(dir, "tx.jsonl"); var audit = Path.Combine(dir, "audit.jsonl");
            var zip = Path.Combine(dir, "evidence.zip");
            await File.WriteAllTextAsync(tx, "{\"k\":1}\n");
            await Assert.ThrowsAsync<FileNotFoundException>(() =>
                DeviceEvidenceArchiveWriter.ExportAsync(tx, audit, zip, Context()));
            Assert.False(File.Exists(zip));
            await File.WriteAllTextAsync(audit, "{\"k\":2}\n");
            await File.WriteAllTextAsync(zip, "protect");
            await Assert.ThrowsAsync<IOException>(() =>
                DeviceEvidenceArchiveWriter.ExportAsync(tx, audit, zip, Context()));
            Assert.Equal("protect", await File.ReadAllTextAsync(zip));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Loss_is_explicit_and_never_reported_as_physical_pass()
    {
        string dir = NewDir();
        try
        {
            var tx = Path.Combine(dir, "tx.jsonl"); var audit = Path.Combine(dir, "audit.jsonl");
            await File.WriteAllTextAsync(tx, "{}\n"); await File.WriteAllTextAsync(audit, "{}\n");
            var zipPath = Path.Combine(dir, "out.zip");
            await DeviceEvidenceArchiveWriter.ExportAsync(tx, audit, zipPath, Context(lost: 4));
            using var zip = ZipFile.OpenRead(zipPath);
            using var doc = JsonDocument.Parse(ReadEntry(zip, "manifest.json"));
            Assert.Equal("DEGRADED_OR_UNKNOWN", doc.RootElement.GetProperty("EvidenceCompleteness").GetString());
            Assert.False(doc.RootElement.GetProperty("PhysicalTestPassed").GetBoolean());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData("USB", "SEQ=4 RAW=A501", "raw=a501", true)]
    [InlineData("NATIVE", "Win32 6", "win32 6", true)]
    [InlineData("CAN", "CAN0 DUT", "PING", false)]
    [InlineData("CAN", "CAN0 DUT", "  ", true)]
    public void Filter_is_case_insensitive_and_does_not_mutate_raw_trace(
        string source, string details, string filter, bool expected)
        => Assert.Equal(expected, DeviceEvidenceTextFilter.Matches(source, details, filter));


    [Fact]
    public async Task Failed_operation_audit_blocks_connect_before_open_but_disconnect_remains_possible()
    {
        var backend = new AuditedBackend();
        bool healthy = true;
        await using var session = new DeviceDiagnosticSession(backend, () => "unused.jsonl", () => healthy);
        session.Select(backend.Candidates[0]);
        session.SetB0IsolationConfirmed(true);
        session.OperationRecorded += evt =>
        {
            if (evt.Action == "CONNECT" && evt.Outcome == "ATTEMPT") healthy = false;
        };
        var err = await Assert.ThrowsAsync<IOException>(() => session.ConnectAsync());
        Assert.True(err.Message.Contains("audit", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, backend.OpenCalls);
        await session.DisconnectAsync();
    }

    [Fact]
    public async Task Failed_operation_audit_blocks_read_without_calling_backend()
    {
        var backend = new AuditedBackend();
        bool healthy = true;
        await using var session = new DeviceDiagnosticSession(backend, () => "unused.jsonl", () => healthy);
        session.Select(backend.Candidates[0]);
        session.SetB0IsolationConfirmed(true);
        await session.ConnectAsync();
        session.OperationRecorded += evt =>
        {
            if (evt.Action == "READ_0xF0" && evt.Outcome == "ATTEMPT") healthy = false;
        };
        await Assert.ThrowsAsync<IOException>(() => session.ExecuteReadOnlyAsync(
            FirmwareReadOnlyProtocol.Ping, TimeSpan.FromMilliseconds(500)));
        Assert.Equal(0, backend.ReadCalls);
        await session.DisconnectAsync();
    }

    private sealed class AuditedBackend : IDeviceDiagnosticBackend
    {
        public readonly WinUsbCandidate[] Candidates =
            { new("mock-path", "USB\\VID_1183&PID_5850\\TEST", "WinUSB") };
        public int OpenCalls { get; private set; }
        public int ReadCalls { get; private set; }
        public UsbSessionState State { get; private set; } = UsbSessionState.Disconnected;
        public string? TraceFile => null;
        public string? TraceError => null;
        public long TraceDropped => 0;
        public long Generation => 1;
        public long CanPublished => 0;
        public long CanDropped => 0;
        public long CanMalformed => 0;
        public long HubDropped => 0;
        public event Action<UsbTraceRecord>? UsbTrace { add { } remove { } }
        public event Action<WinUsbOperationEvent>? NativeOperation { add { } remove { } }
        public event Action<HardwareCanTrace>? CanTrace { add { } remove { } }
        public event Action<UsbSessionState>? ConnectionChanged;
        public IReadOnlyList<WinUsbCandidate> Enumerate() => Candidates;
        public Task ConnectAsync(string exactInstanceId, string traceFile, CancellationToken ct = default)
        { OpenCalls++; State = UsbSessionState.Connected; ConnectionChanged?.Invoke(State); return Task.CompletedTask; }
        public Task DisconnectAsync()
        { State = UsbSessionState.Disconnected; ConnectionChanged?.Invoke(State); return Task.CompletedTask; }
        public Task<UsbResponse> ReadOnlyCommandAsync(byte command, TimeSpan timeout, CancellationToken ct = default)
        { ReadCalls++; return Task.FromResult(new UsbResponse(7, command, 0, "PONG"u8.ToArray())); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static string NewDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "autotest-device-export-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path); return path;
    }
    private static byte[] ReadEntry(ZipArchive zip, string name)
    {
        using var stream = zip.GetEntry(name)!.Open();
        using var ms = new MemoryStream(); stream.CopyTo(ms); return ms.ToArray();
    }
}
