using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Specialists.Uds;

namespace AutoTest.Specialists.Flash.Workbench;

public sealed record FlashDryRunConfiguration(string Target, string ExpectedIdentity,
    string ObservedMockIdentity, uint AllowedStart, ulong AllowedEndExclusive,
    double SimulatedVoltage, double MinVoltage, double MaxVoltage,
    int ChunkSize = 64, uint UdsRequestId = 0x7E0, uint UdsResponseId = 0x7E8);
public sealed record FlashSegmentDetail(string Start, string EndExclusive, int Bytes);
public sealed record FlashImageDetail(string FileName, string Format, string Sha256,
    long Bytes, IReadOnlyList<FlashSegmentDetail> Segments);
public sealed record FlashPreflightDetail(bool Ready, string Source,
    IReadOnlyList<string> Blockers, FlashImageDetail? Image);

// A local, mock-only programming engine. Never receives a CAN hub, USB handle,
// TX authorization or physical programming profile.
public sealed class FlashDryRunWorkspace
{
    public const int MaxInteractiveMockBytes = 1024 * 1024;
    private readonly object _sync = new();
    private FirmwareImage? _image;
    private string _name = "";
    private ProgrammingJobCoordinator? _coordinator;
    private ProgrammingJobId? _jobId;
    private FlashImageDetail? _jobImageSnapshot;
    private FlashDryRunConfiguration? _jobConfigSnapshot;
    private DateTimeOffset? _jobInputCapturedUtc;
    public FlashImageDetail? Image => _image is null ? null : Describe(_image, _name);
    public ProgrammingJobId? CurrentJob => _jobId;
    public FlashImageDetail? LastJobImage => _jobImageSnapshot;

    public FlashImageDetail LoadImage(string fileName, ReadOnlySpan<byte> content, uint binAddress)
    {
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("Filename required.");
        if (content.Length == 0 || content.Length > FirmwareImageParser.MaxImageBytes)
            throw new FirmwareFormatException("Empty/oversized image.");
        if (CurrentJob is { } id && _coordinator is not null &&
            _coordinator.Observe(id).State is ProgrammingJobState.Queued or ProgrammingJobState.Running)
            throw new InvalidOperationException("Cannot replace image during a dry-run.");
        string suffix = Path.GetExtension(fileName).ToLowerInvariant();
        FirmwareImage parsed = suffix switch
        {
            ".bin" => FirmwareImageParser.ParseBin(content, binAddress),
            ".hex" or ".ihex" => FirmwareImageParser.ParseHex(Encoding.UTF8.GetString(content)),
            ".s19" or ".s28" or ".s37" or ".srec" or ".mot" =>
                FirmwareImageParser.ParseS19(Encoding.UTF8.GetString(content)),
            _ => throw new FirmwareFormatException("Only BIN / Intel HEX / S-record images are supported.")
        };
        lock (_sync) { _image = parsed; _name = Path.GetFileName(fileName); }
        return Describe(parsed, _name);
    }
    private static FlashImageDetail Describe(FirmwareImage image, string fileName) =>
        new(fileName, image.Format, image.SourceFingerprint.Sha256, image.ByteCount,
            image.Segments.Select(x => new FlashSegmentDetail($"0x{x.Address:X8}",
                $"0x{x.EndExclusive:X8}", x.Data.Length)).ToArray());

