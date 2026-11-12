#!/usr/bin/env python3
"""Portable Project Control management server.

Serve several explicitly registered local Git workspaces without importing the
pilot-project data. No Git checkout, push, merge, reset or cleanup.
All writes are local draft management records and require the session owner token.
"""
from __future__ import annotations

import argparse
from contextlib import contextmanager
import hashlib
from receipt_evidence import assess as assess_receipt, verify_test_logs, ReceiptError
from remote_sync import inspect as inspect_remote, SyncError
from remote_module_status import inspect as inspect_remote_modules
from github_bridge import GitHub, RemoteError
from governance import apply as apply_governance, ACTIONS as GOVERNANCE_ACTIONS, KINDS as GOVERNANCE_FIELDS
from module_control import (add_module, assign_work as assign_module_work,
                            defined_modules, local_source_snapshot, assess_candidate_paths,
                            ModuleError)
from workspace_manager import (read_registry as workspace_registry, inspect_one as inspect_workspace,
                               canonical as workspace_origin, registry_location, registry_lock)
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path
import re
import secrets
import subprocess
import threading
from urllib.parse import parse_qs, urlsplit
import sys

HERE = Path(__file__).resolve().parent
UI = HERE / "web"
ID = re.compile(r"^[a-zA-Z0-9][a-zA-Z0-9_.-]{0,99}$")
SHA = re.compile(r"^[0-9a-f]{40}$")
SCHEMA = "project-control-target-management/1"
MAX_BODY = 65_536


class ManagementError(ValueError):
    pass


def stamp():
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def checked_text(value, label, maximum=3000):
    if not isinstance(value, str) or not value.strip() or len(value) > maximum:
        raise ManagementError(label + " must be nonblank bounded text")
    return value.strip()


def checked_id(value, label):
    value = checked_text(value, label, 100)
    if not ID.fullmatch(value):
        raise ManagementError("invalid " + label)
    return value


def git(root, *args):
    try:
        result = subprocess.run(["git", "-C", str(root), *args], text=True,
                                encoding="utf-8", errors="replace",
                                capture_output=True, timeout=12)
    except (OSError, subprocess.TimeoutExpired) as error:
        raise ManagementError("Git is unavailable: " + str(error)) from error
    if result.returncode:
        raise ManagementError("Git inspection failed: " + result.stderr.strip()[:200])
    return result.stdout.strip()


def origin_identity(remote):
    # No remote operations here. Only compare the local identity to project.json.
    from urllib.parse import urlsplit
    u = urlsplit(remote)
    if u.scheme != "https" or u.hostname != "github.com" or u.username or u.password or u.port or u.query or u.fragment:
        raise ManagementError("only a registered HTTPS GitHub origin is supported")
    pair = u.path.strip("/").removesuffix(".git").split("/")
    if len(pair) != 2 or not all(re.fullmatch(r"[a-zA-Z0-9_.-]+", a) for a in pair):
        raise ManagementError("invalid GitHub origin identity")
    return "/".join(pair).lower()


def load_json(file):
    try:
        data = json.loads(file.read_text(encoding="utf-8"))
    except (OSError, ValueError) as error:
        raise ManagementError("cannot read management file: " + file.name) from error
    if not isinstance(data, dict):
        raise ManagementError("invalid management JSON: " + file.name)
    return data


@contextmanager
def manager_file_lock(root):
    """Cooperative cross-process lock stored in Git metadata, not tracked files.

    A stale lock is never silently deleted; a human inspects the old process.
    This is not protection from processes that ignore the protocol.
    """
    raw = git(root, "rev-parse", "--git-path", "project-control/management.lock")
    path = Path(raw)
    if not path.is_absolute():
        path = Path(root) / path
    path.parent.mkdir(parents=True, exist_ok=True)
    try:
        fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    except FileExistsError as exc:
        raise ManagementError("project management is locked by another process; no overwrite") from exc
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as handle:
            handle.write("pid=" + str(os.getpid()) + " at=" + stamp() + "\n")
            handle.flush()
            os.fsync(handle.fileno())
        yield
    finally:
        path.unlink(missing_ok=True)


def empty_management(project_id):
    return {
        "schema": SCHEMA, "project_id": project_id, "revision": 1,
        "status": "DRAFT", "deliverables": [], "work": [], "milestones": [],
        "risks": [], "progress": {}, "events": [], "receipts": [],
        "requirements": [], "plans": [], "changes": [], "gate_reviews": [], "trace_links": [],
        "modules": []
    }


def project_catalog_file(registry_dir=None):
    """Persistent local application registration, separate from target projects."""
    return registry_location(registry_dir) / "targets.json"


