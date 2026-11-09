using System.Collections.Concurrent;
using AutoTest.Contracts;
using AutoTest.Core;

namespace AutoTest.Specialists.Flash;

// V0 contract returns JobId; Observe/Wait/Cancel are module-local methods
// pending INTEGRATION's public job-state/record contract decision.
public sealed class ProgrammingJobCoordinator : IProgrammingService
{
    private sealed class Job(ProgrammingEvidence evidence,CancellationTokenSource stop)
    {
        public ProgrammingEvidence Evidence { get; } = evidence;
        public CancellationTokenSource Stop { get; } = stop;
        public Task Completion { get; set; } = Task.CompletedTask;
    }
    private readonly IProgrammingContextProvider _provider;
    private readonly ProgrammingPreflight _preflight;
    private readonly MockProgrammingWorkflow _workflow;
    private readonly ConcurrentDictionary<Guid,Job> _jobs = new();
    private readonly HashSet<StationId> _activeStations = [];
    private readonly object _sync = new();
    public ProgrammingJobCoordinator(IProgrammingContextProvider provider,MockProgrammingWorkflow workflow)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        _preflight = new ProgrammingPreflight(provider);
    }
    public ValueTask<ProgrammingReadiness> PreflightAsync(ProgrammingRequest request,CancellationToken ct)
        => _preflight.CheckAsync(request,ct);
    public async ValueTask<ProgrammingPlan> BuildPlanAsync(ProgrammingRequest request,CancellationToken ct)
    {
        ProgrammingPreflight.RejectMismatch(await _preflight.CheckAsync(request,ct).ConfigureAwait(false));
        return _workflow.BuildPlan(request);
    }
    public async ValueTask<ProgrammingJobId> ExecuteAsync(ProgrammingPlan plan,
        SafetyAuthorization authorization,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(authorization);
        var auth=authorization.Authorization;
        if (auth.Scope != AuthorizationScope.MockOnly || auth.Issuer != "V0_MOCK_ISSUER"
            || auth.Station != plan.Request.Station || auth.ExpiresUtc <= DateTimeOffset.UtcNow
            || authorization.ApprovedScope != "MOCK_FLASH"
            || string.IsNullOrWhiteSpace(authorization.ApprovalIdentity))
            throw new ProgrammingRejectedException("Only bounded V0 MockOnly FLASH authorization is accepted.");
        ProgrammingPreflight.RejectMismatch(await _preflight.CheckAsync(plan.Request,ct).ConfigureAwait(false));
        var expected = _workflow.BuildPlan(plan.Request);
        if (!expected.PlanFingerprint.Matches(plan.PlanFingerprint)
            || !expected.LockedStages.SequenceEqual(plan.LockedStages))
            throw new ProgrammingRejectedException("Programming plan fingerprint/stages mismatch.");
        var assets=await _provider.ResolveAsync(plan.Request,ct).ConfigureAwait(false);
        // Re-evaluate fully against the exact snapshot used for this job.
        if (!assets.Environment.IsMock || assets.Environment.Station != auth.Station
            || assets.Environment.SessionGeneration != auth.Generation
            || !assets.Profile.Revision.Matches(plan.Request.ProfileRevision)
            || !assets.Image.SourceFingerprint.Matches(plan.Request.Image.Fingerprint)
            || !string.Equals(assets.Profile.Target,plan.Request.Target,StringComparison.Ordinal)
            || !string.Equals(assets.Profile.ExpectedDeviceIdentity,assets.Environment.DeviceIdentity,StringComparison.Ordinal)
            || !assets.Environment.Connected || !double.IsFinite(assets.Environment.Voltage)
            || assets.Environment.Voltage < assets.Profile.MinVoltage
            || assets.Environment.Voltage > assets.Profile.MaxVoltage)
            throw new ProgrammingRejectedException("Job snapshot changed since preflight.");
        assets.Profile.Validate(); assets.Profile.ValidateImage(assets.Image);
        var id=Guid.NewGuid(); var evidence=new ProgrammingEvidence(id);
        var job=new Job(evidence,CancellationTokenSource.CreateLinkedTokenSource(ct));
        lock(_sync)
        {
            if (!_activeStations.Add(plan.Request.Station))
            { job.Stop.Dispose(); throw new ProgrammingRejectedException("STATION_PROGRAMMING_BUSY"); }
            if (!_jobs.TryAdd(id,job)) throw new InvalidOperationException("Job ID collision.");
            // Task scheduling occurs after job publication, in the station lock.
            job.Completion=Task.Run(async () =>
            {
                evidence.Finalize(ProgrammingJobState.Running);
                evidence.RecordStage("Job","START",0,"MOCK_ONLY");
                try
                {
                    await _workflow.ExecuteAsync(plan,assets,evidence,job.Stop.Token).ConfigureAwait(false);
                    evidence.Finalize(ProgrammingJobState.Completed);
                    evidence.RecordStage("Job","PASS",assets.Image.ByteCount,"MOCK_VERIFIED_ONLY");
                }
                catch(OperationCanceledException) when(job.Stop.IsCancellationRequested)
                {
                    evidence.Finalize(ProgrammingJobState.Cancelled,"CANCELLED");
                    evidence.RecordStage("Job","CANCELLED",0,"Partial mock writes may remain; no resume assumed.");
                }
                catch(ProgrammingUncertainException ex)
                {
                    evidence.Finalize(ProgrammingJobState.Uncertain,"UNCONFIRMED_UDS_RESPONSE");
                    evidence.RecordStage("Job","UNCERTAIN",0,
                        ex.Message+"; Mock contents may be partially written; no auto-resume.");
                }
                catch(Exception ex)
                {
                    evidence.Finalize(ProgrammingJobState.Failed,ex.GetType().Name);
                    evidence.RecordStage("Job","FAIL",0,ex.GetType().Name+": "+ex.Message);
                }
                finally
                {
                    lock(_sync) _activeStations.Remove(plan.Request.Station);
                }
            },CancellationToken.None);
        }
        return new ProgrammingJobId(id);
    }
    public ProgrammingJobSnapshot Observe(ProgrammingJobId id) =>
        _jobs.TryGetValue(id.Value,out var job) ? job.Evidence.Observe()
        : throw new KeyNotFoundException("Programming job not found.");
    public bool RequestCancel(ProgrammingJobId id)
    {
        if (!_jobs.TryGetValue(id.Value,out var job)) return false;
        var status=job.Evidence.Observe().State;
        if (status is ProgrammingJobState.Completed or ProgrammingJobState.Failed or
            ProgrammingJobState.Cancelled or ProgrammingJobState.Uncertain) return false;
        job.Stop.Cancel(); return true;
    }
    public async ValueTask<ProgrammingJobSnapshot> WaitForCompletionAsync(ProgrammingJobId id,CancellationToken ct)
    {
        if (!_jobs.TryGetValue(id.Value,out var job)) throw new KeyNotFoundException("Programming job not found.");
        await job.Completion.WaitAsync(ct).ConfigureAwait(false);
        return job.Evidence.Observe();
    }
}
