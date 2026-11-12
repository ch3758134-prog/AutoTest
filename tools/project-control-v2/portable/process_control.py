#!/usr/bin/env python3
"""Project Control process-obligation and AI handoff candidate kernel.

A semantic process guide plus deterministic evidence/authorization boundaries.
This module deliberately NEVER approves a Gate, design, implementation or release.
Callers must persist resulting records through the target project's CAS revision layer.
"""
from __future__ import annotations
import copy
import hashlib
import json
from pathlib import Path
import re

CATALOG_PATH = Path(__file__).with_name("process_catalog.json")
SCHEMA = "project-control-process-state/1"
ID = re.compile(r"^[A-Za-z][A-Za-z0-9_.-]{1,99}$")
HEAD = re.compile(r"^[a-f0-9]{40}$")
ACTIONS = frozenset(("open_process_activity", "set_process_obligation", "review_process_activity",
                     "request_process_review", "record_ai_intake", "record_ai_work_report", "set_process_focus"))
DISPOSITIONS = frozenset(("SATISFIED_CANDIDATE", "NOT_APPLICABLE_PROPOSED",
                          "DEFERRED_PROPOSED", "EXCEPTION_REQUESTED", "OPEN", "CONFLICT"))
REVIEWS = frozenset(("ADEQUATE_CANDIDATE", "REWORK_REQUIRED", "INSUFFICIENT_EVIDENCE"))
ROLES = frozenset(("coordinator", "specialist", "integration", "reviewer", "local_executor"))

class ProcessError(ValueError):
    pass

def word(value, label, size=2000):
    if not isinstance(value, str) or not value.strip() or len(value) > size:
        raise ProcessError(label + " must be nonblank bounded text")
    return value.strip()

def identifier(value, label):
    value = word(value, label, 100)
    if not ID.fullmatch(value):
        raise ProcessError("invalid " + label)
    return value

def strings(value, label, *, required=False, maximum=32):
    if not isinstance(value, list) or len(value) > maximum or (required and not value):
        raise ProcessError("invalid " + label)
    if any(not isinstance(v, str) or not v.strip() or len(v) > 500 for v in value):
        raise ProcessError("invalid " + label)
    return [v.strip() for v in value]

def catalog():
    result = json.loads(CATALOG_PATH.read_text(encoding="utf-8"))
    if result.get("schema") != "project-control-process-catalog/1":
        raise ProcessError("unknown process catalog")
    ids = [p["id"] for p in result["processes"]]
    if len(ids) != len(set(ids)):
        raise ProcessError("duplicate process id")
    for p in result["processes"]:
        obligations = [o["id"] for o in p["obligations"]]
        if len(obligations) != len(set(obligations)) or not obligations:
            raise ProcessError("invalid obligation catalog")
        if any(o["kind"] not in ("CORE", "CONDITIONAL") for o in p["obligations"]):
            raise ProcessError("invalid obligation kind")
    for proc in result["processes"]:
        guide=proc.get("owner_guidance")
        if not isinstance(guide,dict) or any(not isinstance(guide.get(k),str) or not guide[k].strip() for k in ("why","expected_result","owner_focus")):
            raise ProcessError("missing owner guidance")
        for key in ("entry_checks","work_method","review_checks","handoff_expectations"):
            if not proc.get(key) or not isinstance(proc[key],list):
                raise ProcessError("missing process methods and review criteria")
        role_notes=proc.get("reporting_guidance",{})
        if any(not role_notes.get(r) for r in proc["responsible_roles"]):
            raise ProcessError("missing role handoff rules")
    for profile in result["profiles"]:
        if any(x not in ids for x in profile["process_ids"]):
            raise ProcessError("invalid process profile")
    return result

def initial_state(profile_id="general-rd", config=None):
    config = config or catalog()
    if profile_id not in {p["id"] for p in config["profiles"]}:
        raise ProcessError("unknown process profile")
    return {"schema": SCHEMA, "catalog_version": config["version"],
            "catalog_sha256": hashlib.sha256(CATALOG_PATH.read_bytes()).hexdigest(),
            "profile_id": profile_id, "profile_status": "PROPOSED_NOT_APPROVED", "activities": [], "intakes": [], "reports": [],
            "review_requests": [], "current_focus": None, "focus_history": [], "warning": "No formal owner approval or task authorization is issued here."}

