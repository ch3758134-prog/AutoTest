"""Module ownership must follow actual Git, not task status or AI assertions."""
from pathlib import Path
import json
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

HERE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HERE/"portable"))
import module_control as modules
import target_resolver as resolver


def git(root,*args):
    p=subprocess.run(["git","-C",str(root),*args],capture_output=True,text=True,
                     encoding="utf-8",errors="replace",timeout=15)
    if p.returncode: raise AssertionError(p.stderr)
    return p.stdout.strip()


def fixture():
    return {"work":[{"id":"W1","title":"Device integration"}],"modules":[]}


class ModuleSourceTests(unittest.TestCase):
    def setUp(self):
        self.record=fixture()
        self.dev=modules.add_module(self.record,{
            "id":"DEVICE", "title":"Device driver",
            "responsibility":"Physical protocol and CAN adapter",
            "owned_prefixes":["src/device/"],
            "owner_role":"specialist","contracts":["src/device/contracts.py"],
            "depends_on":[]})
        self.ui=modules.add_module(self.record,{
            "id":"UI", "title":"Desktop UI",
            "responsibility":"Visual command surface",
            "owned_prefixes":["src/ui/"], "owner_role":"specialist",
            "contracts":["src/ui/contracts.py"],"depends_on":["DEVICE"]})

    def test_module_registration_prevents_overlap_and_cycles(self):
        with self.assertRaisesRegex(modules.ModuleError,"overlapping"):
            modules.add_module(self.record,{
                "id":"WRONG","title":"Overlaps","responsibility":"Risk",
                "owned_prefixes":["src/"],"contracts":[],"depends_on":[]})
        self.assertEqual(len(self.record["modules"]),2)
        assigned=modules.assign_work(self.record,{"work_id":"W1","module_id":"DEVICE"})
        self.assertEqual(assigned["module_id"],"DEVICE")
        with self.assertRaisesRegex(modules.ModuleError,"already assigned"):
            modules.assign_work(self.record,{"work_id":"W1","module_id":"UI"})

    def test_cross_module_diff_requires_review_even_when_owned(self):
        result=modules.assess_candidate_paths(self.record,[
            "src/device/api.py","src/ui/main.py"],None)
        self.assertEqual(result["affected_module_ids"],["DEVICE","UI"])
        self.assertEqual(result["result"],"CROSS_MODULE_OR_UNOWNED_REVIEW_REQUIRED")
        self.assertEqual(len(result["cross_module_paths"]),2)
        contract=modules.assess_candidate_paths(self.record,[
            "src/device/contracts.py"],"DEVICE")
        self.assertEqual(contract["interface_changes"],["src/device/contracts.py"])
        self.assertEqual(contract["result"],"CROSS_MODULE_OR_UNOWNED_REVIEW_REQUIRED")
        single=modules.assess_candidate_paths(self.record,["src/device/driver.py"],"DEVICE")
        self.assertEqual(single["result"],"MODULE_SCOPE_MATCHES_DECLARED_BOUNDARIES")
        self.assertFalse(single["auto_approved"])

    def test_git_source_snapshot_is_actual_commit_and_not_approval(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp)
            git(root,"init","-b","main")
            git(root,"config","user.name","Fixture")
            git(root,"config","user.email","test@example.invalid")
            (root/"src/device").mkdir(parents=True)
            (root/"src/ui").mkdir(parents=True)
            (root/"src/device/driver.py").write_text("driver=1\n")
            (root/"src/ui/window.py").write_text("ui=1\n")
            git(root,"add",".")
            git(root,"commit","-m","original modules")
            head=git(root,"rev-parse","HEAD")
            (root/"src/device/driver.py").write_text("driver=2\n")
            observed=modules.local_source_snapshot(root,self.record)
            self.assertEqual(observed["git_head"],head)
            seen={x["id"]:x for x in observed["registered_modules"]}
            self.assertEqual(seen["DEVICE"]["working_change_count"],1)
            self.assertEqual(seen["UI"]["working_change_count"],0)
            self.assertEqual(seen["DEVICE"]["last_changed_commit"],head)
            self.assertEqual(seen["DEVICE"]["integration_status"],
                             "UNKNOWN_NOT_REMOTE_VERIFIED")


class CodexModuleScopeTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name)
        self.control=self.root/".project-control"
        self.control.mkdir()
        record=fixture()
        modules.add_module(record,{"id":"DEVICE","title":"Driver",
            "responsibility":"Device protocol", "owned_prefixes":["src/device/"],
            "contracts":["src/device/contracts.py"],"depends_on":[]})
        modules.add_module(record,{"id":"UI","title":"UI",
            "responsibility":"Desktop", "owned_prefixes":["src/ui/"],
            "contracts":[],"depends_on":[]})
        (self.control/"management.json").write_text(json.dumps(record),encoding="utf-8")
        self.decisions=[]
        self.write_decisions()
        self.task={
            "id":"IMPL-1","module_id":"DEVICE","mode":"CONTROLLED",
            "source":{"pinned_sha":"a"*40},
            "allowed_paths":["src/device/**"],
        }

    def write_decisions(self):
        (self.control/"decisions.json").write_text(
            json.dumps({"decisions":self.decisions}),encoding="utf-8")

    def call(self,paths=None):
        with patch.object(resolver,"STATE",self.control):
            return resolver.module_scope(self.task,paths)

    def test_scoped_specialist_cannot_claim_another_module(self):
        self.assertEqual(self.call(["src/device/driver.py"])["outside_module"],[])
        self.task["allowed_paths"]=["src/ui/**"]
        with self.assertRaisesRegex(resolver.TaskError,"exceeds module"):
            self.call()

    def test_module_cross_scope_requires_source_pinned_human_approval(self):
        self.task["allowed_paths"]=["src/ui/**"]
        self.decisions=[{
            "kind":"MODULE_CROSS_SCOPE_APPROVAL","state":"APPROVED",
            "task_id":"IMPL-1","module_id":"DEVICE","source_sha":"a"*40,
            "paths":["src/ui/**","src/ui/main.py"],
            "approved_by":"Owner","approval_reference":"review-001"}]
        self.write_decisions()
        self.assertEqual(self.call(["src/ui/main.py"])["outside_module"],[])
        self.decisions[0]["source_sha"]="b"*40
        self.write_decisions()
        with self.assertRaisesRegex(resolver.TaskError,"exceeds module"):
            self.call()

    def test_public_contract_change_blocks_without_specific_review(self):
        scope=self.call(["src/device/contracts.py"])
        self.assertEqual(scope["interface_review_required"],["src/device/contracts.py"])
        self.decisions=[{
            "kind":"MODULE_INTERFACE_CHANGE_APPROVAL","state":"APPROVED",
            "task_id":"IMPL-1","module_id":"DEVICE","source_sha":"a"*40,
            "paths":["src/device/contracts.py"],"approved_by":"Owner",
            "approval_reference":"interface-review-001"}]
        self.write_decisions()
        self.assertEqual(self.call(["src/device/contracts.py"])["interface_review_required"],[])

    def test_missing_module_identity_blocks_controlled_work(self):
        self.task.pop("module_id")
        with self.assertRaisesRegex(resolver.TaskError,"must name"):
            self.call()


if __name__=="__main__":
    unittest.main()
