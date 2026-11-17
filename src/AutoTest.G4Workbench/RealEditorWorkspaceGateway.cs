using System.Collections.Immutable;
using AutoTest.Core;
using AutoTest.Desktop.Projects;
using AutoTest.Desktop.Testing;
using AutoTest.Testing.Authoring;
using AutoTest.UiPreview;

namespace AutoTest.G4Workbench;

/// <summary>
/// Only stage-4 real Project/TEST adapter. No device or runtime execution is invoked.
/// Sessions and canonical mappings are retained; all writes go through the existing
/// content-hashed ProjectWorkspaceService and StepEditTransaction.
/// </summary>
public sealed class RealEditorWorkspaceGateway : IEditorWorkspaceGateway
{
    private readonly ProjectWorkspaceService _workspace = new();
    private readonly Dictionary<string, TestEditorSession> _open = new(StringComparer.Ordinal);
    private IReadOnlyList<UiDbcCatalog> _dbcCatalogs = [];


    public IReadOnlyList<PaletteKind> StepTypes { get; } =
        StepUiCatalog.Choices.Select(x => new PaletteKind(
            x.Type, x.Label, x.Runnable ? "测试步骤" : "待接入的步骤", "◇")).ToArray();

    public DemoStep CreateStep(string type) => ToUi(StepUiCatalog.NewNode(type));

    private UiProjectSnapshot CurrentProject()
    {
        var current = _workspace.Current ?? throw new InvalidOperationException("尚未打开工程。");
        return new UiProjectSnapshot(current.Definition.Name, current.ManifestPath,
            current.Definition.Assets
                .Where(a => a.Kind == ProjectWorkspaceService.TestProgramKind)
                .Select(a => new UiTestAsset(a.Id.ToString(), a.Name))
                .OrderBy(a => a.Name, StringComparer.CurrentCulture)
                .ToArray(),
            current.Definition.Assets
                .Where(a => a.Kind != ProjectWorkspaceService.TestProgramKind)
                .Select(a => new UiProjectAsset(a.Id.ToString(), a.Name, a.Kind))
                .OrderBy(a => a.Kind, StringComparer.Ordinal).ThenBy(a => a.Name, StringComparer.CurrentCulture)
                .ToArray());
    }

    public async Task<UiProjectSnapshot> OpenProjectAsync(string manifestPath, CancellationToken ct = default)
    {
        await _workspace.OpenAsync(manifestPath, ct);
        _open.Clear();
        _dbcCatalogs = [];
        return CurrentProject();
    }

    public async Task<UiProjectSnapshot> CreateProjectAsync(string name, string parentFolder, CancellationToken ct = default)
    {
        await _workspace.CreateAsync(name, parentFolder, ct);
        _open.Clear();
        return CurrentProject();
    }

    public async Task<UiProjectSnapshot> CreateTestAsync(string name, CancellationToken ct = default)
    {
        await _workspace.CreateTestProgramAsync(name, ct);
        return CurrentProject();
    }

    public async Task<UiProjectSnapshot> ImportDbcAsync(string filePath, CancellationToken ct = default)
    {
        await _workspace.ImportDbcAsync(filePath, ct);
        _dbcCatalogs = [];
        return CurrentProject();
    }

    /// <summary>Read the official DBC editor document after verifying file and manifest hashes.</summary>
    public async Task<IReadOnlyList<UiDbcCatalog>> LoadDbcCatalogAsync(CancellationToken ct = default)
    {
        var project = _workspace.Current ?? throw new InvalidOperationException("请先打开工程。");
        var result = new List<UiDbcCatalog>();
        foreach (var asset in project.Definition.Assets.Where(a => a.Kind == ProjectWorkspaceService.DbcKind))
        {
            ct.ThrowIfCancellationRequested();
            var loaded = await _workspace.OpenDbcEditorAsync(asset.Id, ct);
            result.Add(new UiDbcCatalog(asset.Id.ToString(), asset.Name, asset.Revision,
                asset.Sha256,
                loaded.Document.Model.Messages.OrderBy(m => m.Name, StringComparer.CurrentCulture)
                    .Select(m => new UiDbcMessage(asset.Id.ToString(), asset.Name, asset.Revision,
                        asset.Sha256, m.Id, m.ArbitrationId, m.IsExtended, m.Name, m.Dlc,
                        m.Signals.Select(s => new UiDbcSignal(s.Name, s.Unit, s.Minimum, s.Maximum,
                            s.Factor, s.Offset, s.Signed, s.Multiplexer)).ToArray()))
                    .ToArray()));
        }
        _dbcCatalogs = result;
        return result;
    }

    public async Task<UiProjectSnapshot> RenameTestAsync(string assetId, string name, CancellationToken ct = default)
    {
        if (!AssetId.TryParse(assetId, out var id)) throw new ArgumentException("非法 TEST 资源 ID。");
        await _workspace.RenameAssetAsync(id, name, ct);
        _open.Remove(assetId); // force an up-to-date resource session on next open
        return CurrentProject();
    }

    public async Task<UiTestSnapshot> OpenTestAsync(string assetId, CancellationToken ct = default)
    {
        if (!AssetId.TryParse(assetId, out var id)) throw new ArgumentException("非法 TEST 资源 ID。");
        var session = await _workspace.OpenTestEditorAsync(id, ct);
        _open[assetId] = session;
        return new UiTestSnapshot(assetId, session.AssetName, ToUiPhases(session.Document));
    }

