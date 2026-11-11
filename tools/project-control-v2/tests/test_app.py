"""Project Control V2 cannot turn drafts, plan proposals or local Git into approvals."""
import json
from pathlib import Path
import sys
import unittest
from urllib.request import urlopen
from http.server import ThreadingHTTPServer
import threading
import subprocess
import shutil

HERE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HERE))
from server import handler, load_model


class ProjectControlTests(unittest.TestCase):
    def test_real_project_and_unapproved_plan(self):
        data = load_model()
        self.assertEqual(data["project"]["id"], "autotest")
        self.assertEqual(data["plan"]["status"], "PROPOSED")
        self.assertEqual(data["assessment"]["plan_status"], "PROPOSED_NOT_APPROVED")
        self.assertEqual(data["assessment"]["product_anchor"]["verified"], True)
        approved = [x for x in data["objects"] if x["state"] == "APPROVED"]
        self.assertEqual([x["id"] for x in approved], ["at-p0-approved-charter-v01"])
        self.assertTrue(any(x["id"] == "at-g1-integrated-autotest-study" and
                            x["state"] != "APPROVED" for x in data["objects"]))
        self.assertEqual(len(data["plan"]["work"]), 5)

    def test_workplan_does_not_forge_dates_or_gate_pass(self):
        data = load_model()
        self.assertTrue(all(not x.get("planned_finish") for x in data["plan"]["work"]))
        self.assertTrue(all(x["review"] not in ("PASS", "APPROVED") for x in data["process"]["stages"]))
        self.assertTrue(any(i["code"] == "UNAPPROVED_INPUT" for i in data["assessment"]["issues"]))

    def test_only_read_endpoints(self):
        server = ThreadingHTTPServer(("127.0.0.1", 0), handler())
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        self.addCleanup(server.server_close)
        self.addCleanup(server.shutdown)
        base = "http://127.0.0.1:" + str(server.server_port)
        for path, mime, marker in (
            ("/", "text/html", b'project'),
            ("/app.js", "application/javascript", b'function workPlan()'),
            ("/app.css", "text/css", b'.hero'),
            ("/api/model", "application/json", b'"plan_status": "PROPOSED_NOT_APPROVED"'),
        ):
            with self.subTest(path=path):
                with urlopen(base + path, timeout=8) as resp:
                    self.assertEqual(resp.status, 200)
                    self.assertIn(mime, resp.headers.get("Content-Type", ""))
                    self.assertIn(marker, resp.read())

    def test_js_parses(self):
        if not shutil.which("node"):
            self.skipTest("node unavailable")
        result = subprocess.run(["node", "--check", str(HERE / "app.js")],
                                text=True, capture_output=True)
        self.assertEqual(result.returncode, 0, result.stderr)


if __name__ == "__main__":
    unittest.main()
