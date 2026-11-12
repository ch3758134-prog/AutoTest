/* AutoTest Project Control V2: clean, read-only project-management workspace.
 * All state is derived from committed engineering evidence and an explicit
 * unapproved planning proposal. No implicit approvals, AI loops or remote writes.
 */
'use strict';
let data=null, page='overview', filter='ALL', selectedStage='G1', toastHandle=null;
const $=s=>document.querySelector(s);
const esc=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const a=(name)=>esc(name);
const obj=id=>data.objects.find(x=>x.id===id);
const issueCount=(id)=>data.assessment.issues.filter(x=>x.at===id).length;
const workById=id=>data.plan.work.find(x=>x.id===id);
const aiEntryUrl='https://github.com/ch3758134-prog/AutoTest/blob/management/project-control-redesign-20261111/AI_START_HERE.md';
const aiEntryMessage=()=>['请自行读取以下 GitHub 工程项目管理系统统一 AI 入口：',aiEntryUrl,
 '按入口中的规则自行读取角色、项目索引、有效工作依据，并据此明确当前项目管理/研发职责。',
 '如果这个对话的身份或项目目标尚未确定，请你主动向我问一个必要的问题；不要让我编写角色提示词。',
 '不要将自身默认为工程总协调，也不要把研究任务自动当成编码实施授权。'].join('\n\n');
const statusOf=w=>data.assessment.work.find(x=>x.id===w.id)?.assessment || 'UNKNOWN';
const phaseStatus=s=>{
 const found=(data?.control?.phase_reviews||[]).filter(r=>r.phase_id===s.id);
 if(!found.length)return reviewDesc[s.review]||'未核实';
 const last=found[found.length-1];
 const labels={PASS:'本地 Gate 通过记录 · 尚需 Git 发布核验',PASS_WITH_LIMITATIONS:'本地限制性通过记录 · 待核验',RETURN:'已退回（本地记录）',DEFER:'已暂缓（本地记录）'};
 return labels[last.decision]||'阶段决定待核验';
};
const workKind={RESEARCH:'调查与研究',DESIGN:'系统与软件设计',IMPLEMENT:'编码与实现',INTEGRATE:'集成',VERIFY:'验证与验收',COORDINATE:'协调与计划'};
const planningHorizon={CURRENT:'当前工作',NEXT:'下一步',LATER:'后续规划'};
const workMode={EXPLORATORY:'探索研究',CONTROLLED:'受控工程工作'};
const reviewDesc={ARTIFACT_APPROVED_GATE_UNVERIFIED:'有批准成果 · Gate 未核实',NOT_VERIFIED:'尚无可核实的阶段决定',REPORT_PREPARED_UNAPPROVED:'研究报告已整理 · 未批准',CANDIDATE_CODE_EXISTS_NO_ACCEPTANCE:'存在候选代码 · 未验收'};
const stageTone=s=>s.review==='ARTIFACT_APPROVED_GATE_UNVERIFIED'?'approved':s.review==='REPORT_PREPARED_UNAPPROVED'?'pending':'unknown';
const pill=(label,tone='unknown')=>'<span class="pill '+a(tone)+'">'+a(label)+'</span>';
const sectionHead=(title,action='',actionText='')=>'<div class="section-head"><h2>'+a(title)+'</h2>'+(action?'<button class="link" data-go="'+a(action)+'">'+a(actionText)+'</button>':'')+'</div>';
const heading=(eyebrow,title,desc,actions='')=>'<div class="page-heading"><div><div class="eyebrow">'+a(eyebrow)+'</div><h1>'+a(title)+'</h1><p>'+a(desc)+'</p></div>'+actions+'</div>';
const btn=(text,action,attr='',kind='secondary')=>'<button class="'+kind+'" '+action+'="'+a(attr)+'">'+a(text)+'</button>';
const stageCard=s=>'<button class="phase '+(s.id==='G1'?'active':'')+'" data-stage="'+a(s.id)+'"><span class="code">'+a(s.id)+'</span><strong>'+a(s.name)+'</strong><span class="small-status">'+a(phaseStatus(s))+'</span></button>';
const dateHint='未建立经过验证的时间排期，因此不生成虚构的甘特图或完成率。';

