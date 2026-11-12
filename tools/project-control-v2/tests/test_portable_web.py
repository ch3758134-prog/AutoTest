"""Real HTTP and multi-target tests for the portable management server.

Uses only temporary local Git fixtures. Never accesses the user's workspaces.
"""
from __future__ import annotations
import importlib.util
from http.server import ThreadingHTTPServer
from pathlib import Path
import json
import subprocess
import tempfile
import threading
import unittest
from urllib.error import HTTPError
from urllib.request import Request, urlopen

HERE = Path(__file__).resolve().parents[1]


def load_module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


initialization = load_module("pc_portable_init_for_web", HERE / "portable/project_init.py")
service = load_module("pc_portable_web_server", HERE / "portable/web_server.py")


def git(path, *args):
    result = subprocess.run(["git", "-C", str(path), *args], capture_output=True,
                            text=True, encoding="utf-8")
    if result.returncode:
        raise AssertionError(result.stderr)
    return result.stdout.strip()


class PortableWebTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.roots = []
        self.targets = {}
        for num in (1, 2):
            root = Path(self.temp.name) / ("target" + str(num))
            root.mkdir()
            url = "https://github.com/example-org/project-" + str(num) + ".git"
            git(root, "init", "-b", "main")
            git(root, "config", "user.name", "Fixture")
            git(root, "config", "user.email", "fixture@example.invalid")
            git(root, "remote", "add", "origin", url)
            if num == 2:
                (root / "README.md").write_text("Existing engineering work\n", encoding="utf-8")
                git(root, "add", "README.md")
                git(root, "commit", "-m", "existing asset")
            inspection = initialization.inspect_target(root, url)
            mode = "new" if num == 1 else "adopt"
            candidate = initialization.create_preview(inspection, mode,
                                                       project_id="project-" + str(num))
            initialization.apply_preview(root, inspection, candidate)
            git(root, "add", ".")
            git(root, "commit", "-m", "candidate management bootstrap")
            self.roots.append(root)
            t = service.Target(root)
            self.targets[t.project_id] = t
        self.server = ThreadingHTTPServer(("127.0.0.1", 0), service.handler(self.targets, "test-owner-secret"))
        thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        thread.start()
        self.addCleanup(self.server.server_close)
        self.addCleanup(self.server.shutdown)
        self.base = "http://127.0.0.1:" + str(self.server.server_port)

    def get(self, path, expected_status=200):
        try:
            with urlopen(self.base + path, timeout=5) as resp:
                self.assertEqual(resp.status, expected_status)
                return json.loads(resp.read().decode("utf-8"))
        except HTTPError as exc:
            self.assertEqual(exc.code, expected_status)
            return json.loads(exc.read().decode("utf-8"))

    def post(self, project_id="project-1", action="create_deliverable", payload=None,
             token="test-owner-secret", origin=None):
        payload = payload if payload is not None else {
            "id": "d-initial", "title": "First outcome",
            "success_condition": "Real engineering review",
            "reason": "Test fixture source"
        }
        request = Request(self.base + "/api/action",
                          data=json.dumps({"project_id": project_id, "action": action,
                                           "payload": payload}).encode(),
                          method="POST",
                          headers={"Content-Type": "application/json",
                                   "X-Owner-Token": token,
                                   **({"Origin": origin} if origin else {})})
        try:
            with urlopen(request, timeout=5) as resp:
                return resp.status, json.loads(resp.read())
        except HTTPError as error:
            return error.code, json.loads(error.read())

    def revision(self, project_id):
        return self.get("/api/model?project_id=" + project_id)["management"]["revision"]

    def test_projects_are_explicit_and_isolated(self):
        listed = self.get("/api/projects")["projects"]
        self.assertEqual({x["project_id"] for x in listed}, {"project-1", "project-2"})
        self.assertEqual(self.get("/api/model?project_id=unknown", 404)["error"], "unknown project")
        one = self.get("/api/model?project_id=project-1")
        two = self.get("/api/model?project_id=project-2")
        self.assertEqual(one["project"]["intake_mode"], "NEW")
        self.assertEqual(two["project"]["intake_mode"], "ADOPT")
        self.assertEqual(one["assets"]["tracked_file_count"], 0)
        self.assertEqual(two["assets"]["tracked_file_count"], 1)
        self.assertEqual(one["management"]["deliverables"], [])
        self.assertEqual(two["management"]["deliverables"], [])
        self.assertFalse(one["limits"]["formal_approval"])
        self.assertFalse(one["limits"]["remote_write"])

    def test_local_workflow_and_evidence_are_not_approvals(self):
        state = self.revision("project-1")
        code, result = self.post(payload={
            "id": "d-1", "title": "Demonstrable output",
            "success_condition": "Reviewed against evidence", "reason": "Initial proposal",
            "expected_revision": state
        })
        self.assertEqual(code, 200, result)
        self.assertEqual(result["revision"], state + 1)
        code, result = self.post(action="create_work", payload={
            "id": "w-1", "title": "Research test approach",
            "deliverable_id": "d-1", "kind": "RESEARCH",
            "expected_result": "Technical evidence prepared",
            "reason": "Plan proposal", "expected_revision": self.revision("project-1")
        })
        self.assertEqual(code, 200, result)
        code, result = self.post(action="set_progress", payload={
            "id": "w-1", "state": "REPORTED_COMPLETE",
            "evidence_refs": ["https://github.com/example-org/project-1/pull/1"],
            "reason": "Reported evidence", "expected_revision": self.revision("project-1")
        })
        self.assertEqual(code, 200, result)
        one = self.get("/api/model?project_id=project-1")
        two = self.get("/api/model?project_id=project-2")
        self.assertEqual(one["management"]["deliverables"][0]["state"], "PROPOSED")
        self.assertFalse(one["management"]["work"][0]["execution_authorized"])
        self.assertEqual(one["management"]["progress"]["w-1"]["verification"], "NOT_VERIFIED")
        self.assertEqual(two["management"]["work"], [])
        self.assertEqual(one["tasks"]["tasks"], [])
        self.assertEqual(one["decisions"]["decisions"], [])
        self.assertEqual(one["project"]["formal_product_identity"], "UNDECIDED")

    def test_auth_stale_revision_and_unapproved_operations_blocked(self):
        revision = self.revision("project-1")
        payload = {"id": "d-1", "title": "Risk", "success_condition": "Evidence",
                   "reason": "fixture", "expected_revision": revision}
        status, _ = self.post(token="wrong", payload=payload)
        self.assertEqual(status, 403)
        status, _ = self.post(payload=payload, origin="https://attacker.example")
        self.assertEqual(status, 403)
        status, _ = self.post(payload=payload)
        self.assertEqual(status, 200)
        status, body = self.post(payload={**payload, "id": "d-2"})
        self.assertEqual(status, 400)
        self.assertIn("stale revision", body["error"])
        status, body = self.post(action="approve_design", payload={
            "reason": "not allowed", "expected_revision": self.revision("project-1")
        })
        self.assertEqual(status, 400)
        self.assertIn("not supported", body["error"])
        self.assertEqual(self.revision("project-1"), revision + 1)

    def test_progress_requires_evidence_and_related_work(self):
        code, result = self.post(action="set_progress", payload={
            "id": "not-known", "state": "REPORTED_COMPLETE", "evidence_refs": ["x"],
            "reason": "test", "expected_revision": 1
        })
        self.assertEqual(code, 400)
        self.assertIn("unknown work", result["error"])

    def test_wrong_origin_or_schema_is_fail_closed(self):
        git(self.roots[0], "remote", "set-url", "origin",
            "https://github.com/another/wrong.git")
        response = self.get("/api/model?project_id=project-1", 409)
        self.assertIn("disagree", response["error"])

    def test_web_assets_are_generic_and_local(self):
        for path, marker in (("/", b"Project Control"), ("/app.js", b"function overview()"),
                             ("/app.css", b".sidebar")):
            with urlopen(self.base + path, timeout=5) as response:
                self.assertEqual(response.status, 200)
                self.assertIn(marker, response.read())
                self.assertIn("no-store", response.headers.get("Cache-Control", ""))
        response = self.get("/../../etc/passwd", 404)
        self.assertIn("error", response)


if __name__ == "__main__":
    unittest.main()