def read_project_catalog(registry_dir=None):
    path = project_catalog_file(registry_dir)
    if not path.exists():
        return []
    if path.is_symlink() or not path.is_file() or path.stat().st_size > 65536:
        raise ManagementError("unsafe local project registration catalog")
    value = load_json(path)
    paths = value.get("paths")
    if (value.get("schema") != "project-control-local-targets/1"
            or not isinstance(paths, list) or len(paths) > 64
            or any(not isinstance(p, str) or not p or not Path(p).is_absolute()
                   for p in paths) or len(paths) != len(set(paths))):
        raise ManagementError("invalid locally registered project paths")
    return paths


def persist_registered_target(path, registry_dir=None):
    """After an authenticated explicit registration only; never touch target Git."""
    with registry_lock(registry_dir):
        current = read_project_catalog(registry_dir)
        target = str(Path(path).resolve(strict=True))
        if target in current:
            return
        if len(current) >= 64:
            raise ManagementError("too many saved target registrations")
        destination = project_catalog_file(registry_dir)
        raw = json.dumps(
            {"schema": "project-control-local-targets/1", "paths": current + [target]},
            ensure_ascii=False, indent=2).encode("utf-8") + b"\n"
        tmp = destination.with_name(".targets." + secrets.token_hex(8) + ".tmp")
        try:
            with tmp.open("xb") as stream:
                stream.write(raw)
                stream.flush()
                os.fsync(stream.fileno())
            if destination.is_symlink():
                raise ManagementError("unsafe local target registration file")
            os.replace(tmp, destination)
        finally:
            tmp.unlink(missing_ok=True)


