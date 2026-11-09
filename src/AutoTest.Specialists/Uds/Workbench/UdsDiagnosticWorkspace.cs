using System.Collections.Immutable;
using System.Text.Json;
using AutoTest.Contracts;
using AutoTest.Specialists.Flash;

namespace AutoTest.Specialists.Uds.Workbench;

// A document is a diagnostic resource, not a DBC resource. This is a local
// import/export boundary; PROJECT/INTEGRATION may store the same JSON as an asset.
public sealed record UdsToolProfile(string Name, string MockIdentity, uint TxId, uint RxId,
    int P2Ms, int P2StarMs, string Addressing = "Normal");
public sealed record UdsServiceDefinition(string Name, string RequestHex, string ExpectedHex);
public sealed record UdsToolDocument(int Schema, UdsToolProfile Profile,
    List<UdsServiceDefinition> Services);
public sealed record UdsCanTraceRow(string Direction, string Hex);
public sealed record UdsToolExchange(string Source, string RequestHex, string ResponseHex,
    string Outcome, string Nrc, bool? ExpectedMatched, Guid CorrelationId,
    IReadOnlyList<UdsCanTraceRow> Frames, string ErrorCode)
{
    // Ordered UDS response PDUs, including NRC 0x78, as actually observed
    // by this Mock request. These are not timestamped CAN-bus observations.
    public IReadOnlyList<string> ResponseSequence { get; init; } = [];
    public string FailureLayer { get; init; } = "NONE";
    public string DiagnosticDetail { get; init; } = "";
}

public static class UdsHex
{
    public static byte[] Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new FormatException("UDS PDU is empty.");
        string normalized = text.Replace("0x", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" ", "").Replace("\t", "").Replace("\r", "")
            .Replace("\n", "").Replace(",", "").Replace("-", "");
        if (normalized.Length is < 2 or > 8190 || normalized.Length % 2 != 0 ||
            !normalized.All(Uri.IsHexDigit))
            throw new FormatException("UDS PDU must contain 1..4095 hexadecimal bytes.");
        return Convert.FromHexString(normalized);
    }
    public static string Format(IEnumerable<byte> bytes) => string.Join(" ", bytes.Select(b => b.ToString("X2")));
}

public static class UdsToolDocumentStore
{
    private const int MaxBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public static void Validate(UdsToolDocument document)
    {
        if (document.Schema != 1 || document.Profile is null || document.Services is null ||
            document.Services.Count > 200 || string.IsNullOrWhiteSpace(document.Profile.Name) ||
            string.IsNullOrWhiteSpace(document.Profile.MockIdentity))
            throw new InvalidDataException("Invalid UDS service document schema/profile.");
        _ = ToEndpoint(document.Profile);
        var catalog = new UdsServiceCatalog();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var service in document.Services)
        {
            if (service is null || string.IsNullOrWhiteSpace(service.Name) ||
                !names.Add(service.Name)) throw new InvalidDataException("Invalid/duplicate service name.");
            var request = UdsHex.Parse(service.RequestHex);
            var descriptor = catalog.Get(request[0]);
            if (request.Length < descriptor.MinimumRequestLength)
                throw new InvalidDataException($"Request too short: {service.Name}");
            if (!string.IsNullOrWhiteSpace(service.ExpectedHex)) _ = UdsHex.Parse(service.ExpectedHex);
        }
    }
    public static UdsEndpointProfile ToEndpoint(UdsToolProfile profile)
    {
        if (profile.P2Ms is < 1 or > 60000 || profile.P2StarMs < profile.P2Ms ||
            profile.P2StarMs > 120000) throw new ArgumentOutOfRangeException(nameof(profile), "Invalid P2/P2* bounds.");
        var endpoint = new UdsEndpointProfile(BusRole.DutCan, profile.TxId, profile.RxId,
            TimeSpan.FromMilliseconds(profile.P2Ms), TimeSpan.FromMilliseconds(profile.P2StarMs),
            profile.Addressing);
        UdsEndpointProfileValidator.ValidateAddressing(endpoint);
        return endpoint;
    }
    public static async Task SaveAsync(string path, UdsToolDocument document, CancellationToken ct = default)
    {
        Validate(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        if (bytes.Length > MaxBytes) throw new InvalidDataException("UDS document exceeds 1 MiB.");
        string absolute = Path.GetFullPath(path);
        string temp = absolute + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temp, bytes, ct).ConfigureAwait(false);
            File.Move(temp, absolute, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static async Task<UdsToolDocument> LoadAsync(string path, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var file = new FileInfo(path);
        if (!file.Exists || file.Length > MaxBytes) throw new InvalidDataException("Missing/oversized UDS document.");
        var document = JsonSerializer.Deserialize<UdsToolDocument>(
            await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false), JsonOptions)
            ?? throw new InvalidDataException("Empty UDS document.");
        Validate(document);
        return document;
    }
}

