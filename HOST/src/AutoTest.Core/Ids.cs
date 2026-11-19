using System.Diagnostics.CodeAnalysis;

namespace AutoTest.Core;

// Distinct value types are the cross-module identity boundary (G3-0-T01).

public readonly record struct StationId
{
    public string Value { get; }
    private StationId(string value) => Value = value;
    public static StationId Create(string value) => !string.IsNullOrWhiteSpace(value)
        ? new(value.Trim()) : throw new ArgumentException("Nonempty StationId required.", nameof(value));
    public static bool TryParse(string? text, out StationId result)
    {
        if (string.IsNullOrWhiteSpace(text)) { result = default; return false; }
        result = new(text.Trim()); return true;
    }
    public override string ToString() => Value ?? "<invalid>";
}

public readonly record struct EndpointId
{
    public string Value { get; }
    private EndpointId(string value) => Value = value;
    public static EndpointId Create(string value) => !string.IsNullOrWhiteSpace(value)
        ? new(value.Trim()) : throw new ArgumentException("Nonempty EndpointId required.", nameof(value));
    public static bool TryParse(string? text, out EndpointId result)
    {
        if (string.IsNullOrWhiteSpace(text)) { result = default; return false; }
        result = new(text.Trim()); return true;
    }
    public override string ToString() => Value ?? "<invalid>";
}

public readonly record struct ProjectId
{
    public Guid Value { get; }
    private ProjectId(Guid value) => Value = value;
    public static ProjectId New() => new(Guid.NewGuid());
    public static ProjectId Create(Guid value) => value != Guid.Empty
        ? new(value) : throw new ArgumentException("Nonempty ProjectId required.", nameof(value));
    public static bool TryParse(string? text, out ProjectId result)
    {
        if (!Guid.TryParse(text, out var value) || value == Guid.Empty) { result = default; return false; }
        result = new(value); return true;
    }
    public override string ToString() => Value.ToString("D");
}

public readonly record struct AssetId
{
    public Guid Value { get; }
    private AssetId(Guid value) => Value = value;
    public static AssetId New() => new(Guid.NewGuid());
    public static AssetId Create(Guid value) => value != Guid.Empty
        ? new(value) : throw new ArgumentException("Nonempty AssetId required.", nameof(value));
    public static bool TryParse(string? text, out AssetId result)
    {
        if (!Guid.TryParse(text, out var value) || value == Guid.Empty) { result = default; return false; }
        result = new(value); return true;
    }
    public override string ToString() => Value.ToString("D");
}

public readonly record struct TestDocumentId
{
    public Guid Value { get; }
    private TestDocumentId(Guid value) => Value = value;
    public static TestDocumentId New() => new(Guid.NewGuid());
    public static TestDocumentId Create(Guid value) => value != Guid.Empty
        ? new(value) : throw new ArgumentException("Nonempty TestDocumentId required.", nameof(value));
    public static bool TryParse(string? text, out TestDocumentId result)
    {
        if (!Guid.TryParse(text, out var value) || value == Guid.Empty) { result = default; return false; }
        result = new(value); return true;
    }
    public override string ToString() => Value.ToString("D");
}

public readonly record struct StepId
{
    public Guid Value { get; }
    private StepId(Guid value) => Value = value;
    public static StepId New() => new(Guid.NewGuid());
    public static StepId Create(Guid value) => value != Guid.Empty
        ? new(value) : throw new ArgumentException("Nonempty StepId required.", nameof(value));
    public static bool TryParse(string? text, out StepId result)
    {
        if (!Guid.TryParse(text, out var value) || value == Guid.Empty) { result = default; return false; }
        result = new(value); return true;
    }
    public override string ToString() => Value.ToString("D");
}

public readonly record struct RunId
{
    public Guid Value { get; }
    private RunId(Guid value) => Value = value;
    public static RunId New() => new(Guid.NewGuid());
    public static RunId Create(Guid value) => value != Guid.Empty
        ? new(value) : throw new ArgumentException("Nonempty RunId required.", nameof(value));
    public static bool TryParse(string? text, out RunId result)
    {
        if (!Guid.TryParse(text, out var value) || value == Guid.Empty) { result = default; return false; }
        result = new(value); return true;
    }
    public override string ToString() => Value.ToString("D");
}
