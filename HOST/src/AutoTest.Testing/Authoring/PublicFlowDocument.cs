using System.Collections.Immutable;
using AutoTest.Core;

namespace AutoTest.Testing.Authoring;

/// <summary>A version-pinned reference to another public flow; never resolves by display name.</summary>
public sealed record PublicFlowReference(Guid AssetId, int Revision, string Sha256)
{
    public void Validate()
    {
        if (AssetId == Guid.Empty || Revision < 1 || Sha256.Length != 64 ||
            !Sha256.All(Uri.IsHexDigit))
            throw new FormatException("公共流程引用缺少有效的资源 ID、修订或 SHA256。");
    }
}

/// <summary>Typed boundary for future input/output bindings; no implicit global variables.</summary>
public sealed record PublicFlowParameter(string Name, string Type, bool Required, string? DefaultValue = null);

/// <summary>
/// A public flow is a single continuous StepNode tree, NOT a three-phase TEST
/// document or a test-project package. It has no standalone production verdict.
/// </summary>
public sealed record PublicFlowDocument(Guid FlowId, string Name, int FormatVersion,
    ImmutableArray<StepNode> Body)
{
    public const int CurrentFormat = 1;
    public ImmutableArray<TestTargetMapping> Mappings { get; init; } =
        ImmutableArray<TestTargetMapping>.Empty;
    public ImmutableArray<PublicFlowReference> References { get; init; } =
        ImmutableArray<PublicFlowReference>.Empty;
    public ImmutableArray<PublicFlowParameter> Parameters { get; init; } =
        ImmutableArray<PublicFlowParameter>.Empty;

    public static PublicFlowDocument Create(string name) =>
        new(Guid.NewGuid(), name, CurrentFormat, ImmutableArray<StepNode>.Empty);

    public IEnumerable<StepNode> Walk() => TestDocument.WalkNodes(Body);

    /// <summary>Copying creates a distinct flow and new IDs for every nested step.</summary>
    public PublicFlowDocument CopyAs(string name) => this with
    {
        FlowId = Guid.NewGuid(),
        Name = name,
        Body = Body.Select(node => node.CloneNewIds()).ToImmutableArray()
    };
}
