"""Bounded, revision-controlled candidate evidence and remediation linkage.

No approvals, no Codex tasks, no automatic plans. Caller owns CAS persistence.
"""
from __future__ import annotations
import hashlib
import json
import re
from context_assessment import audit, AssessmentError

ID = re.compile(r"^[A-Za-z0-9][A-Za-z0-9_.-]{0,99}$")
KINDS = {"RESEARCH", "REQUIREMENTS", "DESIGN", "VERIFY", "COORDINATE"}
SUITABLE = {
    "RESEARCH_SOURCE": {"RESEARCH"},
    "REVIEW_EXISTING_ARTIFACT": {"RESEARCH", "VERIFY", "COORDINATE"},
    "RECONCILE_HISTORY": {"RESEARCH", "COORDINATE"},
    "DRAFT_MISSING_ARTIFACT": {"RESEARCH", "REQUIREMENTS", "DESIGN", "COORDINATE"},
    "VERIFY_REALITY": {"VERIFY", "RESEARCH"},
    "OWNER_DECISION": {"COORDINATE"},
}

class ContextWorkflowError(ValueError):
    pass

def required(value, label, limit=3000):
    if not isinstance(value, str) or not value.strip() or len(value) > limit:
        raise ContextWorkflowError("invalid " + label)
    return value.strip()

def identity(value, label):
    value = required(value, label, 100)
    if not ID.fullmatch(value):
        raise ContextWorkflowError("invalid " + label)
    return value

def digest(obj):
    raw = json.dumps(obj, ensure_ascii=False, sort_keys=True, separators=(",", ":"))
    return hashlib.sha256(raw.encode("utf-8")).hexdigest()

def check_collections(record):
    reviews = record.get("context_reviews", [])
    links = record.get("context_action_links", [])
    if not isinstance(reviews, list) or not isinstance(links, list):
        raise ContextWorkflowError("invalid context review collections")
    if len(reviews) > 32 or len(links) > 256:
        raise ContextWorkflowError("context history needs archival review")
    if len({v.get("id") for v in reviews if isinstance(v, dict)}) != len(reviews):
        raise ContextWorkflowError("duplicate context review")
    if any(not isinstance(v, dict) or not isinstance(v.get("assessment"), dict)
           or not isinstance(v.get("digest"), str)
           or digest(v["assessment"]) != v["digest"] for v in reviews):
        raise ContextWorkflowError("context review digest mismatch")
    if any(not isinstance(v, dict) or v.get("review_id") not in {x["id"] for x in reviews}
           or not isinstance(v.get("work_id"), str) for v in links):
        raise ContextWorkflowError("invalid context work link")
    return reviews, links

def current_view(record):
    reviews, links = check_collections(record)
    if not reviews:
        return None
    v = reviews[-1]
    checked = audit(v["assessment"])
    return {"status": "CANDIDATE_STRUCTURAL_REVIEW",
            "review_id": v["id"], "digest": v["digest"],
            "audit": checked,
            "proposed_actions": [
                {k: a[k] for k in ("id", "topic", "kind", "reason", "expected_output")}
                for a in v["assessment"]["proposed_actions"][:40]],
            "action_links": [
                {"action_id": x["action_id"], "work_id": x["work_id"],
                 "status": x["status"], "review_id": x["review_id"]}
                for x in links if x["review_id"] == v["id"]],
            "note": "候选证据审查，非正式批准。补全工作仅是 PROPOSED，未经授权不得实施。"}

