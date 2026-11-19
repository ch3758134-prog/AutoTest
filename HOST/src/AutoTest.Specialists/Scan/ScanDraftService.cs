using AutoTest.Contracts;
using AutoTest.Core;

namespace AutoTest.Specialists.Scan;

public enum ScanDraftStatus { Empty, Captured, Matched, Rejected, Confirmed }
public sealed record ScanDraft(Guid DraftId, RawScan Raw, MatchDecision? Decision,
    ScanDraftStatus Status, string? Error);

// Drafts are replaceable only before confirmation; confirmed identity belongs to a run.
public sealed class ScanDraftService
{
    private readonly object _sync = new();
    private ScanDraft? _draft;
    public ScanDraft? Current { get { lock (_sync) return _draft; } }

    public ScanDraft Capture(RawScan raw)
    {
        lock (_sync)
        {
            if (_draft?.Status == ScanDraftStatus.Confirmed)
                throw new InvalidOperationException("SCAN_ALREADY_CONFIRMED");
            return _draft = new ScanDraft(Guid.NewGuid(), raw, null, ScanDraftStatus.Captured, null);
        }
    }

    public ScanDraft Evaluate(Guid draftId, IIdentityMatcher matcher,
        RuleRevision ruleRevision, ProjectRevision projectRevision)
    {
        lock (_sync)
        {
            if (_draft is null || _draft.DraftId != draftId || _draft.Status != ScanDraftStatus.Captured)
                throw new InvalidOperationException("SCAN_DRAFT_STALE");
            var decision = matcher.Match(_draft.Raw, ruleRevision, projectRevision);
            return _draft = _draft with { Decision = decision,
                Status = decision.Allowed ? ScanDraftStatus.Matched : ScanDraftStatus.Rejected,
                Error = decision.RejectionCode };
        }
    }

    public ScanDraft Confirm(Guid draftId)
    {
        lock (_sync)
        {
            if (_draft is null || _draft.DraftId != draftId ||
                _draft.Status != ScanDraftStatus.Matched || _draft.Decision?.Allowed != true)
                throw new InvalidOperationException("SCAN_NOT_MATCHED");
            return _draft = _draft with { Status = ScanDraftStatus.Confirmed };
        }
    }
    public void Discard() { lock (_sync) _draft = null; }
}