function toText(v,n=220){const x=String(v||'').replace(/^#+.*$/gm,'').replace(/\*\*/g,'').replace(/\s+/g,' ').trim();return x.length>n?x.slice(0,n)+'…':x;}
function taskStatus(w){
 const progress=data?.control?.progress?.[w.id];
 const invalid=data?.control?.invalidation?.[w.id];
 if(invalid&&!invalid.resolved)return pill('变更后需复核','danger');
 if(progress?.state==='VERIFIED_COMPLETE')return pill('已核实工作结果','approved');
 if(progress?.state==='REPORTED_COMPLETE')return pill('已报告待核实','pending');
 if(progress?.state==='IN_PROGRESS')return pill('进行中','unknown');
 if(progress?.state==='BLOCKED')return pill('执行受阻','danger');
 if(statusOf(w)==='NEEDS_REVIEW')return pill('输入需复核','pending');
 return pill(w.state==='PLANNED'?'计划中':'规划候选','unknown');
}
function taskLine(w){return '<div class="work-line" data-work="'+a(w.id)+'" tabindex="0" role="button" aria-label="查看工作 '+a(w.title)+'"><div><strong>'+a(w.title)+'</strong><div class="helper">'+a(w.expected_result)+'</div></div><div>'+a(workKind[w.kind])+'</div><div>'+a(planningHorizon[w.horizon])+'</div><div>'+taskStatus(w)+'</div><div class="go">›</div></div>';}
function miniTask(w){return '<div class="list-row clickable" data-work="'+a(w.id)+'"><div class="list-icon">'+(w.kind==='RESEARCH'?'⌕':w.kind==='DESIGN'?'◇':w.kind==='VERIFY'?'✓':'▤')+'</div><div class="list-copy"><strong>'+a(w.title)+'</strong><p>'+a(workKind[w.kind])+' · '+a(planningHorizon[w.horizon])+' · '+a(w.expected_result)+'</p></div>'+taskStatus(w)+'</div>';}

function overview(){
 const current=data.plan.work.filter(x=>x.horizon==='CURRENT');
 const g1=data.process.stages.find(x=>x.id==='G1');
 const verified=Object.values(data.control?.progress||{}).filter(x=>x.state==='VERIFIED_COMPLETE').length;
 const needsReview=data.assessment.work.filter(x=>x.assessment==='NEEDS_REVIEW').length;
 const planStatus={DRAFT:'计划草拟中',IN_REVIEW:'计划待审',BASELINED:'已有批准计划',REVISION_DRAFT:'计划修订中'}[data.control?.planning?.status]||'未知';
 let html=heading('项目管理 / 概览',data.project.name,'项目目标、开发计划、阶段要求和实际证据应当相互一致。这里优先展示你现在需要把握的事项。');
 html+='<div class="hero"><div class="hero-main"><div class="eyebrow">当前关键管理事项</div><h2>审查 G1 的正式系统需求成果</h2><p>'+a(g1.summary)+' 这份研究可以继续讨论和修订；不因为“报告已经整理”就自动开启缺少有效设计输入的编码任务。</p><div class="actions">'+btn('阅读 G1 完整研究','data-open-doc','at-g1-integrated-autotest-study','primary')+btn('查看阶段要求','data-stage','G1')+'</div></div><div class="hero-side"><div class="eyebrow">当前项目依据</div><h3>产品定义 P0 V0.1</h3><p>已获得正式批准；阶段 Gate 与后续需求、架构和实际验收仍需分别核实。</p><div class="rule"></div><div class="eyebrow">计划真实性</div><p>计划是否已经批准，以本地决策及 GitHub 共享版本为准。产品定义不会随计划调整被覆盖。</p></div></div>';
 html+='<section class="section">'+sectionHead('研发流程','process','查看阶段要求')+'<div class="timeline">'+data.process.stages.map(stageCard).join('')+'</div></section>';
 html+='<section class="section split"><div class="panel"><div class="pane-title"><h2>近期工作</h2><button class="ghost" data-go="plan">全部工作 →</button></div><div class="list">'+current.map(miniTask).join('')+'</div><div class="callout">'+a('这些任务来自项目管理计划，只有明确的执行记录才能证明实际派发、开始或完成。')+'</div></div><div class="panel"><h2>需要警惕的偏差</h2><div class="list"><div class="list-row"><div class="list-icon">!</div><div class="list-copy"><strong>已整理的 G1 不是有效设计基线</strong><p>后续架构和编码不能直接将候选研究当成正式批准输入。</p></div></div><div class="list-row"><div class="list-icon">↻</div><div class="list-copy"><strong>真实 ECU 自动测试闭环尚无整体验收证据</strong><p>已有源码和软件检查应与真实台架验收明确区分。</p></div></div><div class="list-row"><div class="list-icon">◎</div><div class="list-copy"><strong>工作计划与证据需持续核对</strong><p>现有 '+a(String(data.plan.work.length))+' 项规划与真实执行进展须分别核对，不能据此推断排期。</p></div></div></div></div></section>';
 html+='<section class="section">'+sectionHead('当前资料与计划一致性','plan','查看影响与依赖')+'<div class="value-grid"><div class="value-box"><span>计划工作</span><strong>'+data.plan.work.length+'</strong><span>'+a(planStatus)+'</span></div><div class="value-box"><span>工作结果已核实</span><strong>'+verified+'</strong><span>完成核实不等于产品验收</span></div><div class="value-box"><span>输入有待复核</span><strong>'+needsReview+'</strong><span>不会自动禁止独立调查</span></div></div><div class="callout">当前 Git HEAD：'+a((data.source.git_head||'未知').slice(0,12))+'。'+a(data.source.note)+' '+a(dateHint)+'</div></section>';
 return html;
}

function deliverables(){
 let html=heading('项目管理 / 目标与范围','目标与交付','首先知道要交付什么，然后才谈任务和工作量。交付范围从已批准的产品定义出发，候选计划不得修改 P0。');
 const anchor=obj(data.plan.product_anchor.id);
 html+='<section class="panel"><div class="hstack"><div><div class="eyebrow">当前有效产品定义 · 来源版本 r'+a(data.plan.product_anchor.revision)+'</div><h2>'+a(anchor?anchor.title:'未找到有效定义')+'</h2></div>'+pill(anchor&&anchor.state==='APPROVED'?'文件已批准':'待核实',anchor&&anchor.state==='APPROVED'?'approved':'pending')+'</div><p class="subtitle">'+a(anchor?toText(anchor.body,500):'正式对象无法读取。')+'</p><div class="actions">'+btn('阅读正式定义','data-open-doc',anchor?anchor.id:'')+'</div></section>';
 html+='<section class="section">'+sectionHead('版本交付结构')+data.plan.deliverables.map(d=>'<div class="deliverable-card"><div><div class="eyebrow">交付成果 · 当前规划引用</div><h2>'+a(d.title)+'</h2><p>'+a(d.success_condition)+'</p><div class="summary-row">'+pill(d.commitment==='IN_SCOPE_FROM_APPROVED_BASELINE'?'源于已批准目标':'候选范围','approved')+pill('交付验证未完成','pending')+'</div></div><div>'+btn('关联工作','data-go','plan')+'</div></div>').join('')+'</section>';
 html+='<section class="section"><div class="panel"><h2>为什么目标与计划要分开？</h2><p class="subtitle">产品目标与交付范围不能因为 AI 找到一段旧代码就自动改变；工作计划则必须根据研究发现、依赖和实际验证持续调整。范围变更需要研究影响，计划调整不必每次重新批准整个产品定义。</p></div></section>';
 return html;
}
function workPlan(){
 const counts={CURRENT:0,NEXT:0,LATER:0};data.plan.work.forEach(w=>counts[w.horizon]++);
 let list=data.plan.work.filter(w=>filter==='ALL'||w.horizon===filter);
 let html=heading('项目管理 / 工作计划','开发计划','开发工作不只包括编码，也包括调查、设计、集成和验证。当前以滚动式计划组织，尚不具备可靠日期的工作不虚构排期。');
 html+='<div class="callout warn">计划与有效基线应分别核对；工作是否具备执行条件，应看实际来源和证据。'+a(dateHint)+'</div>';
 html+='<section class="section"><div class="toolbar">'+[['ALL','全部工作'],['CURRENT','当前 '+counts.CURRENT],['NEXT','下一步 '+counts.NEXT],['LATER','后续 '+counts.LATER]].map(x=>'<button class="chip '+(filter===x[0]?'selected':'')+'" data-filter="'+x[0]+'">'+a(x[1])+'</button>').join('')+'</div><div class="work-table"><div class="table-header"><span>工作与预期成果</span><span>工作性质</span><span>计划层次</span><span>计划核对</span><span></span></div>'+list.map(taskLine).join('')+'</div></section>';
 html+='<section class="section split"><div class="panel"><h2>工作依赖</h2><div class="list">'+data.plan.work.map(w=>'<div class="list-row"><div class="list-copy"><strong>'+a(w.title)+'</strong><p>前置工作：'+a(w.depends_on.map(x=>workById(x)?.title||x).join('、')||'无明确前置项')+'</p></div></div>').join('')+'</div></div><div class="panel"><h2>计划依据与不确定性</h2><p class="subtitle">'+a(data.plan.planning_assumptions)+'</p><div class="callout">计划进度与实际进展分别登记；没有可信工作记录时不推断已开始或完成。</div></div></section>';
 return html;
}
function processView(){
 const s=data.process.stages.find(x=>x.id===selectedStage)||data.process.stages[0];
 let html=heading('项目管理 / 研发流程','既定研发流程','流程定义阶段应达到什么结果，不替代产品交付结构和日常任务计划。每轮可以按实际影响重新执行相应工作，但不能无凭据推断 Gate 已通过。');
 html+='<div class="timeline">'+data.process.stages.map(x=>'<button class="phase '+(x.id===s.id?'active':'')+'" data-stage="'+a(x.id)+'"><span class="code">'+a(x.id)+'</span><strong>'+a(x.name)+'</strong><span class="small-status">'+a(phaseStatus(x))+'</span></button>').join('')+'</div>';
 html+='<section class="section split"><div class="panel"><div class="eyebrow">阶段要求</div><div class="hstack"><h2>'+a(s.id)+' · '+a(s.name)+'</h2>'+pill(phaseStatus(s),stageTone(s))+'</div><p class="subtitle">'+a(s.summary)+'</p><div class="stage-feature"><h3>本阶段的工作结果应当包括</h3><div class="checklist">'+s.required_outcomes.map(x=>'<div class="check-item">□ '+a(x)+'</div>').join('')+'</div></div>'+(s.primary_object?'<div class="actions">'+btn('阅读对应正式/候审文件','data-open-doc',s.primary_object,'primary')+'</div>':'')+'</div><div class="panel"><h2>螺旋开发怎样反映在这里？</h2><p class="subtitle">新的事实、需求调整或设计问题应先形成影响判断，再决定哪些成果与工作需要修订。已有批准版本保持可追溯；未受影响的工作不因为阶段回访而被清空。</p><div class="callout">当前周期正式身份及 Gate 决定未完成核实。界面不会假设 P0 Gate 已通过，也不会把 G1 报告的“研究完成”解释为需求批准。</div></div></section>';
 return html;
}
function reviews(){
 const approved=data.objects.filter(o=>o.state==='APPROVED');
 const g1=data.objects.filter(o=>o.id==='at-g1-integrated-autotest-study');
 let html=heading('项目管理 / 正式成果与审查','成果与审查','内部草稿和对话记录默认不在此展示。这里只呈现已批准依据和整理完成、可供负责人阅读的研究报告。');
 html+='<div class="review-banner"><div><div class="eyebrow">已有整理的研究报告</div><h3>G1 V1.0 整体需求研究 · 未批准</h3><p>报告已整理，但尚无独立正式提交审查记录。可先阅读全文，批准或退回须遵循对应正式工程流程。</p></div>'+btn('阅读 G1 报告','data-open-doc','at-g1-integrated-autotest-study','primary')+'</div>';
 html+='<section class="section">'+sectionHead('正式批准的工程依据')+approved.map(o=>'<div class="document-item"><div><strong>'+a(o.title)+'</strong>'+pill('已批准','approved')+'<p>'+a(toText(o.body,210))+'</p></div>'+btn('阅读','data-open-doc',o.id)+'</div>').join('')+'</section>';
 html+='<section class="section">'+sectionHead('供负责人阅读的研究报告')+g1.map(o=>'<div class="document-item"><div><strong>'+a(o.title)+'</strong>'+pill('未批准','pending')+'<p>'+a(toText(o.body,240))+'</p></div>'+btn('阅读全文','data-open-doc',o.id)+'</div>').join('')+'</section>';
 html+='<div class="callout">注意：“候审材料”表示可供你检查的研究工作，不表示已经生成不可变的正式提交审批事务。项目计划、进度和变更可在管理界面操作；这不构成 G1 需求文件批准，正式审查需另行记录。</div>';
 return html;
}
function handoff(){
 let html=heading('项目管理 / 协作交接','协作交接','你只需要让新 ChatGPT 对话读取 GitHub 统一入口。AI 先自行核对项目与角色；身份未明确时由它主动提问。你决定何时切换对话与触发 Codex，系统不运行后台 Agent。');
 html+='<div class="hero"><div class="hero-main"><div class="eyebrow">目前可生成的交接材料</div><h2>把同一项工作交给不同对话</h2><p>交接文字只引用明确的计划候选、工程版本及当前未解决事项。该操作不会创建 GitHub Issue、自动调用模型或授权 Codex 开始修改源码。</p><div class="actions">'+btn('复制统一 AI 阅读入口','data-ai-entry','','primary')+btn('复制 G1 任务交接','data-handoff','w-g1-review')+btn('查看全部工作','data-go','plan')+'</div></div><div class="hero-side"><h3>真实执行回执</h3><p>当前已记录 '+(data.control?.receipts?.length||0)+' 份本地执行回执。登记回执、核实证据、GitHub 共享和产品验收必须分别判断。</p></div></div>';
 html+='<section class="section">'+sectionHead('工作交接入口')+'<div class="kanban"><div class="kanban-col"><h3>近期研究</h3>'+data.plan.work.filter(w=>w.horizon==='CURRENT').map(w=>'<div class="kanban-item" data-work="'+a(w.id)+'"><strong>'+a(w.title)+'</strong><small>可查看任务与复制交接说明</small></div>').join('')+'</div><div class="kanban-col"><h3>后续受控工作</h3>'+data.plan.work.filter(w=>w.horizon==='NEXT').map(w=>'<div class="kanban-item" data-work="'+a(w.id)+'"><strong>'+a(w.title)+'</strong><small>需要有效上游输入才能转入正式工作</small></div>').join('')+'</div><div class="kanban-col"><h3>更远期规划</h3>'+data.plan.work.filter(w=>w.horizon==='LATER').map(w=>'<div class="kanban-item" data-work="'+a(w.id)+'"><strong>'+a(w.title)+'</strong><small>尚未具备执行授权</small></div>').join('')+'</div></div></section>';
 html+='<section class="section panel"><h2>GitHub 当前事实来源</h2><p class="subtitle">当前工作区 HEAD：'+a(data.source.git_head||'未能读取')+'。浏览器展示的是本地当前文件，不保证已同步远端最新提交。新候选设计未把本地执行状态与 GitHub 缓存混同。</p></section>';
 return html;
}
function renderDocBody(text){
 const lines=String(text||'').split(/\r?\n/);
 let out='', inTable=false, inList=false;
 const close=()=>{if(inTable){out+='</tbody></table>';inTable=false}if(inList){out+='</ul>';inList=false}};
 for(const line of lines){
  const s=line.trim();
  if(!s){close();out+='<div style="height:9px"></div>';continue}
  if(/^\|/.test(s)&&s.endsWith('|')){
   if(/^\|[\s:\-|]+\|$/.test(s))continue;
   if(!inTable){close();out+='<table style="border-collapse:collapse;width:100%;font-size:13px"><tbody>';inTable=true}
   out+='<tr>'+s.slice(1,-1).split('|').map(x=>'<td style="border:1px solid #e1e9ef;padding:9px 10px">'+a(x.trim())+'</td>').join('')+'</tr>';continue;
  }
  if(/^\s*[-*]\s+/.test(s)){
   if(!inList){close();out+='<ul>';inList=true}
   out+='<li>'+a(s.replace(/^[-*]\s+/,''))+'</li>';continue;
  }
  close();
  if(s.startsWith('### '))out+='<h3>'+a(s.slice(4))+'</h3>';
  else if(s.startsWith('## '))out+='<h2>'+a(s.slice(3))+'</h2>';
  else if(s.startsWith('# '))out+='<h2>'+a(s.slice(2))+'</h2>';
  else out+='<p style="margin:12px 0">'+a(s)+'</p>';
 }
 close();
 return out;
}
function docPage(id){
 const o=obj(id);if(!o)return heading('文档','无法读取','所选工程文档不存在。');
 return '<div class="actions" style="margin:0 0 20px">'+btn('← 返回成果与审查','data-go','reviews')+'</div>'+heading('工程成果 / 阅读',o.title,'r'+o.revision+' · '+(o.state==='APPROVED'?'已批准':'候审或内部记录，未批准'))+'<article class="reader"><div class="eyebrow">工程文档正文</div><div class="doc-body">'+renderDocBody(o.body)+'</div><div class="callout">阅读不会改变审批状态，所有批准必须经独立的正式流程记录。</div></article>';
}
function makeHandoff(w){
 const evidence=w.sources.map(s=>{const o=obj(s.object_id);return '- '+(o?.title||s.object_id)+' / r'+s.revision+' / '+(o?.state||'MISSING')+' / '+s.role}).join('\n');
 const blocker=data.assessment.issues.filter(x=>x.at===w.id).map(x=>'- '+x.code+'：'+x.detail).join('\n')||'无当前模型已识别的问题；不代表工作具备正式执行授权';
 const progress=data.control?.progress?.[w.id];
 const currentProgress=progress?progress.state+'；'+progress.detail:'未登记实际进展';
 const planState=data.control?.planning?.status||'UNKNOWN';
 return ['AutoTest｜工作交接（以 GitHub 共享版本为准）','工作：'+w.title,'任务标识：'+w.id,'计划修订：r'+(data.control?.planning?.revision||'?')+' / '+planState,'本地工作进展：'+currentProgress,'GitHub：https://github.com/ch3758134-prog/AutoTest','当前工作区 Git HEAD：'+(data.source.git_head||'UNKNOWN'),'工作性质：'+workKind[w.kind]+'；模式：'+workMode[w.mode],'预期成果：'+w.expected_result,'完成条件：'+w.exit_condition,'上游依据：\n'+evidence,'当前核对结果：\n'+blocker,'前置工作：'+(w.depends_on.join(', ')||'无明确前置项'),'请先从 GitHub 核对这些版本及真实状态。不得将候选计划、未批准的 G1 或未核实的本地执行记录当成批准输入；研究可继续，正式编码需相应准入。完成时提供成果链接、实际来源版本和待处理问题。'].join('\n\n');
}
function openWork(id){
 const w=workById(id);if(!w)return;
 const issues=data.assessment.issues.filter(x=>x.at===w.id);
 let html='<div class="eyebrow">'+a(workKind[w.kind])+' · '+a(planningHorizon[w.horizon])+'</div><h2>'+a(w.title)+'</h2><div class="summary-row">'+pill(data.control?.planning?.status==='BASELINED'?'已有批准计划基线':'计划尚未批准',data.control?.planning?.status==='BASELINED'?'approved':'pending')+pill(workMode[w.mode])+'</div><div class="rule"></div><h3>预期成果</h3><p>'+a(w.expected_result)+'</p><div class="rule"></div><h3>任务完成条件</h3><p>'+a(w.exit_condition)+'</p><div class="rule"></div><h3>上游工作</h3><p>'+a(w.depends_on.map(x=>workById(x)?.title||x).join('；')||'当前未声明前置工作')+'</p><div class="rule"></div><h3>工程输入与有效性</h3><div class="checklist">'+w.sources.map(s=>{const o=obj(s.object_id);return '<div class="check-item">'+a(o?.title||s.object_id)+' / r'+a(s.revision)+' / '+a(s.role==='CONTEXT'?'研究上下文':'要求有效的正式基线')+' / '+a(o?.state||'MISSING')+'</div>'}).join('')+'</div><div class="rule"></div><h3>计划核对</h3>'+(issues.length?issues.map(i=>'<div class="callout warn">'+a(i.code)+'：'+a(i.detail)+'</div>').join(''):'<div class="callout">没有当前模型识别的缺口，但不代表已经得到正式执行或批准。</div>')+'<div class="actions">'+btn('复制交接给 ChatGPT / Codex','data-handoff',w.id,'primary')+'</div>';
 html+=window.projectActions?.workButtons?.(w)||'';
 showDrawer(html);
}
function showDrawer(html){$('#drawer-content').innerHTML=html;$('#drawer').classList.add('visible');$('#drawer').setAttribute('aria-hidden','false')}
function closeDrawer(){$('#drawer').classList.remove('visible');$('#drawer').setAttribute('aria-hidden','true')}
function toast(text){const node=$('#toast');node.textContent=text;node.classList.add('visible');clearTimeout(toastHandle);toastHandle=setTimeout(()=>node.classList.remove('visible'),2900)}
async function copy(text){try{await navigator.clipboard.writeText(text);toast('交接内容已复制');}catch(e){const box=document.createElement('textarea');box.value=text;box.style.position='fixed';box.style.left='-9999px';document.body.append(box);box.select();try{document.execCommand('copy');toast('交接内容已复制')}catch(_){toast('复制失败，请检查浏览器权限')}box.remove()}}
function navigate(p){page=p;filter='ALL';render();closeDrawer();window.scrollTo(0,0)}
function render(){
 if(!data)return;
 document.querySelectorAll('#navigation button').forEach(b=>b.classList.toggle('active',b.dataset.page===page||(page==='document'&&b.dataset.page==='reviews')));
 const titles={overview:'总览',deliverables:'目标与交付',plan:'开发计划',process:'研发流程',reviews:'成果与审查',handoff:'协作交接',document:'文档阅读',sync:'GitHub 与本地记录'};
 $('#crumb-current').textContent=titles[page]||'总览';
 let html=page==='sync'?(window.projectActions?.syncPage()||'<p>正在加载同步信息</p>'):page==='overview'?overview():page==='deliverables'?deliverables():page==='plan'?workPlan():page==='process'?processView():page==='reviews'?reviews():page==='handoff'?handoff():page==='document'?docPage(window.currentDoc):overview();
 $('#main').innerHTML=html;
 $('#plan-count').textContent=data.plan.work.length;
 if(window.projectActions?.renderExtras)window.projectActions.renderExtras();
}
async function refresh(){
 $('#main').innerHTML='<div class="empty">正在读取工程资料……</div>';
 try{
  const r=await fetch('/api/model',{cache:'no-store'});const body=await r.json();
  if(!r.ok)throw Error(body.error||'API 请求失败');
  data=body;render();
 }catch(e){$('#main').innerHTML='<div class="empty"><h2>无法读取当前工程资料</h2><p>'+a(e.message)+'</p><p>请核对 Git 版本、工程快照和规划示例是否完整；不会为缺失的数据生成假的进度。</p></div>'}
}
document.addEventListener('click',async e=>{
 const b=e.target.closest('[data-go],[data-stage],[data-open-doc],[data-work],[data-filter],[data-handoff],[data-ai-entry]');
 if(!b)return;
 if(b.hasAttribute('data-ai-entry')){await copy(aiEntryMessage());return}
 if(b.dataset.go){navigate(b.dataset.go);return}
 if(b.dataset.stage){selectedStage=b.dataset.stage;navigate('process');return}
 if(b.dataset.openDoc){window.currentDoc=b.dataset.openDoc;navigate('document');return}
 if(b.dataset.work){openWork(b.dataset.work);return}
 if(b.dataset.filter){filter=b.dataset.filter;render();return}
 if(b.dataset.handoff){const w=workById(b.dataset.handoff);if(w)await copy(makeHandoff(w))}
});
document.addEventListener('keydown',e=>{if(e.key==='Escape')closeDrawer();if((e.key==='Enter'||e.key===' ')&&e.target.matches('[data-work]')){e.preventDefault();openWork(e.target.dataset.work)}});
document.querySelectorAll('#navigation [data-page]').forEach(b=>b.addEventListener('click',()=>navigate(b.dataset.page)));
$('#drawer-close').onclick=closeDrawer;$('#reload').onclick=refresh;
refresh();
