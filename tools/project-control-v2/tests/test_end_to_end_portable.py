"""Full *local fixture* handoff: intake -> authority -> Codex -> PR -> review.

This verifies interfaces without fabricating actual Windows/GitHub provider
acceptance. No network calls, no user directories, no fake formal Gate PASS.
"""
from __future__ import annotations

import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "portable"))
import project_init as init
import task_publication as publication
import task_submission as submission
import workspace_manager as workspace
import web_server as management


def git(root, *args):
    p=subprocess.run(["git","-C",str(root),*args],capture_output=True,
                     text=True,encoding="utf-8",errors="replace")
    if p.returncode:
        raise AssertionError("git " + " ".join(args) + ": " + p.stderr)
    return p.stdout.strip()


def run_resolver(root, command):
    path = root / ".project-control/resolve.py"
    p = subprocess.run([sys.executable, str(path), command], cwd=root,
                       text=True,capture_output=True,encoding="utf-8")
    if p.returncode:
        raise AssertionError("resolve " + command + ": " + p.stderr)
    return json.loads(p.stdout)


def write(root, name, data):
    target = root / ".project-control" / name
    target.write_text(json.dumps(data, ensure_ascii=False, indent=2)+"\n",encoding="utf-8")


class IndependentPortableWorkflowTests(unittest.TestCase):
    def test_local_new_and_existing_projects_plus_codex_review(self):
        with tempfile.TemporaryDirectory() as tmp:
            home=Path(tmp)
            existing=home/"existing-project"
            existing.mkdir()
            remote="https://github.com/example-org/target-project.git"
            git(existing,"init","-b","main")
            git(existing,"config","user.name","Fixture")
            git(existing,"config","user.email","test@example.invalid")
            git(existing,"remote","add","origin",remote)
            (existing/"design").mkdir()
            (existing/"design/approved.md").write_text("# Design approved by fixture owner\n")
            git(existing,"add",".")
            git(existing,"commit","-m","preexisting engineering asset")
            plan=init.create_preview(init.inspect_target(existing,remote),"adopt",
                                     project_id="target-project")
            init.apply_preview(existing,init.inspect_target(existing,remote),plan)
            self.assertTrue((existing/".project-control/workspace_manager.py").is_file())
            git(existing,"add",".")
            git(existing,"commit","-m","candidate management bootstrap")
            pin=git(existing,"rev-parse","HEAD")
            design_sha=git(existing,"rev-parse","HEAD:design/approved.md")
            project=json.loads((existing/".project-control/project.json").read_text())
            project["formal_product_identity"]="APPROVED"
            project["scope_decision"]="APPROVED"
            write(existing,"project.json",project)
            decisions=json.loads((existing/".project-control/decisions.json").read_text())
            decisions["decisions"]=[
                {"kind":"DESIGN_APPROVAL","state":"APPROVED",
                 "path":"design/approved.md","blob_sha":design_sha,
                 "approved_by":"Fixture Owner","approval_reference":"fixture-owner-review"},
                {"kind":"TASK_EXECUTION_APPROVAL","state":"APPROVED",
                 "task_id":"IMPL-001","source_sha":pin,
                 "approved_by":"Fixture Owner","approval_reference":"fixture-task-approval"}
            ]
            write(existing,"decisions.json",decisions)
            task={"id":"IMPL-001","title":"Implement one feature",
                  "project_id":"target-project","state":"DRAFT","executor":"local_codex",
                  "mode":"CONTROLLED",
                  "authorization":{"approved_by":"Fixture Owner",
                                   "approval_reference":"fixture-task-approval"},
                  "source":{"repo_url":remote,"branch":"main","pinned_sha":pin,
                            "work_branch":"feature/impl-001"},
                  "inputs":[{"type":"DESIGN_BASELINE","path":"design/approved.md",
                             "blob_sha":design_sha}],
                  "allowed_paths":["src/**"],"acceptance":["Evidence recorded"],
                  "restrictions":["NO_FORCE_PUSH","NO_MERGE","NO_RESET",
                                  "NO_UNAUTHORIZED_DELETE"]}
            write(existing,"tasks.json",{
                "schema":"project-control-target-tasks/1","project_id":"target-project",
                "tasks":[task]})
            git(existing,"add",".")
            git(existing,"commit","-m","actual fixture decisions and task draft")
            publication_plan=publication.make_plan(existing,"IMPL-001")
            self.assertEqual(publication_plan["status"],"AWAITING_REMOTE_CONTENTS_API_CAS")
            (existing/".project-control/tasks.json").write_text(publication_plan["proposed_content"])
            git(existing,"add",".project-control/tasks.json")
            git(existing,"commit","-m","simulate reviewed task publication")
            source_head=git(existing,"rev-parse","HEAD")
            registry_root=home/"registry"
            registered=workspace.register_existing(
                "TARGET-TASK","target-project","IMPL-001","TASK",
                "Implement approved feature",existing,home,remote,"main",pin,
                registry_root,apply=True)
            self.assertEqual(registered["status"],"ACTIVE_EXISTING_REGISTERED")
            self.assertFalse(registered["initial_dirty"])
            self.assertEqual(run_resolver(existing,"select")["task"]["id"],"IMPL-001")
            run_resolver(existing,"claim")
            git(existing,"switch","-c","feature/impl-001")
            manager=home/"manager-worktree"
            git(existing,"worktree","add",str(manager),"main")
            (existing/"src").mkdir()
            (existing/"src/feature.py").write_text("def result():\n    return 3\n")
            git(existing,"add","src/feature.py")
            git(existing,"commit","-m","Codex candidate implementation")
            candidate_sha=git(existing,"rev-parse","HEAD")
            self.assertEqual(run_resolver(existing,"verify-scope")["status"],"SCOPE_OK")
            # Logs remain local Git metadata; never pushed by a task source commit.
            raw=git(existing,"rev-parse","--git-path","project-control")
            metadata=Path(raw)
            if not metadata.is_absolute():
                metadata=existing/metadata
            (metadata/"logs").mkdir(parents=True,exist_ok=True)
            (metadata/"logs/unit.log").write_text("1 passed\n")
            (metadata/"test-runs.json").write_text(json.dumps([
                {"name":"unit tests","exit_code":0,"log_file":"logs/unit.log"}]))
            receipt=run_resolver(existing,"report")
            self.assertEqual(receipt["candidate_head"],candidate_sha)
            self.assertEqual(len(receipt["test_evidence"]),1)
            # Shared git objects allow manager to verify code across linked worktrees.
            target=management.Target(manager)
            collected=target.apply("record_receipt",{
                "expected_revision":1,"reason":"Independent code review candidate",
                "receipt":receipt})
            self.assertEqual(collected["result"],"LOCAL_DRAFT_SAVED_NOT_REMOTE_PUBLISHED")
            record=target.model()["management"]["receipts"][0]
            self.assertEqual(record["git_assessment"],"LOCAL_COMMIT_SCOPE_VERIFIED")
            self.assertNotEqual(record["review_status"],"ACCEPTED")
            pr={"number":1,"url":"https://github.com/example-org/target-project/pull/1",
                "head_repo_full_name":"example-org/target-project",
                "base":"main","head":"feature/impl-001","head_sha":candidate_sha,
                "draft":True,"state":"open","merged":False}
            transition=submission.make_plan(manager,"IMPL-001",receipt,pr)
            self.assertEqual(transition["status"],"PR_CANDIDATE_AWAITING_GITHUB_CONTENTS_CAS")
            candidate_tasks=json.loads(transition["proposed_content"])
            self.assertEqual(candidate_tasks["tasks"][0]["state"],"SUBMITTED_FOR_REVIEW")
            self.assertEqual(candidate_tasks["tasks"][0]["submission"]["ci_review"],"NOT_CHECKED")
            self.assertEqual(target.model()["tasks"]["tasks"][0]["state"],
                             "READY_FOR_LOCAL_IMPLEMENTATION")
            self.assertEqual(target.model()["project"]["phase_gate"],"NOT_VERIFIED")
            self.assertEqual(workspace.status("TARGET-TASK",registry_root)["workspaces"][0]
                             ["retirement_proposal"],"HOLD_FOR_REVIEW")
            # New project has no unearned Gate or task, independently bootstraps.
            fresh=home/"new-project"
            fresh.mkdir()
            new_remote="https://github.com/example-org/new-project.git"
            git(fresh,"init","-b","main")
            git(fresh,"remote","add","origin",new_remote)
            new_intake=init.inspect_target(fresh,new_remote)
            init.apply_preview(fresh,new_intake,
                               init.create_preview(new_intake,"new",project_id="new-project"))
            self.assertEqual(management.Target(fresh).model()["tasks"]["tasks"],[])
            self.assertEqual(management.Target(fresh).model()["project"]["phase_gate"],
                             "NOT_VERIFIED")


if __name__=="__main__":
    unittest.main()