    private static ProgrammingProfile CreateProfile(FlashDryRunConfiguration config)
    {
        var endpoint = new UdsEndpointProfile(BusRole.DutCan, config.UdsRequestId,
            config.UdsResponseId, TimeSpan.FromMilliseconds(1000),
            TimeSpan.FromMilliseconds(5000), "Normal");
        string canonical = string.Join("|", config.Target, config.ExpectedIdentity,
            config.AllowedStart.ToString("X8", CultureInfo.InvariantCulture),
            config.AllowedEndExclusive.ToString("X", CultureInfo.InvariantCulture),
            config.MinVoltage.ToString("R", CultureInfo.InvariantCulture),
            config.MaxVoltage.ToString("R", CultureInfo.InvariantCulture), config.ChunkSize);
        return new ProgrammingProfile(config.Target, config.ExpectedIdentity, endpoint,
            ImmutableArray.Create(new FlashAddressRange(config.AllowedStart, config.AllowedEndExclusive)),
            config.ChunkSize, config.MinVoltage, config.MaxVoltage,
            RevisionFingerprint.FromBytes(Encoding.UTF8.GetBytes(canonical)), true);
    }
    private sealed class SnapshotProvider(ProgrammingAssets assets) : IProgrammingContextProvider
    {
        public ValueTask<ProgrammingAssets> ResolveAsync(ProgrammingRequest request, CancellationToken ct) =>
            ValueTask.FromResult(assets);
    }
    private static (ProgrammingRequest Request, ProgrammingAssets Assets, SessionInfo Session)
        Context(FirmwareImage image, FlashDryRunConfiguration config)
    {
        var station = StationId.Create("MOCK_FLASH_WORKBENCH");
        var profile = CreateProfile(config);
        var environment = new ProgrammingEnvironment(station, config.ObservedMockIdentity,
            config.SimulatedVoltage, Connected: true, IsMock: true, SessionGeneration: 1);
        var asset = new AssetRef(AssetId.New(), AssetRevision.Create(1), image.SourceFingerprint);
        var request = new ProgrammingRequest(station, config.Target, asset, profile.Revision);
        var session = new SessionInfo(station, 1, "Mock", config.ObservedMockIdentity,
            "MOCK_BOOTLOADER_TEST_ONLY", StationConnectionState.Ready);
        return (request, new ProgrammingAssets(profile, image, environment), session);
    }
    public async ValueTask<FlashPreflightDetail> PreflightAsync(FlashDryRunConfiguration config,
        CancellationToken ct = default)
    {
        FirmwareImage? image = _image;
        if (image is null) return new(false, "Mock", new[] { "IMAGE_NOT_SELECTED" }, null);
        try
        {
            var (request, assets, _) = Context(image, config);
            var preflight = new ProgrammingPreflight(new SnapshotProvider(assets));
            var check = await preflight.CheckAsync(request, ct).ConfigureAwait(false);
            return new(check.Ready, "Mock", check.Blockers.ToArray(), Image);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or
            NotSupportedException or OverflowException)
        {
            return new(false, "Mock", new[] { "INVALID_CONFIGURATION:" + ex.GetType().Name }, Image);
        }
    }
    public async ValueTask<ProgrammingJobId> StartDryRunAsync(FlashDryRunConfiguration config,
        byte? faultNrcSid = null, byte? faultNrc = null, int dropBeforeBlock = -1,
        CancellationToken ct = default)
    {
        FirmwareImage image;
        string imageName;
        lock (_sync)
        {
            image = _image ?? throw new ProgrammingRejectedException("IMAGE_NOT_SELECTED");
            imageName = _name;
        }
        if (image.ByteCount > MaxInteractiveMockBytes)
            throw new ProgrammingRejectedException("INTERACTIVE_MOCK_IMAGE_LIMIT_1MIB");
        if (_jobId is { } previous && _coordinator is not null &&
            _coordinator.Observe(previous).State is ProgrammingJobState.Queued or ProgrammingJobState.Running)
            throw new ProgrammingRejectedException("FLASH_WORKBENCH_BUSY");
        var (request, assets, session) = Context(image, config);
        var boot = new MockBootloader(config.ObservedMockIdentity,
            config.AllowedStart, checked((uint)(config.AllowedEndExclusive - config.AllowedStart)),
            Math.Max(16, config.ChunkSize));
        if (faultNrcSid.HasValue != faultNrc.HasValue)
            throw new ArgumentException("Fault NRC requires both SID and code.");
        if (faultNrcSid.HasValue) boot.SimulateNrc(faultNrcSid.Value, faultNrc!.Value);
        if (dropBeforeBlock > 0) boot.DisconnectBeforeBlock = dropBeforeBlock;
        var workflow = new MockProgrammingWorkflow(boot);
        var coordinator = new ProgrammingJobCoordinator(new SnapshotProvider(assets), workflow);
        var check = await coordinator.PreflightAsync(request, ct).ConfigureAwait(false);
        ProgrammingPreflight.RejectMismatch(check);
        var plan = await coordinator.BuildPlanAsync(request, ct).ConfigureAwait(false);
        var auth = new SafetyAuthorization(Authorization.MockOnly(session, DateTimeOffset.UtcNow),
            "MOCK_FLASH", "FLASH_WORKBENCH_MOCK_ONLY");
        // Capture the input identity before the job is dispatched, not after
        // an exceptionally fast Mock job has already finished.
        var capturedUtc = DateTimeOffset.UtcNow;
        var capturedImage = Describe(image,imageName);
        var id = await coordinator.ExecuteAsync(plan, auth, ct).ConfigureAwait(false);
        lock (_sync)
        {
            _coordinator = coordinator;
            _jobId = id;
            // Immutable job-input snapshot: later image import must never
            // rewrite the evidence identity of an already executed job.
            _jobImageSnapshot = capturedImage;
            _jobConfigSnapshot = config;
            _jobInputCapturedUtc = capturedUtc;
        }
        return id;
    }
    public ProgrammingJobSnapshot Observe(ProgrammingJobId job)
    {
        var coordinator = _coordinator ?? throw new InvalidOperationException("No dry-run job.");
        if (_jobId != job) throw new ArgumentException("Unknown job ID.");
        return coordinator.Observe(job);
    }
    public bool Cancel(ProgrammingJobId job) => _coordinator is not null &&
        _jobId == job && _coordinator.RequestCancel(job);
    public async Task ExportMockAuditAsync(string path, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var id=_jobId ?? throw new InvalidOperationException("No mock job to export.");
        var snapshot=Observe(id);
        if (snapshot.State is ProgrammingJobState.Running or ProgrammingJobState.Queued)
            throw new InvalidOperationException("Cannot export incomplete job as final evidence.");
        var ackFacts=snapshot.Facts.Where(x=>x.Stage=="Transfer" && x.Status=="ACK")
            .ToArray();
        var lastAck=ackFacts.LastOrDefault();
        var verifyCount=snapshot.Facts.Count(x=>x.Stage=="VerifyMock" && x.Status=="PASS");
        FlashImageDetail? jobImage;
        FlashDryRunConfiguration? jobConfig;
        DateTimeOffset? captured;
        lock (_sync)
        {
            jobImage=_jobImageSnapshot;
            jobConfig=_jobConfigSnapshot;
            captured=_jobInputCapturedUtc;
        }
        if (jobImage is null || jobConfig is null || captured is null)
            throw new InvalidOperationException("Job input evidence is unavailable.");
        var expectedVerifyCount=jobImage.Segments.Count;
        var segmentStates=snapshot.Facts
            .Where(x=>x.Stage.StartsWith("Segment_",StringComparison.Ordinal))
            .GroupBy(x=>x.Stage,StringComparer.Ordinal)
            .ToDictionary(g=>g.Key,g=>g.Select(x=>x.Status).ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);
        var segmentEvidence=jobImage.Segments.Select((segment,index)=>
        {
            string name=$"Segment_{index+1:D4}";
            segmentStates.TryGetValue(name,out var states);
            return new {
                Index=index+1, segment.Start, segment.EndExclusive, segment.Bytes,
                Started=states?.Contains("BEGIN")==true,
                TransferAcknowledged=states?.Contains("TRANSFER_ACKED")==true,
                MockHashVerified=states?.Contains("VERIFIED")==true
            };
        }).ToArray();
        bool mockVerified=snapshot.State==ProgrammingJobState.Completed &&
            verifyCount==expectedVerifyCount && expectedVerifyCount>0 &&
            segmentEvidence.All(x=>x.MockHashVerified);
        var payload=new
        {
            Schema=2, Source="Mock", PhysicalProgrammingVerified=false,
            RealEcuIdentityObserved=false, PhysicalTransportUsed=false,
            Job=snapshot.Job, State=snapshot.State.ToString(), SnapshotUtc=DateTimeOffset.UtcNow,
            JobInputCapturedUtc=captured, Image=jobImage,
            MockConfiguration=jobConfig, FailureCode=snapshot.FailureCode,
            AcknowledgedTransferBytes=lastAck?.ConfirmedBytes??0,
            LastAcknowledgedTransfer=lastAck is null ? null : new {
                Utc=lastAck.Utc, Detail=lastAck.Detail, ConfirmedBytes=lastAck.ConfirmedBytes },
            Verification="MOCK_ONLY",
            MockHashVerified=mockVerified,
            ExpectedSegmentVerifications=expectedVerifyCount,
            ActualSegmentVerifications=verifyCount,
            SegmentEvidence=segmentEvidence,
            FullBackendFactsIncluded=true, Stages=snapshot.Facts
        };
        byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(payload,
            new JsonSerializerOptions { WriteIndented=true });
        string absolute=Path.GetFullPath(path);
        string temp=absolute+"."+Guid.NewGuid().ToString("N")+".tmp";
        try { await File.WriteAllBytesAsync(temp,bytes,ct).ConfigureAwait(false);
            File.Move(temp,absolute,true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public async ValueTask<ProgrammingJobSnapshot> WaitAsync(ProgrammingJobId job,
        CancellationToken ct = default)
    {
        var coordinator = _coordinator ?? throw new InvalidOperationException("No dry-run job.");
        if (_jobId != job) throw new ArgumentException("Unknown job ID.");
        return await coordinator.WaitForCompletionAsync(job, ct).ConfigureAwait(false);
    }
}
