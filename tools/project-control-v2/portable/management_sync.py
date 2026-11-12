#!/usr/bin/env python3
"""Opt-in GitHub CAS sync for one target project's local draft management.json.

Only the exact local management record may be published. The remote branch
must equal the source HEAD used while editing, and the original Git blob
must match the remote file. This is deliberately NOT an auto-push, merge,
remote approval, Git pull, or multi-file transaction.
"""
from __future__ import annotations

import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
from urllib.parse import quote

from github_bridge import GitHub, RemoteError, repo_parts, assert_sha


MANAGED_PATH = ".project-control/management.json"
SCHEMA = "project-control-target-management/1"


class SyncConflict(ValueError):
    pass


def local_git(root, *args, missing_ok=False):
    try:
        p = subprocess.run(["git", "-C", str(root), *args], capture_output=True,
                           text=True, encoding="utf-8", errors="replace", timeout=15)
    except (OSError, subprocess.TimeoutExpired) as error:
        raise SyncConflict("local Git inspection unavailable") from error
    if p.returncode:
        if missing_ok:
            return None
        raise SyncConflict("local Git inspection failed: " + p.stderr.strip()[:180])
    return p.stdout.strip()


def check_record(obj, project_id):
    if (not isinstance(obj, dict) or obj.get("schema") != SCHEMA
            or obj.get("project_id") != project_id
            or obj.get("status") != "DRAFT"
            or type(obj.get("revision")) is not int or obj["revision"] < 1
            or not isinstance(obj.get("events"), list)):
        raise SyncConflict("invalid or formally approved management record")
    if len(obj["events"]) != obj["revision"] - 1:
        raise SyncConflict("management revision and event history are inconsistent")
    for i, event in enumerate(obj["events"]):
        if (not isinstance(event, dict) or event.get("revision") != i + 2
                or not isinstance(event.get("operation"), str)
                or not event.get("operation")):
            raise SyncConflict("management event revision lineage is invalid")
    for name in ("deliverables", "work", "requirements", "plans", "changes",
                 "gate_reviews", "risks", "milestones", "receipts"):
        entries = obj.get(name, [])
        if not isinstance(entries, list):
            raise SyncConflict("invalid management collection " + name)
        for x in entries:
            if not isinstance(x, dict):
                raise SyncConflict("malformed management collection " + name)
            if x.get("state") in ("APPROVED", "ACCEPTED", "GATE_PASS", "PASS"):
                raise SyncConflict("management draft must not self-approve deliverables")
            if x.get("execution_authorized") is True:
                raise SyncConflict("management draft must not grant execution authority")
            if x.get("decision") in ("APPROVED", "PASS"):
                raise SyncConflict("management draft contains unverified approval")
    return obj


def preview(root, client):
    root = Path(root).resolve(strict=True)
    if not root.is_dir():
        raise SyncConflict("target root is not a directory")
    if Path(local_git(root, "rev-parse", "--show-toplevel")).resolve() != root:
        raise SyncConflict("target must be the Git repository root")
    project_file = root / ".project-control/project.json"
    management_file = root / MANAGED_PATH
    if any(p.is_symlink() or not p.is_file() for p in (project_file, management_file)):
        raise SyncConflict("unsafe or missing project records")
    project = json.loads(project_file.read_text(encoding="utf-8"))
    parts = repo_parts(project.get("repo_url"))
    origin = repo_parts(local_git(root, "remote", "get-url", "origin"))
    if tuple(x.lower() for x in origin) != tuple(x.lower() for x in parts):
        raise SyncConflict("wrong Git remote")
    branch = local_git(root, "branch", "--show-current")
    if not branch or branch == "(detached HEAD)":
        raise SyncConflict("named Git branch required for remote sync")
    local_head = assert_sha(local_git(root, "rev-parse", "HEAD"))
    # Source HEAD is a known synced baseline only if it matches the live
    # remote branch HEAD. Local commits or rebase require manual reconciliation.
    remote_head = client.branch_head(parts, branch)
    if remote_head != local_head:
        raise SyncConflict("remote branch differs from local source HEAD; reconcile first")
    current_blob = local_git(root, "rev-parse", "HEAD:" + MANAGED_PATH, missing_ok=True)
    if current_blob is None:
        raise SyncConflict("no committed baseline management.json in local HEAD")
    current_blob = assert_sha(current_blob)
    remote_blob, remote_raw = client.file(parts, MANAGED_PATH, branch)
    if remote_blob != current_blob:
        raise SyncConflict("remote management.json changed relative to local baseline")
    raw = management_file.read_bytes()
    local_obj = json.loads(raw)
    remote_obj = json.loads(remote_raw)
    project_id = project.get("project_id")
    check_record(local_obj, project_id)
    check_record(remote_obj, project_id)
    if len(raw) > 2_000_000:
        raise SyncConflict("management record too large for controlled sync")
    if remote_obj["revision"] > local_obj["revision"]:
        raise SyncConflict("remote management revision is newer")
    if remote_obj["events"] != local_obj["events"][:len(remote_obj["events"])]:
        raise SyncConflict("remote event history does not match local record")
    if remote_obj["revision"] == local_obj["revision"] and raw != remote_raw:
        raise SyncConflict("same revision has conflicting content")
    if raw == remote_raw:
        return {"status": "ALREADY_MATCHING", "project_id": project_id,
                "branch": branch, "remote_head": remote_head}
    # Prevent publishing unrelated local file modifications, unknown task/
    # decision overrides, or untracked content to GitHub.
    tracked = local_git(root, "diff", "--name-only", "--relative")
    staged = local_git(root, "diff", "--cached", "--name-only")
    untracked = local_git(root, "ls-files", "--others", "--exclude-standard")
    extra = set((tracked + "\n" + staged + "\n" + untracked).splitlines()) - {"", MANAGED_PATH}
    if extra:
        raise SyncConflict("other unreviewed local files exist; cannot publish a single-file snapshot")
    return {
        "schema": "project-control-management-sync/1",
        "project_id": project_id, "repo_url": project["repo_url"], "branch": branch,
        "source_head": local_head, "expected_remote_blob_sha": remote_blob,
        "candidate_sha256": hashlib.sha256(raw).hexdigest(),
        "candidate_content": raw.decode("utf-8"),
        "remote_revision": remote_obj["revision"],
        "candidate_revision": local_obj["revision"],
        "status": "REVIEW_REQUIRED_BEFORE_REMOTE_WRITE",
        "warning": "Only one management.json file can be CAS-updated; test logs, formal approval and GitHub PR review are independent.",
    }