def state_for(record, config=None, *, allow_stale=False):
    if not isinstance(record, dict):
        raise ProcessError("invalid management record")
    value = record.setdefault("process_control", initial_state(config=config))
    if value.get("schema") != SCHEMA:
        raise ProcessError("unknown process state schema")
    config = config or catalog()
    if (value.get("catalog_version") != config["version"] or
        value.get("catalog_sha256") != hashlib.sha256(CATALOG_PATH.read_bytes()).hexdigest()):
        if not allow_stale:
            raise ProcessError("process policy version/hash conflict: explicit migration review required")
    if value.get("profile_id") not in {p["id"] for p in config["profiles"]}:
        raise ProcessError("unknown project process profile")
    for name in ("activities", "intakes", "reports", "review_requests"):
        if not isinstance(value.get(name), list):
            raise ProcessError("invalid process state: " + name)
    if value.get("current_focus") is not None and not isinstance(value["current_focus"],dict):
        raise ProcessError("invalid focus record")
    return value

def policy_is_current(state):
    return (state.get("catalog_version")==catalog()["version"] and
            state.get("catalog_sha256")==hashlib.sha256(CATALOG_PATH.read_bytes()).hexdigest())

def locate(seq, id, label):
    return next((x for x in seq if x.get("id") == id), None) or _missing(label)

def _missing(label):
    raise ProcessError("unknown " + label)

def unique(seq, id):
    if any(x.get("id") == id for x in seq):
        raise ProcessError("duplicate id")

def process_for(state, process_id, config):
    profile = next(x for x in config["profiles"] if x["id"] == state["profile_id"])
    if process_id not in profile["process_ids"]:
        raise ProcessError("process not enabled in selected project profile")
    return next(x for x in config["processes"] if x["id"] == process_id)

def assessment(activity):
    """Structural readiness only; content quality and legal authority remain external."""
    issues = []
    pending = []
    for o in activity["obligations"]:
        status = o["disposition"]
        if status == "SATISFIED_CANDIDATE":
            if not o["evidence_refs"]:
                issues.append(o["id"] + ": missing evidence")
        elif status == "NOT_APPLICABLE_PROPOSED" and o["kind"] == "CONDITIONAL":
            pending.append(o["id"] + ": proposed tailoring")
        elif status in ("DEFERRED_PROPOSED", "EXCEPTION_REQUESTED"):
            pending.append(o["id"] + ": requires human risk/exception decision")
        else:
            issues.append(o["id"] + ": " + status)
    review = activity.get("technical_review")
    if not review or review.get("outcome") != "ADEQUATE_CANDIDATE" or review.get("activity_revision") != activity["revision"]:
        issues.append("current technical review missing")
    status = "NOT_READY" if issues else ("READY_FOR_OWNER_DECISION_WITH_PROPOSALS" if pending else "READY_FOR_OWNER_REVIEW")
    return {"state": status, "issues": issues, "pending_decisions": pending,
            "scope": "STRUCTURAL_ONLY_NOT_GATE_APPROVAL"}

