"""Fake GitHub API: validates exact SHA-CAS and PR file evidence, no network."""
import base64
import hashlib
import importlib.util
import json
from pathlib import Path
import sys
import unittest

HERE=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(HERE/"portable"))
import github_bridge as bridge


class FakeGitHub:
    def __init__(self):
        self.head="a"*40
        self.old_blob="b"*40
        self.new_head="c"*40
        self.writes=[]
        self.content=json.dumps({"schema":"project-control-target-tasks/1",
                                 "project_id":"demo","tasks":[{"id":"IMPL-001","state":"DRAFT"}]}).encode()
        self.pr_head="d"*40
        self.remote_files=["src/feature.py"]

    def branch_head(self,parts,branch):
        return self.head

    def file(self,parts,path,branch):
        return (self.old_blob if not self.writes else "e"*40),self.content

    def commit_parents(self,parts,sha):
        assert sha==self.new_head
        return [self.head]

    def request(self,method,path,data):
        if method=="PUT":
            self.writes.append((path,data))
            assert data["sha"]==self.old_blob
            self.content=base64.b64decode(data["content"])
            return {"commit":{"sha":self.new_head}}
        raise AssertionError("No network operation beyond guarded PUT expected")

    def pr(self,parts,number):
        return {"number":number,"url":"https://github.com/acme/demo/pull/"+str(number),
                "head_repo_full_name":"acme/demo","base":"main",
                "head":"feature/impl","head_sha":self.pr_head,
                "draft":True,"state":"open","merged":False}

    def pr_files(self,parts,number):
        return [{"filename":p,"status":"added"} for p in self.remote_files]


class GitHubBridgeTests(unittest.TestCase):
    def setUp(self):
        self.client=FakeGitHub()
        self.plan={"schema":"project-control-task-publication-plan/1",
                   "project_id":"demo","task_id":"IMPL-001",
                   "repo_url":"https://github.com/acme/demo.git",
                   "source_branch":"main","preflight_branch_head":self.client.head,
                   "expected_tasks_blob_sha":self.client.old_blob,
                   "desired_file":".project-control/tasks.json",
                   "proposed_content":json.dumps({
                       "schema":"project-control-target-tasks/1",
                       "project_id":"demo",
                       "tasks":[{"id":"IMPL-001","state":"READY_FOR_LOCAL_IMPLEMENTATION"}]})}

    def test_preview_never_contacts_github(self):
        result=bridge.publish_plan(self.client,self.plan,
                                   "https://github.com/acme/demo.git")
        self.assertEqual(result["status"],"PREVIEW_NOT_PUBLISHED")
        self.assertEqual(self.client.writes,[])

    def test_apply_checks_branch_blob_and_written_content(self):
        result=bridge.publish_plan(self.client,self.plan,
                                   "https://github.com/acme/demo.git",apply=True)
        self.assertEqual(result["status"],"GITHUB_WRITE_VERIFIED_PENDING_INDEPENDENT_REVIEW")
        self.assertEqual(result["commit_sha"],self.client.new_head)
        self.assertEqual(len(self.client.writes),1)
        self.assertEqual(json.loads(self.client.content)["tasks"][0]["state"],
                         "READY_FOR_LOCAL_IMPLEMENTATION")

    def test_stale_branch_head_fails_without_writes(self):
        self.plan["preflight_branch_head"]="f"*40
        with self.assertRaisesRegex(bridge.RemoteError,"HEAD changed"):
            bridge.publish_plan(self.client,self.plan,"https://github.com/acme/demo.git",apply=True)
        self.assertEqual(self.client.writes,[])

    def test_stale_file_blob_fails_without_writes(self):
        self.plan["expected_tasks_blob_sha"]="f"*40
        with self.assertRaisesRegex(bridge.RemoteError,"tasks.json changed"):
            bridge.publish_plan(self.client,self.plan,"https://github.com/acme/demo.git",apply=True)
        self.assertEqual(self.client.writes,[])

    def test_repo_mismatch_and_invalid_target_fail_without_writes(self):
        for url in ["https://github.com/acme/other.git",
                    "https://github.com.evil.org/acme/demo.git"]:
            with self.subTest(url=url),self.assertRaises(bridge.RemoteError):
                bridge.publish_plan(self.client,self.plan,url,apply=True)
        self.assertEqual(self.client.writes,[])

    def test_untrusted_file_target_and_wrong_state_refused(self):
        self.plan["desired_file"]="README.md"
        with self.assertRaisesRegex(bridge.RemoteError,"only target project"):
            bridge.publish_plan(self.client,self.plan,"https://github.com/acme/demo.git",apply=True)
        self.plan["desired_file"]=".project-control/tasks.json"
        data=json.loads(self.plan["proposed_content"])
        data["tasks"][0]["state"]="ACCEPTED"
        self.plan["proposed_content"]=json.dumps(data)
        with self.assertRaisesRegex(bridge.RemoteError,"candidate state"):
            bridge.publish_plan(self.client,self.plan,"https://github.com/acme/demo.git",apply=True)
        self.assertEqual(self.client.writes,[])

    def test_race_in_commit_parent_never_claims_verified_transaction(self):
        self.client.commit_parents=lambda parts,sha:["f"*40]
        with self.assertRaisesRegex(bridge.RemoteError,"changed during"):
            bridge.publish_plan(self.client,self.plan,"https://github.com/acme/demo.git",apply=True)
        self.assertEqual(len(self.client.writes),1)

    def test_actual_pr_files_match_receipt_before_review(self):
        project={"project_id":"demo","repo_url":"https://github.com/acme/demo.git"}
        task={"id":"IMPL-001","project_id":"demo",
              "source":{"repo_url":project["repo_url"],"branch":"main",
                        "work_branch":"feature/impl"}}
        receipt={"project_id":"demo","task_id":"IMPL-001","repo_url":project["repo_url"],
                 "candidate_head":self.client.pr_head,"source_head":"a"*40,
                 "scope_status":"SCOPE_OK","workspace_clean":True,
                 "changed_paths":["src/feature.py"]}
        result=bridge.verify_remote_pr(self.client,project,task,receipt,9)
        self.assertEqual(result["status"],"PR_FILES_AND_IDENTITY_MATCHED_PENDING_CI_AND_REVIEW")
        self.assertTrue(result["pr_files_checked"])
        self.assertFalse(result["ci_checked"])
        self.client.remote_files=["README.md"]
        with self.assertRaisesRegex(bridge.RemoteError,"does not match"):
            bridge.verify_remote_pr(self.client,project,task,receipt,9)


if __name__=="__main__":
    unittest.main()