// MOCK ONLY, by construction: no ICanFrameHub / physical transport can enter
// this service. Its host and simulated ECU use the production ISO-TP codec.
public sealed class UdsDiagnosticWorkspace : IAsyncDisposable
{
    private readonly SemaphoreSlim _exclusive = new(1, 1);
    private MockBootloader _ecu;
    private MockUdsMessageChannel _channel;
    private UdsClient _client;
    private bool _disposed;
    private readonly int _maxCapturedExchanges;
    private int _capturedExchanges;
    public string MockIdentity { get; private set; }
    public int CapturedExchanges => _capturedExchanges;
    public int MaxCapturedExchanges => _maxCapturedExchanges;
    public UdsDiagnosticWorkspace(string identity = "MOCK_ECU_A", int maxCapturedExchanges = 512)
    {
        if (maxCapturedExchanges is < 1 or > 10000)
            throw new ArgumentOutOfRangeException(nameof(maxCapturedExchanges));
        _maxCapturedExchanges = maxCapturedExchanges;
        MockIdentity = identity;
        _ecu = new MockBootloader(identity);
        _channel = new MockUdsMessageChannel(_ecu);
        _client = new UdsClient(_channel);
    }
    public async Task ResetMockAsync(string identity, CancellationToken ct = default)
    {
        await _exclusive.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (string.IsNullOrWhiteSpace(identity)) throw new ArgumentException("Mock ECU identity required.");
            await _channel.DisposeAsync().ConfigureAwait(false);
            _ecu = new MockBootloader(identity);
            _channel = new MockUdsMessageChannel(_ecu);
            _client = new UdsClient(_channel);
            _capturedExchanges = 0;
            MockIdentity = identity;
        }
        finally { _exclusive.Release(); }
    }
    public void InjectNextNrc(byte sid, byte nrc) => _ecu.SimulateNrc(sid, nrc);
    public void InjectNextPending(byte sid) => _ecu.SimulatePendingOnce(sid);
    public void SetNoResponse(bool enabled) => _ecu.IgnoreRequests = enabled;
    public void SimulateDisconnect() => _ecu.Disconnect();

    public async Task<UdsToolExchange> SendMockAsync(UdsToolProfile profile, string requestText,
        string expectedHex = "", CancellationToken ct = default, Guid? correlationId = null)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(UdsDiagnosticWorkspace));
        byte[] request = UdsHex.Parse(requestText);
        var endpoint = UdsToolDocumentStore.ToEndpoint(profile);
        if (!string.Equals(profile.MockIdentity, MockIdentity, StringComparison.Ordinal))
            throw new InvalidOperationException("Mock ECU identity differs. Reset Mock explicitly.");
        byte[]? expected = string.IsNullOrWhiteSpace(expectedHex) ? null : UdsHex.Parse(expectedHex);
        await _exclusive.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_capturedExchanges >= _maxCapturedExchanges)
                throw new InvalidOperationException(
                    "MOCK_CAPTURE_CAPACITY_REACHED: reset Mock ECU to release old frame history.");
            _capturedExchanges++;
            int h = _channel.HostFrames().Length;
            int e = _channel.EcuFrames().Length;
            var id = correlationId ?? Guid.NewGuid();
            if (id == Guid.Empty) throw new ArgumentException("Correlation ID must be nonempty.");
            var result = await _client.RequestAsync(new UdsRequest(request.ToImmutableArray(), id),
                endpoint, ct).ConfigureAwait(false);
            byte[] response = result.ResponsePdu.ToArray();
            var operationTrace = _client.Trace.Snapshot()
                .Where(x => x.CorrelationId == id).ToArray();
            var responseSequence = operationTrace
                .Where(x => x.Direction == UdsTraceDirection.Response)
                .Select(x => UdsHex.Format(x.Data))
                .ToArray();
            var errors = operationTrace.Where(x => x.Direction == UdsTraceDirection.Error)
                .Select(x => x.Note).ToArray();
            string diagnostic = string.Join("; ", errors);
            string failureLayer = result.Outcome switch
            {
                AutoTest.Core.Outcome.Accepted => "NONE",
                AutoTest.Core.Outcome.Rejected => result.NegativeResponseCode.HasValue
                    ? "UDS_ECU_NEGATIVE_RESPONSE" : "UDS_POLICY_OR_BUSY",
                _ when diagnostic.Contains("ISO-TP frame/flow-control timeout",
                    StringComparison.OrdinalIgnoreCase) => "ISOTP_TIMEOUT",
                _ when diagnostic.Contains("Consecutive-frame",
                    StringComparison.OrdinalIgnoreCase) => "ISOTP_SEQUENCE",
                _ => "UDS_TRANSPORT_UNISOLATED"
            };
            var trace = _channel.HostFrames().Skip(h)
                .Select(x => new UdsCanTraceRow("HOST TX", UdsHex.Format(x)))
                .Concat(_channel.EcuFrames().Skip(e)
                    .Select(x => new UdsCanTraceRow("MOCK ECU TX", UdsHex.Format(x))))
                .ToArray();
            return new("Mock", UdsHex.Format(request), UdsHex.Format(response),
                result.Outcome.ToString(), result.NegativeResponseCode is byte n ? $"0x{n:X2}" : "—",
                expected is null ? null : response.SequenceEqual(expected), id, trace,
                result.Outcome == AutoTest.Core.Outcome.Uncertain ? "NO_CONFIRMED_RESPONSE" : "")
                { ResponseSequence = responseSequence,
                  FailureLayer = failureLayer, DiagnosticDetail = diagnostic };
        }
        finally { _exclusive.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await _exclusive.WaitAsync().ConfigureAwait(false);
        try { if (!_disposed) { _disposed = true; await _channel.DisposeAsync().ConfigureAwait(false); } }
        finally { _exclusive.Release(); }
    }
}
