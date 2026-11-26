using System.IO;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Specialists.Wave;
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
        Assert.Equal(WaveInterpolationMode.Linear, oldDecoded.Comparison.InterpolationMode);

        var discrete = original with
        {
            Comparison = original.Comparison with
            {
                InterpolationMode = WaveInterpolationMode.StepHold
            }
        };
        var discreteBytes = ProjectConfigurationCodec.Encode(
            ProjectConfigurationCodec.WaveSettingsKind, discrete);
        var discreteReopened = (WaveSettingsDocument)ProjectConfigurationCodec.Decode(
            ProjectConfigurationCodec.WaveSettingsKind, discreteBytes);
        Assert.Equal(WaveInterpolationMode.StepHold,
            discreteReopened.Comparison.InterpolationMode);

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
    public void AlgorithmReviewIsExplicitPinnedAndInvalidatedByAnyCriteriaOrReferenceChange()
    {
        var draft = WaveSettingsDocument.Create("ReviewedCurrent") with
        {
            DbcAssetId = Guid.NewGuid(),
            DbcAssetRevision = 1,
            DbcAssetFingerprint = new string('a', 64),
            Bus = BusRole.DutCan,
            CanId = 0x123,
            SignalName = "MotorCurrent",
            Unit = "A",
            ReferenceName = "Approved-Reference"
        };
        Assert.False(draft.IsComparisonReviewCurrent);
        var approved = draft.ApproveCurrentComparison();
        Assert.True(approved.IsComparisonReviewCurrent);
        Assert.Equal(WaveSettingsDocument.CurrentComparisonAlgorithmRevision,
            approved.ApprovedAlgorithmRevision);

        var reopened = (WaveSettingsDocument)ProjectConfigurationCodec.Decode(
            ProjectConfigurationCodec.WaveSettingsKind,
            ProjectConfigurationCodec.Encode(ProjectConfigurationCodec.WaveSettingsKind, approved));
        Assert.True(reopened.IsComparisonReviewCurrent);
        Assert.False((reopened with
        {
            ReferenceName = "Different-Reference"
        }).IsComparisonReviewCurrent);
        Assert.False((reopened with
        {
            Comparison = reopened.Comparison with { AbsoluteTolerance = 0.7 }
        }).IsComparisonReviewCurrent);
        Assert.False((reopened with
        {
            DbcAssetFingerprint = new string('b', 64)
        }).IsComparisonReviewCurrent);
        Assert.False((reopened with
        {
            Comparison = reopened.Comparison with
            {
                InterpolationMode = WaveInterpolationMode.StepHold
            }
        }).IsComparisonReviewCurrent);
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
    [Fact]
    public async Task CapturedWave_RequiresExplicitReferencePromotionAndPreservesIdentity()
    {
        var directory = Path.Combine(Path.GetTempPath(), "at-wave-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var workspace = new ProjectWorkspaceService();
            await workspace.CreateAsync("WaveReference", directory);
            var signal = new SignalRef(new AssetRef(AssetId.New(), AssetRevision.Create(1),
                RevisionFingerprint.FromSha256(new string('a', 64))),
                BusRole.DutCan, 0x123, "Current", "A");
            var origin = new TimePoint(DateTimeOffset.UtcNow, 0,
                TimeSource.Replay, TimeQuality.ReplayDerived);
            var buffer = new WaveSampleBuffer(signal, origin, 32);
            for (var i = 1; i <= 4; i++)
            {
                buffer.Append(new SignalObservation(signal, i, "A",
                    new TimePoint(origin.Utc.AddMilliseconds(100 * i), i,
                        TimeSource.Replay, TimeQuality.ReplayDerived),
                    EvidenceQuality.Derived, EvidenceRef.New(), false));
            }
            var evidence = new WaveEvidenceDocument(EvidenceRef.New(), "Capture",
                buffer.Snapshot(), null, null, null, null, null, null,
                null, null, DateTimeOffset.UtcNow);
            var store = new WaveEvidenceStore(Path.Combine(
                workspace.Current!.RootDirectory, "wave-records", "evidence"));
            store.Write(evidence);
            var configured = WaveSettingsDocument.Create("Current") with
            {
                DbcAssetId = signal.DbcAsset.Id.Value,
                DbcAssetRevision = signal.DbcAsset.Revision.Value,
                DbcAssetFingerprint = signal.DbcAsset.Fingerprint.Sha256,
                Bus = signal.Role, CanId = signal.FrameId,
                SignalName = signal.SignalName, Unit = signal.Unit
            };
            var choices = WaveReferenceWorkspaceService.FindCandidates(workspace, configured);
            Assert.Single(choices);
            Assert.Equal(evidence.Id.Value, choices[0].Evidence);
            Assert.DoesNotContain("confirmed",
                WaveReferenceWorkspaceService.ReferenceNames(workspace, configured));
            WaveReferenceWorkspaceService.Promote(workspace, configured, evidence.Id.Value, "confirmed");
            Assert.Contains("confirmed",
                WaveReferenceWorkspaceService.ReferenceNames(workspace, configured));
            Assert.Throws<InvalidDataException>(() =>
                WaveReferenceWorkspaceService.Promote(workspace, configured,
                    evidence.Id.Value, "confirmed"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

}