def apply(record, operation, payload, *, project_id, repo_url, recorded_at):
    if not isinstance(payload, dict):
        raise ContextWorkflowError("invalid payload")
    reviews, links = check_collections(record)
    if operation == "record_context_assessment":
        assessment = payload.get("assessment")
        if not isinstance(assessment, dict):
            raise ContextWorkflowError("assessment JSON object required")
        if assessment.get("project_ref") not in (project_id, repo_url):
            raise ContextWorkflowError("project identity mismatch")
        if len(json.dumps(assessment, ensure_ascii=False)) > 48000:
            raise ContextWorkflowError("assessment too large for bounded transaction")
        try:
            audit(assessment)
        except AssessmentError as exc:
            raise ContextWorkflowError("invalid source/claim assessment: " + str(exc)) from exc
        sha = digest(assessment)
        if any(x["digest"] == sha for x in reviews):
            raise ContextWorkflowError("assessment already recorded; no silent duplicate")
        if len(reviews) >= 32:
            raise ContextWorkflowError("review history full; archive explicitly")
        review_id = "CTX-" + sha[:16]
        if any(v["id"] == review_id for v in reviews):
            raise ContextWorkflowError("review identifier collision")
        reviews.append({"id": review_id, "digest": sha, "assessment": assessment,
                        "recorded_at": recorded_at,
                        "state": "CANDIDATE_NOT_APPROVED"})
        affected = set()
        for work in record.get("work", []):
            if work.get("context_review_id") and work.get("context_review_id") != review_id:
                work["context_evidence_status"] = "STALE_AFTER_NEW_ASSESSMENT"
                affected.add(work["id"])
        for plan in record.get("plans", []):
            if any(w in affected for w in plan.get("work_ids", [])):
                if plan.get("state") in ("SUBMITTED_FOR_HUMAN_REVIEW",
                                         "REVIEWED_PENDING_EXTERNAL_APPROVAL"):
                    plan["state"] = "REASSESSMENT_REQUIRED"
                    plan["reason"] = "Upstream evidence review changed; resubmit scoped plan"
        for gate in record.get("gate_reviews", []):
            if any(w in affected for w in gate.get("work_ids", [])):
                gate["context_source_warning"] = "EVIDENCE_REASSESSMENT_REQUIRED"
        record["context_reviews"] = reviews
        record["context_action_links"] = links
        return {"review_id": review_id, "status": "CANDIDATE_NOT_APPROVED"}
    if operation == "propose_context_remediation_work":
        if not reviews:
            raise ContextWorkflowError("no assessed source record")
        review = reviews[-1]
        if payload.get("review_id") != review["id"]:
            raise ContextWorkflowError("stale assessment, reload before linking work")
        action_id = identity(payload.get("action_id"), "action_id")
        actions = [a for a in review["assessment"]["proposed_actions"] if a["id"] == action_id]
        if len(actions) != 1:
            raise ContextWorkflowError("unknown remediation action")
        action = actions[0]
        if any(link["review_id"] == review["id"] and link["action_id"] == action_id
               for link in links):
            raise ContextWorkflowError("remediation already linked to a work item")
        work_id = identity(payload.get("work_id"), "work_id")
        if any(w["id"] == work_id for w in record.get("work", [])):
            raise ContextWorkflowError("duplicate work id")
        deliverable_id = identity(payload.get("deliverable_id"), "deliverable_id")
        if not any(d["id"] == deliverable_id for d in record.get("deliverables", [])):
            raise ContextWorkflowError("remediation requires existing project deliverable")
        kind = payload.get("kind")
        if kind not in KINDS or kind not in SUITABLE[action["kind"]]:
            raise ContextWorkflowError("work kind incompatible with remediation; review specialization")
        work = {"id": work_id, "deliverable_id": deliverable_id,
                "kind": kind, "title": required(payload.get("title"), "title", 240),
                "expected_result": required(payload.get("expected_result"), "expected_result"),
                "state": "PROPOSED", "execution_authorized": False, "depends_on": [],
                "context_review_id": review["id"], "context_action_id": action_id,
                "context_evidence_status": "CURRENT_CANDIDATE"}
        record.setdefault("work", []).append(work)
        links.append({"review_id": review["id"], "action_id": action_id, "work_id": work_id,
                      "status": "PROPOSED_NOT_AUTHORIZED", "recorded_at": recorded_at})
        record["context_action_links"] = links
        return {"work_id": work_id, "status": "PROPOSED_NOT_AUTHORIZED"}
    raise ContextWorkflowError("unsupported context operation")
