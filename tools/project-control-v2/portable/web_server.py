#!/usr/bin/env python3
"""Portable Project Control management server.

Serve several explicitly registered local Git workspaces without importing the
AutoTest demonstration data.  No Git checkout, push, merge, reset or cleanup.
All writes are local draft management records and require the session owner token.
"""
from __future__ import annotations

import argparse
import hashlib
from receipt_evidence import assess as assess_receipt, ReceiptError
from remote_sync import inspect as inspect_remote, SyncError
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


def empty_management(project_id):
    return {
        "schema": SCHEMA, "project_id": project_id, "revision": 1,
        "status": "DRAFT", "deliverables": [], "work": [], "milestones": [],
        "risks": [], "progress": {}, "events": [], "receipts": []
    }


class Target:
    def __init__(self, root):
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
            if project.get("project_id") != self.project_id:
                raise ManagementError("project identity changed; reopen the server")
            management = load_json(self.data_path)
            self.check_record(management)
            management.setdefault("receipts", [])
            assets = load_json(self.directory / "assets.json")
            decisions = load_json(self.directory / "decisions.json")
            tasks = load_json(self.directory / "tasks.json")
            if any(x.get("project_id") != self.project_id for x in (assets, decisions, tasks)):
                raise ManagementError("cross-project record mismatch")
            return {
                "project": project, "management": management,
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

    def apply(self, name, payload):
        if not isinstance(payload, dict):
            raise ManagementError("action payload must be a JSON object")
        reason = checked_text(payload.get("reason"), "change reason", 2000)
        revision = payload.get("expected_revision")
        if type(revision) is not int:
            raise ManagementError("integer expected_revision required")
        with self.lock:
            self.check_identity()
            record = load_json(self.data_path)
            self.check_record(record)
            if revision != record["revision"]:
                raise ManagementError("stale revision; refresh and review current changes")
            record.setdefault("receipts", [])
            allowed_names = {"create_deliverable", "create_work", "set_progress", "raise_risk",
                             "create_milestone", "record_receipt", "review_receipt"}
            if name not in allowed_names:
                raise ManagementError("operation not supported: no approval/push/merge is exposed")
            objects = record["deliverables"]
            works = record["work"]
            if name == "create_deliverable":
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
                os.replace(tmp, self.data_path)
            finally:
                tmp.unlink(missing_ok=True)
            return {"project_id": self.project_id, "revision": record["revision"],
                    "result": "LOCAL_DRAFT_SAVED_NOT_REMOTE_PUBLISHED"}


def handler(targets, owner_token):
    assets = {
        "/": ("index.html", "text/html; charset=utf-8"),
        "/app.css": ("app.css", "text/css; charset=utf-8"),
        "/app.js": ("app.js", "application/javascript; charset=utf-8"),
    }

    class Handler(BaseHTTPRequestHandler):
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
            url = urlsplit(self.path)
            if url.path == "/api/projects":
                return self.respond({
                    "projects": [
                        {"project_id": t.project_id, "display_name": t.initial.get("display_name"),
                         "repo_url": t.initial.get("repo_url"), "intake_mode": t.initial.get("intake_mode")}
                        for t in targets.values()
                    ],
                    "note": "Only explicitly registered local projects are shown.",
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
            if self.client_address[0] not in ("127.0.0.1", "::1"):
                return self.respond({"error": "localhost only"}, 403)
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
            if urlsplit(self.path).path != "/api/action":
                return self.respond({"error": "unknown operation"}, 404)
            try:
                payload = json.loads(self.rfile.read(int(length)).decode("utf-8"))
                if not isinstance(payload, dict):
                    raise ManagementError("JSON object required")
                target = targets.get(payload.get("project_id"))
                if target is None:
                    raise ManagementError("project not registered")
                return self.respond(target.apply(payload.get("action"), payload.get("payload")))
            except (OSError, ManagementError, ValueError, TypeError) as error:
                return self.respond({"error": str(error)}, 400)

        def log_message(self, fmt, *args):
            # Deliberately omit request URLs and local file paths from shared logs.
            return

    return Handler


def main(argv=None):
    parser = argparse.ArgumentParser(description="Project Control: explicit multi-project local manager")
    parser.add_argument("--target", type=Path, action="append", required=True,
                        help="Local Git checkout containing .project-control/management.json; repeat for more projects")
    parser.add_argument("--port", type=int, default=8791)
    options = parser.parse_args(argv)
    try:
        found = {}
        roots = set()
        for directory in options.target:
            target = Target(directory)
            if target.project_id in found or str(target.root) in roots:
                raise ManagementError("duplicate project ID or directory")
            found[target.project_id] = target
            roots.add(str(target.root))
    except (OSError, ManagementError) as error:
        parser.exit(2, "Cannot load projects: " + str(error) + "\n")
    token = os.environ.get("PROJECT_CONTROL_OWNER_TOKEN") or secrets.token_urlsafe(32)
    web = ThreadingHTTPServer(("127.0.0.1", options.port), handler(found, token))
    print("Project Control portable → http://127.0.0.1:" + str(web.server_port), flush=True)
    print("Owner token (this process only): " + token, flush=True)
    print("Registered projects: " + ", ".join(found), flush=True)
    try:
        web.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        web.server_close()


if __name__ == "__main__":
    raise SystemExit(main())
