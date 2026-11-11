#!/usr/bin/env python3
"""Clean-slate Git-aware local project management workbench.

The committed project model is read from *git show HEAD*, not from a stale
SQLite copy. All UI edits are explicitly uncommitted local suggestions.
No operation in this server approves a document, changes a Gate, starts
Codex, commits a file, or pushes a Git branch.
"""
from __future__ import annotations

import argparse
from http.server import ThreadingHTTPServer, BaseHTTPRequestHandler
import json
import os
from pathlib import Path
import secrets
import subprocess
import tempfile
from urllib.parse import urlsplit

HERE = Path(__file__).resolve().parent
DATA_PATH = "tools/project_control/project.json"
LOCAL = ".local"
MAX_POST = 24_000
SUGGESTED_STATES = {"NO_CHANGE", "PLANNED", "IN_PROGRESS", "BLOCKED", "REPORTED_COMPLETE"}
PRIORITIES = {"NO_CHANGE", "P0", "P1", "P2", "P3"}


class ProjectError(ValueError):
    pass


def git(repo, *args):
    completed = subprocess.run(
        ["git", "-C", str(repo), *args], capture_output=True, check=False,
        timeout=15,
    )
    if completed.returncode:
        raise ProjectError("无法从当前 Git 仓库读取所需数据：" +
                           completed.stderr.decode("utf-8", "replace").strip()[:320])
    return completed.stdout.decode("utf-8", "replace").strip()


def committed_project(repo):
    sha = git(repo, "rev-parse", "HEAD")
    branch = git(repo, "rev-parse", "--abbrev-ref", "HEAD")
    raw = git(repo, "show", "HEAD:" + DATA_PATH)
    try:
        data = json.loads(raw)
    except json.JSONDecodeError as exc:
        raise ProjectError("Git 提交中的项目记录不是有效 JSON") from exc
    validate_project(data)
    dirty = bool(git(repo, "status", "--porcelain", "--", DATA_PATH))
    evidence_dirty = bool(git(repo, "status", "--porcelain", "--", "tools/project_control/receipts.json"))
    return data, {
        "sha": sha, "branch": branch, "record": DATA_PATH,
        "record_dirty_in_worktree": dirty,
        "evidence_dirty_in_worktree": evidence_dirty,
        "note": "内容来自当前本地 Git HEAD，未联网验证 GitHub 远端是否有更新。"
    }


def validate_project(p):
    if not isinstance(p, dict) or p.get("schema") != "project-control/1":
        raise ProjectError("项目记录的结构版本不受支持")
    for key in ("project", "workflow", "collaboration"):
        if not isinstance(p.get(key), dict):
            raise ProjectError("项目缺少 " + key)
    for key in ("deliverables", "work", "decisions", "sources"):
        if not isinstance(p.get(key), list):
            raise ProjectError("项目缺少 " + key)
    for key in ("deliverables", "work", "decisions", "sources"):
        ids = [x.get("id") for x in p[key] if isinstance(x, dict)]
        if len(ids) != len(p[key]) or any(not isinstance(x, str) or not x for x in ids):
            raise ProjectError(key + " 存在无效记录")
        if len(ids) != len(set(ids)):
            raise ProjectError(key + " 包含重复 ID")
    deliveries = {x["id"] for x in p["deliverables"]}
    works = {x["id"] for x in p["work"]}
    sources = {x["id"] for x in p["sources"]}
    for d in p["deliverables"]:
        if d.get("parent") is not None and d["parent"] not in deliveries:
            raise ProjectError("交付结构中存在无效的父子关系")
    for w in p["work"]:
        if w.get("deliverable") not in deliveries:
            raise ProjectError("工作没有有效交付目标：" + w["id"])
        if not isinstance(w.get("depends"), list) or any(
                dep not in works or dep == w["id"] for dep in w["depends"]):
            raise ProjectError("工作依赖无效：" + w["id"])
        if any(s not in sources for s in w.get("source_ids", [])):
            raise ProjectError("工作引用了不存在的来源：" + w["id"])
    def visit(key, visiting, visited):
        if key in visiting:
            raise ProjectError("工作依赖形成了循环：" + key)
        if key in visited:
            return
        visiting.add(key)
        for dep in next(x["depends"] for x in p["work"] if x["id"] == key):
            visit(dep, visiting, visited)
        visiting.remove(key)
        visited.add(key)
    done = set()
    for key in works:
        visit(key, set(), done)


