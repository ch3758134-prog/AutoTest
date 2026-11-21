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
