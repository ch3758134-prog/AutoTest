"""Execution-centre regression using fixed GitHub API fixtures (not hardware mocks)."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from studio_store import Store,RuleError
from studio_execution import Execution, GitHubReader,require_repo

REPO="ch3758134-prog/AutoTest"

class FixtureReader:
    def __init__(self):
        self.issue_state="open"
        self.fail_sync=False
    def snapshot(self,repo):
        if self.fail_sync:
            raise RuleError("offline")
        return {"repository":repo,"source":"test fixture, not real GitHub",
                "fetched_at":"2026-11-10T00:00:00+00:00",
                "issues":[{"number":17,"title":"CAN hub coding task","state":self.issue_state,
                           "url":"https://github.com/"+repo+"/issues/17","updated_at":"2026-11-10",
                           "assignees":[],"labels":[],"body":"Code per approved design"},
                          {"number":25,"title":"[RESEARCH] CanTool behavior","state":"open",
                           "url":"https://github.com/"+repo+"/issues/25","updated_at":"2026-11-10",
                           "assignees":[],"labels":[],"body":"Investigate, no code"}],
                "pull_requests":[{"number":19,"title":"Device implementation","state":"open",
                                  "draft":True,"head_sha":"b"*40,"base_branch":"main",
                                  "head_branch":"device","merged_at":None,
                                  "url":"https://github.com/"+repo+"/pull/19",
                                  "ci":{"status":"completed","conclusion":"success",
                                        "head_sha":"b"*40}}],
                "workflow_runs":[],"truncated":False}

class ExecutionTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name)
        self.store=Store(self.root/"workspace.sqlite3")
        self.fixture=FixtureReader()
        self.engine=Execution(self.store,self.fixture)
        self.project=self.store.create_project({"name":"Embedded Lab","summary":"real devices",
                                                "version":"R1"})["project"]["id"]
        self.engine.configure(self.project,REPO)
        self.engine.refresh(self.project)

    def _approved_design(self):
        data={"behavior":"One RX owner publishes framed data",
              "interfaces":"CAN ID, channel, DLC, payload and timestamp",
              "states":"Connected, disconnected, reconnecting",
              "errors":"Timeout and bus reset are explicit",
              "concurrency":"One consumer of device RX; bounded UI subscriptions",
              "verification":"Correct frames and disconnect/reconnect assertions"}
        item=self.store.create_object({"project_id":self.project,"kind":"design",
            "title":"CAN ingress design","body":"Signed architecture and unit behavior",
            "meta":{"unit_design":data}})["item"]
        return self.store.approve(item["id"],item["revision"],"Owner","Reviewed unit details")["item"]

    def _work(self,design=None):
        if design is None:design=self._approved_design()
        obj=self.store.create_object({"project_id":self.project,"kind":"work_package",
            "title":"CAN hub implementation","body":"Implement only a shared receiver",
            "meta":{"execution_role":"CODER","acceptance":"bench and unit results must be separate",
                    "source_sha":"a"*40, "file_scope":["src/device/can.cs"],
                    "input_revisions":{design["id"]:design["revision"]}}})["item"]
        link=self.store.create_relation({"source_id":design["id"],"target_id":obj["id"],"kind":"depends"})
        self.store.approve_relation(link["id"],"Owner","Dependency approved")
        self.store.approve(obj["id"],obj["revision"],"Owner","Ready for coding")
        return obj,design

    def test_snapshot_uses_real_fact_categories(self):
        view=self.engine.overview(self.project)
        self.assertEqual(view["repository"],REPO)
        self.assertEqual(len(view["snapshot"]["issues"]),2)
        self.assertEqual(view["snapshot"]["pull_requests"][0]["ci"]["conclusion"],"success")
        self.assertEqual(view["tasks"],[])

    def test_import_issue_is_not_code_ready_and_duplicate_is_rejected(self):
        data=self.engine.import_issue(self.project,17)
        task=data["tasks"][0]
        self.assertEqual(task["state"],"PLANNING")
        self.assertFalse(task["readiness"]["ready"])
        self.assertEqual(task["agent_role"],"UNASSIGNED")
        with self.assertRaises(RuleError):
            self.engine.dispatch(task["object_id"],"Owner","Send coding task")
        with self.assertRaises(RuleError):
            self.engine.import_issue(self.project,17)

    def test_code_ready_dispatch_claim_and_pr_mapping(self):
        wp,design=self._work()
        self.engine.bind(wp["id"],17,"DEVICE")
        result=self.engine.dispatch(wp["id"],"Owner","Reviewed scope and contract")
        self.assertEqual(result["tasks"][0]["effective_state"],"DISPATCHED")
        self.assertEqual(len(self.engine.queue(self.project,"DEVICE")),1)
        self.assertEqual(self.engine.queue(self.project,"UI"),[])
        packet=self.engine.claim(wp["id"],"DEVICE","CODEX-DEVICE-1")
        self.assertEqual(packet["manifest"]["schema"],"engineering-coding-task/1")
        self.assertEqual(packet["manifest"]["source_sha"],"a"*40)
        self.assertIn("Signed architecture",packet["markdown"])
        self.assertIn("No research",packet["manifest"]["forbidden"])
        self.engine.link_pr(wp["id"],19)
        self.assertEqual(self.engine.overview(self.project)["tasks"][0]["pr_number"],19)
        with self.assertRaises(RuleError):
            self.engine.claim(wp["id"],"DEVICE","SECOND-WORKER")

    def test_design_revision_invalidates_dispatched_task(self):
        wp,design=self._work()
        self.engine.bind(wp["id"],17,"DEVICE")
        self.engine.dispatch(wp["id"],"Owner","Ready")
        self.store.update_object(design["id"],{"expected_revision":design["revision"],
                            "body":"Changed architecture, needs review"})
        info=self.engine.overview(self.project)
        self.assertEqual(info["tasks"][0]["effective_state"],"BLOCKED_BY_CHANGED_DESIGN")
        self.assertEqual(self.engine.queue(self.project,"DEVICE"),[])
        with self.assertRaises(RuleError):
            self.engine.claim(wp["id"],"DEVICE","CODEX-DEVICE")

    def test_issue_closed_or_unapproved_cannot_dispatch(self):
        wp,_=self._work()
        self.engine.bind(wp["id"],17,"DEVICE")
        self.fixture.issue_state="closed"
        self.engine.refresh(self.project)
        with self.assertRaisesRegex(RuleError,"not open"):
            self.engine.dispatch(wp["id"],"Owner","Try")

    def test_refresh_failure_preserves_snapshot_and_marks_stale(self):
        self.fixture.fail_sync=True
        with self.assertRaisesRegex(RuleError,"offline"):
            self.engine.refresh(self.project)
        info=self.engine.overview(self.project)
        self.assertEqual(info["sync_error"],"offline")
        self.assertEqual(len(info["snapshot"]["issues"]),2)

    def test_revoke_and_no_undocumented_auto_claim(self):
        wp,_=self._work()
        self.engine.bind(wp["id"],17,"DEVICE")
        self.engine.dispatch(wp["id"],"Owner","Ready")
        self.engine.revoke(wp["id"],"Owner","New interface review required")
        self.assertEqual(self.engine.queue(self.project,"DEVICE"),[])
        self.assertEqual(self.engine.overview(self.project)["tasks"][0]["effective_state"],"REVOKED")

    def test_repo_validation(self):
        for bad in ("https://evil.com/foo","localhost/../../hi","owner/.git",
                    "/owner/repo","owner/repo?x=1","owner/repo/extra"):
            with self.assertRaises(RuleError):
                require_repo(bad)

    def test_publish_never_commits_and_rejects_main(self):
        wp,_=self._work()
        self.engine.bind(wp["id"],17,"DEVICE")
        self.engine.dispatch(wp["id"],"Owner","Ready")
        worktree=self.root/"gitwork"
        worktree.mkdir()
        subprocess.run(["git","init","-q","-b","feature/execution",str(worktree)],check=True)
        subprocess.run(["git","-C",str(worktree),"remote","add","origin",
                        "https://github.com/"+REPO+".git"],check=True)
        result=self.engine.publish(wp["id"],worktree)
        self.assertEqual(result["status"],"WRITTEN_NOT_COMMITTED")
        for p in result["files"]:
            self.assertTrue((worktree/p).is_file())
        changed=subprocess.run(["git","-C",str(worktree),"status","--porcelain"],
                               check=True,capture_output=True,text=True).stdout
        self.assertIn("?? docs/",changed)
        subprocess.run(["git","-C",str(worktree),"branch","-m","main"],check=True)
        with self.assertRaisesRegex(RuleError,"main"):
            self.engine.publish(wp["id"],worktree)

    def test_export_complete_project_archive_to_reviewable_git_branch(self):
        design=self._approved_design()
        work,_=self._work(design)
        self.engine.bind(work["id"],17,"DEVICE")
        self.engine.dispatch(work["id"],"Owner","Confirmed")
        tempdir=self.root/"archivework"
        tempdir.mkdir()
        subprocess.run(["git","init","-q","-b","feature/archive",str(tempdir)],check=True)
        subprocess.run(["git","-C",str(tempdir),"remote","add","origin",
                        "git@github.com:"+REPO+".git"],check=True)
        with self.assertRaises(RuleError):
            self.engine.archive_project(self.project,tempdir,"","")
        result=self.engine.archive_project(self.project,tempdir,"Owner",
                    "Reviewed for confidential information")
        self.assertEqual(result["state"],"WRITTEN_NOT_COMMITTED")
        target=tempdir/result["path"]
        archived=json.loads(target.read_text(encoding="utf-8"))
        self.assertTrue(archived["review_required_before_remote_push"])
        self.assertEqual(archived["source_project_id"],self.project)
        self.assertEqual(len(archived["engineering"]["execution"]["tasks"]),1)
        self.assertNotIn("github_snapshot",archived["engineering"]["execution"])
        pending=subprocess.run(["git","-C",str(tempdir),"status","--porcelain"],
                  check=True,text=True,capture_output=True).stdout
        self.assertIn("?? docs/",pending)
        subprocess.run(["git","-C",str(tempdir),"branch","-m","main"],check=True)
        with self.assertRaisesRegex(RuleError,"main"):
            self.engine.archive_project(self.project,tempdir,"Owner","Review")

    def test_git_origin_must_match_real_github_not_spoofed_host(self):
        from studio_execution import _github_origin_matches
        self.assertTrue(_github_origin_matches("https://github.com/"+REPO+".git",REPO))
        self.assertTrue(_github_origin_matches("git@github.com:"+REPO+".git",REPO))
        self.assertFalse(_github_origin_matches("https://evil.com/foo/"+REPO+".git",REPO))
        self.assertFalse(_github_origin_matches("https://github.com/"+REPO+"-copy",REPO))

    def test_reader_normalizes_public_rest_payload(self):
        from studio_execution import normalize_issue,normalize_pr,latest_run_for
        issue=normalize_issue({"number":17,"title":"real issue","state":"open",
                               "labels":[{"name":"P1"}],"assignees":[]})
        self.assertEqual(issue["labels"],["P1"])
        pr=normalize_pr({"number":26,"draft":True,"state":"open",
            "head":{"sha":"f"*40,"ref":"device"},"base":{"ref":"main"}})
        self.assertIsNone(latest_run_for(pr,[{"head_sha":"b"*40,"conclusion":"success"}]))

if __name__=="__main__":
    unittest.main()
