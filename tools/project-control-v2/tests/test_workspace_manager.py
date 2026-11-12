"""Safe workspace lifecycle integration tests with disposable local Git fixtures."""
from __future__ import annotations
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

HERE = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("pc_workspace_lifecycle", HERE / "portable/workspace_manager.py")
wm = importlib.util.module_from_spec(spec)
spec.loader.exec_module(wm)


def git(root, *args):
    p = subprocess.run(["git", "-C", str(root), *args], capture_output=True,
                       text=True, encoding="utf-8", errors="replace")
    if p.returncode:
        raise AssertionError(p.stderr)
    return p.stdout.strip()


class WorkspaceLifecycleTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.base = Path(self.temp.name)
        self.allowed = self.base / "authorized"
        self.allowed.mkdir()
        self.registry = self.base / "durable-registry"
        self.source = self.base / "source-repository"
        self.source.mkdir()
        self.remote = "https://github.com/owner/project.git"
        git(self.source, "init", "-b", "main")
        git(self.source, "config", "user.name", "Fixture")
        git(self.source, "config", "user.email", "fixture@example.invalid")
        git(self.source, "remote", "add", "origin", self.remote)
        (self.source / "README.md").write_text("Source version\n", encoding="utf-8")
        git(self.source, "add", ".")
        git(self.source, "commit", "-m", "baseline")
        self.pin = git(self.source, "rev-parse", "HEAD")
        self.work = self.allowed / "temporary-review"
        self.work.mkdir()
        self.args = dict(
            workspace_id="TEMP-001", project_id="project", task_id="TASK-1",
            kind="TEMP", purpose="Read-only candidate review",
            path=self.work, allowed_root=self.allowed, repo_url=self.remote,
            source_ref="main", pinned_sha=self.pin, parent_repo=self.source,
            location=self.registry
        )

    def create(self):
        return wm.create(**self.args, apply=True)

    def test_preview_is_read_only_and_does_not_claim_registry(self):
        result = wm.create(**self.args)
        self.assertEqual(result["status"], "PREVIEW_ONLY")
        self.assertEqual(list(self.work.iterdir()), [])
        self.assertEqual(wm.read_registry(self.registry)["workspaces"], [])

    def test_create_and_close_clean_temp_worktree_with_explicit_review(self):
        result = self.create()
        self.assertEqual(result["status"], "ACTIVE_REGISTERED")
        self.assertEqual(result["head"], self.pin)
        self.assertTrue((self.work / "README.md").exists())
        registry = wm.read_registry(self.registry)
        self.assertEqual(registry["workspaces"][0]["state"], "ACTIVE")
        self.assertEqual(registry["workspaces"][0]["relation"], "LINKED_WORKTREE")
        preview = wm.close("TEMP-001", None, None, False, location=self.registry)
        self.assertEqual(preview["status"], "CANDIDATE_FOR_REVIEW")
        self.assertTrue(self.work.is_dir())
        with self.assertRaisesRegex(wm.WorkspaceError, "reviewer"):
            wm.close("TEMP-001", preview["inspection"]["inspection_digest"], None,
                     True, location=self.registry, apply=True)
        with self.assertRaisesRegex(wm.WorkspaceError, "confirm"):
            wm.close("TEMP-001", preview["inspection"]["inspection_digest"], "Owner",
                     False, location=self.registry, apply=True)
        with self.assertRaisesRegex(wm.WorkspaceError, "stale"):
            wm.close("TEMP-001", "f" * 64, "Owner", True,
                     location=self.registry, apply=True)
        result = wm.close("TEMP-001", preview["inspection"]["inspection_digest"],
                          "Owner", True, location=self.registry, apply=True)
        self.assertEqual(result["status"], "RETIRED")
        self.assertFalse(self.work.exists())
        record = wm.read_registry(self.registry)["workspaces"][0]
        self.assertEqual(record["state"], "RETIRED")
        self.assertEqual(record["os_process_check"], "HUMAN_ATTESTED_NOT_AUTOMATICALLY_VERIFIED")
        with self.assertRaises(wm.WorkspaceError):
            wm.close("TEMP-001", preview["inspection"]["inspection_digest"],
                     "Owner", True, location=self.registry, apply=True)

    def test_untracked_and_ignored_files_are_never_discarded(self):
        self.create()
        (self.work / ".gitignore").write_text("cache.bin\n", encoding="utf-8")
        (self.work / "cache.bin").write_bytes(b"important ignored data")
        (self.work / "local-notes.txt").write_text("not in Git", encoding="utf-8")
        review = wm.close("TEMP-001", None, None, False, location=self.registry)
        self.assertEqual(review["status"], "HOLD_FOR_REVIEW")
        self.assertIn("TRACKED_UNTRACKED_OR_IGNORED_FILES", review["inspection"]["issues"])
        with self.assertRaises(wm.WorkspaceError):
            wm.close("TEMP-001", review["inspection"]["inspection_digest"],
                     "Owner", True, location=self.registry, apply=True)
        self.assertEqual((self.work / "cache.bin").read_bytes(), b"important ignored data")
        self.assertTrue(self.work.is_dir())

    def test_head_commit_changes_are_preserved(self):
        self.create()
        git(self.work, "config", "user.name", "Fixture")
        git(self.work, "config", "user.email", "fixture@example.invalid")
        (self.work / "README.md").write_text("New local commit\n", encoding="utf-8")
        git(self.work, "add", "README.md")
        git(self.work, "commit", "-m", "local unique work")
        checked = wm.close("TEMP-001", None, None, False, location=self.registry)
        self.assertEqual(checked["status"], "HOLD_FOR_REVIEW")
        self.assertIn("HEAD_CHANGED_OR_UNREVIEWED_COMMITS", checked["inspection"]["issues"])
        self.assertTrue(self.work.is_dir())

    def test_wrong_parent_repo_or_wrong_head_is_blocked(self):
        git(self.source, "remote", "set-url", "origin", "https://github.com/other/wrong.git")
        with self.assertRaisesRegex(wm.WorkspaceError, "origin mismatches"):
            self.create()
        self.assertFalse(wm.registry_path(self.registry).exists())
        git(self.source, "remote", "set-url", "origin", self.remote)
        self.args["pinned_sha"] = "f" * 40
        with self.assertRaisesRegex(wm.WorkspaceError, "not present"):
            self.create()

    def test_nonempty_or_outside_root_is_blocked(self):
        (self.work / "private.txt").write_text("preserve", encoding="utf-8")
        with self.assertRaisesRegex(wm.WorkspaceError, "not empty"):
            self.create()
        self.assertEqual((self.work / "private.txt").read_text(), "preserve")
        out = self.base / "other-folder"
        out.mkdir()
        self.args["path"] = out
        with self.assertRaisesRegex(wm.WorkspaceError, "authorized root"):
            self.create()

    def test_duplicate_id_or_path_never_overwrites(self):
        self.create()
        extra = self.allowed / "second"
        extra.mkdir()
        with self.assertRaisesRegex(wm.WorkspaceError, "ID already exists"):
            wm.create(**{**self.args, "path": extra}, apply=True)
        with self.assertRaisesRegex(wm.WorkspaceError, "not empty"):
            wm.create(**self.args, apply=True)
        self.assertEqual(wm.read_registry(self.registry)["revision"], 2)

    def test_scan_reports_unknown_git_directories_without_adopting(self):
        unmanaged = self.allowed / "stray-worktree"
        unmanaged.mkdir()
        git(unmanaged, "init")
        result = wm.scan(self.allowed, self.registry)
        self.assertEqual(result["unregistered"][0]["status"], "UNREGISTERED_HOLD_FOR_REVIEW")
        self.assertEqual(wm.read_registry(self.registry)["workspaces"], [])
        self.assertTrue((unmanaged / ".git").exists())

    def test_registry_lock_prevents_concurrent_ownership_changes(self):
        self.registry.mkdir()
        (self.registry / "write.lock").write_text("someone else", encoding="utf-8")
        with self.assertRaisesRegex(wm.WorkspaceError, "held"):
            self.create()
        self.assertEqual((self.registry / "write.lock").read_text(), "someone else")
        self.assertTrue(self.work.exists())

    def test_archive_renames_state_not_files_and_preserves_history(self):
        self.create()
        preview = wm.archive("TEMP-001", "Needs investigation", location=self.registry)
        self.assertEqual(preview["status"], "PREVIEW_ONLY")
        result = wm.archive("TEMP-001", "Needs investigation",
                            location=self.registry, apply=True)
        self.assertEqual(result["status"], "ARCHIVED_RETAINED")
        self.assertTrue((self.work / "README.md").exists())
        self.assertEqual(wm.read_registry(self.registry)["workspaces"][0]["state"],
                         "ARCHIVED_RETAINED")
        with self.assertRaises(wm.WorkspaceError):
            wm.close("TEMP-001", None, "Owner", True,
                     location=self.registry, apply=True)

    def test_creation_failure_is_registered_not_silently_deleted(self):
        original = wm.run_git
        def failed(parent, *args, **kwargs):
            if len(args) >= 2 and args[:2] == ("worktree", "add"):
                raise wm.WorkspaceError("simulated worktree failure")
            return original(parent, *args, **kwargs)
        with patch.object(wm, "run_git", side_effect=failed):
            with self.assertRaisesRegex(wm.WorkspaceError, "simulated"):
                self.create()
        value = wm.read_registry(self.registry)
        self.assertEqual(value["workspaces"][0]["state"], "CREATION_FAILED_KEEP_FOR_REVIEW")
        self.assertTrue(self.work.exists())
        self.assertEqual(value["events"][-1]["action"], "CREATION_FAILED")


if __name__ == "__main__":
    unittest.main()
