"""Generic project plan, dependency and change-review candidate operations.

Operate only on an in-memory copy of a per-project management record.
The caller handles owner authentication, a revision CAS, durable write and audit.
No function here grants actual project, design, task, CI or Gate approval.
"""
from __future__ import annotations
import hashlib
import json
import re

ID = re.compile(r"^[A-Za-z0-9][A-Za-z0-9_.-]{0,99}$")
KINDS = ("requirements", "plans", "changes", "gate_reviews", "trace_links")
ACTIONS = {"create_requirement", "link_dependency", "create_plan", "revise_plan", "submit_plan",
           "record_plan_review", "create_change", "assess_change", "request_gate_review"}


class GovernanceError(ValueError):
    pass


def ident(value, name):
    if not isinstance(value, str) or not ID.fullmatch(value):
        raise GovernanceError("invalid " + name)
    return value


def text(value, name, limit=3000):
    if not isinstance(value, str) or not value.strip() or len(value) > limit:
        raise GovernanceError("missing or excessive " + name)
    return value.strip()


def refs(value, name):
    if not isinstance(value, list) or not (1 <= len(value) <= 40):
        raise GovernanceError(name + " requires 1-40 source references")
    return [text(x, name, 600) for x in value]


def ids(value, name, *, allow_empty=False):
    if not isinstance(value, list) or (not allow_empty and not value) or len(value) > 200:
        raise GovernanceError("invalid " + name)
    result = [ident(x, name) for x in value]
    if len(result) != len(set(result)):
        raise GovernanceError("duplicate identifiers in " + name)
    return result


def find(items, identifier, name):
    match = [x for x in items if isinstance(x, dict) and x.get("id") == identifier]
    if len(match) != 1:
        raise GovernanceError(name + " not uniquely found")
    return match[0]


def ensure_new(items, identifier):
    if any(x.get("id") == identifier for x in items):
        raise GovernanceError("record ID already exists")


def snapshot(record, work_ids):
    selected = [find(record["work"], x, "work item") for x in work_ids]
    deliveries = sorted({w["deliverable_id"] for w in selected})
    linked = [find(record["deliverables"], x, "delivery") for x in deliveries]
    raw = json.dumps({"work": selected, "deliverables": linked}, sort_keys=True,
                     ensure_ascii=False, separators=(",", ":")).encode()
    return hashlib.sha256(raw).hexdigest()


def dependency_cycle(works):
    edges = {w["id"]: w.get("depends_on", []) for w in works}
    visited, active = set(), set()
    def visit(node):
        if node in active:
            raise GovernanceError("cyclic task dependency")
        if node in visited:
            return
        active.add(node)
        for dependency in edges[node]:
            if dependency not in edges:
                raise GovernanceError("dependency references missing work item")
            visit(dependency)
        active.remove(node)
        visited.add(node)
    for key in edges:
        visit(key)


def affected_graph(works, roots):
    children = {}
    for item in works:
        for parent in item.get("depends_on", []):
            children.setdefault(parent, []).append(item["id"])
    affected = set(roots)
    queue = list(roots)
    while queue:
        for child in children.get(queue.pop(0), []):
            if child not in affected:
                affected.add(child)
                queue.append(child)
    return sorted(affected)


def reject_stale_context(work, work_ids):
    """Prevent an obsolete AI context claim from supporting new review requests."""
    selected = [find(work, wid, "work item") for wid in work_ids]
    stale = [w["id"] for w in selected
             if w.get("context_evidence_status") == "STALE_AFTER_NEW_ASSESSMENT"]
    if stale:
        raise GovernanceError("stale context evidence requires review before submission: " +
                              ", ".join(stale))


