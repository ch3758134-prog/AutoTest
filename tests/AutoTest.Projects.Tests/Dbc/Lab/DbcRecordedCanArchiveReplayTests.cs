using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoTest.Projects.Dbc.Lab;
using AutoTest.Projects.Project;
using Xunit;

namespace AutoTest.Projects.Tests.Dbc.Lab;

public sealed class DbcRecordedCanArchiveReplayTests
{
    private const string Dbc = """
        VERSION ""
        NS_ :
        BS_:
        BU_: ECU Tester
        BO_ 291 Status: 2 ECU
         SG_ Speed : 0|16@1+ (0.1,0) [0|6553.5] "km/h" Tester
        """;

    private static (DbcWorkbenchDocument Doc, DbcRevisionBinding Binding) Pinned()
    {
        var doc = DbcWorkbenchDocument.Import("Replay", Dbc);
        return (doc, new DbcRevisionBinding(Guid.NewGuid(), 2, doc.SourceSha256));
    }

    private static async Task<string> ArchiveFileAsync(DbcRevisionBinding binding,
        long secondOffset = 1000, string secondHex = "C800", int? revision = null,
        string? role = null)
    {
        var obj = new
        {
            SchemaVersion = 1, RecordId = Guid.NewGuid(),
            DbcId = binding.DbcAssetId, DbcRevision = revision ?? binding.Revision,
            DbcSha256 = binding.Sha256,
            StartUtc = new DateTimeOffset(2026, 11, 8, 10, 0, 0, TimeSpan.Zero),
            Frames = new object[]
            {
                new { OffsetMicroseconds = 0L, Role = (object)(role ?? "DutCan"),
                    FrameId = 291u, Extended = false, PayloadHex = "6400",
                    LostFrames = 0L, TraceEvidence = Guid.NewGuid() },
                new { OffsetMicroseconds = secondOffset, Role = (object)(role ?? "DutCan"),
                    FrameId = 291u, Extended = false, PayloadHex = secondHex,
                    LostFrames = 2L, TraceEvidence = Guid.NewGuid() }
            }
        };
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(obj));
        return file;
    }

    [Fact]
    public async Task KnownWaveArchive_DecodesFramesAndPreservesSourceTimeAndLoss()
    {
        var (doc, binding) = Pinned();
        var path = await ArchiveFileAsync(binding);
        try
        {
            var replay = await DbcRecordedCanArchiveReplay.ReadAsync(path, doc, binding);
            Assert.Equal(2, replay.TotalFrames);
            Assert.Equal(2, replay.DecodedFrames);
            Assert.Equal(2, replay.MaximumReportedLostFrames);
            Assert.Equal("DECODED", replay.Frames[0].Status);
            Assert.Contains("Speed=10", replay.Frames[0].DecodedSummary);
            Assert.Equal(1000, replay.Frames[1].OffsetMicroseconds);
            Assert.Equal(TimeSpan.FromMilliseconds(1),
                replay.Frames[1].ReconstructedUtc - replay.Frames[0].ReconstructedUtc);
            Assert.Contains("UNKNOWN", replay.ClockQuality);
            Assert.Equal("REPLAY_ARCHIVE_NOT_LIVE", replay.Source);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task StaleRevision_IsRejectedBeforeAnyDecode()
    {
        var (doc, binding) = Pinned();
        var path = await ArchiveFileAsync(binding, revision: 1);
        try
        {
            var error = await Assert.ThrowsAsync<ProjectDataException>(() =>
                DbcRecordedCanArchiveReplay.ReadAsync(path, doc, binding));
            Assert.Equal("REFERENCE_STALE", error.Code);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task BadOrdering_IsRejected_NoTruncatedSuccess()
    {
        var (doc, binding) = Pinned();
        var path = await ArchiveFileAsync(binding, secondOffset: 0);
        try
        {
            var error = await Assert.ThrowsAsync<ProjectDataException>(() =>
                DbcRecordedCanArchiveReplay.ReadAsync(path, doc, binding));
            Assert.Equal("DBC_ARCHIVE_ORDER", error.Code);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task BadHex_IsRejected_AndDlcMismatchBecomesEvidenceRow()
    {
        var (doc, binding) = Pinned();
        var bad = await ArchiveFileAsync(binding, secondHex: "XX");
        var shortDlc = await ArchiveFileAsync(binding, secondHex: "01");
        try
        {
            var error = await Assert.ThrowsAsync<ProjectDataException>(() =>
                DbcRecordedCanArchiveReplay.ReadAsync(bad, doc, binding));
            Assert.Equal("DBC_ARCHIVE_HEX", error.Code);
            var result = await DbcRecordedCanArchiveReplay.ReadAsync(shortDlc, doc, binding);
            Assert.Equal(1, result.RejectedFrames);
            Assert.Equal("DBC_REPLAY_DLC", result.Frames[1].ErrorCode);
            Assert.Equal("01", result.Frames[1].PayloadHex);
        }
        finally { File.Delete(bad); File.Delete(shortDlc); }
    }

    [Fact]
    public async Task FrameBudget_IsExplicitlyEnforced()
    {
        var (doc, binding) = Pinned();
        var path = await ArchiveFileAsync(binding);
        try
        {
            var error = await Assert.ThrowsAsync<ProjectDataException>(() =>
                DbcRecordedCanArchiveReplay.ReadAsync(path, doc, binding, maxFrames: 1));
            Assert.Equal("DBC_ARCHIVE_BUDGET", error.Code);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task KeypadFrames_DoNotPretendToBeDutData()
    {
        var (doc, binding) = Pinned();
        var path = await ArchiveFileAsync(binding, role: "KeypadCan");
        try
        {
            var replay = await DbcRecordedCanArchiveReplay.ReadAsync(path, doc, binding);
            Assert.Equal(0, replay.DecodedFrames);
            Assert.Equal(2, replay.SkippedKeypadFrames);
        }
        finally { File.Delete(path); }
    }
}
