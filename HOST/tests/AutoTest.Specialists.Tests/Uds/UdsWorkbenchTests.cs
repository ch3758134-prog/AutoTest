using AutoTest.Specialists.Uds.Workbench;
using Xunit;

namespace AutoTest.Specialists.Tests.Uds;

public sealed class UdsWorkbenchTests
{
    private static UdsToolProfile Profile(int p2 = 1000) =>
        new("ECU", "ECU_A", 0x7E0, 0x7E8, p2, Math.Max(p2, 1000));

    [Theory]
    [InlineData("22 F1 90")]
    [InlineData("0x22,0xF1,0x90")]
    [InlineData("22-F1-90")]
    public void HexEditorParsesCommonRepresentations(string source) =>
        Assert.Equal(new byte[] { 0x22,0xF1,0x90 }, UdsHex.Parse(source));

    [Theory]
    [InlineData("22F19")]
    [InlineData("22 GG 90")]
    [InlineData("")]
    public void MalformedHexIsRejected(string text) =>
        Assert.Throws<FormatException>(() => UdsHex.Parse(text));

    [Fact]
    public async Task ServiceFileReopensAndRejectsInvalidEndpoint()
    {
        string path=Path.Combine(Path.GetTempPath(),"uds_service_"+Guid.NewGuid().ToString("N")+".json");
        try
        {
            var original = new UdsToolDocument(1,Profile(),[
                new UdsServiceDefinition("Read VIN","22 F1 90","")]);
            await UdsToolDocumentStore.SaveAsync(path,original);
            var loaded=await UdsToolDocumentStore.LoadAsync(path);
            Assert.Equal("Read VIN",Assert.Single(loaded.Services).Name);
            Assert.Equal(original.Profile,loaded.Profile);
            Assert.Throws<ArgumentException>(() => UdsToolDocumentStore.Validate(
                original with { Profile=original.Profile with { TxId=original.Profile.RxId } }));
        }
        finally { if(File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void ReplayRejectsMismatchedRequestWithoutAdvancing()
    {
        var replay = new UdsReplayWorkspace();
        replay.Load(new UdsReplayDocument(1,[new UdsReplayRecord(
            "22 F1 90", ["7F 22 78","62 F1 90 4D 4F 43 4B"],
            [new UdsCanTraceRow("RX", "03 7F 22 78")])]));
        Assert.Throws<InvalidOperationException>(()=>replay.Consume("22 AA BB"));
        Assert.Equal(0,replay.Position);
        var result=replay.Consume("22 F1 90");
        Assert.Equal("Replay",result.Source);
        Assert.Equal("Accepted",result.Outcome);
        Assert.Equal(1,replay.Position);
        Assert.Single(result.Frames);
    }

    [Fact]
    public void OnlyPendingResponsesCannotCreateAReplayPositive()
    {
        var replay = new UdsReplayWorkspace();
        replay.Load(new UdsReplayDocument(1,[new UdsReplayRecord("22 F1 90",
            ["7F 22 78"],[])]));
        var result=replay.Consume("22 F1 90");
        Assert.Equal("Uncertain",result.Outcome);
        Assert.Equal("REPLAY_NO_FINAL_RESPONSE",result.ErrorCode);
    }
}
