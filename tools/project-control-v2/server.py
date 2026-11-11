#!/usr/bin/env python3
"""Local, revisioned project-management workbench for ChatGPT/GitHub/Codex."""
from __future__ import annotations

import argparse
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path
import secrets
import subprocess
import sys
from urllib.parse import urlsplit

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
ARCHIVE = REPO / "docs/engineering-control/projects/autotest/snapshot.json"
PROCESS = HERE / "process.json"
CONTROL_PATH = HERE / "workspace/autotest.json"

sys.path.insert(0, str(REPO / "tools/lifecycle"))
from control import Control, ControlError
from sync import status as git_status, check_remote


def git_head():
    try:
        return subprocess.check_output(["git", "-C", str(REPO), "rev-parse", "HEAD"],
                                       stderr=subprocess.DEVNULL, timeout=5).decode().strip()
    except (OSError, subprocess.SubprocessError):
        return None


def load_model(control=None):
    control = control or Control(CONTROL_PATH, ARCHIVE)
    source = json.loads(ARCHIVE.read_text(encoding="utf-8"))
    archive = source["engineering"]
    record, plan, assessment = control.model()
    process = json.loads(PROCESS.read_text(encoding="utf-8"))
    if archive["project"]["id"] != plan["project_id"] or process["project_id"] != plan["project_id"]:
        raise ValueError("project id mismatch across data sources")
    return {
        "project": archive["project"],
        "objects": archive["objects"],
        "relations": archive["relations"],
        "plan": plan,
        "assessment": assessment,
        "process": process,
        "control": record,
        "sync": git_status(REPO),
        "source": {
            "git_head": git_head(),
            "project_archive": str(ARCHIVE.relative_to(REPO)).replace("\\", "/"),
            "plan_file": str(CONTROL_PATH.relative_to(REPO)).replace("\\", "/"),
            "process_file": str(PROCESS.relative_to(REPO)).replace("\\", "/"),
            "source": "CURRENT_LOCAL_WORKTREE",
            "note": "本页读取当前本地仓库文件；是否与 GitHub 远端最新一致须独立核对。"
        }
    }


def handler(control=None, owner_token=None):
    control = control or Control(CONTROL_PATH, ARCHIVE)
    owner_token = owner_token or secrets.token_urlsafe(32)
    paths = {
        "/": ("index.html", "text/html; charset=utf-8"),
        "/app.js": ("app.js", "application/javascript; charset=utf-8"),
        "/app.css": ("app.css", "text/css; charset=utf-8"),
        "/actions.js": ("actions.js", "application/javascript; charset=utf-8")
    }

    class Handler(BaseHTTPRequestHandler):
        def _send(self, content, content_type, status=200):
            self.send_response(status)
            self.send_header("Content-Type", content_type)
            self.send_header("Content-Length", str(len(content)))
            self.send_header("Cache-Control", "no-store")
            self.send_header("X-Content-Type-Options", "nosniff")
            self.send_header("Content-Security-Policy",
                             "default-src 'self'; script-src 'self'; style-src 'self'; "
                             "connect-src 'self'; object-src 'none'; base-uri 'none'")
            self.end_headers()
            self.wfile.write(content)

        def _json(self, value, status=200):
            return self._send(json.dumps(value, ensure_ascii=False).encode("utf-8"),
                              "application/json; charset=utf-8", status)

        def do_GET(self):
            uri = urlsplit(self.path).path
            if uri == "/api/model":
                try:
                    return self._json(load_model(control))
                except (OSError, ValueError, KeyError) as exc:
                    return self._json({"error": str(exc)}, 500)
            entry = paths.get(uri)
            if entry is None:
                return self._send(b"Not found", "text/plain; charset=utf-8", 404)
            file, content_type = entry
            try:
                return self._send((HERE / file).read_bytes(), content_type)
            except OSError:
                return self._send(b"Asset unavailable", "text/plain; charset=utf-8", 500)

        def do_POST(self):
            if self.client_address[0] not in ("127.0.0.1", "::1"):
                return self._json({"error": "Only localhost is allowed"}, 403)
            supplied = self.headers.get("X-Owner-Token", "")
            if not supplied or not secrets.compare_digest(supplied, owner_token):
                return self._json({"error": "负责人授权无效；从本地启动终端获取授权码"}, 403)
            origin = self.headers.get("Origin", "")
            expected = ("http://127.0.0.1:" + str(self.server.server_port),
                        "http://localhost:" + str(self.server.server_port))
            if origin and origin not in expected:
                return self._json({"error": "Cross-origin writes are blocked"}, 403)
            if self.headers.get("Content-Type", "").split(";")[0] != "application/json":
                return self._json({"error": "JSON required"}, 415)
            size = self.headers.get("Content-Length", "0")
            if not size.isdigit() or not 1 <= int(size) <= 150_000:
                return self._json({"error": "Invalid request length"}, 413)
            try:
                payload = json.loads(self.rfile.read(int(size)).decode("utf-8"))
                uri = urlsplit(self.path).path
                if uri == "/api/control/action":
                    if not isinstance(payload, dict):
                        raise ControlError("action must be an object")
                    return self._json(control.apply(payload.get("action"), payload.get("payload"),
                                                    payload.get("actor")))
                if uri == "/api/sync/check":
                    return self._json(check_remote(REPO))
                return self._json({"error": "Route not found"}, 404)
            except (ControlError, ValueError, KeyError, TypeError) as exc:
                return self._json({"error": str(exc)}, 400)
            except OSError:
                return self._json({"error": "Cannot save local project record"}, 500)
    return Handler


def main(argv=None):
    parser = argparse.ArgumentParser(description="Independent project development control workbench")
    parser.add_argument("--port", type=int, default=8790)
    args = parser.parse_args(argv)
    token = os.environ.get("PROJECT_CONTROL_OWNER_TOKEN") or secrets.token_urlsafe(32)
    server = ThreadingHTTPServer(("127.0.0.1", args.port), handler(owner_token=token))
    print("Project Control → http://127.0.0.1:" + str(server.server_port), flush=True)
    print("负责人本地授权码（仅本次运行有效）: " + token, flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
