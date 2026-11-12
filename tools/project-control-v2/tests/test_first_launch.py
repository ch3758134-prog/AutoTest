"""First-launch acceptance: no target needed, no fake project, explicit intake only."""
from __future__ import annotations

import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import unittest
from urllib.error import HTTPError
from urllib.request import Request, urlopen

HERE = Path(__file__).resolve().parents[1]
SERVER = HERE / "portable/web_server.py"


def git(root, *args):
    run = subprocess.run(
        ["git", "-C", str(root), *args], text=True,
        stdout=subprocess.PIPE, stderr=subprocess.PIPE,
        encoding="utf-8", errors="replace", timeout=15)
    if run.returncode:
        raise AssertionError(run.stderr)


class FirstLaunchTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.home = Path(self.temp.name)
        self.local_registry = self.home / "pc-state"
        env = dict(os.environ)
        env["PROJECT_CONTROL_OWNER_TOKEN"] = "unit-first-launch-token"
        self.process = subprocess.Popen(
            [sys.executable, str(SERVER), "--port", "0", "--registry-dir",
             str(self.local_registry)],
            stdout=subprocess.PIPE, stderr=subprocess.PIPE,
            text=True, encoding="utf-8", errors="replace", env=env)
        self.addCleanup(self.stop)
        line = self.process.stdout.readline().strip()
        match = re.search(r"http://127\.0\.0\.1:(\d+)", line)
        if not match:
            raise AssertionError("server did not start without --target: " + line)
        self.base = "http://127.0.0.1:" + match.group(1)

    def stop(self):
        if self.process.poll() is None:
            self.process.terminate()
        try:
            self.process.communicate(timeout=8)
        except subprocess.TimeoutExpired:
            self.process.kill()
            self.process.communicate(timeout=5)

    def get(self, path):
        with urlopen(self.base + path, timeout=5) as response:
            return json.loads(response.read().decode("utf-8"))

    def post(self, path, value, token=True):
        headers = {"Content-Type": "application/json"}
        if token:
            headers["X-Owner-Token"] = "unit-first-launch-token"
        request = Request(
            self.base + path, method="POST", data=json.dumps(value).encode("utf-8"),
            headers=headers)
        with urlopen(request, timeout=10) as response:
            return json.loads(response.read().decode("utf-8"))

    def make_project(self):
        root = self.home / "real-project"
        root.mkdir()
        git(root, "init", "-b", "main")
        git(root, "config", "user.name", "Fixture")
        git(root, "config", "user.email", "fixture@example.invalid")
        git(root, "remote", "add", "origin", "https://github.com/acme/real-project.git")
        (root / "README.md").write_text("Real existing asset\n", encoding="utf-8")
        git(root, "add", ".")
        git(root, "commit", "-m", "existing engineering source")
        return root

    def test_empty_server_and_static_web_respond_without_project_data(self):
        state = self.get("/api/projects")
        self.assertEqual(state["status"], "READY_NO_PROJECTS")
        self.assertEqual(state["projects"], [])
        self.assertTrue(state["can_register_target"])
        with urlopen(self.base + "/", timeout=5) as response:
            html = response.read().decode("utf-8")
        self.assertIn("新建 / 接管", html)
        with urlopen(self.base + "/app.js", timeout=5) as response:
            javascript = response.read().decode("utf-8")
        self.assertIn("emptyProjectHome", javascript)
        self.assertFalse((self.home / ".project-control").exists())

    def test_preview_does_not_modify_real_project_and_apply_requires_confirmation(self):
        root = self.make_project()
        body = {"path": str(root), "repo_url": "https://github.com/acme/real-project.git",
                "mode": "adopt", "project_id": "real-project",
                "project_name": "真实项目"}
        request = Request(self.base + "/api/project-preview", method="POST",
                          data=json.dumps(body).encode(), headers={"Content-Type": "application/json"})
        with self.assertRaises(HTTPError) as blocked:
            urlopen(request, timeout=5)
        self.assertEqual(blocked.exception.code, 403)
        preview = self.post("/api/project-preview", body)
        self.assertEqual(preview["status"], "PREVIEW_ONLY_NO_FILES_WRITTEN")
        self.assertEqual(preview["project_id"], "real-project")
        self.assertFalse((root / ".project-control").exists())
        self.assertEqual(self.get("/api/projects")["projects"], [])
        with self.assertRaises(HTTPError) as denied:
            self.post("/api/project-apply", {
                "review_token": preview["review_token"], "confirm": False})
        self.assertEqual(denied.exception.code, 400)
        # A denied confirmation does not consume the capability.
        applied = self.post("/api/project-apply", {
            "review_token": preview["review_token"], "confirm": True})
        self.assertEqual(applied["status"], "LOCAL_CANDIDATE_WRITTEN_NOT_COMMITTED")
        self.assertTrue((root / ".project-control/management.json").is_file())
        self.assertEqual((root / "README.md").read_text(), "Real existing asset\n")
        self.assertEqual(len(self.get("/api/projects")["projects"]), 1)
        catalog = json.loads((self.local_registry / "targets.json").read_text())
        self.assertEqual(catalog["paths"], [str(root)])
        with self.assertRaises(HTTPError) as duplicate:
            self.post("/api/register-target", {"path": str(root)})
        self.assertEqual(duplicate.exception.code, 400)

    def test_uninitialized_target_registration_is_rejected_without_files(self):
        root = self.make_project()
        with self.assertRaises(HTTPError) as error:
            self.post("/api/register-target", {"path": str(root)})
        self.assertEqual(error.exception.code, 400)
        self.assertFalse((root / ".project-control").exists())
        self.assertEqual(self.get("/api/projects")["projects"], [])


if __name__ == "__main__":
    unittest.main()
