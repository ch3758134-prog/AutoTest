using System.Collections.Immutable;
using System.Text;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Specialists.Flash;

namespace AutoTest.Application.G4;

public sealed record MockFlashOutcome(string Code, ProgrammingJobSnapshot? Job,
    ImmutableArray<string> Blockers, bool IsMock, bool PhysicalWriteAllowed);

/// <summary>Uses shipped ProgrammingJobCoordinator and MockBootloader. Never holds a CAN/USB handle.</summary>
public sealed class MockFlashWorkbench
{
    private readonly StationId _station = StationId.Create("G4-MOCK-STATION");
    private sealed class SnapshotProvider(ProgrammingAssets assets) : IProgrammingContextProvider
    {
        public ValueTask<ProgrammingAssets> ResolveAsync(ProgrammingRequest request, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); return ValueTask.FromResult(assets); }
    }

    public async Task<MockFlashOutcome> ExecuteBinAsync(byte[] imageBytes,
        string expectedModel = "MODEL-A", string observedModel = "MODEL-A",
        bool injectDisconnect = false, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        // User-supplied image bytes are parsed/hashed and locked into a MockOnly profile.
        var image = FirmwareImageParser.ParseBin(imageBytes, 0x1000);
        var endpoint = new UdsEndpointProfile(BusRole.DutCan, 0x7E0, 0x7E8,
            TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(600), "Normal");
        var revision = RevisionFingerprint.FromBytes(Encoding.UTF8.GetBytes("G4-MOCK-PROGRAMMING-PROFILE"));
        var profile = new ProgrammingProfile("MODEL-A", expectedModel, endpoint,
            ImmutableArray.Create(new FlashAddressRange(0x1000, 0x11000)),
            23, 11.5, 14.8, revision, true);
        var bootloader = new MockBootloader(observedModel, 0x1000, 0x10000, 17);
        if (injectDisconnect) bootloader.DisconnectBeforeBlock = 1;
        var assets = new ProgrammingAssets(profile, image,
            new ProgrammingEnvironment(_station, observedModel, 12.5, true, true));
        var coordinator = new ProgrammingJobCoordinator(new SnapshotProvider(assets),
            new MockProgrammingWorkflow(bootloader));
        var request = new ProgrammingRequest(_station, "MODEL-A",
            new AssetRef(AssetId.New(), AssetRevision.Create(1), image.SourceFingerprint), revision);
        var preflight = await coordinator.PreflightAsync(request, ct).ConfigureAwait(false);
        if (!preflight.Ready)
            return new("MOCK_FLASH_PREFLIGHT_BLOCKED", null, preflight.Blockers, true, false);
        var plan = await coordinator.BuildPlanAsync(request, ct).ConfigureAwait(false);
        var permission = new SafetyAuthorization(new Authorization(AuthorizationScope.MockOnly,
            _station, 1, DateTimeOffset.UtcNow.AddMinutes(1), "V0_MOCK_ISSUER"),
            "MOCK_FLASH", "G4_LOCAL_SIMULATION");
        var id = await coordinator.ExecuteAsync(plan, permission, ct).ConfigureAwait(false);
        var result = await coordinator.WaitForCompletionAsync(id, ct).ConfigureAwait(false);
        return new(result.State == ProgrammingJobState.Completed ? "MOCK_FLASH_VERIFIED_ONLY" :
            "MOCK_FLASH_" + result.State.ToString().ToUpperInvariant(), result,
            ImmutableArray<string>.Empty, true, false);
    }

    public Task<MockFlashOutcome> DemoAsync(CancellationToken ct = default) =>
        ExecuteBinAsync(Enumerable.Range(0, 72).Select(x => (byte)(x * 7)).ToArray(), ct: ct);
}
