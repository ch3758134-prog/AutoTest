"""Safe sync of local draft management.json through an explicit GitHub file CAS."""
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

HERE=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location("management_sync_tested",HERE/"portable/management_sync.py")
import sys
sys.path.insert(0,str(HERE/"portable"))
sync=importlib.util.module_from_spec(spec)
spec.loader.exec_module(sync)


def git(root,*args):
    p=subprocess.run(["git","-C",str(root),*args],text=True,capture_output=True)
    if p.returncode: raise AssertionError(p.stderr)
    return p.stdout.strip()


class FakeGitHub:
    def __init__(self,head,blob,source):
        self.head=head
        self.blob=blob
        self.remote_source=source
        self.commit="f"*40
        self.writes=[]

    def branch_head(self,parts,branch):
        return self.head

    def file(self,parts,path,branch):
        assert path==".project-control/management.json"
        return self.blob,self.remote_source

    def request(self,method,resource,payload):
        assert method=="PUT"
        self.writes.append((resource,payload))
        import base64
        self.remote_source=base64.b64decode(payload["content"])
        self.blob="e"*40
        return {"commit":{"sha":self.commit}}

    def commit_parents(self,parts,commit):
        return [self.head]


class ManagementSyncTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name)/"target"
        self.root.mkdir()
        git(self.root,"init","-b","main")
        git(self.root,"config","user.email","fixture@example.invalid")
        git(self.root,"config","user.name","Fixture")
        git(self.root,"remote","add","origin","https://github.com/acme/example.git")
        directory=self.root/".project-control"
        directory.mkdir()
        (directory/"project.json").write_text(json.dumps({
            "schema":"project-control-target-project/1",
            "project_id":"example","repo_url":"https://github.com/acme/example.git"
        }))
        self.base={"schema":"project-control-target-management/1",
                   "project_id":"example","status":"DRAFT","revision":1,"events":[],
                   "deliverables":[],"work":[],"milestones":[],"risks":[],
                   "progress":{},"receipts":[]}
        self.file=directory/"management.json"
        self.file.write_text(json.dumps(self.base,indent=2)+"\n")
        git(self.root,"add",".")
        git(self.root,"commit","-m","init management")
        self.sha=git(self.root,"rev-parse","HEAD")
        self.blob=git(self.root,"rev-parse","HEAD:.project-control/management.json")
        self.client=FakeGitHub(self.sha,self.blob,self.file.read_bytes())
        self.change_local()

    def change_local(self):
        new=dict(self.base)
        new["revision"]=2
        new["deliverables"]=[{"id":"D1","title":"Reviewed draft",
                              "success_condition":"Traceability", "state":"PROPOSED"}]
        new["events"]=[{"revision":2,"operation":"create_deliverable",
                        "status":"LOCAL_DRAFT_NOT_REMOTE_PUBLISHED"}]
        self.file.write_text(json.dumps(new,indent=2)+"\n")

    def test_preview_and_publish_is_explicit_and_preserves_gate(self):
        plan=sync.preview(self.root,self.client)
        self.assertEqual(plan["status"],"REVIEW_REQUIRED_BEFORE_REMOTE_WRITE")
        self.assertEqual(plan["expected_remote_blob_sha"],self.blob)
        self.assertEqual(plan["source_head"],self.sha)
        self.assertEqual(self.client.writes,[])
        self.assertEqual(sync.publish(self.client,plan)["status"],"PREVIEW_NOT_PUBLISHED")
        self.assertEqual(self.client.writes,[])
        result=sync.publish(self.client,plan,apply=True)
        self.assertEqual(result["status"],"REMOTE_MANAGEMENT_CANDIDATE_WRITTEN_VERIFIED")
        self.assertEqual(result["revision"],2)
        self.assertEqual(json.loads(self.client.remote_source)["status"],"DRAFT")
        self.assertEqual(len(self.client.writes),1)
        self.assertEqual(git(self.root,"rev-parse","HEAD"),self.sha)

    def test_branch_drift_and_file_conflict_never_write(self):
        plan=sync.preview(self.root,self.client)
        self.client.head="0"*40
        with self.assertRaisesRegex(sync.SyncConflict,"branch changed"):
            sync.publish(self.client,plan,apply=True)
        self.assertEqual(self.client.writes,[])
        self.client.head=self.sha
        self.client.blob="d"*40
        with self.assertRaisesRegex(sync.SyncConflict,"file changed"):
            sync.publish(self.client,plan,apply=True)
        self.assertEqual(self.client.writes,[])

    def test_local_other_modified_files_block(self):
        (self.root/"new-user-file.txt").write_text("keep me")
        with self.assertRaisesRegex(sync.SyncConflict,"other unreviewed local"):
            sync.preview(self.root,self.client)
        self.assertEqual(self.client.writes,[])
        self.assertTrue((self.root/"new-user-file.txt").is_file())

    def test_unverified_approvals_are_not_exported(self):
        changed=json.loads(self.file.read_text())
        changed["work"]=[{"id":"W1","state":"APPROVED"}]
        self.file.write_text(json.dumps(changed))
        with self.assertRaisesRegex(sync.SyncConflict,"self-approve"):
            sync.preview(self.root,self.client)

    def test_remote_revision_or_events_conflict(self):
        remote=dict(self.base)
        remote["revision"]=5
        remote["events"]=[{"revision":5,"operation":"unrelated"}]
        self.client.remote_source=json.dumps(remote).encode()
        with self.assertRaisesRegex(sync.SyncConflict,"remote management revision is newer"):
            sync.preview(self.root,self.client)
        self.client.remote_source=self.file.read_bytes()
        self.client.blob=self.blob
        with self.assertRaisesRegex(sync.SyncConflict,"same revision has conflicting content"):
            sync.preview(self.root,self.client)


if __name__=="__main__":
    unittest.main()
