#!/usr/bin/env python3
"""Validate and stage a Codex execution receipt in Git-tracked project evidence.

Codex must supply actual command results and evidence references. This helper
does NOT collect private chat history, certify a real ECU test, commit, push, or
approve any engineering work. Review receipt content for secrets before commit.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path, PurePosixPath
import re
import subprocess
import uuid

RECEIPTS = Path("tools/project_control/receipts.json")
PROJECT = Path("tools/project_control/project.json")
SCHEMA = "project-control-receipt/1"
SHA = re.compile(r"^[0-9a-fA-F]{40}$")
OUTCOMES = {"PASS", "FAIL", "BLOCKED", "INCONCLUSIVE"}


class ReceiptError(ValueError):
    pass


def required(value, label, limit=6000):
    if not isinstance(value, str) or not value.strip() or len(value) > limit:
        raise ReceiptError(label + " is required and must be bounded text")
    return value.strip()


def validate(receipt, project):
    if not isinstance(receipt, dict) or receipt.get("schema") != SCHEMA:
        raise ReceiptError("unrecognized receipt schema")
    if receipt.get("project_id") != project["project"]["id"]:
        raise ReceiptError("receipt project does not match")
    if receipt.get("work_id") not in {w["id"] for w in project["work"]}:
        raise ReceiptError("receipt must cite an existing planned work item")
    if receipt.get("outcome") not in OUTCOMES:
        raise ReceiptError("receipt outcome must be PASS/FAIL/BLOCKED/INCONCLUSIVE")
    for key in ("input_sha", "actual_head_sha"):
        if not isinstance(receipt.get(key), str) or not SHA.fullmatch(receipt[key]):
            raise ReceiptError(key + " must identify an exact 40-character Git commit")
    for key in ("branch", "executor", "summary"):
        required(receipt.get(key), key)
    commands = receipt.get("commands")
    if not isinstance(commands, list) or not commands or len(commands) > 100:
        raise ReceiptError("at least one actually executed command or attempted action is required")
    for c in commands:
        if not isinstance(c, dict):
            raise ReceiptError("commands must contain objects")
        required(c.get("command"), "command", 2000)
        exit_code = c.get("exit_code")
        if not (exit_code is None or (isinstance(exit_code, int) and not isinstance(exit_code, bool)
                                       and -10000 <= exit_code <= 10000)):
            raise ReceiptError("exit_code must be an integer or null when not run")
        required(c.get("observation"), "command observation", 3000)
    evidence = receipt.get("evidence_refs", [])
    if not isinstance(evidence, list) or len(evidence) > 100:
        raise ReceiptError("evidence_refs must be a bounded list")
    for path in evidence:
        required(path, "evidence_ref", 1500)
        if path.startswith("https://github.com/"):
            continue
        parsed = PurePosixPath(path)
        if parsed.is_absolute() or ".." in parsed.parts or path.startswith(("~", "\\")):
            raise ReceiptError("evidence must be a repository-relative path or a GitHub URL")
    if receipt["outcome"] == "PASS" and any(c["exit_code"] not in (0, None) for c in commands):
        raise ReceiptError("a PASS outcome cannot hide a recorded failed command")
    if receipt["outcome"] == "PASS" and any(c["exit_code"] is None for c in commands):
        raise ReceiptError("a PASS outcome needs recorded command exit codes")
    return True


def stage(repo: Path, receipt: dict):
    repo = Path(repo).resolve()
    project = json.loads((repo / PROJECT).read_text(encoding="utf-8"))
    validate(receipt, project)
    target = repo / RECEIPTS
    data = json.loads(target.read_text(encoding="utf-8"))
    if data.get("schema") != "project-control-receipts/1" or not isinstance(data.get("runs"), list):
        raise ReceiptError("Git evidence ledger has unexpected structure")
    new_id = receipt.get("id") or "RUN-" + uuid.uuid4().hex[:12].upper()
    if any(run.get("id") == new_id for run in data["runs"]):
        raise ReceiptError("receipt id already exists")
    entry = dict(receipt)
    entry["id"] = new_id
    entry["reported_at"] = datetime.now(timezone.utc).isoformat()
    entry["status"] = "EXECUTION_REPORT_NOT_PRODUCT_ACCEPTANCE"
    data["runs"].append(entry)
    # This is intentionally a worktree edit; only an explicit Codex/owner
    # Git commit can publish this evidence for other ChatGPT conversations.
    target.write_text(json.dumps(data,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    return {"id":new_id,"work_id":receipt["work_id"],
            "note":"Worktree record staged. Review for secrets, then commit/push via an authorized Git workflow."}


def main():
    ap=argparse.ArgumentParser()
    ap.add_argument("--repo",type=Path,default=Path(__file__).resolve().parents[2])
    ap.add_argument("--receipt",type=Path,required=True)
    args=ap.parse_args()
    result=stage(args.repo,json.loads(args.receipt.read_text(encoding="utf-8")))
    print(json.dumps(result,ensure_ascii=False,indent=2))


if __name__=="__main__":
    main()