def apply(record, action, payload):
    """Mutation of caller-owned *candidate* record, not a human authority action."""
    if action not in ACTIONS or not isinstance(payload, dict):
        raise ProcessError("unsupported process action")
    config = catalog()
    state = state_for(record, config)
    if action == "set_process_focus":
        a=locate(state["activities"],identifier(payload.get("activity_id"),"activity id"),"activity")
        focus={"activity_id":a["id"],"activity_revision":a["revision"],
               "reason":word(payload.get("reason"),"focus reason",1000),
               "source_refs":strings(payload.get("source_refs"),"focus evidence",required=True),
               "state":"CANDIDATE_NOT_APPROVED"}
        state.setdefault("focus_history",[]).append(focus)
        state["current_focus"]=focus
    elif action == "open_process_activity":
        id = identifier(payload.get("id"), "activity id")
        unique(state["activities"], id)
        process = process_for(state, word(payload.get("process_id"), "process id", 80), config)
        role = word(payload.get("role"), "responsible role", 40)
        if role not in process["responsible_roles"]:
            raise ProcessError("role is not responsible for process")
        objective = word(payload.get("objective"), "objective")
        sources = strings(payload.get("source_refs"), "source refs", required=True)
        state["activities"].append({"id": id, "process_id": process["id"], "role": role,
            "objective": objective, "source_refs": sources, "revision": 1,
            "state": "DRAFT_NOT_APPROVED", "technical_review": None,
            "obligations": [{"id": o["id"], "kind": o["kind"], "disposition": "OPEN",
                             "reason": "", "evidence_refs": [], "impact": "", "owner": "", "close_by": ""}
                            for o in process["obligations"]]})
    elif action == "set_process_obligation":
        activity = locate(state["activities"], identifier(payload.get("activity_id"), "activity id"), "activity")
        obligation = locate(activity["obligations"], word(payload.get("obligation_id"), "obligation id", 80), "obligation")
        disposition = payload.get("disposition")
        if disposition not in DISPOSITIONS:
            raise ProcessError("invalid obligation disposition")
        if disposition == "NOT_APPLICABLE_PROPOSED" and obligation["kind"] == "CORE":
            raise ProcessError("core obligation cannot be declared not applicable")
        reason = word(payload.get("reason"), "rationale")
        evidence = strings(payload.get("evidence_refs", []), "evidence refs",
                           required=disposition == "SATISFIED_CANDIDATE")
        impact = (word(payload.get("impact"), "impact") if disposition in
                  ("NOT_APPLICABLE_PROPOSED", "DEFERRED_PROPOSED", "EXCEPTION_REQUESTED", "CONFLICT") else "")
        owner = (word(payload.get("owner"), "follow-up owner", 160) if disposition in
                 ("DEFERRED_PROPOSED", "EXCEPTION_REQUESTED") else "")
        close_by = (word(payload.get("close_by"), "closure criterion", 300) if disposition in
                    ("DEFERRED_PROPOSED", "EXCEPTION_REQUESTED") else "")
        obligation.update({"disposition": disposition, "reason": reason, "evidence_refs": evidence,
                           "impact": impact, "owner": owner, "close_by": close_by})
        activity["revision"] += 1
        activity["technical_review"] = None
        activity["state"] = "DRAFT_NOT_APPROVED"
        for r in state["reports"]:
            if r.get("activity_id")==activity["id"]:
                r["freshness"]="STALE_AFTER_ACTIVITY_REVISION"
        for req in state["review_requests"]:
            if req["activity_id"] == activity["id"] and req["state"] == "REQUESTED_NOT_APPROVED":
                req["state"] = "STALE_AFTER_REVISION"
    elif action == "review_process_activity":
        activity = locate(state["activities"], identifier(payload.get("activity_id"), "activity id"), "activity")
        result = payload.get("outcome")
        if result not in REVIEWS:
            raise ProcessError("unknown technical review outcome")
        role = word(payload.get("reviewer_role"), "reviewer role", 40)
        if role not in ("coordinator", "reviewer", "integration"):
            raise ProcessError("role cannot perform technical review")
        if role == activity["role"] and payload.get("independent_review") is True:
            raise ProcessError("author cannot self-declare independent review")
        refs = strings(payload.get("evidence_refs"), "review refs", required=True)
        activity["technical_review"] = {"outcome": result, "reviewer_role": role,
            "independent_review_claimed": bool(payload.get("independent_review", False)),
            "reason": word(payload.get("reason"), "review reasoning"),
            "evidence_refs": refs, "activity_revision": activity["revision"],
            "verified_external_identity": False}
        for req in state["review_requests"]:
            if req["activity_id"] == activity["id"] and req["state"] == "REQUESTED_NOT_APPROVED":
                req["state"] = "STALE_AFTER_REVIEW_CHANGE"
        activity["state"] = "TECHNICALLY_REVIEWED_CANDIDATE" if result == "ADEQUATE_CANDIDATE" else "REWORK_REQUIRED"
    elif action == "request_process_review":
        activity = locate(state["activities"], identifier(payload.get("activity_id"), "activity id"), "activity")
        result = assessment(activity)
        if result["state"] == "NOT_READY":
            raise ProcessError("review blocked: " + "; ".join(result["issues"]))
        id = identifier(payload.get("id"), "review request id")
        unique(state["review_requests"], id)
        state["review_requests"].append({"id": id, "activity_id": activity["id"],
            "activity_revision": activity["revision"], "state": "REQUESTED_NOT_APPROVED",
            "pending_decisions": result["pending_decisions"],
            "purpose": word(payload.get("purpose"), "review purpose"),
            "approval_authority": "EXTERNAL_HUMAN_ONLY"})
        activity["state"] = "AWAITING_OWNER_DECISION"
    elif action == "record_ai_intake":
        id = identifier(payload.get("id"), "intake id")
        unique(state["intakes"], id)
        role = word(payload.get("role"), "role", 40)
        if role not in ROLES:
            raise ProcessError("unknown AI role")
        authority_source = payload.get("role_source")
        if authority_source not in ("USER_EXPLICIT", "RECORDED_APPOINTMENT", "UNCONFIRMED"):
            raise ProcessError("unknown role source")
        head = payload.get("observed_head")
        if head not in (None, "") and (not isinstance(head, str) or not HEAD.fullmatch(head)):
            raise ProcessError("invalid observed head")
        state["intakes"].append({"id": id, "role": role, "role_source": authority_source,
            "task_ref": word(payload.get("task_ref"), "task ref", 240) if payload.get("task_ref") else "NONE",
            "observed_head": head or None, "head_verification": "REPORTED_NOT_INDEPENDENTLY_CHECKED",
            "source_refs": strings(payload.get("source_refs"), "source refs", required=True),
            "verified_facts": strings(payload.get("verified_facts", []), "verified facts"),
            "conflicts": strings(payload.get("conflicts", []), "conflicts"),
            "unknowns": strings(payload.get("unknowns", []), "unknowns"),
            "next_action": word(payload.get("next_action"), "next action"),
            "state": "ROLE_REPORTED_NOT_AUTHORIZED", "write_permission": "NONE_FROM_INTAKE"})
    elif action == "record_ai_work_report":
        id = identifier(payload.get("id"), "report id")
        unique(state["reports"], id)
        focus = word(payload.get("focus"), "project focus", 500)
        why = word(payload.get("why_this_work"), "why this work matters", 1200)
        judgement = word(payload.get("judgement"), "project judgement", 1600)
        owner_action = payload.get("owner_action")
        if owner_action not in ("NONE", "REVIEW", "DECIDE", "PROVIDE_INFORMATION"):
            raise ProcessError("invalid owner action")
        owner_request = payload.get("owner_request") or ""
        if owner_action == "NONE":
            if not isinstance(owner_request, str) or owner_request.strip():
                raise ProcessError("NONE owner action cannot request a decision")
        else:
            owner_request = word(owner_request, "specific owner request", 1000)
        intake=locate(state["intakes"],identifier(payload.get("intake_id"),"intake id"),"intake")
        activity_id=payload.get("activity_id") or None
        a=locate(state["activities"],identifier(activity_id,"activity id"),"activity") if activity_id else None
        audience=payload.get("audience") or ("OWNER" if intake["role"]=="coordinator" else "COORDINATOR")
        if audience not in ("OWNER","COORDINATOR","SPECIALIST","EXECUTOR"):
            raise ProcessError("invalid report audience")
        kind=payload.get("report_kind") or "PROGRESS"
        if kind not in ("PROGRESS","FINDING","BLOCKER","REVIEW","HANDOFF","DELIVERY"):
            raise ProcessError("invalid report kind")
        state["reports"].append({"id": id,
            "intake_id":intake["id"],"reporter_role":intake["role"],
            "audience":audience,"report_kind":kind,"activity_id":activity_id,
            "activity_revision":a["revision"] if a else None,
            "freshness":"CURRENT_AT_RECORDING",
            "focus": focus, "why_this_work": why, "judgement": judgement,
            "owner_action": owner_action, "owner_request": owner_request.strip(),
            "summary": word(payload.get("summary"), "completed work"),
            "evidence_refs": strings(payload.get("evidence_refs"), "report evidence", required=True),
            "unfinished": strings(payload.get("unfinished", []), "unfinished"),
            "next_action": word(payload.get("next_action"), "next action"),
            "state": "REPORTED_NOT_ACCEPTED", "approval": "NONE"})
    return {"process_control": copy.deepcopy(state),
            "assessment": assessment(state["activities"][-1]) if action == "open_process_activity" else None,
            "authorized": False, "note": "Process activities and reports never grant formal permission."}
