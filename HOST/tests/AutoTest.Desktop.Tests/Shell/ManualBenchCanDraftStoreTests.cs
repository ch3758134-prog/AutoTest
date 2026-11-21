using AutoTest.Desktop.Shell;
using Xunit;

namespace AutoTest.Desktop.Tests.Shell;

public sealed class ManualBenchCanDraftStoreTests
{
    [Fact]
    public async Task EditingConfigurationPersistsAcrossWorkbenchOpenWithoutSendingAnything()
    {
        var root = Path.Combine(Path.GetTempPath(), "autotest-can-draft-" + Guid.NewGuid().ToString("N"));
        try
        {
            var expected = new ManualBenchCanDraftStore.Document(1, [
                new("周期帧", "18FF5027", "00 20 00 32 00 00 00 00",
                    true, true, "65", true, null),
                new("单发", "701", "00 00 00 00 00 00 00 00",
                    false, false, "100", false, null)
            ]);
            await ManualBenchCanDraftStore.SaveAsync(root, expected);
            var read = await ManualBenchCanDraftStore.LoadAsync(root);
            Assert.NotNull(read);
            Assert.Equal(expected.Schema, read.Schema);
            Assert.Equal(expected.Rows, read.Rows);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ReplacingSendTableRetainsValidatedPreviousRevision()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "autotest-can-backup-" + Guid.NewGuid().ToString("N"));
        try
        {
            var first = new ManualBenchCanDraftStore.Document(1,
                [new("原配置", "701", "00 00", false, false, "100", true, null)]);
            var next = new ManualBenchCanDraftStore.Document(1,
                [new("新配置", "702", "AA BB", false, true, "65", true, null)]);
            await ManualBenchCanDraftStore.SaveAsync(root, first);
            await ManualBenchCanDraftStore.SaveAsync(root, next);
            Assert.Equal("新配置",
                (await ManualBenchCanDraftStore.LoadAsync(root))!.Rows[0].Name);
            Assert.Equal("原配置",
                (await ManualBenchCanDraftStore.LoadBackupAsync(root))!.Rows[0].Name);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void ExpiredDbcPinCannotBeSilentlyIgnoredWhenSendingRawBytes()
    {
        var row = new ManualBenchViewModel.BenchCanRow("过期 DBC 报文");
        row.MarkUnresolvedDbc(new ManualBenchCanDraftStore.DbcPin(
            Guid.NewGuid(), 3, new string('A', 64), 0x701u));
        row.AttachUniqueMatchingDbc();
        var rejection = Assert.Throws<InvalidOperationException>(() => row.BuildFrame());
        Assert.Contains("DBC", rejection.Message);

        // Operator must explicitly clear the obsolete assistance. This action
        // retains the exact raw DATA; it never sends the frame on its own.
        var savedBytes = row.DataHex;
        row.ClearDbcAssistance();
        Assert.Equal(savedBytes, row.DataHex);
        Assert.Equal((uint)0x701, row.BuildFrame().Id);
    }

    [Fact]
    public void DuplicatedExpiredDbcFrameStaysBlockedUntilExplicitOperatorAction()
    {
        var original = new ManualBenchViewModel.BenchCanRow("过期工程帧")
        {
            IdHex = "701",
            DataHex = "AA 00 00 00 00 00 00 01",
            IsPeriodic = true,
            PeriodText = "65",
            Included = true
        };
        var pin = new ManualBenchCanDraftStore.DbcPin(
            Guid.NewGuid(), 7, new string('B', 64), 0x701u);
        original.MarkUnresolvedDbc(pin);
        var copied = original.DuplicateForEditing("副本");
        Assert.Equal(original.DataHex, copied.DataHex);
        Assert.Equal(pin, copied.BoundDbcPin);
        Assert.Contains("DBC", Assert.Throws<InvalidOperationException>(
            () => copied.BuildFrame()).Message);
        copied.DataHex = "BB 00 00 00 00 00 00 01";
        copied.AttachUniqueMatchingDbc();
        Assert.Throws<InvalidOperationException>(() => copied.BuildFrame());
        // Explicit operator choice clears only the stale link, never the data.
        copied.ClearDbcAssistance();
        Assert.Null(copied.BoundDbcPin);
        Assert.Equal("BB 00 00 00 00 00 00 01", copied.DataHex);
        Assert.Equal((uint)0x701, copied.BuildFrame().Id);
    }

    [Fact]
    public async Task CorruptProjectDraftIsRejectedWithoutOverwritingItsBytes()
    {
        var root = Path.Combine(Path.GetTempPath(), "autotest-can-bad-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = ManualBenchCanDraftStore.DocumentPath(root);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, "{ broken json");
            await Assert.ThrowsAsync<System.Text.Json.JsonException>(
                () => ManualBenchCanDraftStore.LoadAsync(root));
            Assert.Equal("{ broken json", await File.ReadAllTextAsync(path));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void RefusesUnexpectedSchemaAndOversizedConfiguration()
    {
        Assert.Throws<InvalidDataException>(() =>
            ManualBenchCanDraftStore.Validate(new(2, [])));
        Assert.Throws<InvalidDataException>(() =>
            ManualBenchCanDraftStore.Validate(new(1, Enumerable.Range(0, 65)
                .Select(i => new ManualBenchCanDraftStore.Row("帧" + i, "701", "00",
                    false, false, "100", false, null)).ToList())));
    }
}
