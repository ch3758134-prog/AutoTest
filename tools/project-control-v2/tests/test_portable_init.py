"""Regression: portable NEW/ADOPT intake never fabricates approval or overwrites work."""
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

HERE = Path(__file__).resolve().parents[1]
MODULE = HERE / "portable/project_init.py"
spec = importlib.util.spec_from_file_location("project_init_tested", MODULE)
init = importlib.util.module_from_spec(spec)
spec.loader.exec_module(init)


def git(path, *args):
    result = subprocess.run(["git", "-C", str(path), *args], text=True,
                            capture_output=True, encoding="utf-8")
    if result.returncode:
        raise AssertionError(result.stderr)
    return result.stdout.strip()


class PortableInitializationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name) / "target"
        self.root.mkdir()
        git(self.root, "init", "-b", "main")
        git(self.root, "config", "user.name", "Fixture")
        git(self.root, "config", "user.email", "fixture@example.invalid")
        self.url = "https://github.com/someone/new-project.git"
        git(self.root, "remote", "add", "origin", self.url)

    def add_asset(self, path="README.md"):
        (self.root / path).write_text("old engineering asset\n", encoding="utf-8")
        git(self.root, "add", path)
        git(self.root, "commit", "-m", "existing source asset")

    def inspect(self):
        return init.inspect_target(self.root, self.url)

    def test_package_entry_inside_deliverable_folder(self):
        manifest = json.loads((HERE / "PORTABLE_MANIFEST.json").read_text(encoding="utf-8"))
        self.assertEqual(manifest["entrypoint"], "START_HERE.md")
        for name in manifest["required_files"]:
            self.assertTrue((HERE / name).is_file(), name)
        self.assertTrue((HERE / "START_HERE.md").is_file())
        self.assertIn("目标 GitHub 仓库", (HERE / "START_HERE.md").read_text(encoding="utf-8"))

    def test_url_security_and_exact_origin(self):
        self.assertEqual(init.canonical_github_repo(self.url), self.url)
        for bad in ("https://github.com/someone",
                    "https://github.com/someone/new-project/tree/main",
                    "https://github.com.evil.test/someone/new-project.git",
                    "https://u:p@github.com/someone/new-project",
                    "http://github.com/someone/new-project"):
            with self.subTest(url=bad), self.assertRaises(init.InitError):
                init.canonical_github_repo(bad)
        with self.assertRaises(init.InitError):
            init.inspect_target(self.root, "https://github.com/someone/wrong.git")

    def test_new_repo_candidate_never_claims_approval_or_ready_tasks(self):
        inspected = self.inspect()
        self.assertEqual(inspected["tracked_files_count"], 0)
        preview = init.create_preview(inspected, "new")
        self.assertEqual(set(preview["files"]), set(init.SCAFFOLD))
        self.assertFalse((self.root / ".project-control").exists())
        proj = json.loads(preview["files"][".project-control/project.json"])
        self.assertEqual(proj["initialization_state"], "CANDIDATE_NOT_APPROVED")
        self.assertEqual(proj["formal_product_identity"], "UNDECIDED")
        self.assertEqual(proj["phase_gate"], "NOT_VERIFIED")
        tasks = json.loads(preview["files"][".project-control/tasks.json"])
        self.assertEqual(tasks["tasks"], [])

    def test_new_mode_rejects_existing_assets(self):
        self.add_asset()
        with self.assertRaises(init.InitError):
            init.create_preview(self.inspect(), "new")

    def test_adopt_records_observed_asset_without_downgrading_or_approving_it(self):
        self.add_asset()
        inspected = self.inspect()
        preview = init.create_preview(inspected, "adopt", "reused-project", "Existing project")
        asset = json.loads(preview["files"][".project-control/assets.json"])
        self.assertEqual(asset["tracked_file_count"], 1)
        self.assertEqual(asset["entries"][0]["path"], "README.md")
        self.assertEqual(asset["entries"][0]["approval"], "UNKNOWN")
        project = json.loads(preview["files"][".project-control/project.json"])
        self.assertEqual(project["intake_mode"], "ADOPT")
        self.assertEqual(project["observed_head"], git(self.root, "rev-parse", "HEAD"))
        self.assertEqual(project["scope_decision"], "NOT_RECORDED")
        self.assertIn("old engineering asset", (self.root / "README.md").read_text())

    def test_apply_is_only_local_and_refuses_overwrite(self):
        inspected = self.inspect()
        preview = init.create_preview(inspected, "new")
        status = init.apply_preview(self.root, inspected, preview)
        self.assertEqual(status["status"], "LOCAL_CANDIDATE_WRITTEN_NOT_COMMITTED")
        self.assertTrue((self.root / "PROJECT_START_HERE.md").is_file())
        self.assertTrue((self.root / ".project-control/tasks.json").is_file())
        self.assertIsNone(init.run_git(self.root, "rev-parse", "--verify", "HEAD", allow_failure=True))
        with self.assertRaises(init.InitError):
            init.apply_preview(self.root, inspected, preview)

    def test_existing_management_directory_is_not_replaced(self):
        (self.root / ".project-control").mkdir()
        original = self.root / ".project-control/project.json"
        original.write_text('{"protected": true}', encoding="utf-8")
        inspected = self.inspect()
        with self.assertRaises(init.InitError):
            init.apply_preview(self.root, inspected, init.create_preview(inspected, "new"))
        self.assertEqual(original.read_text(encoding="utf-8"), '{"protected": true}')

    def test_dirty_repo_blocks_intake(self):
        self.add_asset()
        (self.root / "new-user-file.txt").write_text("preserve", encoding="utf-8")
        inspected = self.inspect()
        with self.assertRaises(init.InitError):
            init.create_preview(inspected, "adopt")
        self.assertEqual((self.root / "new-user-file.txt").read_text(), "preserve")

    def test_upgrade_preserves_old_portable_records(self):
        self.add_asset()
        initial = self.inspect()
        preview = init.create_preview(initial, "adopt", project_id="existing-project")
        init.apply_preview(self.root, initial, preview)
        git(self.root, "add", ".")
        git(self.root, "commit", "-m", "initial intake")
        previous_project = (self.root / ".project-control/project.json").read_bytes()
        previous_tasks = (self.root / ".project-control/tasks.json").read_bytes()
        git(self.root, "rm", ".project-control/management.json")
        git(self.root, "commit", "-m", "old schema without management")
        inspected = self.inspect()
        review = init.upgrade_management(self.root, inspected)
        self.assertEqual(review["status"], "UPGRADE_PREVIEW")
        self.assertFalse((self.root / ".project-control/management.json").exists())
        result = init.upgrade_management(self.root, inspected, apply=True)
        self.assertEqual(result["status"], "LOCAL_UPGRADE_WRITTEN_NOT_COMMITTED")
        self.assertEqual((self.root / ".project-control/project.json").read_bytes(), previous_project)
        self.assertEqual((self.root / ".project-control/tasks.json").read_bytes(), previous_tasks)
        with self.assertRaises(init.InitError):
            init.upgrade_management(self.root, self.inspect(), apply=True)

    def test_existing_project_runtime_upgrade_uses_cas_and_local_backup(self):
        self.add_asset()
        before = self.inspect()
        preview = init.create_preview(before, "adopt", project_id="existing-project")
        init.apply_preview(self.root, before, preview)
        git(self.root, "add", ".")
        git(self.root, "commit", "-m", "initial management")
        resolver = self.root / ".project-control/resolve.py"
        workspace = self.root / ".project-control/workspace_manager.py"
        resolver.write_text("# old resolver\n", encoding="utf-8")
        workspace.unlink()
        git(self.root, "add", ".")
        git(self.root, "commit", "-m", "old runtime version")
        clean = self.inspect()
        plan = init.upgrade_runtime(self.root, clean)
        self.assertEqual(plan["status"], "RUNTIME_UPGRADE_PREVIEW")
        by_name = {x["name"]: x for x in plan["actions"]}
        self.assertEqual(by_name["resolve.py"]["operation"], "REPLACE_WITH_BACKUP")
        self.assertEqual(by_name["workspace_manager.py"]["operation"], "CREATE")
        with self.assertRaisesRegex(init.InitError, "hash mismatch"):
            init.upgrade_runtime(self.root, clean, apply=True,
                                 expected_resolver_sha="0" * 64)
        self.assertEqual(resolver.read_text(encoding="utf-8"), "# old resolver\n")
        result = init.upgrade_runtime(
            self.root, clean, apply=True,
            expected_resolver_sha=by_name["resolve.py"]["current_sha256"])
        self.assertEqual(result["status"], "LOCAL_RUNTIME_UPGRADED_NOT_COMMITTED")
        self.assertTrue(workspace.is_file())
        self.assertIn("TASK_EXECUTION_APPROVAL", resolver.read_text(encoding="utf-8"))
        backup = Path(result["backup_dir"]) / (
            "resolve.py." + by_name["resolve.py"]["current_sha256"] + ".bak")
        self.assertEqual(backup.read_text(encoding="utf-8"), "# old resolver\n")
        self.assertTrue((self.root / "README.md").is_file())

    def test_adopt_without_history_requires_more_investigation(self):
        with self.assertRaises(init.InitError):
            init.create_preview(self.inspect(), "adopt")


if __name__ == "__main__":
    unittest.main()
