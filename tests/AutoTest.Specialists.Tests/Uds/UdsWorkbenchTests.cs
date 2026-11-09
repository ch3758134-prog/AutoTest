using AutoTest.Specialists.Uds.Workbench;
using Xunit;

namespace AutoTest.Specialists.Tests.Uds;

public sealed class UdsWorkbenchTests
{
    private static UdsToolProfile Profile(int p2 = 1000) =>
        new("Mock ECU", "MOCK_ECU_A", 0x7E0, 0x7E8, p2, Math.Max(p2, 1000));

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
    public async Task ReadVinUsesMockAndProducesActualIsoTpFrames()
    {
        await using var workspace = new UdsDiagnosticWorkspace();
        var result = await workspace.SendMockAsync(Profile(),"22 F1 90");
        Assert.Equal("Mock",result.Source);
        Assert.Equal("Accepted",result.Outcome);
        Assert.StartsWith("62 F1 90",result.ResponseHex);
        Assert.Contains(result.Frames,x=>x.Direction=="HOST TX");
        Assert.Contains(result.Frames,x=>x.Direction=="MOCK ECU TX");
        Assert.NotEmpty(result.CorrelationId.ToString());
    }

    [Fact]
    public async Task UnknownDidReturnsRealNegativeResponseNotFabricatedSuccess()
    {
        await using var workspace = new UdsDiagnosticWorkspace();
        var result = await workspace.SendMockAsync(Profile(),"22 AA BB");
        Assert.Equal("Rejected",result.Outcome);
        Assert.Equal("0x31",result.Nrc);
        Assert.Equal("7F 22 31",result.ResponseHex);
    }

    [Fact]
    public async Task PendingIsConsumedBeforePositiveResponse()
    {
        await using var workspace = new UdsDiagnosticWorkspace();
        workspace.InjectNextPending(0x22);
        var result = await workspace.SendMockAsync(Profile(),"22 F1 90");
        Assert.Equal("Accepted",result.Outcome);
        Assert.True(result.Frames.Count >= 4);
    }

    [Fact]
    public async Task BoundedTimeoutIsNotPositiveConfirmation()
    {
        await using var workspace = new UdsDiagnosticWorkspace();
        workspace.SetNoResponse(true);
        var result = await workspace.SendMockAsync(Profile(25),"22 F1 90");
        Assert.Equal("Uncertain",result.Outcome);
        Assert.Equal("NO_CONFIRMED_RESPONSE",result.ErrorCode);
        Assert.Empty(result.ResponseHex);
    }

    [Fact]
    public async Task ExpectedResponseMismatchIsExplicit()
    {
        await using var workspace = new UdsDiagnosticWorkspace();
        var result = await workspace.SendMockAsync(Profile(),"22 F1 90","62 F1 90 00");
        Assert.False(result.ExpectedMatched);
        Assert.Equal("Accepted",result.Outcome);
    }

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

    [Fact]
    public async Task MockPendingPreservesCallerCorrelationAndOrderedResponseSequence()
    {
        await using var workspace=new UdsDiagnosticWorkspace();
        var id=Guid.NewGuid();
        workspace.InjectNextPending(0x22);
        var exchange=await workspace.SendMockAsync(Profile(),"22 F1 90","",
            CancellationToken.None,id);
        Assert.Equal(id,exchange.CorrelationId);
        Assert.Contains("7F 22 78",exchange.ResponseSequence);
        Assert.StartsWith("62 F1 90",exchange.ResponseSequence.Last());
        Assert.Equal("Accepted",exchange.Outcome);
    }

    [Fact]
    public void ReplayPreservesCallerCorrelationAndPendingWithoutPhysicalTx()
    {
        var replay=new UdsReplayWorkspace();
        replay.Load(new UdsReplayDocument(1,[
            new UdsReplayRecord("22 F1 90",["7F 22 78","62 F1 90 4D 4F 43 4B"],
                [new UdsCanTraceRow("RX","03 7F 22 78")])]));
        var id=Guid.NewGuid();
        var exchange=replay.Consume("22 F1 90","",id);
        Assert.Equal(id,exchange.CorrelationId);
        Assert.Equal(new[]{"7F 22 78","62 F1 90 4D 4F 43 4B"},exchange.ResponseSequence);
        Assert.Equal("Replay",exchange.Source);
    }

    [Fact]
    public void SensitiveReplayMismatchNeverEchoesExpectedSeedOrKey()
    {
        var replay=new UdsReplayWorkspace();
        replay.Load(new UdsReplayDocument(1,[
            new UdsReplayRecord("27 02 11 22 33 44",["7F 27 35"],[])]));
        var error=Assert.Throws<InvalidOperationException>(()=>replay.Consume("27 02 AA BB CC DD"));
        Assert.Contains("REPLAY_REQUEST_MISMATCH",error.Message);
        Assert.Contains("redacted",error.Message);
        Assert.DoesNotContain("11 22 33 44",error.Message);
        Assert.Equal(0,replay.Position);
    }

    [Fact]
    public async Task MockTimeoutProvidesTransportDiagnosticWithoutImpersonatingEcuReply()
    {
        await using var workspace=new UdsDiagnosticWorkspace();
        workspace.SetNoResponse(true);
        var exchange=await workspace.SendMockAsync(Profile(25),"22 F1 90");
        Assert.Equal("Uncertain",exchange.Outcome);
        Assert.Equal("ISOTP_TIMEOUT",exchange.FailureLayer);
        Assert.Contains("ISO-TP frame/flow-control timeout",exchange.DiagnosticDetail);
        Assert.Empty(exchange.ResponseSequence);
    }