class Target:
    def __init__(self, root, registry_dir=None):
        self.registry_dir = registry_dir
        self.root = Path(root).resolve(strict=True)
        if not self.root.is_dir():
            raise ManagementError("target path must be a directory")
        actual = Path(git(self.root, "rev-parse", "--show-toplevel")).resolve(strict=True)
        if actual != self.root:
            raise ManagementError("target must be a top-level Git worktree")
        self.directory = self.root / ".project-control"
        if not self.directory.is_dir() or self.directory.is_symlink():
            raise ManagementError("target has no independent .project-control directory")
        self.project_path = self.directory / "project.json"
        self.data_path = self.directory / "management.json"
        self.lock = threading.RLock()
        self.check_identity()
        self.initial = load_json(self.project_path)
        if self.initial.get("schema") != "project-control-target-project/1":
            raise ManagementError("unsupported project identity schema")
        self.project_id = checked_id(self.initial.get("project_id"), "project_id")
        if not self.data_path.is_file():
            raise ManagementError("management.json is missing: initialize or upgrade the target scaffold")
        management = load_json(self.data_path)
        self.check_record(management)

    def check_identity(self):
        # Also guards against symlink substitutions when reading a target.
        for name in ("project.json", "assets.json", "tasks.json", "decisions.json", "management.json"):
            p = self.directory / name
            if p.is_symlink() or not p.is_file() or p.resolve().parent != self.directory:
                raise ManagementError("missing or unsafe target management file: " + name)
        registered = load_json(self.project_path).get("repo_url")
        actual = git(self.root, "remote", "get-url", "origin")
        if origin_identity(registered) != origin_identity(actual):
            raise ManagementError("Git origin and project record disagree")

    def check_record(self, value):
        if (value.get("schema") != SCHEMA or value.get("project_id") != self.project_id
                or type(value.get("revision")) is not int or value["revision"] < 1):
            raise ManagementError("management record or revision mismatch")
        for name in ("deliverables", "work", "milestones", "risks", "events"):
            if not isinstance(value.get(name), list):
                raise ManagementError("invalid management collection: " + name)
        if not isinstance(value.get("progress"), dict):
            raise ManagementError("invalid progress map")
        if "receipts" in value and not isinstance(value["receipts"], list):
            raise ManagementError("invalid receipt collection")
        for name in GOVERNANCE_FIELDS:
            if name in value and not isinstance(value[name], list):
                raise ManagementError("invalid governance collection: " + name)
        try:
            defined_modules(value)
        except ModuleError as error:
            raise ManagementError("invalid module boundary registry: " + str(error)) from error

    def head_or_none(self):
        # A brand-new, locally initialized Git repository may have no first commit.
        check = subprocess.run(["git", "-C", str(self.root), "rev-parse", "--verify", "HEAD"],
                               capture_output=True, text=True, timeout=12)
        if check.returncode == 0 and SHA.fullmatch(check.stdout.strip()):
            return check.stdout.strip()
        # Do not interpret missing HEAD as remote synchronization or approved setup.
        return None

    def model(self):
        with self.lock:
            self.check_identity()
            project = load_json(self.project_path)
            if (project.get("project_id") != self.project_id
                    or project.get("repo_url") != self.initial.get("repo_url")):
                raise ManagementError("project identity or repository changed; reopen the server")
            management = load_json(self.data_path)
            self.check_record(management)
            management.setdefault("receipts", [])
            management.setdefault("modules", [])
            for name in GOVERNANCE_FIELDS:
                management.setdefault(name, [])
            assets = load_json(self.directory / "assets.json")
            decisions = load_json(self.directory / "decisions.json")
            tasks = load_json(self.directory / "tasks.json")
            if any(x.get("project_id") != self.project_id for x in (assets, decisions, tasks)):
                raise ManagementError("cross-project record mismatch")
            if management["modules"]:
                if self.head_or_none():
                    code_status = local_source_snapshot(self.root, management)
                else:
                    code_status = {"source": "NO_COMMITTED_SOURCE",
                                   "registered_modules": [], "git_head": None,
                                   "unowned_working_changes": [],
                                   "note": "A new project has not yet committed a source baseline."}
            else:
                code_status = {"source": "MODULE_BOUNDARIES_NOT_REGISTERED",
                               "registered_modules": [], "git_head": self.head_or_none(),
                               "unowned_working_changes": [],
                               "note": "Register module responsibility before reporting module code state."}
            code_status["candidates"] = []
            for receipt in management["receipts"]:
                if not receipt.get("changed_paths") or not management["modules"]:
                    continue
                task = next((item for item in tasks.get("tasks", [])
                             if item.get("id") == receipt.get("task_id")), None)
                task_module = task.get("module_id") if task else None
                try:
                    boundary = assess_candidate_paths(management,
                                                      receipt["changed_paths"], task_module)
                except ModuleError:
                    continue
                code_status["candidates"].append({
                    "task_id": receipt.get("task_id"),
                    "receipt_id": receipt.get("id"),
                    "candidate_head": receipt.get("candidate_head"),
                    "git_assessment": receipt.get("git_assessment"),
                    "review_status": receipt.get("review_status"),
                    "boundary_assessment": boundary["result"],
                    "touched_modules": boundary["affected_module_ids"],
                    "cross_module_paths": boundary["cross_module_paths"],
                    "unowned_paths": boundary["unowned_paths"],
                    "interface_changes": boundary["interface_changes"],
                    "remote_pr_verified": False, "ci_verified": False,
                    "integrated": "UNKNOWN",
                })
            return {
                "project": project, "management": management, "code_status": code_status,
                "assets": assets, "decisions": decisions, "tasks": tasks,
                "git": {
                    "head": self.head_or_none(),
                    "branch": git(self.root, "branch", "--show-current") or "(detached)",
                    "dirty": bool(git(self.root, "status", "--porcelain", "--untracked-files=normal")),
                    "source": "LOCAL_WORKTREE_NOT_REMOTE_VERIFIED",
                },
                "limits": {"formal_approval": False, "remote_write": False,
                           "task_ready_promotion": False, "local_cleanup": False},
            }

    def remote_status(self):
        with self.lock:
            self.check_identity()
            return inspect_remote(self.root, check_remote=True)

    def module_remote_status(self):
        """User-triggered, strictly read-only GitHub code evidence."""
        with self.lock:
            self.check_identity()
            project = load_json(self.project_path)
            management = load_json(self.data_path)
            self.check_record(management)
            # A public GitHub project can be inspected without an API token.
            # Private repositories require explicitly configured credentials.
            client = GitHub(os.environ.get("PROJECT_CONTROL_GITHUB_TOKEN"))
            return inspect_remote_modules(client, project, management, recent_pr_limit=10)

    def collect_local_receipts(self):
        """Import only the registered worktree's locally saved Codex receipts.

        No other directories are searched. Worktree-specific Git metadata
        must be explicitly registered before cross-worktree collection is possible.
        """
        with self.lock:
            self.check_identity()
            location = git(self.root, "rev-parse", "--git-path", "project-control")
            folder = Path(location)
            if not folder.is_absolute():
                folder = self.root / folder
            if folder.is_symlink():
                raise ManagementError("unsafe Git receipt directory")
            if not folder.is_dir():
                return {"project_id": self.project_id, "imported": [], "skipped": [],
                        "held": [], "scope": "CURRENT_REGISTERED_WORKTREE_ONLY"}
            paths = sorted(folder.glob("receipt-*.json"))
            if len(paths) > 100:
                raise ManagementError("too many unreviewed receipts; manual triage required")
            imported, skipped, held = [], [], []
            for file in paths:
                if file.is_symlink() or not file.is_file() or file.stat().st_size > 65536:
                    held.append({"file": file.name, "reason": "unsafe receipt file"})
                    continue
                try:
                    raw = json.loads(file.read_text(encoding="utf-8"))
                    rid = raw.get("receipt_id") if isinstance(raw, dict) else None
                    if not isinstance(rid, str) or not ID.fullmatch(rid):
                        raise ManagementError("invalid receipt identifier")
                    record = load_json(self.data_path)
                    existing = record.get("receipts", [])
                    if any(item.get("id") == rid for item in existing):
                        skipped.append(rid)
                        continue
                    self.apply("record_receipt", {
                        "expected_revision": record["revision"],
                        "reason": "从已登记的当前 Git 工作区接收 Codex 待审回执",
                        "receipt": raw
                    })
                    imported.append(rid)
                except (OSError, ValueError, TypeError, ManagementError) as error:
                    held.append({"file": file.name, "reason": str(error)[:220]})
            return {"project_id": self.project_id, "imported": imported,
                    "skipped": skipped, "held": held,
                    "scope": "CURRENT_REGISTERED_WORKTREE_ONLY"}

    def workspace_status(self, registry_dir=None):
        """Registered local workspaces, not arbitrary disk search."""
        with self.lock:
            self.check_identity()
            registry = workspace_registry(registry_dir or self.registry_dir)
            matches = [x for x in registry["workspaces"]
                       if x.get("project_id") == self.project_id
                       and x.get("repo_url") == workspace_origin(self.initial["repo_url"])]
            return {"project_id": self.project_id, "registry_revision": registry["revision"],
                    "workspaces": [inspect_workspace(x) for x in matches],
                    "note": "Sensitive local paths are returned only to an authenticated localhost client."}

    def collect_registered_receipts(self, registry_dir=None):
        """Read reports from EXPLICITLY registered workspaces only.

        No fetch/prune/cleanup and no recursive search. The manager may not
        hold the candidate commit, in which case the receipt stays unverified.
        """
        with self.lock:
            self.check_identity()
            data = workspace_registry(registry_dir or self.registry_dir)
            candidates = [x for x in data["workspaces"]
                          if x.get("project_id") == self.project_id
                          and x.get("repo_url") == workspace_origin(self.initial["repo_url"])
                          and x.get("state") == "ACTIVE"]
            imported, skipped, held = [], [], []
            for registered in candidates:
                observation = inspect_workspace(registered)
                if any("MISMATCH" in s or s in ("WRONG_REPOSITORY", "MISSING_OR_UNSAFE_PATH")
                       for s in observation.get("issues", [])):
                    held.append({"workspace_id": registered["workspace_id"],
                                 "reason": "unsafe or mismatching registered Git workspace"})
                    continue
                root = Path(registered["canonical_path"])
                try:
                    sub = git(root, "rev-parse", "--git-path", "project-control")
                    folder = Path(sub)
                    if not folder.is_absolute():
                        folder = root / folder
                    if folder.is_symlink() or not folder.is_dir():
                        continue
                    files = sorted(folder.glob("receipt-*.json"))
                    if len(files) > 100:
                        raise ManagementError("too many local receipts for review")
                    for path in files:
                        if path.is_symlink() or not path.is_file() or path.stat().st_size > 65536:
                            held.append({"workspace_id": registered["workspace_id"],
                                         "reason": "unsafe evidence file"})
                            continue
                        try:
                            report = json.loads(path.read_text(encoding="utf-8"))
                            rid = report.get("receipt_id")
                            if not isinstance(rid, str) or not ID.fullmatch(rid):
                                raise ManagementError("invalid local receipt")
                            current = load_json(self.data_path)
                            if any(x.get("id") == rid for x in current.get("receipts", [])):
                                skipped.append(rid)
                                continue
                            self.apply("record_receipt", {
                                "expected_revision": current["revision"],
                                "reason": "导入已登记工作区的 Codex 候选结果",
                                "receipt": report
                            })
                            if report.get("test_evidence"):
                                latest = load_json(self.data_path)
                                self.apply("verify_log_evidence", {
                                    "expected_revision": latest["revision"],
                                    "reason": "复核已登记工作区中的原始日志 SHA-256",
                                    "id": rid,
                                    "workspace_id": registered["workspace_id"]
                                })
                            imported.append(rid)
                        except (OSError, ValueError, TypeError, ManagementError) as err:
                            held.append({"workspace_id": registered["workspace_id"],
                                         "reason": str(err)[:200]})
                except (OSError, ValueError, ManagementError) as err:
                    held.append({"workspace_id": registered["workspace_id"],
                                 "reason": str(err)[:200]})
            return {"project_id": self.project_id, "registered_examined": len(candidates),
                    "imported": imported, "skipped": skipped, "held": held,
                    "scope": "EXPLICIT_LOCAL_REGISTRY_ONLY"}

    def apply(self, name, payload):
        if not isinstance(payload, dict):
            raise ManagementError("action payload must be a JSON object")
        reason = checked_text(payload.get("reason"), "change reason", 2000)
        revision = payload.get("expected_revision")
        if type(revision) is not int:
            raise ManagementError("integer expected_revision required")
        with self.lock, manager_file_lock(self.root):
            self.check_identity()
            original_bytes = self.data_path.read_bytes()
            try:
                record = json.loads(original_bytes.decode("utf-8"))
            except (ValueError, UnicodeDecodeError) as exc:
                raise ManagementError("unreadable management record") from exc
            self.check_record(record)
            if revision != record["revision"]:
                raise ManagementError("stale revision; refresh and review current changes")
            record.setdefault("receipts", [])
            record.setdefault("modules", [])
            allowed_names = {"create_deliverable", "create_work", "set_progress", "raise_risk",
                             "create_milestone", "record_receipt", "review_receipt",
                             "verify_log_evidence", "create_module", "assign_module_work"} | GOVERNANCE_ACTIONS
            if name not in allowed_names:
                raise ManagementError("operation not supported: no approval/push/merge is exposed")
            objects = record["deliverables"]
            works = record["work"]
            if name in GOVERNANCE_ACTIONS:
                apply_governance(record, name, payload)
            elif name == "create_module":
                add_module(record, payload)
            elif name == "assign_module_work":
                assign_module_work(record, payload)
            elif name == "create_deliverable":
                value = {"id": checked_id(payload.get("id"), "deliverable id"),
                         "title": checked_text(payload.get("title"), "title", 240),
                         "success_condition": checked_text(payload.get("success_condition"), "success condition"),
                         "state": "PROPOSED"}
                if any(x.get("id") == value["id"] for x in objects):
                    raise ManagementError("duplicate deliverable")
                objects.append(value)
            elif name == "create_work":
                delivery = checked_id(payload.get("deliverable_id"), "deliverable_id")
                if not any(d["id"] == delivery for d in objects):
                    raise ManagementError("work needs a valid deliverable")
                kind = payload.get("kind")
                if kind not in ("RESEARCH", "REQUIREMENTS", "DESIGN", "IMPLEMENT",
                                "INTEGRATE", "VERIFY", "COORDINATE"):
                    raise ManagementError("invalid work kind")
                work = {"id": checked_id(payload.get("id"), "work id"),
                        "title": checked_text(payload.get("title"), "work title", 240),
                        "kind": kind, "deliverable_id": delivery,
                        "expected_result": checked_text(payload.get("expected_result"), "expected_result"),
                        "state": "PROPOSED", "execution_authorized": False,
                        "depends_on": []}
                if any(w.get("id") == work["id"] for w in works):
                    raise ManagementError("duplicate work ID")
                works.append(work)
            elif name == "set_progress":
                task = checked_id(payload.get("id"), "work id")
                if not any(w["id"] == task for w in works):
                    raise ManagementError("unknown work item")
                state = payload.get("state")
                if state not in ("NOT_STARTED", "IN_PROGRESS", "BLOCKED", "REPORTED_COMPLETE"):
                    raise ManagementError("only nonapproved work progress may be recorded")
                evidence = payload.get("evidence_refs", [])
                if (not isinstance(evidence, list) or len(evidence) > 20 or
                        any(not isinstance(s, str) or not s.strip() or len(s) > 300 for s in evidence)):
                    raise ManagementError("invalid evidence references")
                if state == "REPORTED_COMPLETE" and not evidence:
                    raise ManagementError("completion report needs evidence")
                record["progress"][task] = {
                    "state": state, "evidence_refs": evidence, "note": reason,
                    "recorded_at": stamp(), "verification": "NOT_VERIFIED",
                }
            elif name == "raise_risk":
                entry = {"id": checked_id(payload.get("id"), "risk id"),
                         "title": checked_text(payload.get("title"), "risk title", 240),
                         "description": checked_text(payload.get("description"), "risk description"),
                         "mitigation": checked_text(payload.get("mitigation"), "mitigation"),
                         "state": "OPEN", "recorded_at": stamp()}
                if any(x.get("id") == entry["id"] for x in record["risks"]):
                    raise ManagementError("duplicate risk ID")
                record["risks"].append(entry)
            elif name == "record_receipt":
                incoming = payload.get("receipt")
                project = load_json(self.project_path)
                tasks_path = self.directory / "tasks.json"
                tasks = load_json(tasks_path)
                receipt = assess_receipt(
                    self.root, project, tasks,
                    hashlib.sha256(tasks_path.read_bytes()).hexdigest(), incoming)
                if any(x.get("id") == receipt["id"] for x in record["receipts"]):
                    raise ManagementError("receipt already imported; cannot overwrite evidence")
                receipt["recorded_at"] = stamp()
                receipt["import_reason"] = reason
                record["receipts"].append(receipt)
            elif name == "verify_log_evidence":
                rid = checked_id(payload.get("id"), "receipt ID")
                workspace_id = checked_id(payload.get("workspace_id"), "workspace ID")
                found = [x for x in record["receipts"] if x.get("id") == rid]
                if len(found) != 1:
                    raise ManagementError("receipt must already be imported uniquely")
                registry = workspace_registry(self.registry_dir)
                matches = [x for x in registry["workspaces"] if
                           x.get("workspace_id") == workspace_id
                           and x.get("project_id") == self.project_id
                           and x.get("repo_url") == workspace_origin(self.initial["repo_url"])
                           and x.get("state") == "ACTIVE"]
                if len(matches) != 1:
                    raise ManagementError("receipt worktree is not registered as active")
                source_root = Path(matches[0]["canonical_path"])
                observed = inspect_workspace(matches[0])
                if ("WRONG_REPOSITORY" in observed["issues"]
                        or "MISSING_OR_UNSAFE_PATH" in observed["issues"]):
                    raise ManagementError("registered receipt workspace cannot be trusted")
                sub = git(source_root, "rev-parse", "--git-path", "project-control")
                folder = Path(sub)
                if not folder.is_absolute():
                    folder = source_root / folder
                file = folder / (rid + ".json")
                if file.is_symlink() or not file.is_file() or file.stat().st_size > 65536:
                    raise ManagementError("original Codex receipt unavailable")
                original = json.loads(file.read_text(encoding="utf-8"))
                entry = found[0]
                if (original.get("project_id") != self.project_id or
                        original.get("candidate_head") != entry["candidate_head"] or
                        original.get("test_evidence") != entry.get("test_evidence")):
                    raise ManagementError("local receipt differs from recorded evidence")
                status = verify_test_logs(source_root, original)
                entry["test_assessment"] = status
                entry["log_verified_at"] = stamp()
                entry["log_verified_workspace_id"] = workspace_id
                # Verifying the raw log SHA is NOT a test/CI/Gate approval.
            elif name == "review_receipt":
                rid = checked_id(payload.get("id"), "receipt id")
                found = [x for x in record["receipts"] if x.get("id") == rid]
                if len(found) != 1:
                    raise ManagementError("receipt not found uniquely")
                decision = payload.get("decision")
                if decision not in ("NEEDS_MORE_EVIDENCE", "REJECTED", "CODE_SCOPE_REVIEWED"):
                    raise ManagementError("unsupported receipt review decision")
                receipt = found[0]
                if decision == "CODE_SCOPE_REVIEWED" and receipt["git_assessment"] != "LOCAL_COMMIT_SCOPE_VERIFIED":
                    raise ManagementError("cannot review local scope without independently checked Git object")
                if receipt.get("review_status") != "PENDING_INDEPENDENT_REVIEW":
                    raise ManagementError("receipt already reviewed; preserve history")
                receipt["review_status"] = decision
                receipt["reviewed_at"] = stamp()
                receipt["review_reason"] = reason
                # Reviewing a code-scope receipt never approves tests or a Gate.
            elif name == "create_milestone":
                delivery = checked_id(payload.get("deliverable_id"), "deliverable_id")
                if not any(d["id"] == delivery for d in objects):
                    raise ManagementError("milestone must reference a deliverable")
                entry = {"id": checked_id(payload.get("id"), "milestone id"),
                         "deliverable_id": delivery,
                         "title": checked_text(payload.get("title"), "milestone title", 240),
                         "exit_condition": checked_text(payload.get("exit_condition"), "exit condition"),
                         "state": "PROPOSED", "target_date": None}
                if any(m.get("id") == entry["id"] for m in record["milestones"]):
                    raise ManagementError("duplicate milestone ID")
                record["milestones"].append(entry)
            record["revision"] += 1
            record["events"].append({"revision": record["revision"], "operation": name,
                                     "at": stamp(), "reason": reason,
                                     "status": "LOCAL_DRAFT_NOT_REMOTE_PUBLISHED"})
            if len(record["events"]) > 10000:
                raise ManagementError("event log needs archival before additional changes")
            # Exclusive temporary creation and atomic rename; no user data is overwritten
            # outside this exact project. External Git changes are not overwritten.
            tmp = self.data_path.with_name(".management." + secrets.token_hex(8) + ".tmp")
            try:
                with tmp.open("x", encoding="utf-8", newline="\n") as stream:
                    json.dump(record, stream, ensure_ascii=False, indent=2)
                    stream.write("\n")
                    stream.flush()
                    os.fsync(stream.fileno())
                if self.data_path.is_symlink():
                    raise ManagementError("record unexpectedly changed to symlink")
                if self.data_path.read_bytes() != original_bytes:
                    raise ManagementError("management file was edited externally; refresh before saving")
                os.replace(tmp, self.data_path)
            finally:
                tmp.unlink(missing_ok=True)
            return {"project_id": self.project_id, "revision": record["revision"],
                    "result": "LOCAL_DRAFT_SAVED_NOT_REMOTE_PUBLISHED"}


