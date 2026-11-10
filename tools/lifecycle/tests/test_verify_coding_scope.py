"""Practical Git scope gate tests with real local commits, no hardware mocks."""
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from verify_coding_scope import ContractViolation, verify, validate_scope


class CodingScopeTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.repo=Path(self.tmp.name)/"repo"
        self.repo.mkdir()
        self.git("init","-q","-b","coding/test")
        self.write("src/service.c","base")
        self.git("add",".")
        self.git("-c","user.name=Test","-c","user.email=test@example.org","commit","-qm","baseline")
        self.base=self.git("rev-parse","HEAD").strip()

    def git(self,*args):
        return subprocess.run(["git","-C",str(self.repo),*args],capture_output=True,
                              text=True,check=True).stdout

    def write(self,rel,value):
        path=self.repo/rel
        path.parent.mkdir(parents=True,exist_ok=True)
        path.write_text(value,encoding="utf-8")

    def commit(self,changes):
        for rel,data in changes.items():
            self.write(rel,data)
        self.git("add",".")
        self.git("-c","user.name=Test","-c","user.email=test@example.org",
                 "commit","-qm","candidate")
        return self.git("rev-parse","HEAD").strip()

    def packet(self,scope=None,base=None):
        source=base or self.base
        return {"schema":"engineering-coding-task/1","agent_role":"DEVICE",
                "source_sha":source,
                "design_manifest":{"execution_role":"CODER","source_sha":source,
                                    "file_scope":scope or ["src/"]}}

    def test_approved_source_change_passes_exact_head(self):
        head=self.commit({"src/service.c":"updated"})
        result=verify(self.repo,self.packet(),head)
        self.assertEqual(result["result"],"SCOPE_PASS")
        self.assertEqual(result["changed_files"],["src/service.c"])

    def test_outside_scope_is_blocked(self):
        head=self.commit({"src/service.c":"updated","tests/anything.py":"changed"})
        with self.assertRaisesRegex(ContractViolation,"outside approved"):
            verify(self.repo,self.packet(),head)

    def test_policy_or_workflow_tamper_is_blocked(self):
        head=self.commit({".github/workflows/ci.yml":"changed",
                          "src/service.c":"updated"})
        with self.assertRaisesRegex(ContractViolation,"protected governance"):
            verify(self.repo,self.packet(["src/",".github/"]),head)

    def test_exact_sha_required_and_head_mismatch_is_blocked(self):
        head=self.commit({"src/service.c":"updated"})
        with self.assertRaisesRegex(ContractViolation,"HEAD differs"):
            verify(self.repo,self.packet(),self.base)
        with self.assertRaisesRegex(ContractViolation,"Invalid pinned"):
            verify(self.repo,self.packet(base="short"),head)

    def test_scope_traversal_and_wildcard_rejected(self):
        for bad in (["."],["src/../"],["src/*"],["/src"],[""],["tools/lifecycle/"]):
            with self.subTest(scope=bad),self.assertRaises(ContractViolation):
                validate_scope(bad)

    def test_one_file_not_parent_directory(self):
        head=self.commit({"src/new.c":"updated"})
        with self.assertRaisesRegex(ContractViolation,"outside approved"):
            verify(self.repo,self.packet(["src/service.c"]),head)

    def test_coding_only_design_role_required(self):
        head=self.commit({"src/service.c":"updated"})
        manifest=self.packet()
        manifest["design_manifest"]["execution_role"]="RESEARCH"
        with self.assertRaisesRegex(ContractViolation,"coding-only"):
            verify(self.repo,manifest,head)


if __name__=="__main__":
    unittest.main()