    [Fact]
    public void ReplayNoFinalResponseHasReplayOnlyFailureLayer()
    {
        var replay=new UdsReplayWorkspace();
        replay.Load(new UdsReplayDocument(1,[
            new UdsReplayRecord("22 F1 90",["7F 22 78"],[])]));
        var result=replay.Consume("22 F1 90");
        Assert.Equal("Uncertain",result.Outcome);
        Assert.Equal("REPLAY_NO_FINAL_RESPONSE",result.FailureLayer);
        Assert.Contains("not a live ECU timeout",result.DiagnosticDetail);
    }

    [Fact]
    public async Task BoundedMockCaptureRejectsSafelyUntilExplicitReset()
    {
        await using var workspace=new UdsDiagnosticWorkspace("MOCK_ECU_A",
            maxCapturedExchanges:2);
        await workspace.SendMockAsync(Profile(),"3E 00");
        await workspace.SendMockAsync(Profile(),"3E 00");
        Assert.Equal(2,workspace.CapturedExchanges);
        var ex=await Assert.ThrowsAsync<InvalidOperationException>(
            async ()=>await workspace.SendMockAsync(Profile(),"3E 00"));
        Assert.Contains("MOCK_CAPTURE_CAPACITY_REACHED",ex.Message);
        Assert.Equal(2,workspace.CapturedExchanges);
        await workspace.ResetMockAsync("MOCK_ECU_A");
        Assert.Equal(0,workspace.CapturedExchanges);
        var result=await workspace.SendMockAsync(Profile(),"3E 00");
        Assert.Equal("Accepted",result.Outcome);
        Assert.Equal(1,workspace.CapturedExchanges);
    }

    [Theory]
    [InlineData("7F 22 31", "62 F1 90 41")]
    [InlineData("62 F1 90 41", "7F 22 78")]
    public void ReplayRejectsResponsesAfterTerminalUdsReply(string first, string second)
    {
        var replay = new UdsReplayWorkspace();
        var doc = new UdsReplayDocument(1,[
            new UdsReplayRecord("22 F1 90",[first,second],[])]);
        Assert.Throws<InvalidDataException>(() => replay.Load(doc));
        Assert.Equal(0,replay.Count);
    }

    [Fact]
    public void ReplayRejectsMorePendingRepliesThanMockClientCanConsume()
    {
        var replay = new UdsReplayWorkspace();
        var doc=new UdsReplayDocument(1,[
            new UdsReplayRecord("22 F1 90",
                Enumerable.Repeat("7F 22 78",9).Append("62 F1 90 41").ToList(),[])]);
        var error=Assert.Throws<InvalidDataException>(()=>replay.Load(doc));
        Assert.Contains("pending limit",error.Message);
    }

    [Fact]
    public void ReplayStillAllowsNoReplyAsUncertain()
    {
        var replay = new UdsReplayWorkspace();
        replay.Load(new UdsReplayDocument(1,[
            new UdsReplayRecord("22 F1 90",[],[])]));
        var exchange=replay.Consume("22 F1 90");
        Assert.Equal("Uncertain",exchange.Outcome);
        Assert.Equal("REPLAY_NO_FINAL_RESPONSE",exchange.FailureLayer);
    }

    [Fact]
    public async Task UncertainMockReplyQuarantinesChannelUntilExplicitReset()
    {
        await using var workspace=new UdsDiagnosticWorkspace();
        workspace.SetNoResponse(true);
        var timedOut=await workspace.SendMockAsync(Profile(25),"22 F1 90");
        Assert.Equal("Uncertain",timedOut.Outcome);
        Assert.True(workspace.RequiresReset);
        var prior=workspace.CapturedExchanges;
        var blocked=await Assert.ThrowsAsync<InvalidOperationException>(
            async()=>await workspace.SendMockAsync(Profile(),"3E 00"));
        Assert.Contains("MOCK_CHANNEL_UNCERTAIN_RESET_REQUIRED",blocked.Message);
        Assert.Equal(prior,workspace.CapturedExchanges);
        await workspace.ResetMockAsync("MOCK_ECU_A");
        Assert.False(workspace.RequiresReset);
        var accepted=await workspace.SendMockAsync(Profile(),"3E 00");
        Assert.Equal("Accepted",accepted.Outcome);
    }

    [Fact]
    public async Task CancelledMockRequestRequiresResetToAvoidStaleReplyCorrelation()
    {
        await using var workspace=new UdsDiagnosticWorkspace();
        workspace.SetNoResponse(true);
        using var cancel=new CancellationTokenSource();
        cancel.CancelAfter(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async()=>await workspace.SendMockAsync(Profile(p2:1500),"22 F1 90",
                ct:cancel.Token));
        Assert.True(workspace.RequiresReset);
        await workspace.ResetMockAsync("MOCK_ECU_A");
        Assert.False(workspace.RequiresReset);
    }
    [Fact]
    public void ReplayDoesNotTrustMutableDocumentAfterItHasBeenValidated()
    {
        var originalResponses=new List<string>{"62 F1 90 41"};
        var originalFrames=new List<UdsCanTraceRow>{
            new("RX","04 62 F1 90 41") };
        var document=new UdsReplayDocument(1,[
            new UdsReplayRecord("22 F1 90",originalResponses,originalFrames)]);
        var replay=new UdsReplayWorkspace();
        replay.Load(document);
        originalResponses[0]="7F 22 31";
        originalFrames[0]=new UdsCanTraceRow("RX","03 7F 22 31");
        document.Exchanges[0]=new UdsReplayRecord("22 AA BB",[],[]);

        var exchange=replay.Consume("22 F1 90");
        Assert.Equal("Accepted",exchange.Outcome);
        Assert.Equal("62 F1 90 41",exchange.ResponseHex);
        Assert.Equal("04 62 F1 90 41",Assert.Single(exchange.Frames).Hex);
        Assert.Equal(1,replay.Position);
    }

}
