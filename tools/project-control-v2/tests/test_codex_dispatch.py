"""Codex's remote-dispatch and scope checker. Tests never touch a real workspace."""
import copy
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
MODULE = ROOT / "ai/codex/resolve.py"
spec = importlib.util.spec_from_file_location("codex_resolver_tested", MODULE)
resolver = importlib.util.module_from_spec(spec)
spec.loader.exec_module(resolver)


def run(repo, *args):
    p = subprocess.run(["git", "-C", str(repo), *args],
                       capture_output=True, text=True, encoding="utf-8")
    if p.returncode:
        raise AssertionError(f"git {' '.join(args)} failed: {p.stderr}")
    return p.stdout.strip()


class CodexDispatchContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.registry, cls.sha = resolver.read_registry()
        cls.task = resolver.pick(cls.registry)

    def test_repository_files_and_ready_task_are_consistent(self):
        self.assertEqual(self.task["id"], "PC-WORKSPACE-LOCAL-001")
        self.assertEqual(self.task["source"]["repo_full_name"], "ch3758134-prog/AutoTest")
        self.assertTrue((ROOT.parents[1] / "CODEX_START_HERE.md").is_file())
        for entry in self.task["inputs"]:
            self.assertTrue((ROOT.parents[1] / entry).is_file(), entry)
        self.assertIn("NO_PUSH", self.task["restrictions"])
        self.assertNotIn("src/**", self.task["allowed_paths"])

    def test_never_guess_when_task_missing_or_multiple(self):
        with self.assertRaises(resolver.DispatchError):
            resolver.pick({"tasks": []})
        duplicate = copy.deepcopy(self.task)
        duplicate["id"] = "PC-OTHER-002"
        with self.assertRaises(resolver.DispatchError):
            resolver.pick({"tasks": [self.task, duplicate]})
        with self.assertRaises(resolver.DispatchError):
            resolver.pick(self.registry, "non-existent-task")

    def test_draft_task_or_missing_restrictions_cannot_run(self):
        draft = copy.deepcopy(self.task)
        draft["status"] = "PROPOSED"
        with self.assertRaises(resolver.DispatchError):
            resolver.pick({"tasks": [draft]}, draft["id"])
        unsafe = copy.deepcopy(self.task)
        unsafe["restrictions"].remove("NO_RESET")
        with self.assertRaises(resolver.DispatchError):
            resolver.pick({"tasks": [unsafe]})

    def test_path_scope(self):
        paths = self.task["allowed_paths"]
        self.assertTrue(resolver.allowed("tools/project-control-v2/workspace_control/registry.py", paths))
        self.assertTrue(resolver.allowed("tools/project-control-v2/tests/test_workspace_control.py", paths))
        self.assertFalse(resolver.allowed("src/AutoTest.Desktop/Shell/MainWindow.xaml", paths))
        self.assertFalse(resolver.allowed("tools/project-control-v2/ai/codex/dispatch.json", paths))

    def test_registry_schema_and_duplicate_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            fp = Path(d) / "dispatch.json"
            fp.write_text(json.dumps({"schema": "different", "tasks": []}), encoding="utf-8")
            with self.assertRaises(resolver.DispatchError):
                resolver.read_registry(fp)
            fp.write_text(json.dumps({"schema": "project-control-codex-dispatch/1",
                                      "tasks": [self.task, self.task]}), encoding="utf-8")
            with self.assertRaises(resolver.DispatchError):
                resolver.read_registry(fp)


class CodexLocalSafetyTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.repo = Path(self.tmp.name) / "source"
        self.repo.mkdir()
        run(self.repo, "init", "-b", "management/project-control-redesign-20261111")
        run(self.repo, "config", "user.name", "Test Fixture")
        run(self.repo, "config", "user.email", "fixture@example.invalid")
        (self.repo / "README.md").write_text("initial\n", encoding="utf-8")
        run(self.repo, "add", "README.md")
        run(self.repo, "commit", "-m", "initial")
        self.base = run(self.repo, "rev-parse", "HEAD")
        run(self.repo, "remote", "add", "origin", "https://github.com/ch3758134-prog/AutoTest.git")
        self.task = copy.deepcopy(CodexDispatchContractTests.task)
        self.task["source"]["minimum_ancestor_sha"] = self.base
        self.digest = "1" * 64

    def test_exact_origin_and_ancestry(self):
        good = resolver.repo_identity(self.repo, self.task)
        self.assertEqual(good["head"], self.base)
        run(self.repo, "remote", "set-url", "origin",
            "https://github.com/ch3758134-prog/OtherRepository.git")
        with self.assertRaises(resolver.DispatchError):
            resolver.repo_identity(self.repo, self.task)

    def test_dirty_start_blocks_claim_and_does_not_erase_files(self):
        (self.repo / "important.txt").write_text("do not delete", encoding="utf-8")
        with self.assertRaises(resolver.DispatchError):
            resolver.create_claim(self.repo, self.task, self.digest)
        self.assertEqual((self.repo / "important.txt").read_text(), "do not delete")
        self.assertFalse(resolver.claim_path(self.repo).exists())

    def test_claim_is_local_and_cannot_be_overwritten(self):
        claim = resolver.create_claim(self.repo, self.task, self.digest)
        self.assertEqual(claim["start_head"], self.base)
        self.assertTrue(resolver.claim_path(self.repo).is_file())
        with self.assertRaises(resolver.DispatchError):
            resolver.create_claim(self.repo, self.task, self.digest)
        self.assertFalse((self.repo / "project-control").exists())

    def test_scope_blocks_untracked_outside_task_area(self):
        resolver.create_claim(self.repo, self.task, self.digest)
        run(self.repo, "switch", "-c", self.task["source"]["local_task_branch"])
        good = self.repo / "tools/project-control-v2/workspace_control/registry.py"
        good.parent.mkdir(parents=True)
        good.write_text("test", encoding="utf-8")
        review = resolver.verify_scope(self.repo, self.task, self.digest)
        self.assertEqual(review["status"], "SCOPE_OK")
        self.assertIn("tools/project-control-v2/workspace_control/registry.py",
                      review["modified_paths"])
        unwanted = self.repo / "src/AutoTest.Desktop/Main.cs"
        unwanted.parent.mkdir(parents=True)
        unwanted.write_text("unsafe", encoding="utf-8")
        review = resolver.verify_scope(self.repo, self.task, self.digest)
        self.assertEqual(review["status"], "BLOCKED_OUT_OF_SCOPE")
        self.assertIn("src/AutoTest.Desktop/Main.cs", review["out_of_scope"])

    def test_scope_blocks_new_commits_outside_task_area(self):
        resolver.create_claim(self.repo, self.task, self.digest)
        run(self.repo, "switch", "-c", self.task["source"]["local_task_branch"])
        (self.repo / "README.md").write_text("out of scope\n", encoding="utf-8")
        run(self.repo, "add", "README.md")
        run(self.repo, "commit", "-m", "changed root README")
        result = resolver.verify_scope(self.repo, self.task, self.digest)
        self.assertEqual(result["status"], "BLOCKED_OUT_OF_SCOPE")
        self.assertIn("README.md", result["out_of_scope"])

    def test_wrong_branch_or_claim_mismatch(self):
        with self.assertRaises(resolver.DispatchError):
            resolver.verify_scope(self.repo, self.task, self.digest)
        resolver.create_claim(self.repo, self.task, self.digest)
        run(self.repo, "switch", "-c", self.task["source"]["local_task_branch"])
        with self.assertRaises(resolver.DispatchError):
            resolver.verify_scope(self.repo, self.task, "different-digest")


if __name__ == "__main__":
    unittest.main()
