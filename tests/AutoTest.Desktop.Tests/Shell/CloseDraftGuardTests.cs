using AutoTest.Desktop.Shell;
using Xunit;

namespace AutoTest.Desktop.Tests.Shell;

public sealed class CloseDraftGuardTests
{
    private static CloseDraftDocument Document(string title, Func<bool> busy,
        Func<bool> dirty, Func<Task<bool>> save) => new(title, busy, dirty, save);

    [Fact]
    public async Task CancelLeavesBothDraftsIntactWithoutSaving()
    {
        var writes = 0;
        var names = Array.Empty<string>();
        var items = new[]
        {
            Document("TEST", () => false, () => true, () => { writes++; return Task.FromResult(true); }),
            Document("DBC", () => false, () => true, () => { writes++; return Task.FromResult(true); })
        };
        var result = await CloseDraftGuard.PrepareAsync(items, dirty =>
        {
            names = dirty.ToArray();
            return CloseDraftDecision.Cancel;
        }, _ => throw new Exception("No error expected"));
        Assert.False(result);
        Assert.Equal(0, writes);
        Assert.Equal(new[] { "TEST", "DBC" }, names);
    }

    [Fact]
    public async Task DiscardDoesNotSaveAnyChangedDocument()
    {
        var saves = 0;
        var item = Document("用例", () => false, () => true,
            () => { saves++; return Task.FromResult(true); });
        Assert.True(await CloseDraftGuard.PrepareAsync([item],
            _ => CloseDraftDecision.Discard, _ => throw new Exception("Unexpected error")));
        Assert.Equal(0, saves);
    }

    [Fact]
    public async Task SaveWaitsForPersistence_AndRejectsUnchangedDirtyFlag()
    {
        var dirty = true;
        var saved = 0;
        var first = Document("程序", () => false, () => dirty,
            async () => { await Task.Yield(); saved++; dirty = false; return true; });
        Assert.True(await CloseDraftGuard.PrepareAsync([first],
            _ => CloseDraftDecision.Save, _ => throw new Exception("Unexpected error")));
        Assert.Equal(1, saved);
    }

    [Fact]
    public async Task FailedSaveBlocksExit_AndSkipsLaterSaves()
    {
        var laterWrites = 0;
        var message = "";
        var documents = new[]
        {
            Document("配置", () => false, () => true, () => Task.FromResult(false)),
            Document("DBC", () => false, () => true,
                () => { laterWrites++; return Task.FromResult(true); })
        };
        Assert.False(await CloseDraftGuard.PrepareAsync(documents,
            _ => CloseDraftDecision.Save, text => message = text));
        Assert.Contains("配置", message);
        Assert.Equal(0, laterWrites);
    }

    [Fact]
    public async Task BusyEditorBlocksExitEvenOnDiscardAndReportsReason()
    {
        var prompted = false;
        var message = "";
        var document = Document("正在运行的用例", () => true, () => true,
            () => Task.FromResult(true));
        Assert.False(await CloseDraftGuard.PrepareAsync([document], _ =>
        {
            prompted = true;
            return CloseDraftDecision.Discard;
        }, text => message = text));
        Assert.False(prompted);
        Assert.Contains("正在运行的用例", message);
    }

    [Fact]
    public async Task SaveCallbackThatClaimsSuccessButLeavesDirtyData_BlocksExit()
    {
        var message = "";
        var document = Document("步骤属性", () => false, () => true,
            () => Task.FromResult(true));
        Assert.False(await CloseDraftGuard.PrepareAsync([document],
            _ => CloseDraftDecision.Save, value => message = value));
        Assert.Contains("步骤属性", message);
    }

    [Fact]
    public async Task SaveExceptionNeverTurnsIntoSilentDiscard()
    {
        var message = "";
        var document = Document("DBC", () => false, () => true,
            () => throw new InvalidOperationException("磁盘写入异常"));
        Assert.False(await CloseDraftGuard.PrepareAsync([document],
            _ => CloseDraftDecision.Save, value => message = value));
        Assert.Contains("磁盘写入异常", message);
    }

    [Fact]
    public async Task CleanEditorsCloseWithoutPrompt()
    {
        Assert.True(await CloseDraftGuard.PrepareAsync([
            Document("项目", () => false, () => false, () => Task.FromResult(false))
        ], _ => throw new Exception("Should not prompt"),
        _ => throw new Exception("Should not report")));
    }
}
