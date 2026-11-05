using System.Collections.Immutable;
using System.Diagnostics;
using AutoTest.Application.Composition;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.TestFixtures;
using Xunit;

namespace AutoTest.Contracts.Tests;

public sealed class CoreContractTests
{
    [Fact]
    public void G3_0_T01_StrongTypesAreDistinctAtRuntime()
    {
        Assert.NotEqual(typeof(StationId), typeof(EndpointId));
        Assert.NotEqual(typeof(RunId), typeof(ProjectId));
        Assert.Throws<ArgumentException>(() => StationId.Create(" "));
        Assert.False(RunId.TryParse(Guid.Empty.ToString(), out _));
    }

    // Negative sample is an out-of-solution project. Its expected CS1503 is the evidence.
    [Fact]
    public void G3_0_T01_CompileNegative()
    {
        var root = FindRoot();
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        process.StartInfo.ArgumentList.Add("build");
        process.StartInfo.ArgumentList.Add("tests/negative/StrongIdMisuse/StrongIdMisuse.csproj");
        process.StartInfo.ArgumentList.Add("--no-restore");
        process.StartInfo.ArgumentList.Add("--nologo");
        process.StartInfo.ArgumentList.Add("-v:q");
        Assert.True(process.Start());
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(30000), "Negative compilation timed out.");
        Assert.NotEqual(0, process.ExitCode);
        Assert.Contains("CS1503", output + error);
    }

    [Fact]
    public async Task G3_0_T02_OldGenerationReplyCannotBecomeCurrent()
    {
        var station = new FakeStation();
        var stationId = StationId.Create("MOCK-1");
        var old = (await station.ConnectAsync(stationId, CancellationToken.None)).Value!;
        var pending = station.ExecuteAsync(new StationCommand(StationAction.MockProbe,
            MockScenario.DelayedResponse, Guid.NewGuid()),
            Authorization.MockOnly(old, DateTimeOffset.UtcNow), CancellationToken.None).AsTask();
        var current = (await station.ConnectAsync(stationId, CancellationToken.None)).Value!;
        Assert.NotEqual(old.Generation, current.Generation);
        var reply = await pending;
        Assert.Equal(Outcome.Rejected, reply.Outcome);
        Assert.Equal("STALE_SESSION", reply.FailureCode);
        Assert.Null(reply.Value);
    }

    [Fact]
    public async Task G3_0_T03_PhysicalActionsDeniedEvenWithMockToken()
    {
        var station = new FakeStation();
        var s = (await station.ConnectAsync(StationId.Create("SIM"), CancellationToken.None)).Value!;
        var rejected = await station.ExecuteAsync(new StationCommand(StationAction.Ignition,
            MockScenario.Normal, Guid.NewGuid()), Authorization.MockOnly(s, DateTimeOffset.UtcNow),
            CancellationToken.None);
        Assert.Equal(Outcome.Rejected, rejected.Outcome);
        Assert.Equal("PHYSICAL_OUTPUT_FORBIDDEN", rejected.FailureCode);
    }

    [Fact]
    public async Task G3_0_T03_MissingAuthorizationDeniedBeforeAnyAck()
    {
        var station = new FakeStation();
        var s = (await station.ConnectAsync(StationId.Create("SIM"), CancellationToken.None)).Value!;
        var denial = await station.ExecuteAsync(new StationCommand(StationAction.MockProbe,
            MockScenario.Normal, Guid.NewGuid()), new Authorization(AuthorizationScope.None,
            s.Station, s.Generation, DateTimeOffset.UtcNow.AddMinutes(1), "NONE"),
            CancellationToken.None);
        Assert.Equal("AUTH_MISSING", denial.FailureCode);
        Assert.Null(denial.Value);
    }

    [Fact]
    public void G3_0_T04_RunSnapshotCannotBeMutatedViaExternalArray()
    {
        var step = new PreparedStep(StepId.New(), "Wait", "fingerprint");
        var steps = ImmutableArray.Create(step);
        var plan = new PreparedPlan(TestDocumentId.New(),
            RevisionFingerprint.FromSha256(new string('c', 64)), steps,
            ImmutableArray<AssetRef>.Empty);
        var added = steps.Add(new PreparedStep(StepId.New(), "Other", "x"));
        Assert.Single(plan.Steps);
        Assert.Equal(2, added.Length);
        var bytes = new byte[] { 3, 4, 5 };
        var frame = CanFrame.Create(BusRole.DutCan, false, 0x321, bytes);
        bytes[0] = 99;
        Assert.Equal(3, frame.Data[0]);
        var locked = ImmutableArray.Create(new AssetRef(AssetId.New(), AssetRevision.Create(1),
            RevisionFingerprint.FromSha256(new string('d', 64))));
        var project = new ProjectSnapshot(ProjectId.New(), new ProjectRevision(1),
            RevisionFingerprint.FromSha256(new string('e', 64)), locked, 1);
        var station = new StationProfile(StationId.Create("SIM"),
            new BusRoleMap(RevisionFingerprint.FromSha256(new string('f', 64)), 0, 1),
            "SIM_ONLY", RevisionFingerprint.FromSha256(new string('a', 64)));
        var snapshot = new RunSnapshot(RunId.New(), project, station, plan.PlanFingerprint, locked);
        var future = locked.Add(new AssetRef(AssetId.New(), AssetRevision.Create(2),
            RevisionFingerprint.FromSha256(new string('b', 64))));
        Assert.Single(snapshot.LockedAssets);
        Assert.Equal(2, future.Length);
    }

    [Fact]
    public void G3_0_T05_ReplayClockIsNeverClaimedAsHardwareTimestamp()
    {
        var clock = new FakeClock();
        var start = TimePoint.FromReplay(clock.GetUtcNow(), clock.GetTimestamp());
        clock.Advance(TimeSpan.FromMilliseconds(25));
        var next = TimePoint.FromReplay(clock.GetUtcNow(), clock.GetTimestamp());
        Assert.Equal(TimeQuality.ReplayDerived, start.Quality);
        Assert.Equal(TimeSource.Replay, next.Source);
        Assert.Equal(25, next.MonotonicTicks - start.MonotonicTicks);
        Assert.NotEqual(TimeQuality.SynchronizedHardware, next.Quality);
    }

    [Fact]
    public void CT_01_23_PublicMethodSignaturesExist()
    {
        // Interface reflection sanity check, not a replacement for compilation and behavioral tests.
        var types = new[] { typeof(IStationSession), typeof(IStationControl), typeof(ICanFrameHub),
            typeof(IProjectService), typeof(IDbcDecoder), typeof(ITestAuthoringService),
            typeof(IPlanCompiler), typeof(IRunController), typeof(IBarcodeInput),
            typeof(IIdentityMatcher), typeof(IUdsClient), typeof(IWaveformService),
            typeof(IProgrammingService), typeof(IRunFactSink), typeof(IApplicationCommandGateway),
            typeof(IProductionService) };
        Assert.All(types, type => Assert.NotEmpty(type.GetMethods()));
        var contracted = new[] { "IStationSession.ConnectAsync", "IStationControl.ExecuteAsync",
            "ICanFrameHub.SubscribeAsync", "ICanFrameHub.TransmitAsync", "IProjectService.OpenAsync",
            "IProjectService.SaveAsync", "IDbcDecoder.Decode", "ITestAuthoringService.Apply",
            "IPlanCompiler.PrepareAsync", "IRunController.StartAsync", "IRunController.RequestStopAsync",
            "IBarcodeInput.ObserveAsync", "IIdentityMatcher.Match", "IUdsClient.RequestAsync",
            "IWaveformService.CaptureAsync", "IWaveformService.CompareAsync",
            "IProgrammingService.PreflightAsync", "IProgrammingService.ExecuteAsync",
            "IRunFactSink.AppendAsync", "IRunFactSink.CommitAsync",
            "IApplicationCommandGateway.ExecuteAsync", "IProductionService.PrepareAsync",
            "IProductionService.StartAsync" };
        Assert.Equal(23, contracted.Length);
        Assert.All(contracted, name => Assert.Contains(types,
            type => type.Name == name.Split('.')[0] &&
                type.GetMethod(name.Split('.')[1]) is not null));
    }

    private static string FindRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AutoTest.sln"))) return current.FullName;
            current = current.Parent;
        }
        throw new InvalidOperationException("Solution root not found for compile-negative test.");
    }
}