def publish(client, plan, *, apply=False):
    if plan.get("schema") != "project-control-management-sync/1":
        raise SyncConflict("unknown management publication plan")
    parts = repo_parts(plan.get("repo_url"))
    branch = plan.get("branch")
    head = assert_sha(plan.get("source_head"))
    blob = assert_sha(plan.get("expected_remote_blob_sha"))
    data = plan.get("candidate_content")
    if not isinstance(data, str) or hashlib.sha256(data.encode()).hexdigest() != plan.get("candidate_sha256"):
        raise SyncConflict("management candidate content digest mismatch")
    record = json.loads(data)
    check_record(record, plan.get("project_id"))
    if not apply:
        return {"status": "PREVIEW_NOT_PUBLISHED", "expected_head": head,
                "expected_file_blob": blob, "project_id": plan["project_id"]}
    if client.branch_head(parts, branch) != head:
        raise SyncConflict("remote branch changed since management preview")
    latest_blob, raw = client.file(parts, MANAGED_PATH, branch)
    if latest_blob != blob:
        raise SyncConflict("remote management file changed since preview")
    previous = json.loads(raw)
    check_record(previous, plan["project_id"])
    if previous["events"] != record["events"][:len(previous["events"])]:
        raise SyncConflict("remote management event history changed")
    if previous["revision"] >= record["revision"]:
        raise SyncConflict("management revision must advance")
    owner, repo = parts
    payload = {
        "message": "chore(project-control): publish candidate management revision "
                   + str(record["revision"]),
        "branch": branch, "sha": blob,
        "content": base64.b64encode(data.encode("utf-8")).decode("ascii")
    }
    try:
        response = client.request(
            "PUT", "/repos/" + quote(owner) + "/" + quote(repo)
            + "/contents/" + MANAGED_PATH, payload)
        commit = assert_sha(response["commit"]["sha"])
    except (KeyError, TypeError) as exc:
        raise SyncConflict("remote update unconfirmed; investigate before retry") from exc
    _, verified = client.file(parts, MANAGED_PATH, branch)
    if verified != data.encode("utf-8"):
        raise SyncConflict("remote data did not match locally reviewed draft")
    if client.commit_parents(parts, commit) != [head]:
        raise SyncConflict("branch moved during management sync; reconcile manually")
    return {"status": "REMOTE_MANAGEMENT_CANDIDATE_WRITTEN_VERIFIED",
            "project_id": plan["project_id"], "commit_sha": commit,
            "revision": record["revision"],
            "note": "No Gate or task approval was created; local checkout still needs a controlled update."}


def main(argv=None):
    p = argparse.ArgumentParser(description="Publish one reviewed local management draft with GitHub CAS")
    p.add_argument("--target", type=Path, required=True)
    p.add_argument("--apply", action="store_true")
    args = p.parse_args(argv)
    try:
        token = os.environ.get("PROJECT_CONTROL_GITHUB_TOKEN")
        if not token:
            raise SyncConflict("GitHub token missing; a connected manager AI writer may be used instead")
        client = GitHub(token)
        plan = preview(args.target, client)
        result = publish(client, plan, apply=args.apply) if "schema" in plan else plan
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 0
    except (SyncConflict, RemoteError, OSError, TypeError, KeyError, ValueError) as error:
        print(json.dumps({"status": "BLOCKED", "error": str(error)}, ensure_ascii=False), file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
