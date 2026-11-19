using AutoTest.Core;
using AutoTest.Projects.Dbc.Editing;

namespace AutoTest.Desktop.Projects;

/// <summary>Binding between a file-backed DBC workbench and its exact project asset revision.</summary>
public sealed record DbcEditorSession(
    ProjectId ProjectId,
    string ManifestPath,
    string ManifestSha256,
    AssetId AssetId,
    int AssetRevision,
    string AssetName,
    DbcWorkbenchDocument Document,
    DbcFileReceipt FileReceipt);
