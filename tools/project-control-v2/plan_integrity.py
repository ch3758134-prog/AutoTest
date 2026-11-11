#!/usr/bin/env python3
"""Review-only reconciliation of a proposed development plan against engineering evidence.

A plan is NOT a requirements baseline, Gate approval, work authorisation, or
a command to dispatch Codex. This module never writes project or Git data.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import re

SCHEMA = "engineering-development-plan-candidate/1"
WORK_KINDS = {"RESEARCH", "DESIGN", "IMPLEMENT", "INTEGRATE", "VERIFY", "COORDINATE"}
HORIZONS = {"CURRENT", "NEXT", "LATER"}
MODES = {"EXPLORATORY", "CONTROLLED"}
SOURCE_ROLES = {"CONTEXT", "REQUIRED_BASELINE"}
WORK_STATES = {"PROPOSED", "PLANNED", "ACTIVE", "BLOCKED", "REPORTED_COMPLETE"}
IDENT = re.compile(r"^[a-zA-Z0-9][a-zA-Z0-9_.-]{0,99}$")


class PlanError(ValueError):
    pass


def _text(value, label, max_length=4000):
    if not isinstance(value, str) or not value.strip() or len(value) > max_length:
        raise PlanError(label + " must be nonempty bounded text")
    return value.strip()


def _id(value, label):
    value = _text(value, label, 100)
    if not IDENT.fullmatch(value):
        raise PlanError(label + " contains an invalid identifier")
    return value


def _records(value, label, limit=1000):
    if not isinstance(value, list) or len(value) > limit:
        raise PlanError(label + " must be an array of at most " + str(limit))
    return value


def _archive(archive):
    if not isinstance(archive, dict):
        raise PlanError("engineering archive must be an object")
    if "engineering" in archive:
        archive = archive["engineering"]
    if not isinstance(archive, dict) or not isinstance(archive.get("project"), dict):
        raise PlanError("engineering project archive is missing")
    return archive


def _assert_acyclic(work):
    graph = {w["id"]: w["depends_on"] for w in work}
    visited, visiting = set(), set()

    def visit(node):
        if node in visiting:
            raise PlanError("work dependency cycle includes " + node)
        if node in visited:
            return
        visiting.add(node)
        for dep in graph[node]:
            visit(dep)
        visiting.remove(node)
        visited.add(node)

    for node in graph:
        visit(node)


def assess(plan, archive):
    """Return a non-authoritative plan assessment; never mutates either input."""
    if not isinstance(plan, dict) or plan.get("schema") != SCHEMA:
        raise PlanError("unknown plan candidate schema")
    if plan.get("status") != "PROPOSED":
        raise PlanError("this review-only schema never publishes a plan baseline")
    pid = _id(plan.get("project_id"), "project_id")
    archive = _archive(archive)
    if archive["project"].get("id") != pid:
        raise PlanError("plan and evidence belong to different projects")
    _text(plan.get("purpose"), "purpose")
    _text(plan.get("planning_assumptions"), "planning_assumptions")
    objects = {o["id"]: o for o in _records(archive.get("objects"), "engineering objects")
               if isinstance(o, dict) and isinstance(o.get("id"), str)}
    source = plan.get("product_anchor")
    if not isinstance(source, dict):
        raise PlanError("product_anchor must pin a real approved object")
    anchor_id = _id(source.get("id"), "product_anchor.id")
    rev = source.get("revision")
    if not isinstance(rev, int) or isinstance(rev, bool) or rev < 1:
        raise PlanError("product_anchor.revision must be positive integer")

    issues = []
    def problem(code, place, detail, severity="BLOCKER"):
        issues.append({"code": code, "at": place, "detail": detail, "severity": severity})

    charter = objects.get(anchor_id)
    if not charter:
        problem("ANCHOR_MISSING", "product_anchor", "approved product anchor does not exist")
    elif charter.get("state") != "APPROVED":
        problem("ANCHOR_NOT_APPROVED", "product_anchor", "the source product definition is not approved")
    if charter and charter.get("revision") != rev:
        problem("ANCHOR_REVISION_CHANGED", "product_anchor", "the pinned product definition revision changed")

    deliverables = _records(plan.get("deliverables"), "deliverables")
    deliv_index = {}
    for d in deliverables:
        if not isinstance(d, dict):
            raise PlanError("deliverable must be an object")
        key = _id(d.get("id"), "deliverable.id")
        if key in deliv_index:
            raise PlanError("duplicate deliverable id: " + key)
        _text(d.get("title"), "deliverable.title")
        _text(d.get("success_condition"), "deliverable.success_condition")
        deliv_index[key] = d
        if d.get("commitment") not in ("PROPOSAL", "IN_SCOPE_FROM_APPROVED_BASELINE"):
            raise PlanError("deliverable commitment classification required")
        if d["commitment"] == "IN_SCOPE_FROM_APPROVED_BASELINE" and (
                not charter or charter.get("state") != "APPROVED" or charter.get("revision") != rev):
            problem("UNSUPPORTED_SCOPE_COMMITMENT", key, "current approved scope anchor is not valid")

    # A delivery milestone is a planning commitment, never a product
    # acceptance or phase Gate. Unknown dates may be left empty.
    milestones = _records(plan.get("milestones", []), "milestones")
    milestone_ids = set()
    for m in milestones:
        if not isinstance(m, dict):
            raise PlanError("milestone must be an object")
        mid = _id(m.get("id"), "milestone.id")
        if mid in milestone_ids:
            raise PlanError("duplicate milestone id: " + mid)
        milestone_ids.add(mid)
        _text(m.get("title"), "milestone.title")
        _text(m.get("exit_condition"), "milestone.exit_condition")
        if m.get("deliverable_id") not in deliv_index:
            problem("MILESTONE_UNLINKED", mid, "milestone lacks a known delivery objective")
        if m.get("target_date"):
            from datetime import date
            try:
                date.fromisoformat(m["target_date"])
            except (TypeError, ValueError):
                problem("INVALID_MILESTONE_DATE", mid, "milestone target date is invalid")
            if not m.get("date_basis"):
                problem("UNSUPPORTED_MILESTONE_DATE", mid,
                        "milestone date has no stated planning rationale")

    works = _records(plan.get("work"), "work")
    work_index = {}
    for w in works:
        if not isinstance(w, dict):
            raise PlanError("work item must be an object")
        key = _id(w.get("id"), "work.id")
        if key in work_index:
            raise PlanError("duplicate work id: " + key)
        _text(w.get("title"), "work.title")
        _text(w.get("expected_result"), "work.expected_result")
        _text(w.get("exit_condition"), "work.exit_condition")
        if w.get("kind") not in WORK_KINDS or w.get("mode") not in MODES or (
                w.get("horizon") not in HORIZONS or w.get("state") not in WORK_STATES):
            raise PlanError("work item missing valid kind/mode/horizon/state: " + key)
        if not isinstance(w.get("depends_on"), list):
            raise PlanError("depends_on must be array")
        if any(not isinstance(dep, str) for dep in w["depends_on"]):
            raise PlanError("dependency keys must be strings")
        if not isinstance(w.get("sources"), list):
            raise PlanError("sources must be array")
        work_index[key] = w
        if w.get("deliverable_id") not in deliv_index:
            problem("UNLINKED_WORK", key, "work lacks a declared delivery result")
        if w.get("planned_finish") and not w.get("schedule_basis"):
            problem("UNSUPPORTED_DATE", key, "planned finish is missing a scheduling basis")
        if w["state"] == "REPORTED_COMPLETE" and not w.get("evidence_refs"):
            problem("NO_COMPLETION_EVIDENCE", key, "reported completion lacks explicit evidence")
        if w["mode"] == "CONTROLLED" and not w["sources"]:
            problem("NO_CONTROLLED_INPUT", key, "controlled work has no pinned engineering input")
        for s in w["sources"]:
            if not isinstance(s, dict) or s.get("role") not in SOURCE_ROLES:
                raise PlanError("source must declare CONTEXT or REQUIRED_BASELINE")
            refid = _id(s.get("object_id"), "source.object_id")
            revision = s.get("revision")
            if not isinstance(revision, int) or isinstance(revision, bool) or revision < 1:
                raise PlanError("source revision must be positive integer")
            obj = objects.get(refid)
            if not obj:
                problem("SOURCE_MISSING", key, "source object does not exist: " + refid)
                continue
            if obj.get("revision") != revision:
                problem("SOURCE_STALE", key, "pinned revision changed: " + refid)
            if s["role"] == "REQUIRED_BASELINE" and obj.get("state") != "APPROVED":
                problem("UNAPPROVED_INPUT", key, "required formal input is not approved: " + refid)
            if s["role"] == "REQUIRED_BASELINE" and w["mode"] != "CONTROLLED":
                problem("INPUT_ROLE_MISMATCH", key,
                        "exploratory work should cite evidence as CONTEXT", "WARNING")
        for dep in w["depends_on"]:
            if dep == key or dep not in [x.get("id") for x in works if isinstance(x, dict)]:
                problem("INVALID_DEPENDENCY", key, "dependency is unknown or self-referential: " + dep)

    if not any(i["code"] == "INVALID_DEPENDENCY" for i in issues):
        _assert_acyclic(works)

    # Propagate only confirmed local blockers; this does not modify upstream
    # baselines, imply a stage rollback, or stop independent research.
    blocked = {i["at"] for i in issues if i["severity"] == "BLOCKER" and i["at"] in work_index}
    changed = True
    while changed:
        changed = False
        for w in works:
            if w["id"] in blocked:
                continue
            if any(dep in blocked for dep in w["depends_on"]):
                blocked.add(w["id"])
                problem("UPSTREAM_WORK_BLOCKED", w["id"],
                        "one or more prerequisite work items need reassessment")
                changed = True
    if any(i["at"] == "product_anchor" and i["severity"] == "BLOCKER" for i in issues):
        # Explicitly report global planning reconciliation rather than
        # silently reinterpreting all candidate work as cancelled.
        problem("SCOPE_REBASE_REQUIRED", "plan", "approved project scope anchor must be reconciled")
    return {
        "schema": "engineering-plan-assessment/1",
        "project_id": pid,
        "plan_status": "PROPOSED_NOT_APPROVED",
        "product_anchor": {"id": anchor_id, "revision": rev,
                           "verified": bool(charter and charter.get("state") == "APPROVED"
                                            and charter.get("revision") == rev)},
        "deliverables": len(deliv_index),
        "milestones": len(milestone_ids),
        "work": [{"id": w["id"], "kind": w["kind"], "mode": w["mode"],
                  "horizon": w["horizon"], "state": w["state"],
                  "assessment": "NEEDS_REVIEW" if w["id"] in blocked else "NO_RECORDED_BLOCKER",
                  "note": "NO_RECORDED_BLOCKER is not permission to execute or approval"}
                 for w in works],
        "issues": issues,
        "counts": {"blockers": sum(i["severity"] == "BLOCKER" for i in issues),
                   "warnings": sum(i["severity"] == "WARNING" for i in issues)},
        "limitations": [
            "Neither this assessment nor a candidate plan is a project scope approval.",
            "Review cannot certify actual implementation, test execution, dates or Gate status.",
            "Unrecorded dependencies and unavailable external Git/Codex evidence remain unknown.",
        ],
    }


def main(argv=None):
    parser = argparse.ArgumentParser(description="Review a proposed development plan without writing")
    parser.add_argument("--plan", type=Path, required=True)
    parser.add_argument("--archive", type=Path, required=True)
    args = parser.parse_args(argv)
    result = assess(json.loads(args.plan.read_text(encoding="utf-8")),
                    json.loads(args.archive.read_text(encoding="utf-8")))
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
