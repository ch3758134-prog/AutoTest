using System.IO;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Desktop.Projects;
using AutoTest.Projects.ControllerConfig;
using AutoTest.Projects.Dbc;
using AutoTest.Projects.Dbc.Editing;
using AutoTest.Projects.Project;
using AutoTest.Testing.Authoring;

namespace AutoTest.Desktop.Testing;

public sealed record MappingChoice(string Label, TestTargetMapping Mapping, DbcMessage? Message = null,
    DbcDisplayNames? DisplayNames = null, IReadOnlyDictionary<long, string>? ValueLabels = null,
    IReadOnlyDictionary<string, IReadOnlyDictionary<long, string>>? MessageValueLabels = null,
    WaveSettingsDocument? WaveSettings = null, string? WaveProjectRoot = null)
{
    public override string ToString() => Label;
}

/// <summary>
/// Enumerate pinned, physically meaningful objects from the current project.
/// The operator chooses a functional alias; no raw JSON IDs are typed into steps.
/// </summary>
public static class TestTargetCatalog
{
    public static async Task<IReadOnlyList<MappingChoice>> LoadAsync(
        ProjectWorkspaceService workspace, CancellationToken ct = default,
        bool includeAllDbcSignals = false)
    {
        var context = workspace.Current ?? throw new InvalidOperationException("工程未打开。");
        return await Task.Run(async () =>
        {
            var choices = new List<MappingChoice>
            {
                new("台架 / IGN", new TestTargetMapping("IGN", TestTargetKind.Ignition,
                    default, 0, ""))
            };
            foreach (var asset in context.Definition.Assets)
            {
                if (asset.Kind is not (ProjectWorkspaceService.DbcKind or
                    ProjectWorkspaceService.ControllerKind or
                    ProjectWorkspaceService.WaveSettingsKind)) continue;
                if (includeAllDbcSignals && asset.Kind != ProjectWorkspaceService.DbcKind)
                    continue;
                ct.ThrowIfCancellationRequested();
                var path = ProjectPathPolicy.Normalize(context.RootDirectory, asset.RelativePath);
                var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
                if (!asset.VerifyHash(bytes))
                    throw new ProjectDataException("MAPPING_ASSET_CHANGED", asset.Name);
                if (asset.Kind == ProjectWorkspaceService.DbcKind)
                {
                    var source = DbcSource.Load(bytes);
                    // Present all messages for operator repair/selection, even when an
                    // imported DBC has overlapping fields. Real decode/encode paths
                    // continue to use the STRICT parser and reject it before any run.
                    var dbc = DbcParserAdapter.Parse(source.Text, asset.Name,
                        allowSignalOverlaps: true);
                    var displayNames = DbcDisplayNames.Parse(source.Text);
                    // Existing DBC VAL_ definitions are the only authoritative
                    // source of symbolic states such as 驻车=2.
                    var valueTables = DbcWorkbenchDocument.Import(asset.Name, source.Text).ValueTables;
                    foreach (var msg in dbc.Messages)
                    {
                        var frameId = msg.ArbitrationId;
                        var msgAlias = asset.Name + "." + msg.Name;
                        var messageSummary = displayNames.MessageSummary(msg.Id);
                        choices.Add(new MappingChoice("CAN 报文 / " +
                            displayNames.MessageLabel(msg) + " · " + asset.Name +
                            " [0x" + frameId.ToString("X") + "]" +
                            (messageSummary.Length == 0 ? "" : " — " +
                             (messageSummary.Length > 48 ? messageSummary[..48] + "…" : messageSummary)),
                            new TestTargetMapping(
                                msgAlias, TestTargetKind.CanMessage, asset.Id,
                                asset.Revision, asset.Sha256, BusRole.DutCan,
                                frameId, msg.IsExtended), msg, displayNames, null,
                            valueTables.Where(x => x.MessageId == msg.Id).ToDictionary(
                                x => x.SignalName, x => (IReadOnlyDictionary<long, string>)x.Labels,
                                StringComparer.Ordinal)));
                        foreach (var signal in msg.Signals)
                        {
                            var alias = msgAlias + "." + signal.Name;
                            choices.Add(new MappingChoice("信号 / " +
                                displayNames.MessageLabel(msg) + " / " +
                                displayNames.SignalLabel(msg, signal) +
                                (signal.Unit.Length == 0 ? "" : " (" + signal.Unit + ")") +
                                (string.IsNullOrWhiteSpace(displayNames.SignalSummary(msg.Id, signal.Name))
                                    ? "" : " — " + displayNames.SignalSummary(msg.Id, signal.Name)),
                                new TestTargetMapping(alias, TestTargetKind.Signal, asset.Id,
                                    asset.Revision, asset.Sha256, BusRole.DutCan,
                                    frameId, msg.IsExtended, signal.Name, signal.Unit), msg, displayNames,
                                valueTables.FirstOrDefault(x =>
                                    x.MessageId == msg.Id && x.SignalName == signal.Name)?.Labels));
                        }
                    }
                }
                else if (asset.Kind == ProjectWorkspaceService.WaveSettingsKind)
                {
                    var analysis = (WaveSettingsDocument)ProjectConfigurationCodec.Decode(asset.Kind, bytes);
                    choices.Add(new MappingChoice("波形分析方案 / " + asset.Name +
                        (string.IsNullOrWhiteSpace(analysis.SignalName) ? "（未配置信号）" :
                        " · " + analysis.SignalName),
                        new TestTargetMapping(asset.Name, TestTargetKind.WaveProfile,
                            asset.Id, asset.Revision, asset.Sha256),
                        WaveSettings: analysis, WaveProjectRoot: context.RootDirectory));
                }
                else
                {
                    var profile = ControllerRepository.Decode(bytes);
                    foreach (var controller in profile.Controllers)
                        foreach (var state in controller.States)
                        {
                            var alias = controller.Name + "." + state.Name;
                            choices.Add(new MappingChoice("按键 / " + alias,
                                new TestTargetMapping(alias, TestTargetKind.Button, asset.Id,
                                    asset.Revision, asset.Sha256, ControllerId: controller.Id,
                                    StateId: state.Id)));
                        }
                }
            }
            if (workspace.Current?.ManifestPath != context.ManifestPath ||
                workspace.Current.ManifestSha256 != context.ManifestSha256)
                throw new ProjectDataException("PROJECT_CHANGED",
                    "加载测试对象期间 ECU 工程或资源修订发生变化。");
            return (IReadOnlyList<MappingChoice>)choices;
        }, ct);
    }
}
