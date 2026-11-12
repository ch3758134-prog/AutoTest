"""Publication preflight: project approval, source history, and GitHub CAS plan."""
from __future__ import annotations

import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

HERE = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location(
    "pc_task_publication", HERE / "portable/task_publication.py")
publication = importlib.util.module_from_spec(spec)
spec.loader.exec_module(publication)


def git(root, *args):
    result = subprocess.run(["git", "-C", str(root), *args], text=True,
                            capture_output=True, encoding="utf-8")
    if result.returncode:
        raise AssertionError(result.stderr)
    return result.stdout.strip()


class PublicationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name) / "target"
        self.root.mkdir()
        self.repo_url = "https://github.com/verified/example.git"
        git(self.root, "init", "-b", "main")
        git(self.root, "config", "user.email", "fixture@example.invalid")
        git(self.root, "config", "user.name", "Fixture")
        git(self.root, "remote", "add", "origin", self.repo_url)
        (self.root / "docs").mkdir()
        (self.root / "docs/design.md").write_text("# Actual design baseline\n", encoding="utf-8")
        git(self.root, "add", ".")
        git(self.root, "commit", "-m", "design source")
        self.pin = git(self.root, "rev-parse", "HEAD")
        self.blob = git(self.root, "rev-parse", "HEAD:docs/design.md")
        self.control = self.root / ".project-control"
        self.control.mkdir()
        self.project = {
            "schema": "project-control-target-project/1", "project_id": "example",
            "repo_url": self.repo_url, "formal_product_identity": "APPROVED",
            "scope_decision": "APPROVED"
        }
        self.task = {
            "id": "IMPL-001", "project_id": "example", "title": "Implement tested feature",
            "state": "DRAFT", "executor": "local_codex", "mode": "CONTROLLED",
            "authorization": {"approved_by": "Owner", "approval_reference": "approval-001"},
            "source": {"repo_url": self.repo_url, "branch": "main",
                       "pinned_sha": self.pin, "work_branch": "feature/impl-001"},
            "inputs": [{"type": "DESIGN_BASELINE", "path": "docs/design.md",
                        "blob_sha": self.blob}],
            "allowed_paths": ["src/**", "tests/unit.py"],
            "acceptance": ["Implementation is reviewable and test evidence is collected"],
            "restrictions": ["NO_FORCE_PUSH", "NO_MERGE", "NO_RESET", "NO_UNAUTHORIZED_DELETE"],
        }
        self.registry = {"schema": "project-control-target-tasks/1",
                         "project_id": "example", "tasks": [self.task]}
        self.decisions = {"schema": "project-control-target-decisions/1",
                          "project_id": "example", "decisions": [
            {"kind": "DESIGN_APPROVAL", "state": "APPROVED", "path": "docs/design.md",
             "blob_sha": self.blob, "approved_by": "Owner", "approval_reference": "approved-design"},
            {"kind": "TASK_EXECUTION_APPROVAL", "state": "APPROVED", "task_id": "IMPL-001",
             "source_sha": self.pin, "approved_by": "Owner",
             "approval_reference": "approval-001"},
        ]}
        self.write()
        git(self.root, "add", ".")
        git(self.root, "commit", "-m", "human approval and draft task")

    def write(self):
        for name, data in (("project.json", self.project),
                           ("tasks.json", self.registry), ("decisions.json", self.decisions)):
            (self.control / name).write_text(
                json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")

    def test_prepare_is_read_only_and_matches_git_blob(self):
        original = (self.control / "tasks.json").read_bytes()
        plan = publication.make_plan(self.root, "IMPL-001")
        self.assertEqual(plan["status"], "AWAITING_REMOTE_CONTENTS_API_CAS")
        self.assertEqual(plan["source_commit"], self.pin)
        self.assertNotEqual(plan["preflight_branch_head"], self.pin)
        self.assertEqual(plan["desired_file"], ".project-control/tasks.json")
        self.assertEqual(plan["expected_tasks_blob_sha"],
                         git(self.root, "hash-object", ".project-control/tasks.json"))
        future = json.loads(plan["proposed_content"])
        self.assertEqual(future["tasks"][0]["state"], publication.READY)
        self.assertEqual(future["tasks"][0]["published_source_sha"], self.pin)
        self.assertEqual(original, (self.control / "tasks.json").read_bytes())
        self.assertFalse(git(self.root, "status", "--porcelain"))

    def test_missing_task_approval_blocks(self):
        self.decisions["decisions"].pop()
        self.write()
        git(self.root, "add", ".")
        git(self.root, "commit", "-m", "remove task approval")
        with self.assertRaisesRegex(publication.PublicationError, "separately recorded"):
            publication.make_plan(self.root, "IMPL-001")

    def test_wrong_task_auth_ref_blocks(self):
        self.task["authorization"]["approval_reference"] = "invented"
        self.write()
        git(self.root, "add", ".")
        git(self.root, "commit", "-m", "invalid authority")
        with self.assertRaisesRegex(publication.PublicationError, "separately recorded"):
            publication.make_plan(self.root, "IMPL-001")

    def test_wrong_design_blob_blocks(self):
        self.task["inputs"][0]["blob_sha"] = "0" * 40
        self.write()
        git(self.root, "add", ".")
        git(self.root, "commit", "-m", "invalid design blob")
        with self.assertRaisesRegex(publication.PublicationError, "approved design"):
            publication.make_plan(self.root, "IMPL-001")

    def test_unapproved_scope_blocks(self):
        self.project["scope_decision"] = "NOT_RECORDED"
        self.write()
        git(self.root, "add", ".")
        git(self.root, "commit", "-m", "scope undecided")
        with self.assertRaisesRegex(publication.PublicationError, "approved product"):
            publication.make_plan(self.root, "IMPL-001")

    def test_dirty_repo_blocks_without_deleting_assets(self):
        note = self.root / "my-own-notes.txt"
        note.write_text("keep me", encoding="utf-8")
        with self.assertRaisesRegex(publication.PublicationError, "dirty"):
            publication.make_plan(self.root, "IMPL-001")
        self.assertEqual(note.read_text(), "keep me")

    def test_wrong_repo_and_out_of_scope_governance_block(self):
        git(self.root, "remote", "set-url", "origin", "https://github.com/other/repo.git")
        with self.assertRaisesRegex(publication.PublicationError, "origin"):
            publication.make_plan(self.root, "IMPL-001")
        git(self.root, "remote", "set-url", "origin", self.repo_url)
        self.task["allowed_paths"] = [".project-control/**"]
        self.write()
        git(self.root, "add", ".")
        git(self.root, "commit", "-m", "invalid scope")
        with self.assertRaisesRegex(publication.PublicationError, "governance"):
            publication.make_plan(self.root, "IMPL-001")

    def test_existing_ready_task_blocks_ambiguous_codex_selection(self):
        other = dict(self.task, id="IMPL-002", state=publication.READY)
        self.registry["tasks"].append(other)
        self.write()
        git(self.root, "add", ".")
        git(self.root, "commit", "-m", "multiple tasks")
        with self.assertRaisesRegex(publication.PublicationError, "other READY"):
            publication.make_plan(self.root, "IMPL-001")

    def test_unrelated_pinned_source_blocks(self):
        self.task["source"]["pinned_sha"] = "f" * 40
        self.decisions["decisions"][1]["source_sha"] = "f" * 40
        self.write()
        git(self.root, "add", ".")
        git(self.root, "commit", "-m", "fake pin")
        with self.assertRaisesRegex(publication.PublicationError, "not an ancestor"):
            publication.make_plan(self.root, "IMPL-001")

    def test_exploratory_can_be_approved_without_design_gate(self):
        self.project["formal_product_identity"] = "UNDECIDED"
        self.project["scope_decision"] = "NOT_RECORDED"
        self.task["mode"] = "EXPLORATORY"
        self.decisions["decisions"] = [self.decisions["decisions"][1]]
        self.write()
        git(self.root, "add", ".")
        git(self.root, "commit", "-m", "authorized research task")
        plan = publication.make_plan(self.root, "IMPL-001")
        self.assertEqual(json.loads(plan["proposed_content"])["tasks"][0]["state"], publication.READY)


if __name__ == "__main__":
    unittest.main()