def committed_receipts(repo):
    """Evidence becomes visible only after a Git commit exists."""
    raw = git(repo, "show", "HEAD:tools/project_control/receipts.json")
    try:
        ledger = json.loads(raw)
    except json.JSONDecodeError as exc:
        raise ProjectError("Git evidence ledger is not valid JSON") from exc
    if not isinstance(ledger, dict) or ledger.get("schema") != "project-control-receipts/1" or not isinstance(ledger.get("runs"), list):
        raise ProjectError("Git evidence ledger format is invalid")
    if len(ledger["runs"]) > 2000:
        raise ProjectError("Evidence ledger needs pagination before exceeding 2000 records")
    return ledger["runs"]


def local_path(repo):
    return repo / "tools" / "project_control" / LOCAL / "suggestions.json"


def load_local(repo):
    path = local_path(repo)
    if not path.is_file():
        return {"schema": "local-management-suggestions/1", "base_sha": "",
                "notes": {}, "unsynced": False}
    try:
        record = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as exc:
        raise ProjectError("本地管理建议文件损坏，未自动覆盖：" + str(exc)) from exc
    if (not isinstance(record, dict) or
        record.get("schema") != "local-management-suggestions/1" or
        not isinstance(record.get("notes"), dict)):
        raise ProjectError("本地管理建议文件结构无效")
    return record


def save_local(repo, record):
    path = local_path(repo)
    path.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary = tempfile.mkstemp(prefix=".candidate-", suffix=".tmp", dir=path.parent)
    try:
        with os.fdopen(descriptor, "w", encoding="utf-8") as output:
            json.dump(record, output, ensure_ascii=False, indent=2)
            output.write("\n")
            output.flush()
            os.fsync(output.fileno())
        os.replace(temporary, path)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


def propose_note(repo, payload):
    if not isinstance(payload, dict):
        raise ProjectError("需要提供管理建议对象")
    data, source = committed_project(repo)
    if payload.get("base_sha") != source["sha"]:
        raise ProjectError("工程版本已变化，请刷新后重新核对原建议，不能静默套用旧版本")
    task = payload.get("work_id")
    if task not in {w["id"] for w in data["work"]}:
        raise ProjectError("工作编号不存在")
    owner = payload.get("owner", "")
    text = payload.get("note", "")
    state = payload.get("suggested_state", "NO_CHANGE")
    priority = payload.get("suggested_priority", "NO_CHANGE")
    if not isinstance(owner, str) or len(owner) > 140:
        raise ProjectError("负责人建议超出限制")
    if not isinstance(text, str) or len(text) > 3500:
        raise ProjectError("备注超出限制")
    if state not in SUGGESTED_STATES or priority not in PRIORITIES:
        raise ProjectError("建议状态或优先级非法")
    record = load_local(repo)
    if record["base_sha"] and record["base_sha"] != source["sha"]:
        raise ProjectError("有基于旧 Git SHA 的本地建议；请先导出并处理，不能覆盖")
    record["base_sha"] = source["sha"]
    record["notes"][task] = {
        "owner": owner.strip(), "note": text.strip(),
        "suggested_state": state, "suggested_priority": priority,
        "disposition": "LOCAL_UNCOMMITTED_PROPOSAL",
    }
    record["unsynced"] = True
    save_local(repo, record)
    return record


