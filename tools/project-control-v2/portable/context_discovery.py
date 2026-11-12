#!/usr/bin/env python3
"""Structural audit of project discovery records. Never authorizes implementation."""
from __future__ import annotations
import json
from pathlib import Path

SCHEMA = "project-control-context-discovery/1"
TOPICS = ("purpose", "users_scenarios", "scope_constraints", "history_decisions",
          "requirements_design", "evidence_validation", "current_work_risks")
ROLES = {"coordinator", "specialist", "integration", "reviewer", "local_executor"}
STATES = {"OBSERVED", "REPORTED", "INFERRED", "APPROVED", "UNKNOWN", "CONFLICT"}


class DiscoveryError(ValueError):
    pass


def audit(record):
    if not isinstance(record, dict) or record.get("schema") != SCHEMA:
        raise DiscoveryError("invalid discovery schema")
    if record.get("role") not in ROLES:
        raise DiscoveryError("invalid role")
    for field in ("project_ref", "task_focus", "source_revision"):
        if not isinstance(record.get(field), str) or not record[field].strip():
            raise DiscoveryError("missing " + field)
    sources = record.get("sources")
    if not isinstance(sources, list):
        raise DiscoveryError("sources must be a list")
    source_ids = set()
    for source in sources:
        if not isinstance(source, dict) or any(not isinstance(source.get(k), str) or not source[k].strip()
                                                   for k in ("id", "locator", "type")):
            raise DiscoveryError("invalid source")
        if source["id"] in source_ids:
            raise DiscoveryError("duplicate source")
        source_ids.add(source["id"])
    topics = record.get("topics")
    if not isinstance(topics, dict) or set(topics) != set(TOPICS):
        raise DiscoveryError("missing topic map")
    unresolved = []
    for topic, entry in topics.items():
        if not isinstance(entry, dict) or entry.get("status") not in ("COVERED", "PARTIAL", "UNKNOWN", "NOT_APPLICABLE"):
            raise DiscoveryError("invalid topic " + topic)
        refs = entry.get("source_ids")
        if not isinstance(refs, list) or any(ref not in source_ids for ref in refs):
            raise DiscoveryError("invalid topic source " + topic)
        if entry["status"] == "COVERED" and not refs:
            raise DiscoveryError("unsubstantiated coverage " + topic)
        if entry["status"] != "COVERED":
            if not isinstance(entry.get("reason"), str) or not entry["reason"].strip():
                raise DiscoveryError("missing limitation " + topic)
            unresolved.append(topic)
    claims = record.get("claims")
    if not isinstance(claims, list):
        raise DiscoveryError("claims must be list")
    for claim in claims:
        if not isinstance(claim, dict) or claim.get("status") not in STATES:
            raise DiscoveryError("invalid claim")
        if not isinstance(claim.get("statement"), str) or not claim["statement"].strip():
            raise DiscoveryError("missing claim statement")
        refs = claim.get("source_ids")
        if not isinstance(refs, list) or any(ref not in source_ids for ref in refs):
            raise DiscoveryError("invalid claim sources")
        if claim["status"] != "UNKNOWN" and not refs:
            raise DiscoveryError("claim lacks provenance")
    disposition = record.get("next_disposition")
    if disposition not in ("CONTINUE_RESEARCH", "ASK_OWNER", "CANDIDATE_WORK"):
        raise DiscoveryError("invalid disposition")
    if disposition == "CANDIDATE_WORK" and not isinstance(record.get("readiness_rationale"), str):
        raise DiscoveryError("missing scope rationale")
    if record.get("claims_project_complete") and unresolved:
        raise DiscoveryError("false complete claim")
    return {"schema": "project-control-context-audit/1", "structurally_valid": True,\n            "legacy_read_only": True, "needs_current_assessment_v2": True,
            "unresolved_topics": unresolved, "next_disposition": disposition,
            "authorization_granted": False, "formal_approval_granted": False,
            "warning": "Legacy COVERED only tracks source presence. Reassess validity using context_assessment.py; no authority granted."}


def main():
    import argparse
    p = argparse.ArgumentParser()
    p.add_argument("record", type=Path)
    args = p.parse_args()
    try:
        print(json.dumps(audit(json.loads(args.record.read_text(encoding="utf-8"))),
                         ensure_ascii=False, indent=2))
    except (ValueError, OSError) as e:
        p.exit(2, "DISCOVERY_BLOCKED: " + str(e) + "\n")


if __name__ == "__main__":
    main()
