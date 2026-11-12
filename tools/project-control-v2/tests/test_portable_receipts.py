"""Real local Git fixtures: Codex result -> independently assessed manager receipt."""
from __future__ import annotations
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

HERE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HERE / "portable"))


def import_file(name, file):
    spec = importlib.util.spec_from_file_location(name, file)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


init = import_file("portable_receipt_init", HERE / "portable/project_init.py")
server = import_file("portable_receipt_server", HERE / "portable/web_server.py")
import receipt_evidence as proof


def git(root, *args):
    p = subprocess.run(["git", "-C", str(root), *args], text=True,
                       capture_output=True, encoding="utf-8")
    if p.returncode:
        raise AssertionError("git failure: " + p.stderr)
    return p.stdout.strip()


class ReceiptIntegrationTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name) / "target"
        self.root.mkdir()
        self.remote = "https://github.com/sample-owner/research-project.git"
        git(self.root, "init", "-b", "main")
        git(self.root, "config", "user.name", "Codex Fixture")
        git(self.root, "config", "user.email", "fixture@example.invalid")
        git(self.root, "remote", "add", "origin", self.remote)
        docs = self.root / "docs/design"
        docs.mkdir(parents=True)
        (docs / "approved.md").write_text("# Approved test design\n", encoding="utf-8")
        git(self.root, "add", ".")
        git(self.root, "commit", "-m", "design asset")
        state = init.inspect_target(self.root, self.remote)
        initial = init.create_preview(state, "adopt", "research-project")
        init.apply_preview(self.root, state, initial)
        git(self.root, "add", ".")
        git(self.root, "commit", "-m", "adopt project")
        project_path = self.root / ".project-control/project.json"
        project = json.loads(project_path.read_text(encoding="utf-8"))
        project["formal_product_identity"] = "APPROVED"
        project["scope_decision"] = "APPROVED"
        project_path.write_text(json.dumps(project, indent=2), encoding="utf-8")
        blob = git(self.root, "rev-parse", "HEAD:docs/design/approved.md")
        decisions = self.root / ".project-control/decisions.json"
        approved = json.loads(decisions.read_text(encoding="utf-8"))
        approved["decisions"] = [{"kind": "DESIGN_APPROVAL", "state": "APPROVED",
                                  "path": "docs/design/approved.md", "blob_sha": blob,
                                  "approved_by": "Owner", "approval_reference": "owner-record"}]
        decisions.write_text(json.dumps(approved, indent=2), encoding="utf-8")
        git(self.root, "add", ".")
        git(self.root, "commit", "-m", "owner decisions")
        pinned = git(self.root, "rev-parse", "HEAD")
        self.tasks_path = self.root / ".project-control/tasks.json"
        registry = json.loads(self.tasks_path.read_text(encoding="utf-8"))
        registry["tasks"] = [{
            "id": "IMPL-001", "project_id": "research-project",
            "title": "Implementation of test evidence",
            "state": "READY_FOR_LOCAL_IMPLEMENTATION", "executor": "local_codex",
            "mode": "CONTROLLED",
            "authorization": {"approved_by": "Owner", "approval_reference": "task-decision"},
            "source": {"repo_url": self.remote, "branch": "main",
                       "pinned_sha": pinned, "work_branch": "feature/impl-001"},
            "inputs": [{"type": "DESIGN_BASELINE", "path": "docs/design/approved.md",
                        "blob_sha": blob}],
            "allowed_paths": ["src/**"],
            "acceptance": ["Commit plus test evidence"],
            "restrictions": ["NO_FORCE_PUSH", "NO_MERGE", "NO_RESET", "NO_UNAUTHORIZED_DELETE"]
        }]
        self.tasks_path.write_text(json.dumps(registry, indent=2), encoding="utf-8")
        git(self.root, "add", ".")
        git(self.root, "commit", "-m", "explicit ready task")
        self.resolver = self.root / ".project-control/resolve.py"
        self.target = server.Target(self.root)

    def call(self, action):
        return subprocess.run([sys.executable, str(self.resolver), action], cwd=self.root,
                              capture_output=True, text=True, encoding="utf-8")

    def candidate(self):
        self.assertEqual(self.call("select").returncode, 0)
        self.assertEqual(self.call("claim").returncode, 0)
        git(self.root, "switch", "-c", "feature/impl-001")
        folder = self.root / "src"
        folder.mkdir()
        (folder / "feature.py").write_text("def result():\n    return 7\n", encoding="utf-8")
        git(self.root, "add", "src/feature.py")
        git(self.root, "commit", "-m", "implement")
        p = self.call("report")
        self.assertEqual(p.returncode, 0, p.stderr)
        report = json.loads(p.stdout)
        self.assertEqual(report["result_status"], "REPORTED_NOT_VERIFIED")
        self.assertTrue(report["workspace_clean"])
        return report

    def record(self, receipt, **other):
        return self.target.apply("record_receipt", {
            "expected_revision": self.target.model()["management"]["revision"],
            "reason": "Coordinator receiving candidate evidence", "receipt": receipt, **other
        })

    def test_end_to_end_local_code_evidence_without_gate_promotion(self):
        receipt = self.candidate()
        info = self.record(receipt)
        self.assertEqual(info["result"], "LOCAL_DRAFT_SAVED_NOT_REMOTE_PUBLISHED")
        data = self.target.model()
        entry = data["management"]["receipts"][0]
        self.assertEqual(entry["git_assessment"], "LOCAL_COMMIT_SCOPE_VERIFIED")
        self.assertEqual(entry["test_assessment"], "SELF_REPORTED_LOG_HASHES_NOT_INDEPENDENTLY_VERIFIED")
        self.assertEqual(entry["review_status"], "PENDING_INDEPENDENT_REVIEW")
        self.assertEqual(data["tasks"]["tasks"][0]["state"], "READY_FOR_LOCAL_IMPLEMENTATION")
        self.assertEqual(data["management"]["progress"], {})
        self.assertEqual(data["decisions"]["decisions"][0]["kind"], "DESIGN_APPROVAL")
        with self.assertRaisesRegex(server.ManagementError, "already imported"):
            self.record(receipt)
        self.target.apply("review_receipt", {
            "expected_revision": self.target.model()["management"]["revision"],
            "reason": "Reviewed the scoped Git commit, not test or hardware behavior",
            "id": receipt["receipt_id"], "decision": "CODE_SCOPE_REVIEWED"
        })
        self.assertEqual(self.target.model()["management"]["receipts"][0]["review_status"],
                         "CODE_SCOPE_REVIEWED")

    def test_fake_commit_file_list_rejected_without_record(self):
        receipt = self.candidate()
        bad = copy.deepcopy(receipt)
        bad["changed_paths"] = ["src/fake.py"]
        with self.assertRaises(proof.ReceiptError):
            self.record(bad)
        self.assertEqual(self.target.model()["management"]["receipts"], [])

    def test_cross_project_and_wrong_task_digest_rejected(self):
        receipt = self.candidate()
        wrong_project = dict(receipt, project_id="another-project")
        with self.assertRaisesRegex(proof.ReceiptError, "cross-project"):
            self.record(wrong_project)
        changed = dict(receipt, task_registry_sha256="0" * 64)
        with self.assertRaisesRegex(proof.ReceiptError, "different task registry"):
            self.record(changed)

    def test_scope_break_or_dirty_worktree_cannot_produce_receipt(self):
        self.assertEqual(self.call("claim").returncode, 0)
        git(self.root, "switch", "-c", "feature/impl-001")
        (self.root / "untracked.txt").write_text("do not discard", encoding="utf-8")
        self.assertNotEqual(self.call("report").returncode, 0)
        self.assertTrue((self.root / "untracked.txt").exists())
        (self.root / "untracked.txt").unlink()
        (self.root / "docs/design/approved.md").write_text("# changed without authorization\n", encoding="utf-8")
        git(self.root, "add", "docs/design/approved.md")
        git(self.root, "commit", "-m", "out of scope")
        self.assertNotEqual(self.call("report").returncode, 0)
        self.assertEqual(self.target.model()["management"]["receipts"], [])

    def test_unknown_candidate_retains_unverified_status(self):
        # Manager clone created BEFORE Codex has a new commit.
        clone = Path(self.tmp.name) / "manager"
        git(self.root, "clone", "--branch", "main", str(self.root), str(clone))
        git(clone, "remote", "set-url", "origin", self.remote)
        manager = server.Target(clone)
        receipt = self.candidate()
        result = manager.apply("record_receipt", {
            "expected_revision": 1, "reason": "Imported but result not fetched",
            "receipt": receipt
        })
        self.assertEqual(result["revision"], 2)
        entry = manager.model()["management"]["receipts"][0]
        self.assertEqual(entry["git_assessment"], "RESULT_OBJECT_NOT_LOADED")
        with self.assertRaisesRegex(server.ManagementError, "independently checked"):
            manager.apply("review_receipt", {
                "expected_revision": 2, "reason": "Cannot pretend to verify missing code",
                "id": receipt["receipt_id"], "decision": "CODE_SCOPE_REVIEWED"
            })

    def test_self_reported_test_hash_does_not_become_verified(self):
        receipt = self.candidate()
        receipt["test_evidence"] = [{"name": "unit tests", "exit_code": 0,
                                      "log_sha256": "a" * 64}]
        self.record(receipt)
        entry = self.target.model()["management"]["receipts"][0]
        self.assertEqual(entry["test_assessment"],
                         "SELF_REPORTED_LOG_HASHES_NOT_INDEPENDENTLY_VERIFIED")


if __name__ == "__main__":
    unittest.main()
