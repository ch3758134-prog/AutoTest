"""Project Control: revisioned plans, progress, decisions, changes and receipts.

Local Git-tracked JSON is the exchange format for human-triggered ChatGPT/Codex
workflows. Writes require an authenticated owner HTTP request and a matching
revision. No method modifies approved P0, requirements, Git refs or project code.
"""
from __future__ import annotations

import copy
from datetime import datetime, timezone
from hashlib import sha256
import json
import os
from pathlib import Path
import re
import threading
import uuid

from plan_integrity import assess, PlanError

ID_RE = re.compile(r"^[A-Za-z0-9][A-Za-z0-9_.-]{0,99}$")
SHA_RE = re.compile(r"^[0-9a-fA-F]{40}$")
WORK_KINDS = {"RESEARCH", "DESIGN", "IMPLEMENT", "INTEGRATE", "VERIFY", "COORDINATE"}
HORIZONS = {"CURRENT", "NEXT", "LATER"}
WORK_MODES = {"EXPLORATORY", "CONTROLLED"}
PRIORITIES = {"LOW", "NORMAL", "HIGH", "CRITICAL"}
PROGRESS = {"NOT_STARTED", "IN_PROGRESS", "BLOCKED", "REPORTED_COMPLETE", "VERIFIED_COMPLETE", "CANCELLED"}
CHANGE_KINDS = {"FACT_CORRECTION", "REQUIREMENT_CLARIFICATION", "SCOPE_CHANGE",
                "DESIGN_CHANGE", "IMPLEMENTATION_DEFECT", "PLAN_CHANGE", "OTHER"}
CHANGE_DISPOSITIONS = {"REASSESS", "NO_IMPACT", "INVESTIGATE"}
STAGE_IDS = {"P0", "G0", "G1", "G2", "G3", "G4"}
ACTIONS = {"create_work", "edit_work", "create_deliverable", "edit_deliverable",
           "submit_plan", "withdraw_plan", "approve_plan", "return_plan",
           "update_progress", "verify_work", "raise_change", "assess_change",
           "resolve_change", "reconcile_work", "record_receipt", "review_receipt", "record_phase_review",
           "create_milestone", "edit_milestone", "raise_risk", "review_risk", "start_cycle", "close_cycle"}
FIELD_WORK = {"title", "kind", "mode", "horizon", "deliverable_id",
              "expected_result", "exit_condition", "depends_on", "sources",
              "priority", "assignee", "planned_start", "planned_finish", "schedule_basis", "state", "parent_work_id"}
FIELD_DELIV = {"title", "success_condition", "commitment"}
FIELD_MILESTONE = {"title", "deliverable_id", "exit_condition", "target_date", "date_basis"}
RISK_LEVELS = {"LOW", "MEDIUM", "HIGH", "CRITICAL"}


class ControlError(ValueError):
    pass


def now():
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def ident(value, field):
    if not isinstance(value, str) or not ID_RE.fullmatch(value):
        raise ControlError(field + " must be a safe identifier")
    return value


def text(value, field, maximum=5000):
    if not isinstance(value, str) or not value.strip() or len(value) > maximum:
        raise ControlError(field + " must be nonempty bounded text")
    return value.strip()


def items(value, field, maximum=250):
    if not isinstance(value, list) or len(value) > maximum:
        raise ControlError(field + " must be a bounded array")
    return value


def digest(value):
    return sha256(json.dumps(value, ensure_ascii=False, sort_keys=True,
                             separators=(",", ":")).encode("utf-8")).hexdigest()