def handler(targets, owner_token, registry_dir=None):
    # The local console may start before any managed project exists. Register
    # initialized projects explicitly after opening the application.
    target_lock = threading.RLock()
    assets = {
        "/": ("index.html", "text/html; charset=utf-8"),
        "/app.css": ("app.css", "text/css; charset=utf-8"),
        "/app.js": ("app.js", "application/javascript; charset=utf-8"),
    }

    class Handler(BaseHTTPRequestHandler):
        def local_host_valid(self):
            # Reject DNS-rebinding Host headers. Binding to loopback alone does
            # not protect GET endpoints from an attacker-controlled hostname.
            port = str(self.server.server_port)
            allowed = {"127.0.0.1:" + port, "localhost:" + port}
            return (self.client_address[0] in ("127.0.0.1", "::1")
                    and self.headers.get("Host", "") in allowed)

        def respond(self, value, status=200):
            body = json.dumps(value, ensure_ascii=False).encode("utf-8")
            self.send_bytes(body, "application/json; charset=utf-8", status)

        def send_bytes(self, raw, mime, status=200):
            self.send_response(status)
            self.send_header("Content-Type", mime)
            self.send_header("Content-Length", str(len(raw)))
            self.send_header("Cache-Control", "no-store")
            self.send_header("X-Content-Type-Options", "nosniff")
            self.send_header("Content-Security-Policy",
                             "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; "
                             "connect-src 'self'; object-src 'none'; base-uri 'none'")
            self.end_headers()
            self.wfile.write(raw)

        def do_GET(self):
            if not self.local_host_valid():
                return self.respond({"error": "invalid localhost host"}, 403)
            url = urlsplit(self.path)
            if url.path == "/api/projects":
                with target_lock:
                    projects = [
                        {"project_id": t.project_id, "display_name": t.initial.get("display_name"),
                         "repo_url": t.initial.get("repo_url"), "intake_mode": t.initial.get("intake_mode")}
                        for t in targets.values()
                    ]
                return self.respond({
                    "projects": projects, "can_register_target": True,
                    "status": "READY" if projects else "READY_NO_PROJECTS",
                    "note": "The management application can run without projects. Registration never initializes or modifies the target.",
                })
            if url.path == "/api/remote-status":
                if self.client_address[0] not in ("127.0.0.1", "::1"):
                    return self.respond({"error": "localhost only"}, 403)
                if not secrets.compare_digest(self.headers.get("X-Owner-Token", ""), owner_token):
                    return self.respond({"error": "owner token required"}, 403)
                project_id = parse_qs(url.query).get("project_id", [""])[0]
                target = targets.get(project_id)
                if target is None:
                    return self.respond({"error": "unknown project"}, 404)
                try:
                    return self.respond(target.remote_status())
                except (ManagementError, SyncError, OSError) as error:
                    return self.respond({"error": str(error)}, 409)
            if url.path == "/api/modules/remote":
                if not secrets.compare_digest(self.headers.get("X-Owner-Token", ""), owner_token):
                    return self.respond({"error": "owner token required"}, 403)
                project_id = parse_qs(url.query).get("project_id", [""])[0]
                target = targets.get(project_id)
                if target is None:
                    return self.respond({"error": "unknown project"}, 404)
                try:
                    return self.respond(target.module_remote_status())
                except (ManagementError, ModuleError, RemoteError, OSError, ValueError) as error:
                    return self.respond({"error": str(error)}, 409)
            if url.path == "/api/workspaces":
                if self.client_address[0] not in ("127.0.0.1", "::1"):
                    return self.respond({"error": "localhost only"}, 403)
                if not secrets.compare_digest(self.headers.get("X-Owner-Token", ""), owner_token):
                    return self.respond({"error": "owner token required"}, 403)
                project_id = parse_qs(url.query).get("project_id", [""])[0]
                target = targets.get(project_id)
                if target is None:
                    return self.respond({"error": "unknown project"}, 404)
                try:
                    return self.respond(target.workspace_status())
                except (ManagementError, OSError, ValueError) as err:
                    return self.respond({"error": str(err)}, 409)
            if url.path == "/api/model":
                project_id = parse_qs(url.query).get("project_id", [""])[0]
                target = targets.get(project_id)
                if target is None:
                    return self.respond({"error": "unknown project"}, 404)
                try:
                    return self.respond(target.model())
                except (ManagementError, OSError) as error:
                    return self.respond({"error": str(error)}, 409)
            item = assets.get(url.path)
            if item is None:
                return self.respond({"error": "unknown path"}, 404)
            try:
                return self.send_bytes((UI / item[0]).read_bytes(), item[1])
            except OSError:
                return self.respond({"error": "UI asset missing"}, 500)

        def do_POST(self):
            if not self.local_host_valid():
                return self.respond({"error": "invalid localhost host"}, 403)
            origin = self.headers.get("Origin", "")
            valid = {"http://127.0.0.1:" + str(self.server.server_port),
                     "http://localhost:" + str(self.server.server_port)}
            if origin and origin not in valid:
                return self.respond({"error": "cross-origin writes blocked"}, 403)
            if not secrets.compare_digest(self.headers.get("X-Owner-Token", ""), owner_token):
                return self.respond({"error": "owner token required"}, 403)
            if self.headers.get("Content-Type", "").split(";")[0] != "application/json":
                return self.respond({"error": "JSON body required"}, 415)
            length = self.headers.get("Content-Length", "")
            if not length.isdigit() or not (1 <= int(length) <= MAX_BODY):
                return self.respond({"error": "invalid body size"}, 413)
            operation = urlsplit(self.path).path
            if operation not in ("/api/action", "/api/collect-local-receipts",
                                  "/api/collect-registered-receipts", "/api/register-target"):
                return self.respond({"error": "unknown operation"}, 404)
            try:
                payload = json.loads(self.rfile.read(int(length)).decode("utf-8"))
                if not isinstance(payload, dict):
                    raise ManagementError("JSON object required")
                if operation == "/api/register-target":
                    raw_path = checked_text(payload.get("path"), "target directory", 4096)
                    target = Target(Path(raw_path), registry_dir=registry_dir)
                    with target_lock:
                        if target.project_id in targets or any(
                                existing.root == target.root for existing in targets.values()):
                            raise ManagementError("project already registered in this session")
                        persist_registered_target(target.root, registry_dir)
                        targets[target.project_id] = target
                    return self.respond({
                        "status": "EXISTING_PROJECT_REGISTERED_NOT_INITIALIZED",
                        "project_id": target.project_id,
                        "display_name": target.initial.get("display_name"),
                        "repo_url": target.initial.get("repo_url"),
                        "note": "No files were changed; this is a running-server registration only.",
                    })
                target = targets.get(payload.get("project_id"))
                if target is None:
                    raise ManagementError("project not registered")
                if operation == "/api/collect-local-receipts":
                    return self.respond(target.collect_local_receipts())
                if operation == "/api/collect-registered-receipts":
                    return self.respond(target.collect_registered_receipts())
                return self.respond(target.apply(payload.get("action"), payload.get("payload")))
            except (OSError, ManagementError, ValueError, TypeError) as error:
                return self.respond({"error": str(error)}, 400)

        def log_message(self, fmt, *args):
            # Deliberately omit request URLs and local file paths from shared logs.
            return

    return Handler


