using System.Text.Json;
using AutoTest.Contracts;

namespace AutoTest.Specialists.Uds.Workbench;

// A document is a diagnostic resource, not a DBC resource. This is a local
// import/export boundary; PROJECT/INTEGRATION may store the same JSON as an asset.
public sealed record UdsToolProfile(string Name, string EcuIdentity, uint TxId, uint RxId,
    int P2Ms, int P2StarMs, string Addressing = "Normal");
public sealed record UdsServiceDefinition(string Name, string RequestHex, string ExpectedHex);
public sealed record UdsToolDocument(int Schema, UdsToolProfile Profile,
    List<UdsServiceDefinition> Services);
public sealed record UdsCanTraceRow(string Direction, string Hex);
public sealed record UdsToolExchange(string Source, string RequestHex, string ResponseHex,
    string Outcome, string Nrc, bool? ExpectedMatched, Guid CorrelationId,
    IReadOnlyList<UdsCanTraceRow> Frames, string ErrorCode);

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
            string.IsNullOrWhiteSpace(document.Profile.EcuIdentity))
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