def validate_work(work, existing):
    if not isinstance(work, dict):
        raise ControlError("work item must be an object")
    if any(k not in FIELD_WORK | {"id"} for k in work):
        raise ControlError("work contains forbidden/unknown fields")
    ident(work.get("id"), "work id")
    for field in ("title", "expected_result", "exit_condition"):
        text(work.get(field), field)
    if work.get("kind") not in WORK_KINDS or work.get("mode") not in WORK_MODES or work.get("horizon") not in HORIZONS:
        raise ControlError("invalid work kind, mode or horizon")
    ident(work.get("deliverable_id"), "deliverable_id")
    if work.get("deliverable_id") not in existing["deliverables"]:
        raise ControlError("work must link to a real deliverable")
    if work.get("parent_work_id") is not None:
        ident(work["parent_work_id"], "parent_work_id")
        if work["parent_work_id"] == work["id"]:
            raise ControlError("work item cannot parent itself")
    deps = items(work.get("depends_on", []), "depends_on")
    if len(deps) != len(set(deps)) or any(not isinstance(d, str) or not ID_RE.fullmatch(d) for d in deps):
        raise ControlError("invalid or repeated dependency")
    sources = items(work.get("sources", []), "sources")
    for s in sources:
        if not isinstance(s, dict) or s.get("role") not in ("CONTEXT", "REQUIRED_BASELINE"):
            raise ControlError("source role required")
        ident(s.get("object_id"), "source object")
        rev = s.get("revision")
        if type(rev) is not int or rev < 1:
            raise ControlError("source revision must be positive integer")
    if work.get("state", "PROPOSED") not in ("PROPOSED", "PLANNED"):
        raise ControlError("invalid plan work state")
    if work.get("priority", "NORMAL") not in PRIORITIES:
        raise ControlError("invalid priority")
    if work.get("assignee"):
        text(work["assignee"], "assignee", 140)
    if work.get("planned_start") or work.get("planned_finish"):
        text(work.get("schedule_basis"), "schedule_basis")
        for field in ("planned_start", "planned_finish"):
            if work.get(field):
                d = work[field]
                if not isinstance(d, str) or not re.fullmatch(r"\d{4}-\d{2}-\d{2}", d):
                    raise ControlError(field + " must be YYYY-MM-DD")
                from datetime import date
                date.fromisoformat(d)
    if work.get("planned_start") and work.get("planned_finish") and work["planned_start"] > work["planned_finish"]:
        raise ControlError("planned_finish precedes planned_start")


def validate_deliverable(item):
    if not isinstance(item, dict) or any(k not in FIELD_DELIV | {"id"} for k in item):
        raise ControlError("invalid deliverable fields")
    ident(item.get("id"), "deliverable id")
    text(item.get("title"), "deliverable title")
    text(item.get("success_condition"), "success condition")
    if item.get("commitment") not in ("PROPOSAL", "IN_SCOPE_FROM_APPROVED_BASELINE"):
        raise ControlError("deliverable commitment is required")


def validate_milestone(item, deliverables):
    if not isinstance(item, dict) or any(k not in FIELD_MILESTONE | {"id"} for k in item):
        raise ControlError("invalid milestone fields")
    ident(item.get("id"), "milestone id")
    text(item.get("title"), "milestone title")
    text(item.get("exit_condition"), "milestone success condition")
    if item.get("deliverable_id") not in deliverables:
        raise ControlError("milestone must refer to a real deliverable")
    if item.get("target_date"):
        from datetime import date
        try:
            date.fromisoformat(text(item["target_date"], "milestone target date", 10))
        except ValueError as exc:
            raise ControlError("milestone target date must be real ISO date") from exc
        text(item.get("date_basis"), "milestone schedule basis")

def new_id(prefix):
    return prefix + "-" + uuid.uuid4().hex[:12]


