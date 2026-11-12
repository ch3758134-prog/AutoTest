"""Read-only Git remote-check classification, fully offline fixtures."""
from pathlib import Path
from unittest.mock import patch
import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest

HERE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HERE / "portable"))
import remote_sync


def git(root, *args):
    p = subprocess.run(["git", "-C", str(root), *args], text=True,
                       capture_output=True)
    if p.returncode:
        raise AssertionError(p.stderr)
    return p.stdout.strip()


class RemoteSyncTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name) / "project"
        self.root.mkdir()
        self.repo_url = "https://github.com/sample/a-project.git"
        git(self.root, "init", "-b", "main")
        git(self.root, "config", "user.name", "Fixture")
        git(self.root, "config", "user.email", "fixture@example.invalid")
        git(self.root, "remote", "add", "origin", self.repo_url)
        control = self.root / ".project-control"
        control.mkdir()
        (control / "project.json").write_text(
            json.dumps({"project_id": "a-project", "repo_url": self.repo_url}), encoding="utf-8")
        (self.root / "README.md").write_text("hello", encoding="utf-8")
        git(self.root, "add", ".")
        git(self.root, "commit", "-m", "initial")
        self.head = git(self.root, "rev-parse", "HEAD")
        self.original_call = remote_sync.call

    def fake_remote(self, value):
        def call(root, args, timeout=15, allow_missing=False):
            if args[:2] == ["ls-remote", "--heads"]:
                if value == "FAIL":
                    raise remote_sync.SyncError("offline fixture")
                return (value + "\trefs/heads/main") if value else ""
            return self.original_call(root, args, timeout=timeout, allow_missing=allow_missing)
        return call

    def test_local_only_never_claims_remote_checked(self):
        value = remote_sync.inspect(self.root)
        self.assertFalse(value["remote_checked"])
        self.assertEqual(value["relation"], "REMOTE_NOT_CHECKED")
        self.assertEqual(value["local_head"], self.head)

    def test_explicit_same_head_and_unpublished_branch(self):
        with patch.object(remote_sync, "call", self.fake_remote(self.head)):
            value = remote_sync.inspect(self.root, check_remote=True)
            self.assertTrue(value["remote_checked"])
            self.assertEqual(value["relation"], "SAME_HEAD")
        with patch.object(remote_sync, "call", self.fake_remote("")):
            value = remote_sync.inspect(self.root, check_remote=True)
            self.assertEqual(value["relation"], "BRANCH_NOT_PUBLISHED")

    def test_local_ahead_and_missing_remote_history(self):
        (self.root / "README.md").write_text("next", encoding="utf-8")
        git(self.root, "add", "README.md")
        git(self.root, "commit", "-m", "second")
        with patch.object(remote_sync, "call", self.fake_remote(self.head)):
            value = remote_sync.inspect(self.root, check_remote=True)
            self.assertEqual(value["relation"], "LOCAL_AHEAD")
        with patch.object(remote_sync, "call", self.fake_remote("f" * 40)):
            value = remote_sync.inspect(self.root, check_remote=True)
            self.assertEqual(value["relation"], "REMOTE_HISTORY_NOT_LOADED")

    def test_failed_check_does_not_claim_verification(self):
        with patch.object(remote_sync, "call", self.fake_remote("FAIL")):
            value = remote_sync.inspect(self.root, check_remote=True)
            self.assertFalse(value["remote_checked"])
            self.assertEqual(value["relation"], "REMOTE_CHECK_FAILED")

    def test_wrong_origin_and_detached_head(self):
        git(self.root, "remote", "set-url", "origin", "https://github.com/another/repo.git")
        with self.assertRaises(remote_sync.SyncError):
            remote_sync.inspect(self.root)
        git(self.root, "remote", "set-url", "origin", self.repo_url)
        git(self.root, "switch", "--detach", self.head)
        value = remote_sync.inspect(self.root, check_remote=True)
        self.assertEqual(value["relation"], "NO_NAMED_BRANCH")
        self.assertFalse(value["remote_checked"])


if __name__ == "__main__":
    unittest.main()
