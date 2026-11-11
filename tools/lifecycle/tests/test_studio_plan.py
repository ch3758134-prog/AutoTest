"""Plan queue cannot mistake unapproved dependencies or dispatched code for completion."""
from pathlib import Path
import shutil
import subprocess
import unittest

HERE=Path(__file__).resolve().parents[1]


class PlanUiTests(unittest.TestCase):
    def test_queue_classifies_dependencies_and_staleness(self):
        if not shutil.which("node"):
            self.skipTest("Node.js not installed")
        script=r"""
const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const ctx={};vm.createContext(ctx);
vm.runInContext(fs.readFileSync(process.argv[1],'utf8'),ctx);
const obj=(id,kind)=>({id,title:id,kind,revision:1,state:'DRAFT',meta:{}});
const docs=[
 obj('design','design'),obj('blocked','work_package'),
 obj('ready','work_package'),obj('stale','work_package'),
 obj('milestone','milestone')
];
docs[4].meta.due='2026-12-01';
const result=ctx.engineeringPlanModel({
 engineering_objects:docs,
 engineering_relations:[
  {source_id:'design',target_id:'blocked',kind:'depends',status:'CANDIDATE'},
  {source_id:'design',target_id:'ready',kind:'depends',status:'APPROVED'},
  {source_id:'design',target_id:'stale',kind:'depends',status:'APPROVED'}
 ],
 implementation_readiness:{
  blocked:{ready:true,reasons:[]},
  ready:{ready:true,reasons:[]},
  stale:{ready:false,reasons:['Changed revision']}
 },
 execution_tasks:[
  {object_id:'stale',effective_state:'BLOCKED_BY_CHANGED_DESIGN'}
 ]});
const byId=Object.fromEntries(result.packages.map(x=>[x.object.id,x]));
assert.equal(byId.blocked.action,'REVIEW_DEPENDENCIES');
assert.equal(byId.ready.action,'BIND_GITHUB');
assert.equal(byId.stale.action,'COMPLETE_DESIGN'); // evidence of not-ready is authoritative
assert.equal(result.milestones[0].due,'2026-12-01');
assert.equal(result.packages[0].object.id,'blocked');
assert.equal(ctx.engineeringPlanModel({engineering_objects:[],engineering_relations:[]}).packages.length,0);
"""
        subprocess.run(["node","-e",script,str(HERE/"studio_plan_ui.js")],
                       check=True,capture_output=True,text=True)

    def test_plan_wiring(self):
        server=(HERE/"studio_server.py").read_text(encoding="utf-8")
        html=(HERE/"studio.html").read_text(encoding="utf-8")
        management=(HERE/"studio_management_ui.js").read_text(encoding="utf-8")
        self.assertIn('if path == "/plan.js":',server)
        self.assertIn('<script src="/plan.js"></script>',html)
        self.assertIn('renderEngineeringPlan(result,mount);',management)


if __name__=="__main__":
    unittest.main()