def apply(record, action, payload):
    if action not in ACTIONS:
        raise GovernanceError("unsupported project governance operation")
    if not isinstance(payload, dict):
        raise GovernanceError("invalid management request")
    for kind in KINDS:
        record.setdefault(kind, [])
        if not isinstance(record[kind], list):
            raise GovernanceError("corrupt governance collection: " + kind)
    work = record["work"]
    if action == "create_requirement":
        identifier = ident(payload.get("id"), "requirement ID")
        ensure_new(record["requirements"], identifier)
        record["requirements"].append({
            "id": identifier, "title": text(payload.get("title"), "requirement title", 240),
            "statement": text(payload.get("statement"), "requirement statement"),
            "source_refs": refs(payload.get("source_refs"), "requirement source"),
            "acceptance": text(payload.get("acceptance"), "acceptance"),
            "state": "PROPOSED_NOT_APPROVED",
        })
    elif action == "link_dependency":
        identifier = ident(payload.get("id"), "dependent work ID")
        dep_id = ident(payload.get("depends_on"), "prerequisite ID")
        if identifier == dep_id:
            raise GovernanceError("work item cannot depend on itself")
        item = find(work, identifier, "dependent work")
        find(work, dep_id, "prerequisite")
        if dep_id in item.get("depends_on", []):
            raise GovernanceError("work dependency already exists")
        item.setdefault("depends_on", []).append(dep_id)
        dependency_cycle(work)
        # Existing submitted plans relying on this WBS must be reassessed.
        for plan in record["plans"]:
            if plan.get("state") == "SUBMITTED_FOR_HUMAN_REVIEW" and identifier in plan["work_ids"]:
                plan["state"] = "REASSESSMENT_REQUIRED"
                plan["reason"] = "WBS changed after plan submission"
    elif action == "create_plan":
        identifier = ident(payload.get("id"), "plan ID")
        ensure_new(record["plans"], identifier)
        work_ids = ids(payload.get("work_ids"), "planned work IDs")
        for work_id in work_ids:
            find(work, work_id, "planned work")
        record["plans"].append({
            "id": identifier, "title": text(payload.get("title"), "plan title", 240),
            "objective": text(payload.get("objective"), "plan objective"),
            "work_ids": work_ids,
            "exit_condition": text(payload.get("exit_condition"), "exit condition"),
            "state": "DRAFT", "submission_hash": None, "review_history": []
        })
    elif action == "revise_plan":
        plan = find(record["plans"], ident(payload.get("id"), "plan ID"), "plan")
        if plan["state"] not in ("SUBMITTED_FOR_HUMAN_REVIEW", "REVISION_REQUESTED",
                                 "REASSESSMENT_REQUIRED", "REVIEWED_PENDING_EXTERNAL_APPROVAL"):
            raise GovernanceError("plan cannot be revised from this state")
        updated_ids = ids(payload.get("work_ids"), "revised work IDs")
        for wid in updated_ids:
            find(work, wid, "revised work")
        plan["review_history"].append({
            "result": "REOPENED_FOR_REVISION",
            "comment": text(payload.get("reason"), "revision reason")
        })
        plan["work_ids"] = updated_ids
        plan["objective"] = text(payload.get("objective"), "plan objective")
        plan["exit_condition"] = text(payload.get("exit_condition"), "exit condition")
        plan["state"] = "DRAFT"
        plan["submission_hash"] = None
    elif action == "submit_plan":
        plan = find(record["plans"], ident(payload.get("id"), "plan ID"), "plan")
        if plan["state"] not in ("DRAFT", "REASSESSMENT_REQUIRED"):
            raise GovernanceError("plan is not eligible for submission")
        dependency_cycle(work)
        reject_stale_context(work, plan["work_ids"])
        plan["submission_hash"] = snapshot(record, plan["work_ids"])
        plan["state"] = "SUBMITTED_FOR_HUMAN_REVIEW"
        plan["review_history"].append({"result": "SUBMITTED", "comment": text(payload.get("reason"), "reason")})
    elif action == "record_plan_review":
        plan = find(record["plans"], ident(payload.get("id"), "plan ID"), "plan")
        if plan["state"] != "SUBMITTED_FOR_HUMAN_REVIEW":
            raise GovernanceError("plan has not been submitted for review")
        reject_stale_context(work, plan["work_ids"])
        if plan["submission_hash"] != snapshot(record, plan["work_ids"]):
            plan["state"] = "REASSESSMENT_REQUIRED"
            plan["review_history"].append({
                "result": "SOURCE_CHANGED_REASSESSMENT_REQUIRED",
                "comment": "WBS or deliverable changed after plan submission"
            })
            return record
        decision = payload.get("decision")
        if decision not in ("REQUEST_CHANGES", "REVIEWED_PENDING_FORMAL_APPROVAL"):
            raise GovernanceError("Web candidate review cannot authorize implementation")
        reviewer = text(payload.get("reviewer"), "reviewer identity", 160)
        entry = {"decision": decision, "reviewer_claim": reviewer,
                 "evidence_refs": refs(payload.get("evidence_refs"), "review evidence"),
                 "reason": text(payload.get("reason"), "review reason")}
        plan["review_history"].append(entry)
        plan["state"] = ("REVISION_REQUESTED" if decision == "REQUEST_CHANGES"
                         else "REVIEWED_PENDING_EXTERNAL_APPROVAL")
    elif action == "create_change":
        identifier = ident(payload.get("id"), "change request ID")
        ensure_new(record["changes"], identifier)
        root_ids = ids(payload.get("root_work_ids"), "changed work IDs")
        for rid in root_ids:
            find(work, rid, "affected work")
        record["changes"].append({
            "id": identifier,
            "title": text(payload.get("title"), "change title", 240),
            "justification": text(payload.get("justification"), "change justification"),
            "source_refs": refs(payload.get("source_refs"), "change evidence"),
            "root_work_ids": root_ids,
            "affected_work_ids": [],
            "affected_deliverables": [],
            "impact_summary": None, "state": "IMPACT_ASSESSMENT_REQUIRED"
        })
    elif action == "assess_change":
        change = find(record["changes"], ident(payload.get("id"), "change ID"), "change")
        if change["state"] != "IMPACT_ASSESSMENT_REQUIRED":
            raise GovernanceError("change already assessed; preserve history")
        dependency_cycle(work)
        impacted = affected_graph(work, change["root_work_ids"])
        delivery_ids = sorted({
            find(work, wid, "affected work")["deliverable_id"] for wid in impacted
        })
        change["affected_work_ids"] = impacted
        change["affected_deliverables"] = delivery_ids
        change["impact_summary"] = text(payload.get("impact_summary"), "impact explanation")
        change["state"] = "AWAITING_HUMAN_CHANGE_DECISION"
        change["decision"] = "NOT_APPROVED"
        # Only impacted plans are invalidated; unrelated plans remain in place.
        for plan in record["plans"]:
            if (plan["state"] in ("SUBMITTED_FOR_HUMAN_REVIEW",
                                  "REVIEWED_PENDING_EXTERNAL_APPROVAL")
                    and any(x in impacted for x in plan["work_ids"])):
                plan["state"] = "REASSESSMENT_REQUIRED"
                plan["reason"] = "Affected by change request " + change["id"]
    elif action == "request_gate_review":
        identifier = ident(payload.get("id"), "gate review ID")
        ensure_new(record["gate_reviews"], identifier)
        scope = ids(payload.get("work_ids"), "review work IDs")
        for wid in scope:
            find(work, wid, "work for Gate review")
        reject_stale_context(work, scope)
        record["gate_reviews"].append({
            "id": identifier, "stage": text(payload.get("stage"), "stage", 100),
            "purpose": text(payload.get("purpose"), "review purpose"),
            "work_ids": scope, "evidence_refs": refs(payload.get("evidence_refs"), "Gate evidence"),
            "state": "REQUESTED_NOT_APPROVED", "decision": "NONE"
        })
    return record
