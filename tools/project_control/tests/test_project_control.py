"""Project Control v2 – Git source, local-only proposals and process-boundary checks."""
from __future__ import annotations
import copy
import json
from http.server import ThreadingHTTPServer
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import threading
import unittest
from urllib.error import HTTPError
from urllib.request import Request, urlopen

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from server import ProjectError, committed_project, handler_factory, propose_note, validate_project, load_local, committed_receipts
from receipt import stage as stage_receipt, ReceiptError

HERE = Path(__file__).resolve().parents[1]


class ProjectModelTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.record = json.loads((HERE / "project.json").read_text(encoding="utf-8"))

    def test_real_project_model_is_valid(self):
        validate_project(self.record)
        self.assertEqual(self.record["project"]["approval"]["status"], "OWNER_APPROVED_IN_SOURCE")
        self.assertEqual(self.record["project"]["plan_status"], "PROPOSED_NOT_APPROVED")
        phase = {p["id"]: p for p in self.record["workflow"]["phases"]}
        self.assertEqual(phase["G1"]["status"], "REVIEW_REQUESTED")
        self.assertNotEqual(phase["P0"]["status"], "GATE_APPROVED")

    def test_work_and_delivery_remain_separate_structures(self):
        deliverables = {d["id"] for d in self.record["deliverables"]}
        works = {w["id"] for w in self.record["work"]}
        self.assertTrue(all(w["deliverable"] in deliverables for w in self.record["work"]))
        self.assertTrue(all(w['id'] not in deliverables for w in self.record['work']))
        self.assertTrue(all(d['id'] not in works for d in self.record['deliverables']))
        self.assertTrue(any(w["kind"] == "RESEARCH" for w in self.record["work"]))
        self.assertTrue(any(w["kind"] == "IMPLEMENT" for w in self.record["work"]))
        self.assertTrue(all("planned_finish" not in w for w in self.record["work"]))

    def test_circular_work_dependencies_are_rejected(self):
        data = copy.deepcopy(self.record)
        data["work"][0]["depends"] = ["W-201"]
        with self.assertRaisesRegex(ProjectError, "循环"):
            validate_project(data)

    def test_unlinked_deliverables_are_rejected(self):
        data = copy.deepcopy(self.record)
        data["work"][0]["deliverable"] = "unknown"
        with self.assertRaisesRegex(ProjectError, "交付目标"):
            validate_project(data)


class GitServerTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.repo = Path(temp.name)
        self.run_git("init", "-q")
        self.run_git("config", "user.name", "Test")
        self.run_git("config", "user.email", "local@example.invalid")
        dest = self.repo / "tools/project_control"
        dest.mkdir(parents=True, exist_ok=True)
        shutil.copy(HERE / "project.json", dest / "project.json")
        shutil.copy(HERE / "receipts.json", dest / "receipts.json")
        (dest / ".gitignore").write_text(".local/\n", encoding="utf-8")
        self.run_git("add", ".")
        self.run_git("commit", "-qm", "fixture")
        self.server = ThreadingHTTPServer(("127.0.0.1", 0), handler_factory(self.repo))
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.addCleanup(self.server.server_close)
        self.addCleanup(self.server.shutdown)
        self.url = f"http://127.0.0.1:{self.server.server_port}"

    def run_git(self, *args):
        return subprocess.run(["git", "-C", str(self.repo), *args],
                              check=True, capture_output=True, text=True).stdout.strip()

    def get(self, path):
        with urlopen(self.url + path, timeout=5) as r:
            return r.status, r.headers, r.read()

    def post(self, payload, *, csrf=None, origin=None):
        body = json.dumps(payload).encode("utf-8")
        headers = {"Content-Type": "application/json"}
        if csrf is not None:
            headers["X-CSRF-Token"] = csrf
        if origin is not None:
            headers["Origin"] = origin
        req = Request(self.url + "/api/work-note", data=body, headers=headers)
        with urlopen(req, timeout=5) as r:
            return json.loads(r.read())

    def test_source_is_pinned_to_git_head_not_modified_worktree(self):
        expected_sha = self.run_git("rev-parse", "HEAD")
        payload = json.loads(self.get("/api/project")[2])
        self.assertEqual(payload["source"]["sha"], expected_sha)
        self.assertEqual(payload["project"]["project"]["plan_status"], "PROPOSED_NOT_APPROVED")
        raw = self.repo / "tools/project_control/project.json"
        data = json.loads(raw.read_text(encoding="utf-8"))
        data["project"]["identity"] = "UNAPPROVED LOCAL FORGERY"
        raw.write_text(json.dumps(data), encoding="utf-8")
        payload = json.loads(self.get("/api/project")[2])
        self.assertNotEqual(payload["project"]["project"]["identity"], "UNAPPROVED LOCAL FORGERY")
        self.assertTrue(payload["source"]["record_dirty_in_worktree"])

    def test_local_suggestion_is_not_a_formal_state_change(self):
        snap = json.loads(self.get("/api/project")[2])
        sha, token = snap["source"]["sha"], snap["csrf"]
        rec = self.post({"base_sha": sha, "work_id": "W-101", "owner": "负责人",
                         "note": "复核待办", "suggested_state": "REPORTED_COMPLETE",
                         "suggested_priority": "P0"}, csrf=token, origin=self.url)
        self.assertTrue(rec["ok"])
        self.assertEqual(rec["local"]["notes"]["W-101"]["disposition"],
                         "LOCAL_UNCOMMITTED_PROPOSAL")
        self.assertEqual(self.run_git("status", "--porcelain"), "")
        untouched, _ = committed_project(self.repo)
        self.assertEqual(untouched["work"][0]["state"], "WAITING_OWNER")
        self.assertEqual(untouched["workflow"]["phases"][2]["status"], "REVIEW_REQUESTED")
        self.assertTrue(load_local(self.repo)["unsynced"])

    def test_local_notes_are_stale_after_new_commit(self):
        snap = json.loads(self.get("/api/project")[2])
        self.post({"base_sha": snap["source"]["sha"], "work_id": "W-101",
                   "owner": "", "note": "老版本建议"},
                  csrf=snap["csrf"], origin=self.url)
        (self.repo / "example.txt").write_text("new commit", encoding="utf-8")
        self.run_git("add", "example.txt")
        self.run_git("commit", "-qm", "advance HEAD")
        after = json.loads(self.get("/api/project")[2])
        self.assertTrue(after["local_stale"])
        with self.assertRaises(HTTPError) as err:
            self.post({"base_sha": after["source"]["sha"], "work_id": "W-101",
                       "note": "overwrite"}, csrf=after["csrf"], origin=self.url)
        self.assertEqual(err.exception.code, 409)

    def test_cross_origin_or_missing_session_token_cannot_write(self):
        snap = json.loads(self.get("/api/project")[2])
        data = {"base_sha": snap["source"]["sha"], "work_id": "W-101", "note": "test"}
        with self.assertRaises(HTTPError) as err:
            self.post(data, origin=self.url)
        self.assertEqual(err.exception.code, 403)
        with self.assertRaises(HTTPError) as err:
            self.post(data, csrf=snap["csrf"], origin="http://malicious.example")
        self.assertEqual(err.exception.code, 403)

    def test_receipt_requires_real_command_and_published_git_commit(self):
        sha = self.run_git("rev-parse", "HEAD")
        receipt = {
            "schema": "project-control-receipt/1",
            "project_id": "autotest", "work_id": "W-103",
            "input_sha": sha, "actual_head_sha": sha,
            "branch": "test-fixture", "executor": "local Codex",
            "outcome": "INCONCLUSIVE",
            "summary": "Test is a fixture only",
            "commands": [{"command": "echo fixture", "exit_code": 0,
                          "observation": "fixture command recorded"}],
            "evidence_refs": [],
        }
        self.assertEqual(json.loads(self.get("/api/project")[2])["receipts"], [])
        result = stage_receipt(self.repo, receipt)
        self.assertEqual(result["work_id"], "W-103")
        self.assertTrue(json.loads(self.get("/api/project")[2])["source"]["evidence_dirty_in_worktree"])
        self.assertEqual(json.loads(self.get("/api/project")[2])["receipts"], [])
        self.run_git("add", "tools/project_control/receipts.json")
        self.run_git("commit", "-qm", "publish receipt fixture")
        published = json.loads(self.get("/api/project")[2])["receipts"]
        self.assertEqual(len(published), 1)
        self.assertEqual(published[0]["status"], "EXECUTION_REPORT_NOT_PRODUCT_ACCEPTANCE")

    def test_receipt_pass_cannot_cover_failed_or_unknown_command(self):
        sha = self.run_git("rev-parse", "HEAD")
        record = {"schema": "project-control-receipt/1", "project_id": "autotest",
                  "work_id": "W-101", "input_sha": sha, "actual_head_sha": sha,
                  "branch": "main", "executor": "test", "outcome": "PASS",
                  "summary": "not trustworthy",
                  "commands": [{"command": "pytest", "exit_code": 1,
                                "observation": "one test failed"}],
                  "evidence_refs": []}
        with self.assertRaisesRegex(ReceiptError, "PASS"):
            stage_receipt(self.repo, record)
        record["commands"][0]["exit_code"] = None
        with self.assertRaisesRegex(ReceiptError, "PASS"):
            stage_receipt(self.repo, record)

    def test_static_routes_and_exports_are_available(self):
        for path, typ, phrase in (
            ("/", "text/html", b"Project Control"),
            ("/style.css", "text/css", b".hero"),
            ("/app.js", "application/javascript", b"renderWork"),
        ):
            with self.subTest(path=path):
                status, headers, content = self.get(path)
                self.assertEqual(status, 200)
                self.assertIn(typ, headers.get("Content-Type", ""))
                self.assertIn(phrase, content)
        snap = json.loads(self.get("/api/export")[2])
        self.assertEqual(snap["schema"], "local-management-suggestions/1")


if __name__ == "__main__":
    unittest.main()