def handler_factory(repo: Path):
    session = secrets.token_urlsafe(32)

    class Handler(BaseHTTPRequestHandler):
        server_version = "ProjectControl/0.1"

        def send_bytes(self, payload, mime="application/json; charset=utf-8", status=200,
                       filename=None):
            if isinstance(payload, (dict, list)):
                payload = json.dumps(payload, ensure_ascii=False, indent=2).encode("utf-8")
            elif isinstance(payload, str):
                payload = payload.encode("utf-8")
            self.send_response(status)
            self.send_header("Content-Type", mime)
            self.send_header("Content-Length", str(len(payload)))
            self.send_header("Cache-Control", "no-store")
            self.send_header("X-Content-Type-Options", "nosniff")
            self.send_header("Content-Security-Policy",
                             "default-src 'self'; script-src 'self'; style-src 'self'; "
                             "img-src 'self' data:; connect-src 'self'; object-src 'none'; "
                             "base-uri 'none'; frame-ancestors 'none'")
            if filename:
                self.send_header("Content-Disposition", 'attachment; filename="' + filename + '"')
            self.end_headers()
            self.wfile.write(payload)

        def allowed(self):
            host = self.headers.get("Host", "")
            return host in ("127.0.0.1:" + str(self.server.server_port),
                            "localhost:" + str(self.server.server_port))

        def do_GET(self):
            if not self.allowed():
                return self.send_bytes({"error": "只允许本机访问"}, status=403)
            path = urlsplit(self.path).path
            files = {
                "/": ("index.html", "text/html; charset=utf-8"),
                "/app.js": ("app.js", "application/javascript; charset=utf-8"),
                "/style.css": ("style.css", "text/css; charset=utf-8"),
            }
            if path in files:
                filename, mime = files[path]
                try:
                    return self.send_bytes((HERE / filename).read_bytes(), mime)
                except OSError:
                    return self.send_bytes({"error": "界面资源缺失"}, status=500)
            if path == "/api/project":
                try:
                    data, source = committed_project(repo)
                    receipts = committed_receipts(repo)
                    local = load_local(repo)
                    return self.send_bytes({
                        "project": data, "source": source, "local": local,
                        "receipts": receipts,
                        "local_stale": bool(local["notes"] and local["base_sha"] != source["sha"]),
                        "csrf": session,
                    })
                except (ProjectError, OSError) as exc:
                    return self.send_bytes({"error": str(exc)}, status=409)
            if path == "/api/export":
                try:
                    local = load_local(repo)
                    return self.send_bytes(local, filename="management-suggestions.json")
                except (ProjectError, OSError) as exc:
                    return self.send_bytes({"error": str(exc)}, status=409)
            return self.send_bytes({"error": "未找到路径"}, status=404)

        def do_POST(self):
            if not self.allowed():
                return self.send_bytes({"error": "只允许本机访问"}, status=403)
            if urlsplit(self.path).path != "/api/work-note":
                return self.send_bytes({"error": "该操作不存在"}, status=404)
            origin = self.headers.get("Origin", "")
            if origin not in ("http://127.0.0.1:" + str(self.server.server_port),
                              "http://localhost:" + str(self.server.server_port)):
                return self.send_bytes({"error": "不接受来自其他来源的写入"}, status=403)
            if not secrets.compare_digest(self.headers.get("X-CSRF-Token", ""), session):
                return self.send_bytes({"error": "缺少本机会话校验"}, status=403)
            if self.headers.get("Content-Type", "").split(";")[0] != "application/json":
                return self.send_bytes({"error": "只接受 JSON"}, status=415)
            try:
                length = int(self.headers.get("Content-Length", "0"))
                if not (0 < length <= MAX_POST):
                    return self.send_bytes({"error": "写入过大或为空"}, status=413)
                body = json.loads(self.rfile.read(length))
                return self.send_bytes({"ok": True, "local": propose_note(repo, body),
                                        "note": "仅写入隔离的本地建议；不改变正式工程、Gate 或 Git 历史"})
            except (ProjectError, ValueError, OSError) as exc:
                return self.send_bytes({"error": str(exc)}, status=409)

    return Handler


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo", type=Path, default=HERE.parents[1],
                        help="the local Git checkout; defaults to this script's repository")
    parser.add_argument("--port", type=int, default=8771)
    args = parser.parse_args()
    repo = Path(git(args.repo, "rev-parse", "--show-toplevel")).resolve()
    data, info = committed_project(repo)
    print("Project Control · 本地工作台")
    print("项目：", data["project"]["name"], "| Git HEAD:", info["sha"])
    print("仅提供本地管理建议，不作正式审批或 Git 操作")
    server = ThreadingHTTPServer(("127.0.0.1", args.port), handler_factory(repo))
    print("访问：http://127.0.0.1:" + str(server.server_port))
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
