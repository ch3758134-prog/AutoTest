"""Portable target Codex resolver tests on temporary Git repositories only."""
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

HERE = Path(__file__).resolve().parents[1]
MODULE = HERE / "portable/project_init.py"
spec = importlib.util.spec_from_file_location("portable_project_init_for_target", MODULE)
init = importlib.util.module_from_spec(spec)
spec.loader.exec_module(init)


def git(repo, *args):
    p = subprocess.run(["git", "-C", str(repo), *args], text=True,
                       encoding="utf-8", capture_output=True)
    if p.returncode:
        raise AssertionError(p.stderr)
    return p.stdout.strip()


class PortableTargetResolverTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name) / "target"
        self.root.mkdir()
        self.repo_url = "https://github.com/example-owner/example-repo.git"
        git(self.root, "init", "-b", "main")
        git(self.root, "config", "user.email", "fixture@example.invalid")
        git(self.root, "config", "user.name", "Fixture")
        git(self.root, "remote", "add", "origin", self.repo_url)
        design = self.root / "docs/design.md"
        design.parent.mkdir()
        design.write_text("# design\n\nExact source artifact\n", encoding="utf-8")
        git(self.root, "add", "docs/design.md")
        git(self.root, "commit", "-m", "existing project asset")
        state = init.inspect_target(self.root, self.repo_url)
        candidate = init.create_preview(state, "adopt", project_id="example-project")
        init.apply_preview(self.root, state, candidate)
        self.project_path = self.root / ".project-control/project.json"
        self.decisions_path = self.root / ".project-control/decisions.json"
        self.tasks_path = self.root / ".project-control/tasks.json"
        self.resolver_path = self.root / ".project-control/resolve.py"
        self.assertTrue(self.resolver_path.is_file())
        git(self.root, "add", ".")
        git(self.root, "commit", "-m", "add candidate management records")
        self.source_head = git(self.root, "rev-parse", "HEAD")
        self.design_blob = git(self.root, "rev-parse", "HEAD:docs/design.md")

    def invoke(self, action, *extra):
        return subprocess.run([sys.executable, str(self.resolver_path), action, *extra],
                              cwd=self.root, capture_output=True, text=True, encoding="utf-8")

    def task(self, mode="CONTROLLED"):
        return {
            "id": "IMPL-001",
            "project_id": "example-project",
            "title": "A small implementation task",
            "state": "READY_FOR_LOCAL_IMPLEMENTATION",
            "executor": "local_codex",
            "mode": mode,
            "authorization": {"approved_by": "Project owner",
                              "approval_reference": "human-record-001"},
            "source": {"repo_url": self.repo_url, "branch": "main",
                       "pinned_sha": self.source_head,
                       "work_branch": "feature/impl-001"},
            "inputs": [{"type": "DESIGN_BASELINE", "path": "docs/design.md",
                        "blob_sha": self.design_blob}],
            "allowed_paths": ["src/**"],
            "acceptance": ["Tests and source diff reviewed"],
            "restrictions": ["NO_FORCE_PUSH", "NO_MERGE", "NO_RESET",
                             "NO_UNAUTHORIZED_DELETE"],
        }

    def write_task(self, task, include_execution_approval=True):
        if include_execution_approval:
            decisions = json.loads(self.decisions_path.read_text(encoding="utf-8"))
            decisions["decisions"].append({
                "kind": "TASK_EXECUTION_APPROVAL",
                "state": "APPROVED", "task_id": task["id"],
                "source_sha": task["source"]["pinned_sha"],
                "approved_by": task["authorization"]["approved_by"],
                "approval_reference": task["authorization"]["approval_reference"]
            })
            self.decisions_path.write_text(json.dumps(decisions, indent=2), encoding="utf-8")
        data = json.loads(self.tasks_path.read_text(encoding="utf-8"))
        data["tasks"] = [task]
        self.tasks_path.write_text(json.dumps(data, indent=2), encoding="utf-8")
        git(self.root, "add", ".project-control/tasks.json", ".project-control/decisions.json")
        git(self.root, "commit", "-m", "task candidate")

    def approve(self):
        p = json.loads(self.project_path.read_text(encoding="utf-8"))
        p["formal_product_identity"] = "APPROVED"
        p["scope_decision"] = "APPROVED"
        self.project_path.write_text(json.dumps(p, indent=2), encoding="utf-8")
        d = json.loads(self.decisions_path.read_text(encoding="utf-8"))
        d["decisions"] = [{
            "kind": "DESIGN_APPROVAL", "state": "APPROVED",
            "path": "docs/design.md", "blob_sha": self.design_blob,
            "approved_by": "Project owner", "approval_reference": "decision-001"
        }]
        self.decisions_path.write_text(json.dumps(d, indent=2), encoding="utf-8")
        git(self.root, "add", ".project-control/project.json", ".project-control/decisions.json")
        git(self.root, "commit", "-m", "record explicitly approved scope and design")

    def test_empty_tasks_stop_before_claim(self):
        p = self.invoke("select")
        self.assertEqual(p.returncode, 2, p.stdout)
        self.assertIn("no unique", p.stderr)

    def test_controlled_missing_approvals_stop(self):
        self.write_task(self.task())
        p = self.invoke("select")
        self.assertEqual(p.returncode, 2, p.stdout)
        self.assertIn("not yet approved", p.stderr)

    def test_controlled_approval_and_scope_flow(self):
        self.approve()
        self.write_task(self.task())
        selected = self.invoke("select")
        self.assertEqual(selected.returncode, 0, selected.stderr)
        claimed = self.invoke("claim")
        self.assertEqual(claimed.returncode, 0, claimed.stderr)
        self.assertEqual(self.invoke("claim").returncode, 2)
        git(self.root, "switch", "-c", "feature/impl-001")
        src = self.root / "src/demo.py"
        src.parent.mkdir()
        src.write_text("print('candidate')\n", encoding="utf-8")
        verified = self.invoke("verify-scope")
        self.assertEqual(verified.returncode, 0, verified.stderr)
        self.assertEqual(json.loads(verified.stdout)["status"], "SCOPE_OK")
        # Even if a task accidentally expands allowed_paths, governance is protected.
        self.project_path.write_text(self.project_path.read_text() + "\n", encoding="utf-8")
        blocked = self.invoke("verify-scope")
        self.assertEqual(blocked.returncode, 3, blocked.stderr)
        self.assertIn(".project-control/project.json", blocked.stdout)

    def test_wrong_repo_and_wrong_branch_fail(self):
        self.approve()
        self.write_task(self.task())
        git(self.root, "remote", "set-url", "origin", "https://github.com/other/wrong.git")
        blocked = self.invoke("select")
        self.assertEqual(blocked.returncode, 2)
        self.assertIn("not the registered target", blocked.stderr)
        git(self.root, "remote", "set-url", "origin", self.repo_url)
        git(self.root, "switch", "-c", "feature/unrelated")
        self.assertEqual(self.invoke("select").returncode, 2)

    def test_task_cannot_self_authorize_without_separate_approval(self):
        self.approve()
        self.write_task(self.task(), include_execution_approval=False)
        blocked = self.invoke("select")
        self.assertEqual(blocked.returncode, 2)
        self.assertIn("separately recorded execution approval", blocked.stderr)

    def test_exploratory_task_without_formal_design_but_with_authorization(self):
        self.write_task(self.task("EXPLORATORY"))
        selected = self.invoke("select")
        self.assertEqual(selected.returncode, 0, selected.stderr)


if __name__ == "__main__":
    unittest.main()
