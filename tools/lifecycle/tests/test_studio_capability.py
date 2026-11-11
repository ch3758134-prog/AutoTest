"""Contract tests for the V0.7 capability trace (no browser required)."""
import pathlib
import shutil
import subprocess
import unittest


HERE = pathlib.Path(__file__).resolve().parents[1]


class CapabilityTraceTests(unittest.TestCase):
    def test_model_uses_only_approved_links_and_does_not_cross_capabilities(self):
        if not shutil.which("node"):
            self.skipTest("Node.js unavailable (browser JavaScript model test)")
        script = r"""
const fs=require('node:fs'), vm=require('node:vm'), assert=require('node:assert/strict');
const context={};vm.createContext(context);
vm.runInContext(fs.readFileSync(process.argv[1],'utf8'),context);
const obj=(id,kind,state='DRAFT')=>({id,title:id,kind,state,revision:1});
const objects=[
 obj('cap-a','capability'),obj('cap-b','capability'),
 obj('r1','requirement','APPROVED'),obj('d1','design','APPROVED'),
 obj('test1','test','DRAFT'),obj('r2','requirement','APPROVED'),
 obj('r3','requirement','DRAFT')
];
const edge=(source_id,target_id,status='APPROVED',kind='defines')=>({source_id,target_id,status,kind});
const model=context.capabilityTraceModel({objects,relations:[
 edge('cap-a','r1'),edge('r1','d1'),edge('d1','test1'),
 edge('cap-b','r1'),edge('cap-b','r2'),
 edge('cap-a','r3','CANDIDATE')
]});
assert.equal(model.length,2);
const a=model.find(x=>x.cap.id==='cap-a');
assert.deepEqual(Array.from(a.requirements,x=>x.id),['r1']);
assert.deepEqual(Array.from(a.designs,x=>x.id),['d1']);
assert.deepEqual(Array.from(a.tests,x=>x.id),['test1']);
assert.equal(a.pending,1);
assert.equal(a.tests[0].state,'DRAFT'); // presence is NOT verification
const b=model.find(x=>x.cap.id==='cap-b');
assert.deepEqual(Array.from(b.requirements,x=>x.id),['r1','r2']);
assert.equal(context.capabilityTraceModel({objects:[],relations:[]}).length,0);
const gaps=context.capabilityGapModel(a,{objects,relations:[
 edge('cap-a','r1'),edge('r1','d1'),edge('d1','test1'),
 edge('cap-a','r3','CANDIDATE')
]});
assert.ok(gaps.some(x=>x.code==='RELATION_REVIEW'));
assert.ok(gaps.some(x=>x.code==='VERIFICATION_UNLINKED')===false);
assert.ok(gaps.some(x=>x.code==='DESIGN_REVIEW')===false);
const sparse=context.capabilityTraceModel({objects:[obj('lonely','capability')],relations:[]})[0];
const sparseGaps=context.capabilityGapModel(sparse,{objects:[obj('lonely','capability')],relations:[]});
assert.ok(sparseGaps.some(x=>x.code==='TRACE_MISSING'));
assert.ok(sparseGaps.some(x=>x.code==='REQUIREMENT_UNLINKED'));
assert.ok(sparseGaps.some(x=>x.code==='VERIFICATION_UNLINKED'));
const affectedObjects=[obj('risk-cap','capability'),obj('req','requirement','APPROVED'),obj('open','change','OPEN')];
const affectedLinks=[edge('risk-cap','req'),edge('open','req','APPROVED','impacts')];
const affected=context.capabilityTraceModel({objects:affectedObjects,relations:affectedLinks})[0];
const affectedGaps=context.capabilityGapModel(affected,{objects:affectedObjects,relations:affectedLinks});
assert.ok(affectedGaps.some(x=>x.code==='CHANGE_OPEN'));
assert.equal(affected.linked.some(o=>o.kind==='change'),false);
const unrelated=[...affectedLinks,edge('open','risk-cap','CANDIDATE','impacts')];
assert.equal(context.capabilityTraceModel({objects:affectedObjects,relations:unrelated})[0].requirements.length,1);
"""
        subprocess.run(["node", "-e", script, str(HERE / "studio_capability_ui.js")],
                       check=True, capture_output=True, text=True)

    def test_server_and_dashboard_wire_up_module(self):
        server=(HERE / "studio_server.py").read_text(encoding="utf-8")
        html=(HERE / "studio.html").read_text(encoding="utf-8")
        self.assertIn('if path == "/capability.js":',server)
        self.assertIn('<script src="/capability.js"></script>',html)
        self.assertIn('renderCapabilityTrace();',html)


if __name__ == "__main__":
    unittest.main()
