using System.Collections.Immutable;
using System.Text.Json;

namespace AutoTest.Testing.Authoring;

/// <summary>
/// Distinct public-flow envelope with exactly the same StepNode and Mapping
/// encoding as TEST v1. Never serializes fake Prepare/Test/Cleanup phases.
/// </summary>
public static class PublicFlowCodec
{
    private sealed record Data(int FormatVersion, Guid FlowId, string Name,
        TestDocumentCodec.NodeData[] Body,
        TestDocumentCodec.MappingData[]? Mappings = null,
        PublicFlowReference[]? References = null,
        PublicFlowParameter[]? Parameters = null);

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public static string Serialize(PublicFlowDocument document)
    {
        Validate(document);
        return JsonSerializer.Serialize(new Data(document.FormatVersion,
            document.FlowId, document.Name,
            document.Body.Select(TestDocumentCodec.Encode).ToArray(),
            document.Mappings.Select(TestDocumentCodec.EncodeMapping).ToArray(),
            document.References.ToArray(), document.Parameters.ToArray()), Options);
    }

    public static PublicFlowDocument Deserialize(string json)
    {
        var data = JsonSerializer.Deserialize<Data>(json, Options)
            ?? throw new FormatException("公共流程文件为空。");
        if (data.FormatVersion != PublicFlowDocument.CurrentFormat)
            throw new NotSupportedException("公共流程格式版本不受支持；不得自动迁移。");
        var flow = new PublicFlowDocument(data.FlowId, data.Name, data.FormatVersion,
            (data.Body ?? throw new FormatException("公共流程缺少步骤正文。"))
                .Select(TestDocumentCodec.Decode).ToImmutableArray())
        {
            Mappings = (data.Mappings ?? [])
                .Select(TestDocumentCodec.DecodeMapping).ToImmutableArray(),
            References = (data.References ?? []).ToImmutableArray(),
            Parameters = (data.Parameters ?? []).ToImmutableArray()
        };
        Validate(flow);
        return flow;
    }

    public static void Validate(PublicFlowDocument flow)
    {
        if (flow.FlowId == Guid.Empty || string.IsNullOrWhiteSpace(flow.Name))
            throw new FormatException("公共流程必须具有有效的独立身份和名称。");
        if (flow.FormatVersion != PublicFlowDocument.CurrentFormat)
            throw new NotSupportedException("公共流程版本需要显式迁移。");
        if (flow.Body.IsDefault || flow.Mappings.IsDefault ||
            flow.References.IsDefault || flow.Parameters.IsDefault)
            throw new FormatException("公共流程缺少必要集合。");
        foreach (var reference in flow.References) reference.Validate();
        if (flow.References.Select(x => x.AssetId).Distinct().Count() != flow.References.Length)
            throw new FormatException("公共流程存在重复资源引用。");
        if (flow.Parameters.Any(p => string.IsNullOrWhiteSpace(p.Name) ||
            string.IsNullOrWhiteSpace(p.Type)) ||
            flow.Parameters.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() != flow.Parameters.Length)
            throw new FormatException("公共流程参数名称或类型不合法。");

        // Use the existing tree validator for STRUCTURE only. Executability,
        // transitive references, hardware policy and recursion are run-preflight
        // concerns and must not be declared successful by a codec roundtrip.
        var draft = TestDocument.Create(flow.Name) with
        {
            Phases = TestDocument.Create(flow.Name).Phases.SetItem(
                TestPhase.Test, flow.Body),
            Mappings = flow.Mappings
        };
        var issues = StepTreeValidator.ValidateTree(draft,
            StepDescriptorCatalog.Builtins(), allowIncompleteSteps: true);
        var structural = issues.Where(x => !x.Contains("unresolved asset",
            StringComparison.Ordinal)).ToArray();
        if (structural.Length > 0)
            throw new FormatException("公共流程步骤结构无效：" + string.Join("; ", structural));
    }
}
