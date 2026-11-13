using AutoTest.Desktop.Configuration;
using AutoTest.Desktop.Projects;
using Xunit;

namespace AutoTest.Desktop.Tests.Projects;

public sealed class WaveAnalysisConfigTests
{
    [Fact]
    public void NamedReference_RoundTripsWithBackwardCompatibleConfiguration()
    {
        var original = WaveSettingsDocument.Create("ResponseWindow");
        Assert.Equal("", original.ReferenceName);
        var oldDecoded = (WaveSettingsDocument)ProjectConfigurationCodec.Decode(
            ProjectConfigurationCodec.WaveSettingsKind,
            ProjectConfigurationCodec.Encode(ProjectConfigurationCodec.WaveSettingsKind, original));
        Assert.Equal("", oldDecoded.ReferenceName);

        var configured = original with
        {
            DbcAssetId = Guid.NewGuid(),
            DbcAssetRevision = 2,
            DbcAssetFingerprint = new string('a', 64),
            SignalName = "MotorCurrent",
            ReferenceName = "Confirmed-Reference"
        };
        var bytes = ProjectConfigurationCodec.Encode(ProjectConfigurationCodec.WaveSettingsKind, configured);
        var reopened = (WaveSettingsDocument)ProjectConfigurationCodec.Decode(
            ProjectConfigurationCodec.WaveSettingsKind, bytes);
        Assert.Equal("Confirmed-Reference", reopened.ReferenceName);
        Assert.Equal(configured.DbcAssetId, reopened.DbcAssetId);
    }

    [Fact]
    public void NamedReference_RequiresConfiguredDbcSignal()
    {
        var draft = WaveSettingsDocument.Create("Invalid") with
        { ReferenceName = "Reference" };
        var error = Assert.Throws<AutoTest.Projects.Project.ProjectDataException>(
            () => draft.Validate());
        Assert.Equal("WAVE_REFERENCE", error.Code);
    }

    [Fact]
    public async Task ReferenceChoices_AreEmptyBeforeAnyWaveCapture()
    {
        var root = Path.Combine(Path.GetTempPath(), "at-wave-reference-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var project = new ProjectWorkspaceService();
            await project.CreateAsync("Wave", root);
            var choices = WaveReferenceWorkspaceService.ReferenceNames(project,
                WaveSettingsDocument.Create("Wave"));
            Assert.Single(choices);
            Assert.Equal("", choices[0]);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
