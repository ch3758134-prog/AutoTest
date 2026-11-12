"""Codex PR submission must not turn a pending result into an accepted task."""
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
from task_submission import make_plan, SubmissionError


def git(root, *args):
    p = subprocess.run(["git", "-C", str(root), *args], capture_output=True, text=True)
    if p.returncode:
        raise AssertionError(p.stderr)
    return p.stdout.strip()


class TaskSubmissionTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name) / "manager"
        self.root.mkdir()
        self.repo = "https://github.com/team/target-project.git"
        git(self.root, "init", "-b", "main")
        git(self.root, "config", "user.name", "Fixture")
        git(self.root, "config", "user.email", "fixture@example.invalid")
        git(self.root, "remote", "add", "origin", self.repo)
        docs = self.root / "docs"
        docs.mkdir()
        (docs / "approved.md").write_text("# Real project design\n", encoding="utf-8")
        git(self.root, "add", ".")
        git(self.root, "commit", "-m", "design")
        pin = git(self.root, "rev-parse", "HEAD")
        control = self.root / ".project-control"
        control.mkdir()
        self.tasks_path = control / "tasks.json"
        self.task = {
            "id": "IMPL-001", "project_id": "target-project", "executor": "local_codex",
            "mode": "CONTROLLED", "title": "Real implementation",
            "state": "READY_FOR_LOCAL_IMPLEMENTATION",
            "authorization": {"approved_by": "Owner", "approval_reference": "OWNER-01"},
            "source": {"repo_url": self.repo, "branch": "main", "pinned_sha": pin,
                       "work_branch": "feature/task-impl-001"},
            "allowed_paths": ["src/**"], "inputs": ["docs/approved.md"],
            "acceptance": ["Evidence reviewed"],
        }
        self.tasks = {"schema": "project-control-target-tasks/1",
                      "project_id": "target-project", "tasks": [self.task]}
        self.project = {"schema": "project-control-target-project/1",
                        "project_id": "target-project", "repo_url": self.repo}
        self.decisions = {"schema": "project-control-target-decisions/1",
                          "project_id": "target-project", "decisions": [{
                              "kind": "TASK_EXECUTION_APPROVAL", "state": "APPROVED",
                              "task_id": "IMPL-001", "source_sha": pin,
                              "approved_by": "Owner", "approval_reference": "OWNER-01"}]}
        self.write_all()
        git(self.root, "add", ".")
        git(self.root, "commit", "-m", "approved ready task")
        source = git(self.root, "rev-parse", "HEAD")
        self.receipt = {"schema": "project-control-codex-result/1",
                        "project_id": "target-project", "task_id": "IMPL-001",
                        "repo_url": self.repo, "source_head": source,
                        "candidate_head": "f" * 40,
                        "work_branch": "feature/task-impl-001", "scope_status": "SCOPE_OK",
                        "workspace_clean": True,
                        "changed_paths": ["src/feature.py"],
                        "task_registry_sha256": hashlib.sha256(self.tasks_path.read_bytes()).hexdigest(),
                        "receipt_id": "receipt-01"}
        self.pr = {"number": 12, "url": "https://github.com/team/target-project/pull/12",
                   "head_repo_full_name": "team/target-project", "base": "main",
                   "head": "feature/task-impl-001", "head_sha": "f" * 40,
                   "draft": True, "state": "open", "merged": False}

    def write_all(self):
        for name, value in (("project.json", self.project), ("tasks.json", self.tasks),
                            ("decisions.json", self.decisions)):
            (self.root / ".project-control" / name).write_text(
                json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    def test_submission_is_only_candidate_and_preserves_source(self):
        original = self.tasks_path.read_bytes()
        result = make_plan(self.root, "IMPL-001", self.receipt, self.pr)
        self.assertEqual(result["status"], "PR_CANDIDATE_AWAITING_GITHUB_CONTENTS_CAS")
        self.assertEqual(result["expected_tasks_blob_sha"], git(self.root, "hash-object", ".project-control/tasks.json"))
        proposed = json.loads(result["proposed_content"])
        work = proposed["tasks"][0]
        self.assertEqual(work["state"], "SUBMITTED_FOR_REVIEW")
        self.assertEqual(work["submission"]["candidate_head"], "f" * 40)
        self.assertEqual(work["submission"]["ci_review"], "NOT_CHECKED")
        self.assertEqual(work["submission"]["code_review"], "NOT_APPROVED")
        self.assertEqual(work["submission"]["tests"], "NOT_INDEPENDENTLY_VERIFIED")
        self.assertEqual(original, self.tasks_path.read_bytes())

    def test_stale_registry_prevents_submission(self):
        self.receipt["task_registry_sha256"] = "0" * 64
        with self.assertRaisesRegex(SubmissionError, "stale task registry"):
            make_plan(self.root, "IMPL-001", self.receipt, self.pr)

    def test_pr_head_moved_requires_review(self):
        self.pr["head_sha"] = "e" * 40
        with self.assertRaisesRegex(SubmissionError, "PR head has moved"):
            make_plan(self.root, "IMPL-001", self.receipt, self.pr)

    def test_missing_execution_approval_blocks(self):
        self.decisions["decisions"] = []
        self.write_all()
        git(self.root, "add", ".")
        git(self.root, "commit", "-m", "missing authorization")
        with self.assertRaisesRegex(SubmissionError, "execution approval missing"):
            make_plan(self.root, "IMPL-001", self.receipt, self.pr)

    def test_dirty_manager_checkout_rejected(self):
        (self.root / "local-notes.md").write_text("must keep", encoding="utf-8")
        with self.assertRaisesRegex(SubmissionError, "uncommitted"):
            make_plan(self.root, "IMPL-001", self.receipt, self.pr)
        self.assertTrue((self.root / "local-notes.md").is_file())

    def test_duplicate_submission_does_not_revert_state(self):
        self.task["state"] = "SUBMITTED_FOR_REVIEW"
        self.write_all()
        git(self.root, "add", ".")
        git(self.root, "commit", "-m", "already submitted")
        with self.assertRaisesRegex(SubmissionError, "must be READY"):
            make_plan(self.root, "IMPL-001", self.receipt, self.pr)


if __name__ == "__main__":
    unittest.main()
