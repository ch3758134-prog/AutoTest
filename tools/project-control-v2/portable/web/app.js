"use strict";
const $=s=>document.querySelector(s);
const e=v=>String(v??"").replace(/[&<>"']/g,c=>({"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&#39;"}[c]));
const txt=v=>e(String(v??""));
let projects=[],model=null,selected=null,page="overview",ownerToken="",remoteSnapshot=null,workspaceSnapshot=null,moduleRemoteSnapshot=null,intakeReviewToken=null;
const labels={process:"工程过程与 AI",overview:"项目总览",deliverables:"目标与交付",work:"研发工作",modules:"模块与实际代码",requirements:"需求与依据",plans:"计划与依赖",changes:"变更与阶段审查",risks:"风险与里程碑",evidence:"资产与正式依据",handoff:"AI 与 Codex 交接",workspace:"本地工作区"};
const escapeJSON=s=>JSON.stringify(s).replace(/</g,"\\u003c");
const pill=(value,tone="")=>'<span class="pill '+tone+'">'+e(value)+'</span>';
const empty=message=>'<div class="empty">'+e(message)+'</div>';
const line=(title,desc,right="")=>'<div class="line"><div><strong>'+e(title)+'</strong><p>'+e(desc)+'</p></div><div>'+right+'</div></div>';
const panel=(title,body)=>'<section class="panel"><h2>'+e(title)+'</h2>'+body+'</section>';
const head=(eyebrow,title,desc,button="")=>'<div class="heading"><div><div class="eyebrow">'+e(eyebrow)+'</div><h1>'+e(title)+'</h1><p>'+e(desc)+'</p></div>'+(button?'<div class="actions">'+button+'</div>':"")+'</div>';
const action=(text,id)=>'<button class="primary" data-action="'+e(id)+'">'+e(text)+'</button>';
const project=()=>model.project, management=()=>model.management;
function notify(message,error=false){const n=$("#toast");n.textContent=message;n.style.background=error?"#854747":"#28475f";n.classList.add("visible");setTimeout(()=>n.classList.remove("visible"),3400);}
async function read(url){const r=await fetch(url,{cache:"no-store"});const data=await r.json();if(!r.ok)throw Error(data.error||"读取失败");return data;}
function emptyProjectHome(){
 model=null; selected=null; page="overview";
 $("#project-select").innerHTML='<option value="">尚未登记项目</option>';
 $("#project-select").disabled=true;
 $("#project-source").textContent="管理系统已启动 · 尚未连接任何目标项目";
 $("#sync-pill").textContent="未连接项目";
 $("#crumb-current").textContent="项目接入";
 document.querySelectorAll("nav button").forEach(x=>{x.classList.remove("active");x.disabled=true;});
 $("#main").innerHTML=
 '<div class="heading"><div><div class="eyebrow">PROJECT CONTROL</div><h1>项目管理工作台</h1><p>软件已经启动。你还没有登记项目，不需要创建测试工程，也不应将软件自身当成管理对象。</p></div></div>'+
 '<div class="hero"><div><div class="eyebrow">开始使用</div><h2>先接入一个真实项目</h2><p>可以接管已有源码项目，也可以从真实 GitHub 仓库开始新项目。目标工程与项目管理工具是两套独立文件。</p><p style="margin-top:16px"><button class="primary" id="empty-initialize-project">新建 / 接管项目</button> <button class="secondary" id="empty-register-target">登记已初始化项目</button></p></div><div class="mini"><strong>当前状态</strong><p>服务已就绪</p><br><strong>已登记项目</strong><p>0</p></div></div>'+
 '<section class="section pair">'+
 '<div class="panel"><h2>已有项目</h2><p>先对目标仓库进行资产审查，再初始化自己的 .project-control 记录。完成后在这里登记本地目标项目根目录。</p><p class="muted">不会自动写入 AutoTest 或任何未授权目录。</p></div>'+
 '<div class="panel"><h2>新项目</h2><p>从实际目标仓库建立工程标识、需求和管理基线。初始化必须明确目标目录与 GitHub origin，不能凭管理工具的安装位置猜测。</p><p class="muted">新建 / 接管的命令见 portable/WEB_WORKSPACE.md。</p></div></section>';
}
async function loadProjects(){
 const data=await read("/api/projects");
 projects=data.projects||[];
 if(!projects.length){emptyProjectHome();return;}
 $("#project-select").disabled=false;
 document.querySelectorAll("nav button").forEach(x=>x.disabled=false);
 const id=selected&&projects.some(p=>p.project_id===selected)?selected:projects[0].project_id;
 $("#project-select").innerHTML=projects.map(p=>'<option value="'+e(p.project_id)+'">'+e(p.display_name||p.project_id)+'</option>').join("");
 selected=id;$("#project-select").value=id;await loadModel();
}
function showProjectInitialization(){
 intakeReviewToken=null;
 $("#initialize-form").reset();
 $("#initialize-apply").disabled=true;
 $("#initialize-result").textContent="先检查目标目录与 GitHub origin，预览真实源码基线和将新增的文件；没有明确确认不会写入。";
 $("#initialize-dialog").showModal();
}
async function previewProjectInitialization(event){
 event.preventDefault();
 intakeReviewToken=null;
 $("#initialize-apply").disabled=true;
 if(!await getOwnerToken())return;
 const payload={
   path:$("#initialize-path").value.trim(),
   repo_url:$("#initialize-repo").value.trim(),
   mode:$("#initialize-mode").value,
   project_id:$("#initialize-id").value.trim()||null,
   project_name:$("#initialize-name").value.trim()||null
 };
 $("#initialize-result").textContent="正在核实目标仓库来源与现有文件…";
 try{
   const response=await fetch("/api/project-preview",{
     method:"POST",headers:{"Content-Type":"application/json","X-Owner-Token":ownerToken},
     body:JSON.stringify(payload)
   });
   const data=await response.json();
   if(!response.ok){
     if(response.status===403)ownerToken="";
     throw Error(data.error||"项目预览失败");
   }
   intakeReviewToken=data.review_token;
   $("#initialize-result").textContent=
     "目标仓库："+data.repo_url+"\n"+
     "接入方式："+data.mode+"\n"+
     "项目 ID："+data.project_id+"\n"+
     "源分支："+(data.source_branch||"无提交")+"\n"+
     "源 HEAD："+(data.source_head||"无提交")+"\n"+
     "原有 Git 跟踪文件："+data.tracked_files_count+"\n"+
     "确认后仅新增以下本地文件（不提交、不推送）：\n"+
     data.would_create.map(x=>"  • "+x).join("\n")+
     "\n所有项目管理状态均为候选，未获得正式批准。";
   $("#initialize-apply").disabled=false;
 }catch(error){
   $("#initialize-result").textContent="预检未通过："+error.message+
     "\n原工程不会被修改。请核对是否为正确仓库、工作目录是否干净，以及 new/adopt 模式。";
 }
}
async function applyProjectInitialization(){
 if(!intakeReviewToken)return;
 if(!await getOwnerToken())return;
 if(!confirm("确认只在指定的真实项目根目录创建候选项目管理文件？不会提交或推送 GitHub，也不会授予正式批准。"))return;
 const token=intakeReviewToken;
 intakeReviewToken=null;
 $("#initialize-apply").disabled=true;
 $("#initialize-result").textContent="正在按刚才的预览安全创建本地管理记录…";
 try{
   const response=await fetch("/api/project-apply",{
     method:"POST",headers:{"Content-Type":"application/json","X-Owner-Token":ownerToken},
     body:JSON.stringify({review_token:token,confirm:true})
   });
   const data=await response.json();
   if(!response.ok){
     if(response.status===403)ownerToken="";
     throw Error(data.error||"初始化未完成");
   }
   $("#initialize-dialog").close();
   selected=data.project_id;
   await loadProjects();
   notify("已建立本地候选管理项目；没有提交或推送任何工程文件。");
 }catch(error){
   $("#initialize-result").textContent="初始化未完成："+error.message+
     "\n如果已生成部分文件，请先人工核对，不要删除或重复强制初始化。";
 }
}
function showTargetRegistration(){
 $("#register-path").value="";
 $("#register-result").textContent="已有项目必须先按初始化协议生成 .project-control；此操作不会写入目标工程。";
 $("#register-dialog").showModal();
}
async function registerTarget(event){
 event.preventDefault();
 const folder=$("#register-path").value.trim();
 if(!folder)return;
 if(!await getOwnerToken())return;
 $("#register-result").textContent="正在核对真实 Git 仓库和项目管理记录…";
 try{
   const response=await fetch("/api/register-target",{
     method:"POST",
     headers:{"Content-Type":"application/json","X-Owner-Token":ownerToken},
     body:JSON.stringify({path:folder})
   });
   const data=await response.json();
   if(!response.ok){
     if(response.status===403)ownerToken="";
     throw Error(data.error||"无法登记该目录");
   }
   selected=data.project_id;
   $("#register-dialog").close();
   await loadProjects();
   notify("已登记项目 "+(data.display_name||data.project_id)+"；未修改任何工程文件。");
 }catch(error){
   $("#register-result").textContent=error.message+
   "。若项目尚未初始化，请先在真实项目 Git 根目录运行 portable/project_init.py 的 inspect、preview、apply；不要创建临时假项目。";
 }
}
async function loadModel(){remoteSnapshot=null;workspaceSnapshot=null;moduleRemoteSnapshot=null;model=await read("/api/model?project_id="+encodeURIComponent(selected));$("#project-source").textContent=model.project.repo_url;$("#sync-pill").textContent=model.git.dirty?"本地有未提交变更":"本地 HEAD · 远端未核对";render();}
const count=(collection)=>management()[collection]?.length||0;
const mstatus=()=>management().status;
function ownerBriefingPanel(){
 const c=management().process_control||{},acts=c.activities||[],reports=c.reports||[],focus=c.current_focus||null;
 const activity=focus?acts.find(a=>a.id===focus.activity_id):null;
 const proc=activity?(model.process_catalog?.processes||[]).find(p=>p.id===activity.process_id):null;
 const candidates=reports.filter(r=>(!r.audience||r.audience==="OWNER")&&r.freshness!=="STALE_AFTER_ACTIVITY_REVISION"&&(!focus||r.activity_id===focus.activity_id));
 const report=candidates.length?candidates[candidates.length-1]:null;
 let html='<section class="section"><div class="heading"><div><div class="eyebrow">项目负责人视图 · 候选信息</div><h2>当前最值得关注的事</h2></div></div>';
 if(model.process_policy_current===false)html+='<div class="warning">本项目旧过程版本仅供只读核对；新过程写入需要单独迁移审查，不能沿用旧批准。</div>';
 html+='<h3>'+e(report?.focus||activity?.objective||"尚未指定当前项目关注的工作")+'</h3>';
 html+='<p><strong>为什么现在做：</strong>'+e(report?.why_this_work||proc?.owner_guidance?.why||"当前工作的必要性尚未说明")+'</p>';
 html+='<p><strong>重要判断：</strong>'+e(report?.judgement||"暂无本轮可向负责人报告的实质判断；不能推断工作已完成")+'</p>';
 html+='<p><strong>下一步：</strong>'+e(report?.next_action||"管理 AI 先核实实际目标、资料和当前需要开展的工作")+'</p>';
 html+='<p><strong>需要你处理：</strong>'+e(!report?"待决事项尚不明确":report.owner_action==="NONE"?"此报告暂无需你立即决定的事项":report.owner_request||"旧报告没有说明需要决定什么")+'</p>';
 if(proc?.owner_guidance?.expected_result)html+='<p><strong>预期成果：</strong>'+e(proc.owner_guidance.expected_result)+'</p>';
 html+='<details><summary>展开来源、版本与未完成记录</summary><p>当前焦点：'+e(focus?.activity_id||"未登记")+'；报告：'+e(report?.id||"无")+'</p>';
 if(report?.evidence_refs?.length)html+='<p>证据：'+report.evidence_refs.map(e).join("；")+'</p>';
 if(report?.unfinished?.length)html+='<p>未完成：'+report.unfinished.map(e).join("；")+'</p>';
 return html+'</details></section>';
}
function overview(){
 const p=project(),m=management(),a=model.assets,t=model.tasks,git=model.git;
 let html=head("PROJECT OVERVIEW",p.display_name||p.project_id,"项目现状首先按实际工作与判断汇报；内部账本可展开查看。");
 html+=ownerBriefingPanel();
 html+='<details class="section"><summary>展开：版本、资产与管理账本</summary>';
 html+='<div class="hero"><div><div class="eyebrow">当前管理事实</div><h2>'+e(p.initialization_state)+'</h2><p>项目来源 '+e(p.intake_mode||"UNKNOWN")+' · 项目目标和范围的正式状态由工程依据和审批记录决定；初始化不表示 Gate 通过。</p></div><div class="mini"><strong>目标仓库</strong><p class="source-row">'+e(p.repo_url)+'</p><br><strong>本地 HEAD</strong><p class="mono">'+e(git.head)+'</p></div></div>';
 html+='<section class="section"><div class="grid">'+[['交付成果',count("deliverables"),"均按候选与批准分别管理"],['研发任务',count("work"),"不自动获得编码权限"],['源码模块',count("modules"),"与源码实际提交分别核对"],['风险与问题',count("risks"),"以真实证据核对"],['已记录决策',model.decisions.decisions?.length||0,"不代表所有决策已核实"],['登记 Codex 任务',t.tasks?.length||0,"READY 仍需真实授权"],['Git 跟踪资产',a.tracked_file_count??0,"仅为索引快照"]].map(x=>'<div class="panel stat"><label>'+e(x[0])+'</label><strong>'+e(x[1])+'</strong><p>'+e(x[2])+'</p></div>').join("")+'</div></section>';
 html+='<section class="section pair">'+panel("近期工作",m.work.length?'<div class="list">'+m.work.slice(0,6).map(w=>line(w.title,w.kind+" · "+w.expected_result,pill(m.progress[w.id]?.state||w.state))).join("")+'</div>':empty("尚未建立研发工作。先确认目标与交付，再建立任务。"))+panel("正式性与来源",'<div class="details"><span>项目标识</span><strong>'+e(p.project_id)+'</strong></div><div class="details"><span>产品定义</span>'+pill(p.formal_product_identity||"UNKNOWN")+'</div><div class="details"><span>范围决定</span>'+pill(p.scope_decision||"UNKNOWN")+'</div><div class="details"><span>Gate</span>'+pill(p.phase_gate||"UNKNOWN")+'</div><div class="details"><span>本地分支</span><span class="mono">'+e(git.branch)+'</span></div>')+'</section>';
 return html+'</details>';
}
function deliverables(){
 const m=management();
 let html=head("SCOPE & DELIVERY","目标与交付","交付成果必须有成功条件。新增条目都是候选，不会自动改变正式产品定义。",action("新增交付成果","create_deliverable"));
 html+=panel("交付结构",m.deliverables.length?m.deliverables.map(d=>line(d.title,d.success_condition,pill(d.state))).join(""):empty("尚无交付成果。可先创建候选并由负责人审查。"));
 html+='<section class="section">'+panel("阶段与批准边界",'<div class="info">立项/需求/设计审批、开发计划批准和 Gate 是不同决定。此管理界面仅保存本地候选工作，不签发正式批准。</div><div class="details"><span>项目范围决定</span>'+e(project().scope_decision)+'</div><div class="details"><span>初始化类型</span>'+e(project().intake_mode)+'</div>')+'</section>';
 return html;
}
function work(){
 const m=management();
 let html=head("WORK PLAN","研发工作","任务性质、预期成果和当前实际进度分开记录。研究任务可探索，但不能越过设计准入。",action("新增研发工作","create_work"));
 html+=m.work.length?'<section class="panel"><table class="table"><thead><tr><th>工作项</th><th>性质</th><th>对应交付</th><th>实际记录</th><th>操作</th></tr></thead><tbody>'+m.work.map(w=>'<tr><td><strong>'+e(w.title)+'</strong><small>'+e(w.expected_result)+'</small><small class="mono">'+e(w.id)+'</small></td><td>'+pill(w.kind)+'</td><td>'+e(m.deliverables.find(d=>d.id===w.deliverable_id)?.title||"未知")+'</td><td>'+pill(m.progress[w.id]?.state||"未记录")+'</td><td><button class="copy" data-progress="'+e(w.id)+'">记录进展</button></td></tr>').join("")+'</tbody></table></section>':empty("尚无任务。先在“目标与交付”建立候选交付成果。");
 html+='<section class="section info">本地记录的工作完成，不意味着 G1/G2/G3/G4 阶段通过，也不表示 Codex 已获得编码实施授权。</section>';
 return html;
}
function modules(){
 const m=management(), c=model.code_status||{}, entries=m.modules||[], snapshots=c.registered_modules||[], candidates=c.candidates||[];
 let html=head("SOURCE OWNERSHIP","模块与实际代码","真实源码状态、模块责任、公共接口、候选提交及集成证据分开显示，不按任务完成猜测代码状态。",action("登记模块边界","create_module")+' <button class="secondary" data-action="assign_module_work">绑定工作与模块</button> <button class="secondary" id="check-module-remote">核对 GitHub 源码</button>');
 html+='<div class="info"><strong>证据来源：</strong>'+e(c.source||"UNKNOWN")+' · 本地 HEAD：<code>'+e(c.git_head||"暂无提交")+'</code> · 分支：'+e(c.branch||model.git.branch)+'。远端集成状态必须由 GitHub 真实提交/PR 另行核对。</div>';
 html+='<section class="section">'+panel("模块源码台账",entries.length?
   '<table class="table"><thead><tr><th>模块与职责</th><th>边界和接口</th><th>本地实际代码</th><th>远端集成</th></tr></thead><tbody>'+
   entries.map(mod=>{
     const code=snapshots.find(x=>x.id===mod.id);
     return '<tr><td><strong>'+e(mod.title)+'</strong><small class="mono">'+e(mod.id)+'</small><small>'+e(mod.responsibility)+'</small></td>'+
     '<td>'+pill(mod.owner_role)+'<small class="mono">'+e((mod.owned_prefixes||[]).join("、"))+'</small><small>公共接口：'+e((mod.contracts||[]).join("、")||"未登记")+'</small></td>'+
     '<td>'+pill(code?.working_tree||"尚无可核实 Git 快照","pending")+'<small>追踪文件 '+e(code?.tracked_file_count??"—")+' · 未提交变更 '+e(code?.working_change_count??"—")+'</small><small>最后改动提交：</small><small class="mono">'+e(code?.last_changed_commit||"未知")+'</small></td>'+
     '<td>'+pill("远端未核实","pending")+'<small>边界：'+e(mod.boundary_state)+'</small></td></tr>';
   }).join("")+'</tbody></table>':empty("尚未登记真实代码模块。先分析源码路径及接口边界，不按分支名称随意划模块。"))+'</section>';
 if(moduleRemoteSnapshot){
   const r=moduleRemoteSnapshot;
   html+='<section class="section">'+panel("GitHub 真实仓库快照（人工触发）",
       '<div class="details"><span>目标仓库</span><strong>'+e(r.repository)+'</strong></div>'+
       '<div class="details"><span>默认基线分支</span><strong>'+e(r.branch)+'</strong></div>'+
       '<div class="details"><span>核对的 GitHub HEAD</span><code>'+e(r.head)+'</code></div>'+
       (r.modules||[]).map(x=>line(x.module_id,
           (x.last_commits_by_path||[]).map(y=>y.path+" → "+y.commit_sha).join("；")||"该模块在 GitHub 默认分支尚无代码提交",
           pill("已查真实 GitHub 历史，未核准交付"))).join("")+
       (r.pr_search_truncated?'<div class="warning">近期 PR 窗口不完整，不能据此断言没有其他候选。</div>':""))+'</section>';
   html+='<section class="section">'+panel("近期候选 PR（真实 GitHub 文件清单）",
       (r.recent_prs||[]).length?r.recent_prs.map(x=>
         line("PR #"+x.pr_number+" · "+x.head_sha,
              "影响模块："+x.affected_modules.join("、")+"；接口："+x.interface_changes.join("、")+
              "；未归属："+x.unowned_paths.join("、")+"；状态："+x.state,
              pill(x.merged_by_github?"GitHub 已合并，仍须验证交付":"尚未合并","pending"))).join("")
       :empty("在最近检查的 PR 范围内，没有发现涉及已登记模块的 PR；并不能代表历史上从未提交。"))+'</section>';
 }
 html+='<section class="section">'+panel("候选提交及跨模块影响",candidates.length?
   candidates.map(x=>line(x.task_id+" · "+(x.candidate_head||"缺少 HEAD"),
     "影响模块："+(x.touched_modules||[]).join("、")+"；公共接口："+(x.interface_changes||[]).join("、")+"；跨模块："+(x.cross_module_paths||[]).join("、")+"；未归属："+(x.unowned_paths||[]).join("、")+"；提交："+x.git_assessment,
     pill(x.boundary_assessment,"pending"))).join(""):empty("没有可关联的本地 Codex 候选提交，不代表任何模块已交付。"))+'</section>';
 if((c.unowned_working_change_count||0)>0)html+='<section class="section info">还有 '+e(c.unowned_working_change_count)+' 个本地改动不属于已登记模块：'+e((c.unowned_working_changes||[]).join("、"))+'。先核对归属后再接入稳定基线。</section>';
 html+='<section class="section">'+panel("模块状态如何实际确认",
 '<div class="details"><span>已写源码</span><strong>在实际 Git 对象中找到提交与文件</strong></div>'+
 '<div class="details"><span>可供审查</span><strong>候选 HEAD、真实 diff、回执、接口影响齐全</strong></div>'+
 '<div class="details"><span>可集成</span><strong>审查通过且必要验证有证据</strong></div>'+
 '<div class="details"><span>稳定基线</span><strong>目标 GitHub 分支已经包含对应实现</strong></div>'+
 '<div class="details"><span>可以交付</span><strong>再核对构建、测试和目标场景实测</strong></div>')+'</section>';
 return html;
}
function requirements(){
 const m=management(), items=m.requirements||[];
 let html=head("REQUIREMENTS & EVIDENCE","需求与依据","追溯研究结论和原始来源，未获授权的需求仅是候选，不会自动进入正式设计。",action("新增需求候选","create_requirement"));
 html+=panel("需求登记",items.length?items.map(x=>line(x.id+" · "+x.title,x.statement+" · 验收："+x.acceptance+" · 来源："+(x.source_refs||[]).join("、"),pill(x.state,"pending"))).join(""):empty("没有已登记需求。可以从实际研究与工程证据建立候选。"));
 return html;
}
function plans(){
 const m=management(),plans=m.plans||[];
 let html=head("PLAN & DEPENDENCY","计划与依赖","WBS 形成可追溯计划，提交后冻结任务快照；修改影响到的计划需重新审查。",action("新建计划","create_plan")+' <button class="secondary" data-action="link_dependency">建立工作依赖</button>');
 html+=panel("计划与审查记录",plans.length?plans.map(x=>{
   const buttons=x.state==="DRAFT"||x.state==="REASSESSMENT_REQUIRED"
     ?'<button class="copy" data-plan-submit="'+e(x.id)+'">提交审查</button>'
     :(x.state==="SUBMITTED_FOR_HUMAN_REVIEW"
       ?'<button class="copy" data-plan-review="'+e(x.id)+'">记录审查</button>'
       :'<button class="copy" data-plan-revise="'+e(x.id)+'">修订计划</button>');
   return line(x.id+" · "+x.title,x.objective+" · 工作："+(x.work_ids||[]).join(", ")+" · 完成条件："+x.exit_condition,pill(x.state,"pending")+" "+buttons);
 }).join(""):empty("尚无研发计划。先在“研发工作”页建立 WBS，再形成计划。"));
 html+='<section class="section">'+panel("工作依赖图",m.work.length?m.work.map(w=>line(w.id+" · "+w.title,(w.depends_on||[]).length?"前置任务："+w.depends_on.join(", "):"无已登记前置任务")).join(""):empty("暂无可组成依赖关系的工作项"))+'</section>';
 return html;
}
function changes(){
 const m=management(),changes=m.changes||[],gates=m.gate_reviews||[];
 let html=head("CHANGE & GATES","变更与阶段审查","只重审真实受影响的工作、计划和交付；阶段审查请求不能自行将 Gate 标记为 PASS。",action("提出变更","create_change")+' <button class="secondary" data-action="request_gate_review">请求阶段审查</button>');
 html+='<div class="pair">'+panel("变更影响",changes.length?changes.map(x=>line(x.title,x.justification+" · 影响工作："+(x.affected_work_ids||[]).join(", ")+ " · 影响交付："+(x.affected_deliverables||[]).join(", "),pill(x.state,"pending")+(x.state==="IMPACT_ASSESSMENT_REQUIRED"?' <button class="copy" data-change-assess="'+e(x.id)+'">评估影响</button>':""))).join(""):empty("没有登记的范围变更。"))+panel("Gate 审查请求",gates.length?gates.map(x=>line(x.stage+" · "+x.id,x.purpose+" · 范围："+x.work_ids.join(", "),pill(x.state,"pending"))).join(""):empty("尚未提交阶段审查请求。"))+'</div>';
 html+='<section class="section info">变更评估只是项目管理候选。正式基线修订、Gate 批准和是否重新实施由真实负责人及受控 GitHub 决定；其他未受影响的工作不应全盘重置。</section>';
 return html;
}
function risks(){
 const m=management();
 let html=head("QUALITY & CONTROL","风险与里程碑","以真实证据识别风险和决定交付检查点；没有排期依据则不显示虚假的日期。",action("登记风险","raise_risk")+' <button class="secondary" data-action="create_milestone">新增里程碑</button>');
 html+='<div class="pair">'+panel("风险登记",m.risks.length?m.risks.map(x=>line(x.title,x.description+" · 应对："+x.mitigation,pill(x.state,"pending"))).join(""):empty("当前没有登记的风险，不等于不存在风险。"))+panel("交付里程碑",m.milestones.length?m.milestones.map(x=>line(x.title,x.exit_condition,pill(x.state))).join(""):empty("目前没有建立经论证的里程碑。"))+'</div>';
 return html;
}
function evidence(){
 const p=project(),assets=model.assets,decisions=model.decisions;
 const context=model.context_assessment||{status:"NOT_RECORDED",note:"尚无项目证据评价"};
 let html=head("EVIDENCE & APPROVAL","资产与正式依据","区分资料已找到、当前适用性候选和真正获得批准的工程依据；历史资产记录本身不证明现行有效。");
 const checked=context.audit||{};
 const pending=(checked.not_searched_or_incomplete||[]).length;
 const found=(checked.found_not_assessed||[]).length;
 const gaps=(checked.confirmed_gaps||[]).length;
 const proposed=(context.proposed_actions||[]);
 html+=panel("项目认识与资料补全（只读候选）",
   '<p class="hint">'+e(context.note||"")+'</p>'+
   '<div class="details"><span>评价记录</span>'+pill(context.status||"NOT_RECORDED",
      context.status==="CANDIDATE_STRUCTURAL_REVIEW"?"blue":"pending")+'</div>'+
   (context.status==="CANDIDATE_STRUCTURAL_REVIEW"?
      '<p>尚未查明或检索不足：'+e(pending)+' 项；已找到但未审查：'+e(found)+
      ' 项；确认缺口候选：'+e(gaps)+' 项。</p>'+
      (proposed.length?'<div class="list">'+proposed.map(a=>line(
        a.topic+" · "+a.kind,a.reason+"；候选成果："+a.expected_output,
        pill("待组织 · 未授权","pending"))).join("")+'</div>':
        '<p class="hint">没有登记补全提案，不代表所有工程依据已经齐全。</p>')
      :'<p class="hint">可在目标项目 .project-control/context_assessment.json 保存经过审查的 v2 候选记录。工作台仅只读展示，不自动生成、审批或发布任务。</p>'));

 html+='<div class="pair">'+panel("资产快照",'<div class="stat"><label>已登记 Git 跟踪文件</label><strong>'+e(assets.tracked_file_count??0)+'</strong></div><p class="hint">来源：'+e(assets.observation_source||"未知")+'；远端历史/PR/CI：'+e(assets.remote_history_pr_ci||"未核对")+'</p>'+ (assets.entries?.length?'<div class="list">'+assets.entries.slice(0,40).map(a=>line(a.path,"可信度："+a.confidence+" · 批准："+a.approval)).join("")+'</div>':empty("没有来源清单"))+(assets.entries?.length>40?'<p class="hint">只展示前 40 条；完整资产在目标仓库 assets.json 中。</p>':""))+panel("正式决策记录",decisions.decisions?.length?decisions.decisions.map(d=>line(d.kind||"决策",d.approval_reference||"无来源",pill(d.state||"未核实"))).join(""):empty("尚无经系统登记的正式决定。不能由此否定原有历史成果。"))+'</div>';
 return html;
}
function handoff(){
 const t=model.tasks.tasks||[],git=model.git,p=project();
 let html=head("AI HANDOFF","AI 与 Codex 交接","ChatGPT 组织研发并写入目标 GitHub 管理记录；Codex 自行读取目标仓库已授权任务，用户不必搬运实施方案。");
 html+='<div class="info"><strong>目标仓库入口：</strong> PROJECT_START_HERE.md<br><strong>Codex 任务合同：</strong> .project-control/tasks.json<br><strong>本地任务校验：</strong> python .project-control/resolve.py select<br>以下数据读取自当前本地工作树，是否已发布至远端必须另行核实。</div>';
 html+='<section class="section">'+panel("任务合同",t.length?t.map(x=>line(x.title||x.id,x.id+" · "+(x.executor||"未分配")+" · "+(x.source?.branch||"无来源"),pill(x.state||"未知",x.state==="READY_FOR_LOCAL_IMPLEMENTATION"?"blue":"pending"))).join(""):empty("没有已登记的执行任务。不能仅凭候选计划让 Codex 自动编码。"))+'</section>';
 const receipts=management().receipts||[];
 html+='<section class="section">'+panel("Codex 回执与独立审查",'<p class="muted">Codex 报告不等于验收。只有已加载并复核的 Git 提交才可标记代码范围已核对；日志摘要也不是测试结果的独立证明。</p><div style="margin:15px 0"><button class="secondary" id="collect-receipts">发现当前工作区回执</button></div>'+(receipts.length?receipts.map(x=>line(x.task_id+" · "+x.id,"提交 "+x.candidate_head.slice(0,12)+" · "+x.git_assessment+" · 测试未独立核对",pill(x.review_status,"pending")+(x.review_status==="PENDING_INDEPENDENT_REVIEW"?' <button class="copy" data-review="'+e(x.id)+'">审查</button>':""))).join(""):empty("此项目管理记录尚无 Codex 执行回执。"))+'<p class="hint">只从当前已登记的 Git 工作区发现回执，不搜索其他临时文件夹；跨 worktree 归集尚待工作区登记机制。</p>')+'</section>';
 html+='<section class="section">'+panel("GitHub 远端版本核对",'<div style="margin:0 0 14px"><button id="check-remote" class="secondary">显式核对远端 HEAD</button></div>'+(remoteSnapshot?'<div class="details"><span>对比状态</span>'+pill(remoteSnapshot.relation,remoteSnapshot.remote_checked?"blue":"pending")+'</div><div class="details"><span>本地 HEAD</span><span class="mono">'+e(remoteSnapshot.local_head||"无提交")+'</span></div><div class="details"><span>远端 HEAD</span><span class="mono">'+e(remoteSnapshot.remote_head||"未知")+'</span></div><p class="hint">'+e(remoteSnapshot.note||"")+'</p>':'<p class="muted">尚未向 GitHub 检查此分支的远端提交。页面中的本地 HEAD 不能冒充远端版本。</p>'))+'</section>';
 html+='<section class="section">'+panel("可复制的启动消息",'<p class="muted">仅当目标仓库存在唯一已授权任务，且用户在 Codex 中指定了执行目录时使用。</p><p class="source-row">在我指定的本地工作目录执行目标 GitHub 仓库当前唯一已授权任务。请先读取 PROJECT_START_HERE.md，核对 .project-control/tasks.json、来源版本、正式输入、文件范围和验收条件；冲突时停止并报告。</p><div style="margin-top:14px"><button id="copy-handoff" class="secondary">复制 Codex 启动消息</button></div>')+'</section>';
 return html;
}
function workspaceView(){
 let html=head("LOCAL WORKSPACES","本地工作区","Codex 的克隆和临时 worktree 必须登记、保留和审查。这里仅列举经授权登记的工作区，不扫描或删除未知目录。");
 html+='<div class="info"><strong>工作区管理内核：</strong> portable/workspace_manager.py<br>目录由用户在 Codex 中明确指定；单机独立注册表默认保存到用户目录下的 .project-control-local，也可以通过环境变量自选位置。<br><strong>退休规则：</strong>正式工程和任务工作区不会自动删除；仅 TEMP 链接 worktree 经新鲜检查、明确批准及无活动进程确认后才能正常移除。</div>';
 html+='<section class="section">'+panel("登记簿与实际工作区",'<div style="margin-bottom:14px"><button class="secondary" id="show-workspaces">读取本机登记簿</button> <button class="secondary" id="collect-registered-receipts">收集已登记工作区回执</button></div>'+(workspaceSnapshot?((workspaceSnapshot.workspaces||[]).length?workspaceSnapshot.workspaces.map(x=>line(x.workspace_id+" · "+x.registered_state,x.path+" · "+(x.issues.length?x.issues.join("; "):"未发现 Git 数据问题"),pill(x.retirement_proposal||"保留","pending"))).join(""):empty("当前项目没有登记的工作区。")):empty("尚未读取本机工作区登记簿。需本机负责人令牌。")))+'</section>';
 html+='<section class="section">'+panel("工作区交接与退出",'<p>创建/检视：工作区必须有来源仓库、任务、目的、精确 Git 基线和授权的本地根目录。临时查看结束后先运行只读退出预览，再检查 ignored 文件、未跟踪文件、独有提交、stash、Git 关系和占用情况。</p><div class="warning">未登记的旧文件夹、独立 clone、运行中的服务、未知 PID、断链 worktree 一律先保留并人工审查；不可运行 git clean -fdx、reset --hard、worktree prune 或系统级递归删除。</div>')+'</section>';
 return html;
}
function processView(){
 const state=management().process_control||{activities:[],intakes:[],reports:[],review_requests:[]};
 const catalog=model.process_catalog||{version:"未知",processes:[]};
 let html=head("PROCESS & AI WORK","工程过程与 AI 接管","工作责任、技术审查、负责人决定和正式实施互不等价；这里仅记录候选，不自动批准或派发代码。",action("创建工程活动","open_process_activity"));
 html+=ownerBriefingPanel();
 html+='<section class="section pair">'+panel("当前制度版本",'<div class="details"><span>过程规范</span><strong>'+e(catalog.version)+'</strong></div><div class="details"><span>项目 Profile</span><strong>'+e(state.profile_id||"未建立")+'</strong></div><div class="details"><span>已登记活动</span><strong>'+e((state.activities||[]).length)+'</strong></div><p>过程是工程工作的目的和方法，阶段是项目的组织和审查点。初始责任不会因为文件、代码或旧 PASS 而自动完成。</p>')+
 panel("新 AI 接管与报告",'<p>先自行读取仓库身份、有效依据、当前授权与过程说明，再向负责人汇报来源、角色、冲突和下一步。</p>'+action("记录 AI 接管","record_ai_intake")+' '+action("提交 AI 工作报告","record_ai_work_report")+'<p class="hint">接管或报告均不授予任务、审批、合并或真实硬件权限。</p>')+'</section>';
 html+='<section class="section">'+panel("工程活动及责任履行",(state.activities||[]).length?(state.activities||[]).map(a=>{
  const process=catalog.processes.find(p=>p.id===a.process_id)||{};
  const counts=(a.obligations||[]).filter(o=>o.disposition==="SATISFIED_CANDIDATE").length;
  return '<div class="line"><div><strong>'+e(process.name||a.process_id)+' · '+e(a.id)+'</strong><p>'+e(a.objective)+'</p><p>职责 '+e(a.role)+' · 候选证据 '+counts+'/'+(a.obligations||[]).length+' · 工作修订 '+e(a.revision)+' · '+e(a.state)+'</p>'+(a.obligations||[]).map(o=>'<small>'+e(o.id)+'：'+e(o.disposition)+'</small>').join(' ')+'</div><div><button class="copy" data-focus-for="'+e(a.id)+'">设为当前关注</button> <button class="copy" data-obligation-for="'+e(a.id)+'">处理责任</button> <button class="copy" data-process-review-for="'+e(a.id)+'">技术审查</button> <button class="copy" data-owner-review-for="'+e(a.id)+'">请求审批</button></div></div>';}).join(''):empty("尚无工程活动。先阅读过程规范并由负责角色开展有来源的工作；不会自动生成批准的 P0。"))+'</section>';
 html+='<details class="section"><summary>展开全部研发过程的工作指导</summary>'+catalog.processes.map(p=>'<details class="panel"><summary><strong>'+e(p.name)+'</strong> · '+e(p.purpose)+'</summary><p><strong>为什么做：</strong>'+e(p.owner_guidance?.why||p.purpose)+'</p><p><strong>预期成果：</strong>'+e(p.owner_guidance?.expected_result||"待定")+'</p><p><strong>工作方法：</strong>'+e((p.work_method||p.activities).join("；"))+'</p><p><strong>审查准则：</strong>'+e((p.review_checks||[]).join("；"))+'</p><p><strong>交接：</strong>'+e((p.handoff_expectations||[]).join("；"))+'</p></details>').join("")+'</details>';
 html+='<section class="section pair">'+panel("AI 接管记录",(state.intakes||[]).length?state.intakes.map(i=>line(i.id+' · '+i.role,i.role_source+'；下一步：'+i.next_action,pill(i.state))).join(''):empty("尚未记录新 AI 接管。"))+panel("汇报与负责人审核请求",(state.reports||[]).map(r=>line(r.id,r.summary,pill(r.state))).join('')+(state.review_requests||[]).map(r=>line(r.id,r.activity_id+'@'+r.activity_revision,pill(r.state))).join('')||empty("暂无报告或审核请求。"))+'</section>';
 return html;
}

const pages={process:processView,overview,deliverables,work,modules,requirements,plans,changes,risks,evidence,handoff,workspace:workspaceView};
function render(){if(!model)return;$("#crumb-current").textContent=labels[page];document.querySelectorAll("nav button").forEach(x=>x.classList.toggle("active",x.dataset.page===page));$("#main").innerHTML=pages[page]();}
const definitions={
 open_process_activity:{title:"开展工程过程工作",desc:"根据工程过程建立责任清单；这不是阶段放行。",fields:[["id","活动 ID","text"],["process_id","工程过程","process-id"],["role","承担岗位","ai-role"],["objective","本次工作目标","textarea"],["source_refs","立项或任务来源（逐行）","textarea"]]},
 set_process_focus:{title:"指定当前关注工作",desc:"只修改负责人展示重点，不意味着批准。",fields:[["source_refs","关注这项工作的依据（逐行）","textarea"]]},
 set_process_obligation:{title:"记录责任处理结果",desc:"每项责任需要实际证据或明确的缺口处置；理由不产生放行权。",fields:[["obligation_id","责任项","obligation-id"],["disposition","处理状态","obligation-state"],["evidence_refs","证据引用（满足状态必填）","textarea"],["impact","不适用/延期/例外/冲突的影响（按需）","optional-textarea"],["owner","延期/例外的责任人（按需）","optional-text"],["close_by","延期/例外的关闭标准（按需）","optional-text"]]},
 review_process_activity:{title:"技术内容审查",desc:"记录内容质量意见，不授予项目正式批准；原作者不可自称独立审查。",fields:[["reviewer_role","审查岗位","ai-review-role"],["outcome","审查结论","technical-review"],["evidence_refs","审查依据（逐行）","textarea"]]},
 request_process_review:{title:"提交负责人审查请求",desc:"只有全部责任有候选证据且当前技术审查一致时才能提交；状态仍为待批准。",fields:[["id","审核请求 ID","text"],["purpose","需要负责人决定什么","textarea"]]},
 record_ai_intake:{title:"AI 入场与理解汇报",desc:"读取事实、角色来源和任务后再登记；入场不等于工作授权。",fields:[["id","接管 ID","text"],["role","岗位","ai-role"],["role_source","岗位依据","role-source"],["task_ref","任务引用（可空）","optional-text"],["observed_head","核实的 Git HEAD（可空）","optional-text"],["source_refs","已读依据（逐行）","textarea"],["verified_facts","已核实的事实（逐行）","textarea"],["conflicts","冲突（可空）","optional-textarea"],["unknowns","未知事项（可空）","optional-textarea"],["next_action","下一项工作","textarea"]]},
  record_ai_work_report:{title:"AI 工作报告 · 负责人优先",desc:"先解释项目问题和工作意义，再报告实际进展、判断、下一步和真正待决事项。仅保存候选，不代表审批。",fields:[["id","报告 ID","text"],["intake_id","接管记录","intake-id"],["activity_id","关联的工程活动（可空）","optional-activity"],["audience","报告接收者","report-audience"],["report_kind","报告性质","report-kind"],["focus","当前在解决的项目问题","textarea"],["why_this_work","为什么现在做这项工作","textarea"],["judgement","有依据的重要判断","textarea"],["summary","真实完成的工作","textarea"],["next_action","下一步行动","textarea"],["owner_action","需要负责人采取的动作","owner-action"],["owner_request","需要负责人处理的具体问题（无则留空）","optional-textarea"],["evidence_refs","证据来源（逐行）","textarea"],["unfinished","尚未完成事项（可空）","optional-textarea"]]},
 create_deliverable:{title:"新增候选交付成果",desc:"此操作仅保存候选，不批准产品范围。",fields:[["id","交付 ID","text"],["title","交付名称","text"],["success_condition","完成或验收条件","textarea"]]},
 create_work:{title:"新增研发工作",desc:"实施授权不会因此自动产生。",fields:[["id","工作 ID","text"],["title","工作名称","text"],["deliverable_id","对应交付","delivery"],["kind","工作性质","kind"],["expected_result","预期成果","textarea"]]},
 raise_risk:{title:"登记风险",desc:"风险关闭需要后续证据与审查。",fields:[["id","风险 ID","text"],["title","风险标题","text"],["description","风险情况","textarea"],["mitigation","建议处置","textarea"]]},
 create_milestone:{title:"新增里程碑",desc:"未提供实际日期依据，不虚构完成日期。",fields:[["id","里程碑 ID","text"],["title","里程碑名称","text"],["deliverable_id","对应交付","delivery"],["exit_condition","完成条件","textarea"]]},
 set_progress:{title:"记录工作进展",desc:"已报告完成必须提供实际证据；不会自动标为已验收。",fields:[["state","当前状态","state"],["evidence_refs","证据引用（多项用换行分隔）","textarea"]]},
 review_receipt:{title:"审查 Codex 回执",desc:"这里只审查证据及代码范围，不批准任务交付或测试 Gate。",fields:[["decision","审查结论","review"]]},
 create_requirement:{title:"新增需求候选",desc:"必须记载可核实的来源和验收依据。",fields:[["id","需求 ID","text"],["title","需求名称","text"],["statement","需求内容","textarea"],["source_refs","原始来源（每行一个）","textarea"],["acceptance","验收条件","textarea"]]},
 link_dependency:{title:"建立研发任务依赖",desc:"循环依赖不允许；受影响的已提交计划将要求重审。",fields:[["id","当前工作","work-id"],["depends_on","前置工作","work-id"]]},
 create_plan:{title:"新建计划候选",desc:"计划提交需复核任务和交付基线。",fields:[["id","计划 ID","text"],["title","计划名称","text"],["objective","目标","textarea"],["work_ids","包含工作 ID（每行一个）","textarea"],["exit_condition","退出条件","textarea"]]},
 revise_plan:{title:"修订已提交计划",desc:"保留原有审查记录，新版计划重新提交。",fields:[["objective","目标","textarea"],["work_ids","包含工作 ID（每行一个）","textarea"],["exit_condition","退出条件","textarea"]]},
 submit_plan:{title:"提交计划审查",desc:"仅提交待审候选，不批准执行。",fields:[]},
 record_plan_review:{title:"记录计划候选审查",desc:"正式批准仍需 GitHub 可核实的人类决定。",fields:[["decision","审查意见","plan-review"],["reviewer","审查人声明","text"],["evidence_refs","审查证据（每行一个）","textarea"]]},
 create_change:{title:"提出范围变更",desc:"影响范围需根据工作依赖图实际计算。",fields:[["id","变更 ID","text"],["title","变更名称","text"],["justification","变更原因","textarea"],["source_refs","来源证据（每行一个）","textarea"],["root_work_ids","变化起点工作 ID（每行一个）","textarea"]]},
 assess_change:{title:"评估变更影响",desc:"下游工作及相关计划将要求复核，而非全项目重置。",fields:[["impact_summary","具体影响及处理意见","textarea"]]},
 request_gate_review:{title:"提出阶段审查",desc:"这是审查申请，不会直接让 Gate 通过。",fields:[["id","审查 ID","text"],["stage","阶段名称","text"],["purpose","审查目的","textarea"],["work_ids","审查工作 ID（每行一个）","textarea"],["evidence_refs","证据引用（每行一个）","textarea"]]},
 create_module:{title:"登记源码模块",desc:"按照真实目录建立稳定职责与接口边界；路径必须以 / 结束且不得重叠。这里只形成待批准的模块方案。",fields:[["id","模块 ID","text"],["title","模块名称","text"],["responsibility","模块职责与交付边界","textarea"],["owner_role","负责角色","module-role"],["owned_prefixes","归属源码目录（每行一个）","textarea"],["contracts","公共接口文件（可空）","textarea"],["depends_on","依赖模块 ID（可空）","textarea"]]},
 assign_module_work:{title:"将研发工作归属模块",desc:"不能直接将已属于其他模块的任务跨模块改派，必须经变更审查。",fields:[["work_id","研发工作项","work-id"],["module_id","实际模块","module-id"]]}
};
let editAction="",editWork="";
function openForm(name,id=""){editAction=name;editWork=id;const d=definitions[name];if(!d)return;$("#form-title").textContent=d.title;$("#form-desc").textContent=d.desc;let content="";for(const [field,label,type] of d.fields){let control="";if(type==="textarea")control='<textarea name="'+field+'" '+(["evidence_refs","contracts","depends_on"].includes(field)?"":"required")+'></textarea>';else if(type==="delivery")control='<select name="'+field+'" required>'+management().deliverables.map(x=>'<option value="'+e(x.id)+'">'+e(x.title)+'</option>').join("")+'</select>';else if(type==="kind")control='<select name="'+field+'">'+["RESEARCH","REQUIREMENTS","DESIGN","IMPLEMENT","INTEGRATE","VERIFY","COORDINATE"].map(x=>'<option>'+x+'</option>').join("")+'</select>';else if(type==="state")control='<select name="'+field+'">'+["NOT_STARTED","IN_PROGRESS","BLOCKED","REPORTED_COMPLETE"].map(x=>'<option>'+x+'</option>').join("")+'</select>';else if(type==="review")control='<select name="'+field+'">'+["NEEDS_MORE_EVIDENCE","REJECTED","CODE_SCOPE_REVIEWED"].map(x=>'<option>'+x+'</option>').join("")+'</select>';else if(type==="plan-review")control='<select name="'+field+'">'+["REQUEST_CHANGES","REVIEWED_PENDING_FORMAL_APPROVAL"].map(x=>'<option>'+x+'</option>').join("")+'</select>';else if(type==="work-id")control='<select name="'+field+'" required>'+management().work.map(w=>'<option value="'+e(w.id)+'">'+e(w.id+" · "+w.title)+'</option>').join("")+'</select>';else if(type==="module-id")control='<select name="'+field+'" required>'+(management().modules||[]).map(m=>'<option value="'+e(m.id)+'">'+e(m.title)+'</option>').join("")+'</select>';else if(type==="process-id")control='<select name="'+field+'">'+(model.process_catalog?.processes||[]).map(p=>'<option value="'+e(p.id)+'">'+e(p.name)+'</option>').join("")+'</select>';
else if(type==="obligation-id")control='<select name="'+field+'">'+((management().process_control?.activities||[]).find(a=>a.id===id)?.obligations||[]).map(o=>'<option value="'+e(o.id)+'">'+e(o.id+" · "+o.disposition)+'</option>').join("")+'</select>';
else if(type==="ai-role")control='<select name="'+field+'">'+["coordinator","specialist","integration","reviewer","local_executor"].map(x=>'<option>'+x+'</option>').join("")+'</select>';
else if(type==="ai-review-role")control='<select name="'+field+'">'+["reviewer","coordinator","integration"].map(x=>'<option>'+x+'</option>').join("")+'</select>';
else if(type==="role-source")control='<select name="'+field+'">'+["USER_EXPLICIT","RECORDED_APPOINTMENT","UNCONFIRMED"].map(x=>'<option>'+x+'</option>').join("")+'</select>';
else if(type==="technical-review")control='<select name="'+field+'">'+["ADEQUATE_CANDIDATE","REWORK_REQUIRED","INSUFFICIENT_EVIDENCE"].map(x=>'<option>'+x+'</option>').join("")+'</select>';
else if(type==="obligation-state")control='<select name="'+field+'">'+["SATISFIED_CANDIDATE","NOT_APPLICABLE_PROPOSED","DEFERRED_PROPOSED","EXCEPTION_REQUESTED","OPEN","CONFLICT"].map(x=>'<option>'+x+'</option>').join("")+'</select>';
else if(type==="optional-activity")control='<select name="'+field+'"><option value="">不指定</option>'+(management().process_control?.activities||[]).map(a=>'<option value="'+e(a.id)+'">'+e(a.objective)+'</option>').join("")+'</select>';
 else if(type==="report-audience")control='<select name="'+field+'"><option value="">按岗位默认接收者</option>'+[["OWNER","项目负责人"],["COORDINATOR","项目协调者"],["SPECIALIST","专业人员"],["EXECUTOR","执行人员"]].map(v=>'<option value="'+v[0]+'">'+e(v[1])+'</option>').join("")+'</select>';
 else if(type==="report-kind")control='<select name="'+field+'">'+[["PROGRESS","进展"],["FINDING","发现"],["BLOCKER","阻塞"],["REVIEW","审查"],["HANDOFF","交接"],["DELIVERY","交付"]].map(v=>'<option value="'+v[0]+'">'+e(v[1])+'</option>').join("")+'</select>';
 else if(type==="owner-action")control='<select name="'+field+'">'+[["NONE","当前无需决定"],["REVIEW","需要审阅"],["DECIDE","需要决策"],["PROVIDE_INFORMATION","需要提供关键事实"]].map(x=>'<option value="'+x[0]+'">'+e(x[1])+'</option>').join("")+'</select>';
 else if(type==="intake-id")control='<select name="'+field+'">'+(management().process_control?.intakes||[]).map(i=>'<option value="'+e(i.id)+'">'+e(i.id+" · "+i.role)+'</option>').join("")+'</select>';
else if(type==="optional-textarea")control='<textarea name="'+field+'"></textarea>';
else if(type==="optional-text")control='<input name="'+field+'" maxlength="240">';
else if(type==="module-role")control='<select name="'+field+'">'+["unassigned","specialist","integration","coordinator"].map(x=>'<option>'+x+'</option>').join("")+'</select>';else control='<input name="'+field+'" required maxlength="240">';content+='<label>'+e(label)+control+'</label>';}content+='<label>记录原因<textarea name="reason" required></textarea></label>';$("#form-fields").innerHTML=content;if(name==="revise_plan"){const item=(management().plans||[]).find(x=>x.id===id);if(item){$("#edit-form").elements.namedItem("objective").value=item.objective;$("#edit-form").elements.namedItem("work_ids").value=item.work_ids.join("\n");$("#edit-form").elements.namedItem("exit_condition").value=item.exit_condition;}}$("#edit-dialog").showModal();}
async function submitForm(event){event.preventDefault();if(!model)return;const form=new FormData($("#edit-form"));const payload={};for(const [k,v] of form.entries())payload[k]=String(v).trim();if(editAction==="set_progress"){payload.id=editWork;payload.evidence_refs=payload.evidence_refs.split("\n").map(x=>x.trim()).filter(Boolean);}if(["review_receipt","submit_plan","record_plan_review","revise_plan","assess_change"].includes(editAction))payload.id=editWork;
if(["set_process_obligation","review_process_activity","request_process_review","set_process_focus"].includes(editAction))payload.activity_id=editWork;for(const key of ["source_refs","work_ids","root_work_ids","evidence_refs","owned_prefixes","contracts","depends_on","verified_facts","conflicts","unknowns","unfinished"]){if(key in payload && (key!=="evidence_refs" || editAction!=="set_progress"))payload[key]=String(payload[key]).split(/[\n,，]+/).map(x=>x.trim()).filter(Boolean);}payload.expected_revision=management().revision;const actionName=editAction;const body={project_id:selected,action:actionName,payload};if(!ownerToken){ownerToken=prompt("请输入服务启动终端显示的本次运行负责人授权码")||"";}if(!ownerToken)return;try{const r=await fetch("/api/action",{method:"POST",headers:{"Content-Type":"application/json","X-Owner-Token":ownerToken},body:JSON.stringify(body)});const value=await r.json();if(!r.ok){if(r.status===403)ownerToken="";throw Error(value.error||"写入失败");}$("#edit-dialog").close();await loadModel();notify("已保存本地候选记录；尚未提交到 GitHub。");}catch(err){notify(err.message,true);}}
const handoffText="在我指定的本地工作目录执行目标 GitHub 仓库当前唯一已授权任务。先读取 PROJECT_START_HERE.md，自行核对任务、版本、设计、范围和验收条件；遇到冲突停止报告。不要自动 push/merge/reset 或清理其他目录。";
async function checkRemote(){if(!ownerToken)ownerToken=prompt("请输入服务终端的本次负责人授权码")||"";if(!ownerToken)return;try{const r=await fetch("/api/remote-status?project_id="+encodeURIComponent(selected),{headers:{"X-Owner-Token":ownerToken},cache:"no-store"});const v=await r.json();if(!r.ok){if(r.status===403)ownerToken="";throw Error(v.error||"远端检查失败");}remoteSnapshot=v;render();notify(v.remote_checked?"远端 HEAD 已核对；本地文件状态仍需分别审查":"无法核实远端："+v.relation,!v.remote_checked);}catch(err){notify(err.message,true);}}
async function collectLocalReceipts(){if(!ownerToken)ownerToken=prompt("请输入服务终端的本次负责人授权码")||"";if(!ownerToken)return;try{const r=await fetch("/api/collect-local-receipts",{method:"POST",headers:{"Content-Type":"application/json","X-Owner-Token":ownerToken},body:JSON.stringify({project_id:selected})});const v=await r.json();if(!r.ok){if(r.status===403)ownerToken="";throw Error(v.error||"回执收集失败");}await loadModel();notify("导入 "+v.imported.length+" 份；待人工处理 "+v.held.length+" 份；已存在 "+v.skipped.length+" 份",Boolean(v.held.length));}catch(err){notify(err.message,true);}}
async function getOwnerToken(){if(!ownerToken)ownerToken=prompt("请输入本机管理服务终端的负责人授权码")||"";return ownerToken;}
async function showWorkspaces(){if(!await getOwnerToken())return;try{const r=await fetch("/api/workspaces?project_id="+encodeURIComponent(selected),{headers:{"X-Owner-Token":ownerToken},cache:"no-store"});const data=await r.json();if(!r.ok){if(r.status===403)ownerToken="";throw Error(data.error||"工作区检查失败");}workspaceSnapshot=data;render();}catch(err){notify(err.message,true);}}
async function fetchRemoteModules(){
 if(!await getOwnerToken())return;
 try{
   const r=await fetch("/api/modules/remote?project_id="+encodeURIComponent(selected),
      {headers:{"X-Owner-Token":ownerToken},cache:"no-store"});
   const data=await r.json();
   if(!r.ok){if(r.status===403)ownerToken="";throw Error(data.error||"读取 GitHub 代码状态失败");}
   moduleRemoteSnapshot=data;
   render();
   notify("已核对 GitHub 基线 "+data.branch+" · "+data.head.slice(0,12));
 }catch(err){notify(err.message,true);}
}
async function collectRegisteredReceipts(){if(!await getOwnerToken())return;try{const r=await fetch("/api/collect-registered-receipts",{method:"POST",headers:{"Content-Type":"application/json","X-Owner-Token":ownerToken},body:JSON.stringify({project_id:selected})});const data=await r.json();if(!r.ok){if(r.status===403)ownerToken="";throw Error(data.error||"登记工作区回执收集失败");}await loadModel();notify("已登记工作区："+data.registered_examined+"；导入："+data.imported.length+"；待审："+data.held.length,Boolean(data.held.length));}catch(err){notify(err.message,true);}}
document.addEventListener("click",event=>{if(event.target.closest("#show-initialize-project")||event.target.closest("#empty-initialize-project")){showProjectInitialization();return;}if(event.target.closest("#show-register-target")||event.target.closest("#empty-register-target")){showTargetRegistration();return;}if(event.target.closest("#check-module-remote")){fetchRemoteModules();return;}if(event.target.closest("#show-workspaces")){showWorkspaces();return;}if(event.target.closest("#collect-registered-receipts")){collectRegisteredReceipts();return;}if(event.target.closest("#check-remote")){checkRemote();return;}if(event.target.closest("#collect-receipts")){collectLocalReceipts();return;}for(const [selector,command] of [["[data-focus-for]","set_process_focus"],["[data-obligation-for]","set_process_obligation"],["[data-process-review-for]","review_process_activity"],["[data-owner-review-for]","request_process_review"]]){const el=event.target.closest(selector);if(el){openForm(command,el.getAttribute(selector.slice(1,-1)));return;}}const review=event.target.closest("[data-review]");if(review){openForm("review_receipt",review.dataset.review);return;}for(const [selector,cmd] of [["[data-plan-submit]","submit_plan"],["[data-plan-revise]","revise_plan"],["[data-plan-review]","record_plan_review"],["[data-change-assess]","assess_change"]]){const el=event.target.closest(selector);if(el){openForm(cmd,el.getAttribute(selector.slice(1,-1)));return;}}const nav=event.target.closest("[data-page]");if(nav){page=nav.dataset.page;render();return;}const work=event.target.closest("[data-progress]");if(work){openForm("set_progress",work.dataset.progress);return;}const btn=event.target.closest("[data-action]");if(btn){openForm(btn.dataset.action);return;}if(event.target.closest("#copy-handoff")){navigator.clipboard?.writeText(handoffText).then(()=>notify("已复制启动消息")).catch(()=>notify("浏览器不允许剪贴板，请手动复制",true));}});
$("#initialize-form").addEventListener("submit",previewProjectInitialization);
$("#initialize-apply").addEventListener("click",applyProjectInitialization);
$("#initialize-close").addEventListener("click",()=>$("#initialize-dialog").close());
$("#initialize-cancel").addEventListener("click",()=>$("#initialize-dialog").close());
["initialize-path","initialize-repo","initialize-mode","initialize-id","initialize-name"].forEach(id=>
 $("#"+id).addEventListener("input",()=>{intakeReviewToken=null;$("#initialize-apply").disabled=true;}));
$("#register-form").addEventListener("submit",registerTarget);
$("#register-close").addEventListener("click",()=>$("#register-dialog").close());
$("#register-cancel").addEventListener("click",()=>$("#register-dialog").close());
$("#project-select").addEventListener("change",async event=>{selected=event.target.value;page="overview";try{await loadModel();}catch(err){notify(err.message,true);$("#main").textContent=err.message;}});
$("#refresh").addEventListener("click",()=>loadProjects().catch(err=>notify(err.message,true)));
$("#form-close").addEventListener("click",()=>$("#edit-dialog").close());$("#form-cancel").addEventListener("click",()=>$("#edit-dialog").close());$("#edit-form").addEventListener("submit",submitForm);
loadProjects().catch(err=>{$("#main").innerHTML='<div class="panel error">'+e(err.message)+'</div>';notify(err.message,true);});