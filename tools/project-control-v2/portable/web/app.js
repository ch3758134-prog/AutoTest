"use strict";
const $=s=>document.querySelector(s);
const e=v=>String(v??"").replace(/[&<>"']/g,c=>({"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&#39;"}[c]));
const txt=v=>e(String(v??""));
let projects=[],model=null,selected=null,page="overview",ownerToken="",remoteSnapshot=null,workspaceSnapshot=null;
const labels={overview:"项目总览",deliverables:"目标与交付",work:"研发工作",modules:"模块与实际代码",requirements:"需求与依据",plans:"计划与依赖",changes:"变更与阶段审查",risks:"风险与里程碑",evidence:"资产与正式依据",handoff:"AI 与 Codex 交接",workspace:"本地工作区"};
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
async function loadProjects(){const data=await read("/api/projects");projects=data.projects;if(!projects.length)throw Error("没有已登记的项目");const id=selected&&projects.some(p=>p.project_id===selected)?selected:projects[0].project_id;$("#project-select").innerHTML=projects.map(p=>'<option value="'+e(p.project_id)+'">'+e(p.display_name||p.project_id)+'</option>').join("");selected=id;$("#project-select").value=id;await loadModel();}
async function loadModel(){remoteSnapshot=null;workspaceSnapshot=null;model=await read("/api/model?project_id="+encodeURIComponent(selected));$("#project-source").textContent=model.project.repo_url;$("#sync-pill").textContent=model.git.dirty?"本地有未提交变更":"本地 HEAD · 远端未核对";render();}
const count=(collection)=>management()[collection]?.length||0;
const mstatus=()=>management().status;
function overview(){
 const p=project(),m=management(),a=model.assets,t=model.tasks,git=model.git;
 let html=head("PROJECT OVERVIEW",p.display_name||p.project_id,"根据真实项目登记和本地 Git 状态汇总；未知或未批准内容不会自动变成正式完成。");
 html+='<div class="hero"><div><div class="eyebrow">当前管理事实</div><h2>'+e(p.initialization_state)+'</h2><p>项目来源 '+e(p.intake_mode||"UNKNOWN")+' · 项目目标和范围的正式状态由工程依据和审批记录决定；初始化不表示 Gate 通过。</p></div><div class="mini"><strong>目标仓库</strong><p class="source-row">'+e(p.repo_url)+'</p><br><strong>本地 HEAD</strong><p class="mono">'+e(git.head)+'</p></div></div>';
 html+='<section class="section"><div class="grid">'+[['交付成果',count("deliverables"),"均按候选与批准分别管理"],['研发任务',count("work"),"不自动获得编码权限"],['源码模块',count("modules"),"与源码实际提交分别核对"],['风险与问题',count("risks"),"以真实证据核对"],['已记录决策',model.decisions.decisions?.length||0,"不代表所有决策已核实"],['登记 Codex 任务',t.tasks?.length||0,"READY 仍需真实授权"],['Git 跟踪资产',a.tracked_file_count??0,"仅为索引快照"]].map(x=>'<div class="panel stat"><label>'+e(x[0])+'</label><strong>'+e(x[1])+'</strong><p>'+e(x[2])+'</p></div>').join("")+'</div></section>';
 html+='<section class="section pair">'+panel("近期工作",m.work.length?'<div class="list">'+m.work.slice(0,6).map(w=>line(w.title,w.kind+" · "+w.expected_result,pill(m.progress[w.id]?.state||w.state))).join("")+'</div>':empty("尚未建立研发工作。先确认目标与交付，再建立任务。"))+panel("正式性与来源",'<div class="details"><span>项目标识</span><strong>'+e(p.project_id)+'</strong></div><div class="details"><span>产品定义</span>'+pill(p.formal_product_identity||"UNKNOWN")+'</div><div class="details"><span>范围决定</span>'+pill(p.scope_decision||"UNKNOWN")+'</div><div class="details"><span>Gate</span>'+pill(p.phase_gate||"UNKNOWN")+'</div><div class="details"><span>本地分支</span><span class="mono">'+e(git.branch)+'</span></div>')+'</section>';
 return html;
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
 let html=head("SOURCE OWNERSHIP","模块与实际代码","真实源码状态、模块责任、公共接口、候选提交及集成证据分开显示，不按任务完成猜测代码状态。",action("登记模块边界","create_module")+' <button class="secondary" data-action="assign_module_work">绑定工作与模块</button>');
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
 let html=head("EVIDENCE & APPROVAL","资产与正式依据","读取项目接管时的资产清单和决策记录，保护旧历史，避免把存在的文件误当正式批准。");
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
const pages={overview,deliverables,work,modules,requirements,plans,changes,risks,evidence,handoff,workspace:workspaceView};
function render(){if(!model)return;$("#crumb-current").textContent=labels[page];document.querySelectorAll("nav button").forEach(x=>x.classList.toggle("active",x.dataset.page===page));$("#main").innerHTML=pages[page]();}
const definitions={
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
 request_gate_review:{title:"提出阶段审查",desc:"这是审查申请，不会直接让 Gate 通过。",fields:[["id","审查 ID","text"],["stage","阶段名称","text"],["purpose","审查目的","textarea"],["work_ids","审查工作 ID（每行一个）","textarea"],["evidence_refs","证据引用（每行一个）","textarea"]]}
};
let editAction="",editWork="";
function openForm(name,id=""){editAction=name;editWork=id;const d=definitions[name];if(!d)return;$("#form-title").textContent=d.title;$("#form-desc").textContent=d.desc;let content="";for(const [field,label,type] of d.fields){let control="";if(type==="textarea")control='<textarea name="'+field+'" '+(field==="evidence_refs"?"":"required")+'></textarea>';else if(type==="delivery")control='<select name="'+field+'" required>'+management().deliverables.map(x=>'<option value="'+e(x.id)+'">'+e(x.title)+'</option>').join("")+'</select>';else if(type==="kind")control='<select name="'+field+'">'+["RESEARCH","REQUIREMENTS","DESIGN","IMPLEMENT","INTEGRATE","VERIFY","COORDINATE"].map(x=>'<option>'+x+'</option>').join("")+'</select>';else if(type==="state")control='<select name="'+field+'">'+["NOT_STARTED","IN_PROGRESS","BLOCKED","REPORTED_COMPLETE"].map(x=>'<option>'+x+'</option>').join("")+'</select>';else if(type==="review")control='<select name="'+field+'">'+["NEEDS_MORE_EVIDENCE","REJECTED","CODE_SCOPE_REVIEWED"].map(x=>'<option>'+x+'</option>').join("")+'</select>';else if(type==="plan-review")control='<select name="'+field+'">'+["REQUEST_CHANGES","REVIEWED_PENDING_FORMAL_APPROVAL"].map(x=>'<option>'+x+'</option>').join("")+'</select>';else if(type==="work-id")control='<select name="'+field+'" required>'+management().work.map(w=>'<option value="'+e(w.id)+'">'+e(w.id+" · "+w.title)+'</option>').join("")+'</select>';else control='<input name="'+field+'" required maxlength="240">';content+='<label>'+e(label)+control+'</label>';}content+='<label>记录原因<textarea name="reason" required></textarea></label>';$("#form-fields").innerHTML=content;if(name==="revise_plan"){const item=(management().plans||[]).find(x=>x.id===id);if(item){$("#edit-form").elements.namedItem("objective").value=item.objective;$("#edit-form").elements.namedItem("work_ids").value=item.work_ids.join("\n");$("#edit-form").elements.namedItem("exit_condition").value=item.exit_condition;}}$("#edit-dialog").showModal();}
async function submitForm(event){event.preventDefault();if(!model)return;const form=new FormData($("#edit-form"));const payload={};for(const [k,v] of form.entries())payload[k]=String(v).trim();if(editAction==="set_progress"){payload.id=editWork;payload.evidence_refs=payload.evidence_refs.split("\n").map(x=>x.trim()).filter(Boolean);}if(["review_receipt","submit_plan","record_plan_review","revise_plan","assess_change"].includes(editAction))payload.id=editWork;for(const key of ["source_refs","work_ids","root_work_ids","evidence_refs"]){if(key in payload && (key!=="evidence_refs" || editAction!=="set_progress"))payload[key]=String(payload[key]).split(/[\n,，]+/).map(x=>x.trim()).filter(Boolean);}payload.expected_revision=management().revision;const actionName=editAction;const body={project_id:selected,action:actionName,payload};if(!ownerToken){ownerToken=prompt("请输入服务启动终端显示的本次运行负责人授权码")||"";}if(!ownerToken)return;try{const r=await fetch("/api/action",{method:"POST",headers:{"Content-Type":"application/json","X-Owner-Token":ownerToken},body:JSON.stringify(body)});const value=await r.json();if(!r.ok){if(r.status===403)ownerToken="";throw Error(value.error||"写入失败");}$("#edit-dialog").close();await loadModel();notify("已保存本地候选记录；尚未提交到 GitHub。");}catch(err){notify(err.message,true);}}
const handoffText="在我指定的本地工作目录执行目标 GitHub 仓库当前唯一已授权任务。先读取 PROJECT_START_HERE.md，自行核对任务、版本、设计、范围和验收条件；遇到冲突停止报告。不要自动 push/merge/reset 或清理其他目录。";
async function checkRemote(){if(!ownerToken)ownerToken=prompt("请输入服务终端的本次负责人授权码")||"";if(!ownerToken)return;try{const r=await fetch("/api/remote-status?project_id="+encodeURIComponent(selected),{headers:{"X-Owner-Token":ownerToken},cache:"no-store"});const v=await r.json();if(!r.ok){if(r.status===403)ownerToken="";throw Error(v.error||"远端检查失败");}remoteSnapshot=v;render();notify(v.remote_checked?"远端 HEAD 已核对；本地文件状态仍需分别审查":"无法核实远端："+v.relation,!v.remote_checked);}catch(err){notify(err.message,true);}}
async function collectLocalReceipts(){if(!ownerToken)ownerToken=prompt("请输入服务终端的本次负责人授权码")||"";if(!ownerToken)return;try{const r=await fetch("/api/collect-local-receipts",{method:"POST",headers:{"Content-Type":"application/json","X-Owner-Token":ownerToken},body:JSON.stringify({project_id:selected})});const v=await r.json();if(!r.ok){if(r.status===403)ownerToken="";throw Error(v.error||"回执收集失败");}await loadModel();notify("导入 "+v.imported.length+" 份；待人工处理 "+v.held.length+" 份；已存在 "+v.skipped.length+" 份",Boolean(v.held.length));}catch(err){notify(err.message,true);}}
async function getOwnerToken(){if(!ownerToken)ownerToken=prompt("请输入本机管理服务终端的负责人授权码")||"";return ownerToken;}
async function showWorkspaces(){if(!await getOwnerToken())return;try{const r=await fetch("/api/workspaces?project_id="+encodeURIComponent(selected),{headers:{"X-Owner-Token":ownerToken},cache:"no-store"});const data=await r.json();if(!r.ok){if(r.status===403)ownerToken="";throw Error(data.error||"工作区检查失败");}workspaceSnapshot=data;render();}catch(err){notify(err.message,true);}}
async function collectRegisteredReceipts(){if(!await getOwnerToken())return;try{const r=await fetch("/api/collect-registered-receipts",{method:"POST",headers:{"Content-Type":"application/json","X-Owner-Token":ownerToken},body:JSON.stringify({project_id:selected})});const data=await r.json();if(!r.ok){if(r.status===403)ownerToken="";throw Error(data.error||"登记工作区回执收集失败");}await loadModel();notify("已登记工作区："+data.registered_examined+"；导入："+data.imported.length+"；待审："+data.held.length,Boolean(data.held.length));}catch(err){notify(err.message,true);}}
document.addEventListener("click",event=>{if(event.target.closest("#show-workspaces")){showWorkspaces();return;}if(event.target.closest("#collect-registered-receipts")){collectRegisteredReceipts();return;}if(event.target.closest("#check-remote")){checkRemote();return;}if(event.target.closest("#collect-receipts")){collectLocalReceipts();return;}const review=event.target.closest("[data-review]");if(review){openForm("review_receipt",review.dataset.review);return;}for(const [selector,cmd] of [["[data-plan-submit]","submit_plan"],["[data-plan-revise]","revise_plan"],["[data-plan-review]","record_plan_review"],["[data-change-assess]","assess_change"]]){const el=event.target.closest(selector);if(el){openForm(cmd,el.getAttribute(selector.slice(1,-1)));return;}}const nav=event.target.closest("[data-page]");if(nav){page=nav.dataset.page;render();return;}const work=event.target.closest("[data-progress]");if(work){openForm("set_progress",work.dataset.progress);return;}const btn=event.target.closest("[data-action]");if(btn){openForm(btn.dataset.action);return;}if(event.target.closest("#copy-handoff")){navigator.clipboard?.writeText(handoffText).then(()=>notify("已复制启动消息")).catch(()=>notify("浏览器不允许剪贴板，请手动复制",true));}});
$("#project-select").addEventListener("change",async event=>{selected=event.target.value;page="overview";try{await loadModel();}catch(err){notify(err.message,true);$("#main").textContent=err.message;}});
$("#refresh").addEventListener("click",()=>loadModel().catch(err=>notify(err.message,true)));
$("#form-close").addEventListener("click",()=>$("#edit-dialog").close());$("#form-cancel").addEventListener("click",()=>$("#edit-dialog").close());$("#edit-form").addEventListener("submit",submitForm);
loadProjects().catch(err=>{$("#main").innerHTML='<div class="panel error">'+e(err.message)+'</div>';notify(err.message,true);});