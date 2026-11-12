"use strict";
/* Pure rendering smoke tests with two project fixtures; no browser or server. */
const fs=require("fs"),path=require("path"),vm=require("vm"),assert=require("node:assert/strict");
const script=fs.readFileSync(path.join(__dirname,"../portable/web/app.js"),"utf8");
const node=()=>({innerHTML:"",textContent:"",style:{},classList:{add(){},remove(){},toggle(){}},addEventListener(){},showModal(){},close(){}});
const doc={querySelector:()=>node(),querySelectorAll:()=>[],addEventListener(){}};
const context={document:doc,window:{},setTimeout(){},fetch:()=>new Promise(()=>{}),prompt:()=>"",FormData:class{},console,JSON,encodeURIComponent};
vm.createContext(context);
vm.runInContext(script,context);
const one={project:{project_id:"research-1",display_name:"Generic Battery Research",repo_url:"https://github.com/example/battery",initialization_state:"CANDIDATE_NOT_APPROVED",intake_mode:"ADOPT",formal_product_identity:"UNDECIDED",scope_decision:"NOT_RECORDED",phase_gate:"NOT_VERIFIED"},
management:{revision:1,status:"DRAFT",deliverables:[{id:"D-1",title:"First evidence",success_condition:"Reviewed report",state:"PROPOSED"}],work:[{id:"W-1",title:"Measure signals",kind:"RESEARCH",deliverable_id:"D-1",expected_result:"Evidence",state:"PROPOSED"}],risks:[],milestones:[],progress:{}},
assets:{tracked_file_count:2,entries:[{path:"README.md",confidence:"OBSERVED",approval:"UNKNOWN"}],observation_source:"LOCAL_GIT_INDEX",remote_history_pr_ci:"NOT_CHECKED"},decisions:{decisions:[]},tasks:{tasks:[]},
git:{head:"a".repeat(40),branch:"main",dirty:false,source:"LOCAL_WORKTREE_NOT_REMOTE_VERIFIED"}};
const two=JSON.parse(JSON.stringify(one));two.project.project_id="research-2";two.project.display_name="Laboratory Metrology";two.management.deliverables=[];two.management.work=[];
context.fixture=one;vm.runInContext("model=fixture",context);
assert.match(vm.runInContext("overview()",context),/Generic Battery Research/);
assert.match(vm.runInContext("deliverables()",context),/First evidence/);
assert.match(vm.runInContext("work()",context),/Measure signals/);
assert.match(vm.runInContext("handoff()",context),/PROJECT_START_HERE.md/);
assert.match(vm.runInContext("requirements()",context),/需求与依据/);
assert.match(vm.runInContext("plans()",context),/计划与依赖/);
assert.match(vm.runInContext("changes()",context),/变更与阶段审查/);
assert.match(vm.runInContext("workspaceView()",context),/本地工作区/);
assert.equal(vm.runInContext("typeof definitions.create_plan",context),"object");
assert.equal(vm.runInContext("typeof definitions.assess_change",context),"object");

assert.doesNotMatch(vm.runInContext("handoff()",context),/唯一已授权任务已存在/);
context.fixture=two;vm.runInContext("model=fixture",context);
assert.match(vm.runInContext("overview()",context),/Laboratory Metrology/);
assert.doesNotMatch(vm.runInContext("overview()",context),/Generic Battery Research/);
assert.doesNotMatch(vm.runInContext("deliverables()",context),/First evidence/);
assert.match(vm.runInContext("work()",context),/尚无任务/);
assert.equal(vm.runInContext("e('<script>')",context),"&lt;script&gt;");
console.log("Portable UI behavior: project isolation, empty state, handoff, escaping PASS");
