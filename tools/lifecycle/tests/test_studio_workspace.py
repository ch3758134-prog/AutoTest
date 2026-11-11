"""Read-first workspace behavior: phase results are never confused with Gate approval."""
import json
from pathlib import Path
import shutil
import subprocess
import unittest


HERE = Path(__file__).resolve().parents[1]


class WorkspaceStageTests(unittest.TestCase):
    def test_g1_research_complete_does_not_approve_g1(self):
        if not shutil.which("node"):
            self.skipTest("Node.js required for browser-view model")
        js = r"""
const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const entries=[
 {id:'p-p0-definition',title:'P0｜产品定义',kind:'context',state:'APPROVED',meta:{}},
 {id:'p-g0-inventory',title:'G0｜台架现状',kind:'system',state:'DRAFT',meta:{}},
 {id:'p-g1-study',title:'G1｜整体需求研究',kind:'reference',state:'DRAFT',
  meta:{phase:'G1',research_complete:true}},
 {id:'p-g1-test',title:'G1｜真实闭环验收',kind:'test',state:'DRAFT',
  meta:{phase:'G1',test_state:'NOT_RUN'}}
];
const sandbox={state:{data:{objects:entries}}};
vm.createContext(sandbox);
vm.runInContext(fs.readFileSync(process.argv[1],'utf8'),sandbox);
const phases=vm.runInContext('wsStages().map(p=>({id:p.id,approved:p.approved.length,'+
 'research:p.researched.length,report:p.top.id}))',sandbox);
assert.equal(JSON.stringify(phases),JSON.stringify([
 {id:'P0',approved:1,research:0,report:'p-p0-definition'},
 {id:'G0',approved:0,research:0,report:'p-g0-inventory'},
 {id:'G1',approved:0,research:1,report:'p-g1-study'}]));
assert.equal(vm.runInContext('wsPhaseOf({id:"a-g2-arch",title:"架构讨论",meta:{}})',sandbox),'G2');
"""
        result = subprocess.run(
            ["node", "-e", js, str(HERE / "studio_workspace_ui.js")],
            capture_output=True, text=True, check=False,
        )
        self.assertEqual(result.returncode, 0, result.stderr)


if __name__ == "__main__":
    unittest.main()
