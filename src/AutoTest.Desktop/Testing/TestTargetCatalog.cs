using System.IO;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Desktop.Projects;
using AutoTest.Projects.ControllerConfig;
using AutoTest.Projects.Dbc;
using AutoTest.Projects.Project;
using AutoTest.Testing.Authoring;

namespace AutoTest.Desktop.Testing;

public sealed record MappingChoice(string Label, TestTargetMapping Mapping)
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
        ProjectWorkspaceService workspace, CancellationToken ct = default)
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
                    ProjectWorkspaceService.ControllerKind)) continue;
                ct.ThrowIfCancellationRequested();
                var path = ProjectPathPolicy.Normalize(context.RootDirectory, asset.RelativePath);
                var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
                if (!asset.VerifyHash(bytes))
                    throw new ProjectDataException("MAPPING_ASSET_CHANGED", asset.Name);
                if (asset.Kind == ProjectWorkspaceService.DbcKind)
                {
                    var dbc = DbcSource.Load(bytes).Parse(asset.Name);
                    foreach (var msg in dbc.Messages)
                    {
                        var frameId = msg.ArbitrationId;
                        var msgAlias = asset.Name + "." + msg.Name;
                        choices.Add(new MappingChoice("CAN / " + msgAlias + " [0x" +
                            frameId.ToString("X") + "]", new TestTargetMapping(
                                msgAlias, TestTargetKind.CanMessage, asset.Id,
                                asset.Revision, asset.Sha256, BusRole.DutCan,
                                frameId, msg.IsExtended)));
                        foreach (var signal in msg.Signals)
                        {
                            var alias = msgAlias + "." + signal.Name;
                            choices.Add(new MappingChoice("信号 / " + alias + " (" + signal.Unit + ")",
                                new TestTargetMapping(alias, TestTargetKind.Signal, asset.Id,
                                    asset.Revision, asset.Sha256, BusRole.DutCan,
                                    frameId, msg.IsExtended, signal.Name, signal.Unit)));
                        }
                    }
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
            if (workspace.Current?.ManifestPath != context.ManifestPath)
                throw new ProjectDataException("PROJECT_CHANGED", "工程在选择测试对象时已切换。");
            return (IReadOnlyList<MappingChoice>)choices;
        }, ct);
    }
}
