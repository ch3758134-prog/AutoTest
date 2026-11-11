/* Verify that the interactive project UI tells the truth about real management records.
 * This uses a minimal document host to execute pure render helpers without a browser.
 */
'use strict';
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const assert=require('node:assert/strict');
const here=path.resolve(__dirname,'..');
const workspace=JSON.parse(fs.readFileSync(path.join(here,'workspace/autotest.json'),'utf8'));
const source=JSON.parse(fs.readFileSync(path.join(here,'sources/autotest_engineering.json'),'utf8')).engineering;
const process=JSON.parse(fs.readFileSync(path.join(here,'process.json'),'utf8'));
const plan=workspace.planning;
const fakeNode=()=>({onclick:null,addEventListener(){},classList:{toggle(){},add(){},remove(){}},textContent:'',innerHTML:'',dataset:{}});
const node=fakeNode();
const document={querySelector:()=>fakeNode(),querySelectorAll:()=>[],addEventListener(){}};
const ctx={document,window:{},setTimeout:()=>0,clearTimeout(){},
  fetch:()=>new Promise(()=>{}),console,model:{
   objects:source.objects,
   project:source.project,
   process,
   plan: {...plan,project_id:workspace.project_id},
   assessment:{work:plan.work.map(w=>({id:w.id,assessment:'NO_RECORDED_BLOCKER'})),issues:[]},
   control:workspace,
   source:{git_head:'a'.repeat(40),note:'仅为本地内容'}
 }};
vm.createContext(ctx);
vm.runInContext(fs.readFileSync(path.join(here,'app.js'),'utf8'),ctx);
vm.runInContext('data=model',ctx);
const render=(expr)=>vm.runInContext(expr,ctx);

const reviews=render('reviews()');
assert.ok(reviews.includes('at-g1')===false,'HTML should show human titles, not raw ids');
assert.ok(reviews.includes('成熟通用 ECU 测试软件整体工作流调查'));
assert.ok(!reviews.includes('完整自动测试行为要求矩阵（12 项'));
assert.ok(!reviews.includes('跨功能验收观察条件（未执行）'));
assert.ok(reviews.includes('正式批准'));
assert.ok(!reviews.includes('36 个内部工程草稿'));

const stage=process.stages.find(x=>x.id==='G1');
assert.match(render('phaseStatus(model.process.stages.find(x=>x.id==="G1"))'),/未批准/);
workspace.phase_reviews.push({phase_id:'G1',decision:'RETURN',reason:'needs revisions',at:'2026-11-11'});
assert.match(render('phaseStatus(model.process.stages.find(x=>x.id==="G1"))'),/已退回/);
workspace.phase_reviews.pop();

const work=plan.work[0];
let status=render('taskStatus(model.plan.work[0])');
assert.match(status,/计划|规划/);
workspace.progress[work.id]={state:'REPORTED_COMPLETE',detail:'候选完成报告',evidence_refs:['x']};
status=render('taskStatus(model.plan.work[0])');
assert.match(status,/待核实/);
assert.doesNotMatch(status,/已核实工作结果/);
workspace.progress[work.id]={state:'VERIFIED_COMPLETE',detail:'人工核实',evidence_refs:['x']};
status=render('taskStatus(model.plan.work[0])');
assert.match(status,/已核实/);
workspace.invalidation[work.id]={resolved:false};
status=render('taskStatus(model.plan.work[0])');
assert.match(status,/复核/);
delete workspace.invalidation[work.id];

const handoff=render('makeHandoff(model.plan.work[0])');
assert.match(handoff,/计划修订：r/);
assert.match(handoff,/本地工作进展：/);
assert.match(handoff,/GitHub/);
console.log('UI behavior assertions passed: document visibility, stage decisions, actual progress, change invalidation and handoff versions');
