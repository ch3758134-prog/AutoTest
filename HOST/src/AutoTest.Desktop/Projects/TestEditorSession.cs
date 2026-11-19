using AutoTest.Core;
using AutoTest.Testing.Authoring;

namespace AutoTest.Desktop.Projects;

/// <summary>Formal TEST document and its exact file/manifest asset revision.</summary>
public sealed record TestEditorSession(
    ProjectId ProjectId, string ManifestPath, AssetId AssetId,
    int AssetRevision, string AssetName, string FilePath,
    string FileHash, TestDocument Document);
