"""Git engineering baseline compare uses committed bytes, never a mutable file."""
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from studio_store import Store,RuleError
from studio_execution import Execution
from studio_baseline import GitBaseline


REPO="ch3758134-prog/AutoTest"


class GitBaselineTests(unittest.TestCase):
    def setUp(self):
        temp=tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root=Path(temp.name)
        self.repo=self.root/"worktree"
        self.repo.mkdir()
        self.git("init","-q","-b","feature/audit")
        self.git("remote","add","origin","https://github.com/"+REPO+".git")
        self.store=Store(self.root/"project.sqlite3")
        self.execution=Execution(self.store)
        self.project=self.store.create_project({
            "name":"Embedded Platform","summary":"software and hardware"})["project"]["id"]
        self.execution.configure(self.project,REPO)
        self.a=self.store.create_object({
            "project_id":self.project,"kind":"requirement",
            "title":"Real I/O path","body":"No simulated output",
            "meta":{}})["item"]
        self.baseline=GitBaseline(self.store,self.repo,self.execution)

    def git(self,*args):
        return subprocess.run(["git","-C",str(self.repo),*args],
                              check=True,text=True,capture_output=True).stdout.strip()

    def commit_archive(self):
        self.execution.archive_project(self.project,self.repo,"Human","Review confidential data")
        self.git("add",".")
        self.git("-c","user.name=Human","-c","user.email=human@example.test",
                 "commit","-qm","Reviewed engineering candidate")
        return self.git("rev-parse","HEAD")

    def test_exact_git_commit_and_local_drift(self):
        sha=self.commit_archive()
        clean=self.baseline.preview(self.project,sha)
        self.assertEqual(clean["state"],"IN_SYNC")
        self.assertEqual(clean["git_commit"],sha)
        self.assertFalse(clean["on_local_main_history"])
        self.assertEqual(clean["differences"],[])
        self.store.update_object(self.a["id"],{
            "expected_revision":self.a["revision"],
            "body":"Changed physical protocol after laboratory evidence"})
        changed=self.baseline.preview(self.project,sha)
        self.assertEqual(changed["state"],"REVIEW_REQUIRED")
        self.assertEqual(changed["differences"][0]["comparison"],"LOCAL_NEWER")
        self.assertEqual(changed["differences"][0]["object_id"],self.a["id"])
        # Comparison is read-only: neither history nor design approval changes.
        self.assertEqual(self.store.object(self.a["id"])["item"]["state"],"DRAFT")

    def test_uncommitted_worktree_modification_does_not_change_preview(self):
        sha=self.commit_archive()
        path=self.repo/"docs"/"engineering-control"/"projects"/self.project/"snapshot.json"
        path.write_text("{corrupted on-disk draft",encoding="utf-8")
        still=self.baseline.preview(self.project,sha)
        self.assertEqual(still["state"],"IN_SYNC")
        self.assertEqual(still["git_commit"],sha)

    def test_trusted_main_reference_is_only_a_provenance_hint(self):
        sha=self.commit_archive()
        self.git("branch","main",sha)
        result=self.baseline.preview(self.project,sha)
        self.assertTrue(result["on_local_main_history"])
        self.assertIn("not cryptographic proof",result["note"])

    def test_ref_and_repo_identity_are_checked(self):
        sha=self.commit_archive()
        with self.assertRaisesRegex(RuleError,"Baseline must"):
            self.baseline.preview(self.project,"--wrong-option")
        self.git("remote","set-url","origin","https://evil.example/"+REPO+".git")
        with self.assertRaisesRegex(RuleError,"origin"):
            self.baseline.preview(self.project,sha)

    def test_archive_requires_known_project(self):
        with self.assertRaises(RuleError):
            self.baseline.preview("missing","HEAD")


if __name__=="__main__":
    unittest.main()