class Control:
    def __init__(self, file: Path, engineering_archive: Path):
        self.file = Path(file).resolve()
        self.archive_file = Path(engineering_archive).resolve()
        self.lock = threading.RLock()
        if not self.file.is_file():
            raise ControlError("shared project control record is missing: " + str(self.file))

    def read(self):
        with self.lock:
            record = json.loads(self.file.read_text(encoding="utf-8"))
            if record.get("schema") != "project-control/1" or record.get("project_id") != "autotest":
                raise ControlError("unknown shared project control record")
            return record

    def archive(self):
        return json.loads(self.archive_file.read_text(encoding="utf-8"))

    @staticmethod
    def plan(record):
        p = copy.deepcopy(record["planning"])
        return {"schema": "engineering-development-plan-candidate/1",
                "status": "PROPOSED", "project_id": record["project_id"],
                "purpose": p["purpose"], "planning_assumptions": p["planning_assumptions"],
                "product_anchor": p["product_anchor"], "deliverables": p["deliverables"],
                "milestones": p.get("milestones", []), "work": p["work"]}

    def model(self):
        record = self.read()
        plan = self.plan(record)
        assessment = assess(plan, self.archive())
        return record, plan, assessment

    def _write(self, record):
        self.file.parent.mkdir(parents=True, exist_ok=True)
        raw = json.dumps(record, ensure_ascii=False, sort_keys=True, indent=2) + "\n"
        path = self.file.with_name("." + self.file.name + ".tmp." + uuid.uuid4().hex)
        try:
            with path.open("x", encoding="utf-8") as handle:
                handle.write(raw)
                handle.flush()
                os.fsync(handle.fileno())
            os.replace(path, self.file)
        finally:
            path.unlink(missing_ok=True)

    def _event(self, record, action, actor, reason, subject=None):
        event = {"id": new_id("ev"), "revision": record["revision"] + 1,
                 "action": action, "actor": actor, "reason": reason,
                 "subject": subject, "at": now()}
        record["events"].append(event)
        return event

    def _editing_plan(self, record):
        state = record["planning"]["status"]
        if state == "IN_REVIEW":
            raise ControlError("plan is submitted; withdraw before changing its content")
        if state == "BASELINED":
            record["planning"]["status"] = "REVISION_DRAFT"
        record["planning"]["revision"] += 1

    def _work(self, record, wid):
        ident(wid, "work_id")
        obj = next((w for w in record["planning"]["work"] if w["id"] == wid), None)
        if obj is None:
            raise ControlError("work item does not exist")
        return obj

    def _require_owner(self, actor):
        return text(actor, "human operator", 140)

    def apply(self, name, payload, actor):
        if name not in ACTIONS:
            raise ControlError("unknown operation")
        if not isinstance(payload, dict):
            raise ControlError("payload must be an object")
        actor = self._require_owner(actor)
        reason = text(payload.get("reason"), "reason", 4000)
        revision = payload.get("expected_revision")
        if type(revision) is not int:
            raise ControlError("expected_revision must be integer")
        with self.lock:
            record = self.read()
            if revision != record["revision"]:
                raise ControlError("stale revision; refresh current project before making changes")
            plan = record["planning"]
            outcome = None
            subject = None
            if name in ("create_work", "edit_work", "create_deliverable", "edit_deliverable"):
                self._editing_plan(record)
                if name.endswith("work"):
                    if name == "create_work":
                        w = copy.deepcopy(payload.get("item"))
                        if not isinstance(w, dict):
                            raise ControlError("work item required")
                        w["id"] = ident(w.get("id", new_id("work")), "work id")
                        if any(x["id"] == w["id"] for x in plan["work"]):
                            raise ControlError("work id already exists")
                        w.setdefault("depends_on", [])
                        w.setdefault("sources", [])
                        w.setdefault("priority", "NORMAL")
                        w.setdefault("state", "PROPOSED")
                        validate_work(w, {"deliverables": {d["id"] for d in plan["deliverables"]}})
                        plan["work"].append(w)
                        subject = w["id"]
                    else:
                        subject = ident(payload.get("id"), "work id")
                        w = self._work(record, subject)
                        patch = payload.get("patch")
                        if not isinstance(patch, dict) or not patch or any(k not in FIELD_WORK for k in patch):
                            raise ControlError("invalid work patch")
                        fresh = dict(w, **patch)
                        validate_work(fresh, {"deliverables": {d["id"] for d in plan["deliverables"]}})
                        w.update(patch)
                    self._validate_dependencies(plan["work"])
                else:
                    if name == "create_deliverable":
                        d = copy.deepcopy(payload.get("item"))
                        if not isinstance(d, dict):
                            raise ControlError("deliverable required")
                        d["id"] = ident(d.get("id", new_id("delivery")), "deliverable id")
                        validate_deliverable(d)
                        if any(x["id"] == d["id"] for x in plan["deliverables"]):
                            raise ControlError("deliverable id exists")
                        plan["deliverables"].append(d)
                        subject = d["id"]
                    else:
                        subject = ident(payload.get("id"), "deliverable id")
                        d = next((d for d in plan["deliverables"] if d["id"] == subject), None)
                        if d is None:
                            raise ControlError("deliverable not found")
                        patch = payload.get("patch")
                        if not isinstance(patch, dict) or not patch or any(k not in FIELD_DELIV for k in patch):
                            raise ControlError("invalid deliverable patch")
                        validate_deliverable(dict(d, **patch))
                        d.update(patch)

            elif name in ("create_milestone", "edit_milestone"):
                self._editing_plan(record)
                if name == "create_milestone":
                    milestone = copy.deepcopy(payload.get("item"))
                    if not isinstance(milestone, dict):
                        raise ControlError("milestone item required")
                    milestone["id"] = ident(milestone.get("id", new_id("milestone")), "milestone id")
                    validate_milestone(milestone, {d["id"] for d in plan["deliverables"]})
                    if any(x["id"] == milestone["id"] for x in plan.setdefault("milestones", [])):
                        raise ControlError("milestone id already exists")
                    plan["milestones"].append(milestone)
                    subject = milestone["id"]
                else:
                    subject = ident(payload.get("id"), "milestone id")
                    milestone = next((m for m in plan.get("milestones", []) if m["id"] == subject), None)
                    if milestone is None:
                        raise ControlError("milestone not found")
                    patch = payload.get("patch")
                    if not isinstance(patch, dict) or not patch or any(k not in FIELD_MILESTONE for k in patch):
                        raise ControlError("invalid milestone patch")
                    validate_milestone(dict(milestone, **patch), {d["id"] for d in plan["deliverables"]})
                    milestone.update(patch)

            elif name == "submit_plan":
                if plan["status"] not in ("DRAFT", "REVISION_DRAFT"):
                    raise ControlError("only a draft plan can be submitted for review")
                self._validate_dependencies(plan["work"])
                assessment = assess(self.plan(record), self.archive())
                if not assessment["product_anchor"]["verified"]:
                    raise ControlError("product scope anchor must be verified before submitting the plan")
                if any(i["code"] in ("INVALID_DEPENDENCY", "UNLINKED_WORK", "SOURCE_MISSING", "SOURCE_STALE", "MILESTONE_UNLINKED", "UNSUPPORTED_MILESTONE_DATE", "INVALID_MILESTONE_DATE") for i in assessment["issues"]):
                    raise ControlError("plan has unresolved structural or stale source errors")
                plan["status"] = "IN_REVIEW"
                plan["submitted_digest"] = digest(self.plan(record))
                outcome = {"submitted_digest": plan["submitted_digest"],
                           "known_plan_issues": assessment["issues"],
                           "note": "submission does not authorize tasks or approve Gate"}

            elif name == "withdraw_plan":
                if plan["status"] != "IN_REVIEW":
                    raise ControlError("only a submitted plan can be withdrawn")
                plan["status"] = "REVISION_DRAFT" if record["baselines"] else "DRAFT"
                plan["submitted_digest"] = None

            elif name == "approve_plan":
                if plan["status"] != "IN_REVIEW":
                    raise ControlError("plan must be submitted before approval")
                if plan["submitted_digest"] != digest(self.plan(record)):
                    raise ControlError("submitted plan content changed; withdraw and resubmit")
                assessment = assess(self.plan(record), self.archive())
                if not assessment["product_anchor"]["verified"]:
                    raise ControlError("product scope anchor is no longer valid")
                baseline = {"id": new_id("planbaseline"), "revision": plan["revision"],
                            "at": now(), "actor": actor, "reason": reason,
                            "digest": plan["submitted_digest"], "source_digest": digest(self.archive()),
                            "plan": self.plan(record),
                            "known_issues": assessment["issues"]}
                record["baselines"].append(baseline)
                plan["status"] = "BASELINED"
                outcome = {"baseline": baseline["id"], "digest": baseline["digest"],
                           "warning": "plan approval does not approve requirements, design or execution"}

            elif name == "return_plan":
                if plan["status"] != "IN_REVIEW":
                    raise ControlError("only a submitted plan can be returned")
                plan["status"] = "REVISION_DRAFT" if record["baselines"] else "DRAFT"
                plan["submitted_digest"] = None

            elif name == "update_progress":
                subject = ident(payload.get("id"), "work id")
                self._work(record, subject)
                stage = payload.get("state")
                if stage not in PROGRESS - {"VERIFIED_COMPLETE"}:
                    raise ControlError("unknown work progress state")
                evidence = items(payload.get("evidence_refs", []), "evidence_refs", 30)
                if any(not isinstance(v, str) or not v.strip() or len(v) > 1000 for v in evidence):
                    raise ControlError("invalid evidence reference")
                if stage == "REPORTED_COMPLETE" and not evidence:
                    raise ControlError("reporting completion requires evidence links")
                detail = text(payload.get("detail"), "progress detail", 4000)
                if (stage in ("IN_PROGRESS", "REPORTED_COMPLETE") and
                        subject in record["invalidation"] and
                        not record["invalidation"][subject]["resolved"]):
                    raise ControlError("work is affected by an unresolved reviewed change")
                record["progress"][subject] = {"state": stage, "detail": detail,
                                               "evidence_refs": evidence,
                                               "updated": now(), "actor": actor,
                                               "verified_by": None}
                # Work progress changes do NOT silently rewrite a baseline plan.

            elif name == "verify_work":
                subject = ident(payload.get("id"), "work id")
                current = record["progress"].get(subject)
                if not current or current["state"] != "REPORTED_COMPLETE":
                    raise ControlError("work must be reported complete before owner verification")
                if (subject in record["invalidation"] and
                        not record["invalidation"][subject]["resolved"]):
                    raise ControlError("cannot verify work affected by an unresolved change")
                if not current["evidence_refs"]:
                    raise ControlError("no completion evidence")
                current.update(state="VERIFIED_COMPLETE", verified_by=actor,
                               verification_note=reason, verified_at=now())

            elif name == "raise_change":
                kind = payload.get("kind")
                if kind not in CHANGE_KINDS:
                    raise ControlError("change type required")
                impact = items(payload.get("affected_work", []), "affected_work", 100)
                for wid in impact:
                    self._work(record, wid)
                c = {"id": new_id("change"), "kind": kind,
                     "title": text(payload.get("title"), "change title", 220),
                     "description": text(payload.get("description"), "change description", 5000),
                     "affected_work": impact, "state": "OPEN", "actor": actor,
                     "created": now(), "source_refs": items(payload.get("source_refs", []), "source_refs", 30),
                     "assessments": {}, "decision": None}
                record["changes"].append(c)
                subject = c["id"]
                outcome = {"change_id": subject}

            elif name == "assess_change":
                subject = ident(payload.get("id"), "change id")
                c = next((x for x in record["changes"] if x["id"] == subject), None)
                if c is None or c["state"] not in ("OPEN", "ASSESSED"):
                    raise ControlError("change is not open for assessment")
                assessments = payload.get("assessments")
                if not isinstance(assessments, dict) or set(assessments) != set(c["affected_work"]):
                    raise ControlError("every explicitly affected work item requires assessment")
                for wid, review in assessments.items():
                    self._work(record, wid)
                    if not isinstance(review, dict) or review.get("disposition") not in CHANGE_DISPOSITIONS:
                        raise ControlError("invalid impact disposition")
                    text(review.get("rationale"), "impact rationale")
                c["assessments"] = assessments
                c["state"] = "ASSESSED"

            elif name == "resolve_change":
                subject = ident(payload.get("id"), "change id")
                c = next((x for x in record["changes"] if x["id"] == subject), None)
                if not c or c["state"] != "ASSESSED":
                    raise ControlError("change requires explicit impact review")
                decision = payload.get("decision")
                if decision not in ("ACCEPT_FOR_REPLANNING", "REJECT", "MORE_RESEARCH"):
                    raise ControlError("invalid change decision")
                c["decision"] = {"type": decision, "actor": actor, "reason": reason, "at": now()}
                c["state"] = "RESOLVED"
                if decision == "ACCEPT_FOR_REPLANNING":
                    for wid, assessment in c["assessments"].items():
                        if assessment["disposition"] in ("REASSESS", "INVESTIGATE"):
                            record["invalidation"][wid] = {"change_id": subject, "at": now(),
                                                           "reason": assessment["rationale"],
                                                           "resolved": False, "plan_revision_at_change": plan["revision"]}
                # A change never edits approved product definition automatically.
                outcome = {"note": "approved scope remains unchanged; affected plan tasks require review"}

            elif name == "reconcile_work":
                subject = ident(payload.get("id"), "work id")
                self._work(record, subject)
                invalidation = record["invalidation"].get(subject)
                if not invalidation or invalidation["resolved"]:
                    raise ControlError("work has no pending change impact")
                disposition = payload.get("disposition")
                if disposition not in ("NO_CHANGE", "REPLANNED"):
                    raise ControlError("change impact must be reconciled explicitly")
                if disposition == "REPLANNED" and not (
                        plan["status"] == "BASELINED" and
                        plan["revision"] > invalidation["plan_revision_at_change"]):
                    raise ControlError("replanning must be published as a newer plan baseline first")
                invalidation.update(resolved=True, reviewed_at=now(), reviewed_by=actor,
                                    disposition=disposition, review_reason=reason)

            elif name == "raise_risk":
                risk = {"id": new_id("risk"),
                        "title": text(payload.get("title"), "risk title", 220),
                        "description": text(payload.get("description"), "risk description"),
                        "likelihood": payload.get("likelihood"),
                        "severity": payload.get("severity"),
                        "mitigation": text(payload.get("mitigation"), "mitigation plan"),
                        "owner": text(payload.get("owner"), "risk owner", 140),
                        "source_refs": items(payload.get("source_refs", []), "risk sources", 30),
                        "affected_work": items(payload.get("affected_work", []), "risk affected work", 100),
                        "status": "OPEN", "created": now(), "actor": actor}
                if risk["likelihood"] not in RISK_LEVELS or risk["severity"] not in RISK_LEVELS:
                    raise ControlError("risk likelihood/severity must be explicit")
                for wid in risk["affected_work"]:
                    self._work(record, wid)
                record["risks"].append(risk)
                subject = risk["id"]
                outcome = {"risk_id": subject}

            elif name == "review_risk":
                subject = ident(payload.get("id"), "risk id")
                risk = next((x for x in record["risks"] if x["id"] == subject), None)
                if risk is None or risk["status"] not in ("OPEN", "MITIGATING", "MITIGATED"):
                    raise ControlError("risk is not open")
                decision = payload.get("status")
                if decision not in ("MITIGATING", "MITIGATED", "CLOSED"):
                    raise ControlError("invalid risk review state")
                evidence = items(payload.get("evidence_refs", []), "risk resolution sources", 30)
                if decision in ("MITIGATED", "CLOSED") and not evidence:
                    raise ControlError("closing a risk requires supporting evidence")
                risk.update(status=decision, evidence_refs=evidence,
                            reviewed_by=actor, reviewed_at=now(), review_reason=reason)

            elif name == "start_cycle":
                cycle = {"id": new_id("cycle"),
                         "title": text(payload.get("title"), "iteration title", 220),
                         "trigger": text(payload.get("trigger"), "iteration trigger", 4000),
                         "scope": text(payload.get("scope"), "iteration scope", 4000),
                         "created": now(), "actor": actor, "state": "ACTIVE",
                         "confirmation": "OWNER_LOCAL_RECORDED"}
                record["cycles"].append(cycle)
                subject = cycle["id"]
                outcome = {"cycle_id": subject, "note": "new cycle does not rewrite earlier baselines"}

            elif name == "close_cycle":
                subject = ident(payload.get("id"), "cycle id")
                cycle = next((x for x in record["cycles"] if x["id"] == subject), None)
                if cycle is None or cycle["state"] != "ACTIVE":
                    raise ControlError("only active cycles can be closed")
                cycle.update(state="CLOSED", closed_at=now(), reviewed_by=actor,
                             closure_note=reason)

            elif name == "record_receipt":
                subject = ident(payload.get("work_id"), "receipt work_id")
                self._work(record, subject)
                sha = text(payload.get("head_sha"), "head_sha", 40)
                if not SHA_RE.fullmatch(sha):
                    raise ControlError("receipt head_sha must be a full Git SHA")
                commands = items(payload.get("commands"), "commands", 50)
                if not commands or any(not isinstance(s, dict) or
                                       not isinstance(s.get("command"), str) or
                                       type(s.get("exit_code")) is not int for s in commands):
                    raise ControlError("receipt requires actual commands and integer exit codes")
                output = {"id": new_id("receipt"), "work_id": subject, "head_sha": sha,
                          "commands": commands, "reported_at": now(), "reported_by": actor,
                          "status": "UNVERIFIED", "source": "LOCAL_REPORTED",
                          "evidence_refs": items(payload.get("evidence_refs", []), "evidence_refs", 30),
                          "note": text(payload.get("note"), "receipt note")}
                record["receipts"].append(output)
                outcome = {"receipt_id": output["id"], "status": output["status"]}

            elif name == "review_receipt":
                subject = ident(payload.get("id"), "receipt id")
                receipt = next((r for r in record["receipts"] if r["id"] == subject), None)
                if not receipt or receipt["status"] != "UNVERIFIED":
                    raise ControlError("receipt is not awaiting review")
                result = payload.get("decision")
                if result not in ("EVIDENCE_REVIEWED", "REJECTED"):
                    raise ControlError("unknown evidence review decision")
                receipt["status"] = result
                receipt["reviewer"] = actor
                receipt["review_note"] = reason
                receipt["reviewed_at"] = now()
                # Review of an execution record NEVER approves actual ECU acceptance.

            elif name == "record_phase_review":
                phase = payload.get("phase_id")
                if phase not in STAGE_IDS:
                    raise ControlError("unknown phase")
                decision = payload.get("decision")
                if decision not in ("RETURN", "DEFER", "PASS_WITH_LIMITATIONS", "PASS"):
                    raise ControlError("unknown Gate decision")
                evidence_ids = items(payload.get("evidence_ids"), "evidence_ids", 30)
                archive = self.archive()["engineering"]
                approved = {o["id"] for o in archive["objects"] if o["state"] == "APPROVED"}
                if decision in ("PASS", "PASS_WITH_LIMITATIONS"):
                    if not evidence_ids or any(e not in approved for e in evidence_ids):
                        raise ControlError("passing Gate needs exact approved evidence, not draft research")
                    stages = [o.get("meta", {}).get("phase") for o in archive["objects"]
                              if o.get("id") in evidence_ids]
                    if phase == "P0" and plan["product_anchor"]["id"] not in evidence_ids:
                        raise ControlError("P0 Gate needs the explicitly approved P0 product definition")
                    if phase != "P0" and phase not in stages:
                        raise ControlError("Gate evidence must explicitly belong to the reviewed phase")
                cycle_id = ident(payload.get("cycle_id", record["cycles"][-1]["id"]), "cycle id")
                if not any(x["id"] == cycle_id for x in record["cycles"]):
                    raise ControlError("phase review cycle is unknown")
                entry = {"id": new_id("phase"), "cycle_id": cycle_id, "phase_id": phase, "decision": decision,
                         "evidence_ids": evidence_ids, "actor": actor, "reason": reason,
                         "at": now(), "local_only": True}
                record["phase_reviews"].append(entry)
                outcome = {"id": entry["id"],
                           "warning": "local review is not GitHub-published approval until committed"}

            self._event(record, name, actor, reason, subject)
            record["revision"] += 1
            self._write(record)
            return {"ok": True, "revision": record["revision"],
                    "event": record["events"][-1], "result": outcome}

    @staticmethod
    def _validate_dependencies(work):
        indexed = {x["id"]: x for x in work}
        if len(indexed) != len(work):
            raise ControlError("duplicate work IDs")
        visited, stack = set(), set()
        def dfs(wid):
            if wid in stack:
                raise ControlError("work dependency cycle")
            if wid in visited:
                return
            stack.add(wid)
            upstream = list(indexed[wid].get("depends_on", []))
            if indexed[wid].get("parent_work_id"):
                upstream.append(indexed[wid]["parent_work_id"])
            for dep in upstream:
                if dep not in indexed:
                    raise ControlError("unknown dependency: " + dep)
                dfs(dep)
            stack.remove(wid)
            visited.add(wid)
        for wid in indexed:
            dfs(wid)
