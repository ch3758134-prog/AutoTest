using AutoTest.Core;
using AutoTest.Testing.Authoring;

namespace AutoTest.Desktop.Projects;

public sealed record TestProjectEditorSession(
    ProjectId ProjectId, string ManifestPath, AssetId AssetId, int AssetRevision,
    string AssetName, string FilePath, string FileSha256, TestProjectDocument Document);