    public IReadOnlyList<string> Validate(string assetId, IReadOnlyList<DemoPhase> phases)
    {
        if (!_open.TryGetValue(assetId, out var session))
            return ["TEST 尚未打开，无法验证。"];
        try
        {
            var candidate = CreateCandidate(session.Document, phases);
            return StepTreeValidator.ValidateTree(candidate, StepDescriptorCatalog.Builtins())
                .Concat(ValidateDbcBindings(phases)).ToArray();
        }
        catch (Exception e) { return [e.Message]; }
    }

    public async Task<UiTestSnapshot> SaveTestAsync(string assetId, IReadOnlyList<DemoPhase> phases, CancellationToken ct = default)
    {
        if (!_open.TryGetValue(assetId, out var session))
            throw new InvalidOperationException("当前 TEST 尚未正式打开。");
        var candidate = CreateCandidate(session.Document, phases);
        var problems = StepTreeValidator.ValidateTree(candidate, StepDescriptorCatalog.Builtins())
            .Concat(ValidateDbcBindings(phases)).ToArray();
        if (problems.Length > 0)
            throw new InvalidOperationException("测试程序检查未通过：" + Environment.NewLine +
                string.Join(Environment.NewLine, problems.Take(20)));
        var saved = await _workspace.SaveTestEditorAsync(session, candidate, ct);
        _open[assetId] = saved;
        return new UiTestSnapshot(assetId, saved.AssetName, ToUiPhases(saved.Document));
    }

    private IReadOnlyList<string> ValidateDbcBindings(IReadOnlyList<DemoPhase> phases)
    {
        var errors = new List<string>();
        foreach (var phase in phases)
            foreach (var node in PreviewEditorDocument.Traverse(phase.Steps))
                errors.AddRange(CanDbcBinding.Validate(node, _dbcCatalogs));
        return errors;
    }

    private static TestDocument CreateCandidate(TestDocument original, IReadOnlyList<DemoPhase> phases)
    {
        if (phases.Count != 3 || phases.Select(p => p.Key).Distinct(StringComparer.Ordinal).Count() != 3)
            throw new InvalidOperationException("TEST 必须包含准备、测试和清理三个阶段。");
        var tx = new StepEditTransaction(original);
        foreach (var phase in Enum.GetValues<TestPhase>())
        {
            var key = phase switch { TestPhase.Prepare => "prepare", TestPhase.Test => "test", _ => "cleanup" };
            var rows = phases.SingleOrDefault(p => p.Key == key)
                ?? throw new InvalidOperationException("缺少阶段：" + key);
            foreach (var node in original.Get(phase))
                tx.Delete(node.Id);
            for (var i = 0; i < rows.Steps.Count; i++)
                tx.Insert(phase, null, null, i, ToFormal(rows.Steps[i]));
        }
        return tx.Current;
    }

    private static IReadOnlyList<DemoPhase> ToUiPhases(TestDocument doc)
    {
        var list = new List<DemoPhase>();
        foreach (var phase in Enum.GetValues<TestPhase>())
        {
            var key = phase switch { TestPhase.Prepare => "prepare", TestPhase.Test => "test", _ => "cleanup" };
            var row = new DemoPhase(key, phase switch
            {
                TestPhase.Prepare => "准备", TestPhase.Test => "测试", _ => "清理"
            });
            foreach (var node in doc.Get(phase)) row.Steps.Add(ToUi(node));
            list.Add(row);
        }
        return list;
    }

    private static DemoStep ToUi(StepNode node)
    {
        var step = new DemoStep
        {
            Id = node.Id.Value, Kind = node.Type, Name = node.Name,
            Breakpoint = node.Properties.GetValueOrDefault("editor.breakpoint", "false") == "true",
            Enabled = node.Properties.GetValueOrDefault("editor.disabled", "false") != "true"
        };
        foreach (var property in node.Properties)
        {
            if (property.Key.StartsWith("editor.", StringComparison.Ordinal))
                step.EditorMetadata.Add(property.Key, property.Value);
            else
                step.Parameters.Add(property.Key, property.Value);
        }
        foreach (var slot in node.Slots.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var branch = new DemoBranch(slot.Key);
            foreach (var child in slot.Value) branch.Steps.Add(ToUi(child));
            step.Branches.Add(branch);
        }
        return step;
    }

    private static StepNode ToFormal(DemoStep step)
    {
        var slots = step.Branches.ToImmutableDictionary(x => x.Name,
            x => x.Steps.Select(ToFormal).ToImmutableArray(), StringComparer.Ordinal);
        var properties = step.Parameters.ToImmutableDictionary(StringComparer.Ordinal);
        foreach (var metadata in step.EditorMetadata)
            properties = properties.SetItem(metadata.Key, metadata.Value);
        if (step.Breakpoint) properties = properties.SetItem("editor.breakpoint", "true");
        else if (properties.ContainsKey("editor.breakpoint"))
            properties = properties.SetItem("editor.breakpoint", "false");
        if (!step.Enabled) properties = properties.SetItem("editor.disabled", "true");
        else if (properties.ContainsKey("editor.disabled"))
            properties = properties.SetItem("editor.disabled", "false");
        return new StepNode(StepId.Create(step.Id), step.Kind, step.Name, properties, slots);
    }
}