def main(argv=None):
    # Python on Windows may inherit a legacy charmap console. Every management
    # command writes UTF-8 Chinese project names and JSON, without requiring
    # users to change the system code page.
    for output in (sys.stdout, sys.stderr):
        if hasattr(output, "reconfigure"):
            output.reconfigure(encoding="utf-8", errors="replace")
    parser = argparse.ArgumentParser(description="Project Control: explicit multi-project local manager")
    parser.add_argument("--target", type=Path, action="append", default=[],
                        help="Optional existing initialized Git checkout; repeat for more projects. Omit to start empty.")
    parser.add_argument("--port", type=int, default=8791)
    parser.add_argument("--registry-dir", type=Path,
                        help="Optional user-authorized durable local workspace registry")
    options = parser.parse_args(argv)
    try:
        found = {}
        roots = set()
        explicit_paths = options.target
        for directory in explicit_paths:
            target = Target(directory, registry_dir=options.registry_dir)
            if target.project_id in found or str(target.root) in roots:
                raise ManagementError("duplicate project ID or directory")
            found[target.project_id] = target
            roots.add(str(target.root))
        # Restore only projects previously explicitly registered in this app.
        # Stale/deleted projects are skipped and never recreated or overwritten.
        for directory in read_project_catalog(options.registry_dir):
            if str(Path(directory)) in roots:
                continue
            try:
                target = Target(directory, registry_dir=options.registry_dir)
                if target.project_id in found:
                    print("Skipped duplicate saved project: " + target.project_id, file=sys.stderr)
                    continue
                found[target.project_id] = target
                roots.add(str(target.root))
            except (OSError, ManagementError) as error:
                print("Saved target requires review (not modified): " + str(error), file=sys.stderr)
    except (OSError, ManagementError) as error:
        parser.exit(2, "Cannot load projects: " + str(error) + "\n")
    token = os.environ.get("PROJECT_CONTROL_OWNER_TOKEN") or secrets.token_urlsafe(32)
    web = ThreadingHTTPServer(("127.0.0.1", options.port), handler(found, token, registry_dir=options.registry_dir))
    print("Project Control portable → http://127.0.0.1:" + str(web.server_port), flush=True)
    print("Owner token (this process only): " + token, flush=True)
    print("Registered projects: " + (", ".join(found) if found else "(none; add from the browser)"), flush=True)
    try:
        web.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        web.server_close()


if __name__ == "__main__":
    raise SystemExit(main())
