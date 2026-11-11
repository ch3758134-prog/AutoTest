/* Project Control | fresh build. All project facts must come from a pinned Git commit.
 * This client never promotes a local suggestion into an approved engineering decision.
 */
'use strict';

const $ = s => document.querySelector(s);
const node = (tag, cls, txt) => {
  const el = document.createElement(tag);
  if (cls) el.className = cls;
  if (txt !== undefined && txt !== null) el.textContent = String(txt);
  return el;
};
const append = (parent, ...children) => { for (const child of children) if (child) parent.append(child); return parent; };
const link = (text, href, cls) => {
  const a = node('a', cls, text);
  a.href = href;
  a.target = '_blank';
  a.rel = 'noopener noreferrer';
  return a;
};
const button = (text, fn, cls='button') => {
  const b = node('button', cls, text);
  b.type = 'button';
  if (fn) b.addEventListener('click', fn);
  return b;
};

let snapshot = null, route = 'home', filter = '', kindFilter = 'ALL', horizonFilter = 'ALL';
const LABELS = {
  WAITING_OWNER: '待负责人审查',
  PLANNED: '计划中',
  DEPENDENCY_WAIT: '等待上游决定',
  NOT_SCHEDULED: '尚未展开计划',
  NEEDS_OWNER: '需要你决定',
  PROPOSAL: '候选建议',
  RESEARCH_REQUIRED: '需要调查',
  DOCUMENT_APPROVED_GATE_UNKNOWN: '文件已批 · Gate 待核',
  HISTORICAL_EVIDENCE: '已存历史资料',
  REVIEW_REQUESTED: '成果待审查',
  NOT_BASELINED: '当前基线未批准',
  HISTORICAL_CODE_EXISTS: '有候选源码 · 未验收',
  NO_CHANGE: '不建议改变',
  IN_PROGRESS: '建议进行中',
  BLOCKED: '建议标记阻碍',
  REPORTED_COMPLETE: '建议报告完成',
  P0: '最高',
  P1: '高',
  P2: '中',
  P3: '低',
  RESEARCH:'调查研究',
  DESIGN:'系统或专业设计',
  IMPLEMENT:'编码实施',
  INTEGRATE:'集成',
  VERIFY:'验证',
  REVIEW:'审查',
  COORDINATE:'协调',
  NOW:'当前',
  NEXT:'下一步',
  LATER:'后续',
  PRODUCT_CAPABILITY:'交付能力',
  RELEASE_GOAL:'版本交付目标',
  FUTURE_DELIVERY:'后续发展',
};
const lab = v => LABELS[v] || v || '未确认';
const tone = v => v === 'NEEDS_OWNER' || v === 'WAITING_OWNER' || v === 'REVIEW_REQUESTED' ? 'amber' :
  v === 'DOCUMENT_APPROVED_GATE_UNKNOWN' ? 'green' :
  v === 'NOT_SCHEDULED' || v === 'NOT_BASELINED' || v === 'DEPENDENCY_WAIT' ? 'red' : 'blue';
const chip = (text, color='') => node('span', 'tag' + (color ? ' ' + color : ''), text);
const data = () => snapshot.project;
const source = () => snapshot.source;
const deliverable = id => data().deliverables.find(d => d.id === id);
const work = id => data().work.find(w => w.id === id);
const sourceOf = id => data().sources.find(s => s.id === id);
const noteFor = id => (snapshot.local.notes || {})[id];
const homeUrl = 'https://github.com/ch3758134-prog/AutoTest';

function toast(message) {
  const t = $('#toast');
  t.textContent = message;
  t.classList.add('show');
  clearTimeout(toast.timer);
  toast.timer = setTimeout(() => t.classList.remove('show'), 4200);
}

async function copy(text) {
  try {
    await navigator.clipboard.writeText(text);
    toast('内容已复制，可以粘贴到相应 ChatGPT 或 Codex 对话');
  } catch {
    toast('浏览器复制权限不可用；请在安全的本地环境允许剪贴板访问');
  }
}

function navTo(page) {
  if (!['home','delivery','work','process','decisions','collaboration'].includes(page)) return;
  route = page;
  document.querySelectorAll('.nav-item').forEach(el =>
    el.classList.toggle('active', el.dataset.route === page));
  const routes = {home:'项目总览',delivery:'目标与交付',work:'工作与计划',
    process:'研发流程',decisions:'决策与问题',collaboration:'协作与证据'};
  $('#breadcrumbs').textContent = data().project.name + ' / ' + routes[page];
  render();
  window.scrollTo({top:0,behavior:'instant'});
}
function heading(kicker, title, detail) {
  const h = node('header','page-heading');
  append(h, node('div','eyebrow',kicker), node('h1','',title), node('p','subtext',detail));
  $('#page').append(h);
}
function part(title, subtitle) {
  const sec = node('section','section'), h = node('div','section-head');
  append(h,node('h2','',title));
  if (subtitle) h.append(node('span','fine',subtitle));
  sec.append(h);
  $('#page').append(sec);
  return sec;
}
function sourceBanner() {
  const bar = node('div','source-bar' +
    (snapshot.local_stale || source().record_dirty_in_worktree ? ' warning' : ''));
  append(bar,
    node('strong','', '工程资料：当前 Git HEAD'),
    node('code','',source().sha.slice(0,12)),
    node('span','',source().branch));
  if (source().record_dirty_in_worktree) bar.append(node('span','', '工作树中存在未提交的数据变更，当前视图仍读取已提交版本'));
  if (source().evidence_dirty_in_worktree) bar.append(node('span','', '有未提交的本地执行回执；仅在提交 Git 后才显示为共享证据'));
  if (snapshot.local_stale) bar.append(node('span','', '本地管理建议基于旧 SHA，必须重新核实'));
  if (snapshot.local.unsynced) bar.append(chip('有本地未同步建议','amber'));
  const small = node('span','', '不是 GitHub 远端实时状态');
  bar.append(small);
  $('#page').append(bar);
}
function sourcesList(ids, target) {
  const wrapper = node('div','source-list');
  for (const id of ids || []) {
    const s = sourceOf(id);
    if (!s) continue;
    const row = node('div','source-item');
    append(row,
      link(s.label,s.url),
      node('div','fine',s.revision),
      node('p','',s.note));
    wrapper.append(row);
  }
  target.append(wrapper);
}
function metaRow(label,value){
  const row=node('div','fact');
  append(row,node('span','fact-label',label),node('div','fact-value',value));
  return row;
}
function taskHandoff(w) {
  const refs = (w.source_ids||[]).map(id=>sourceOf(id)).filter(Boolean)
    .map(s=>'• '+s.label+'：'+s.url+'（'+s.revision+'）').join('\n');
  return [
    '# 项目任务交接（由负责人手动触发）',
    '项目：'+data().project.name,
    'GitHub：'+homeUrl,
    '管理依据：'+source().branch+' @ '+source().sha,
    '工作编号：'+w.id,
    '工作：'+w.title,
    '当前候选计划状态：'+lab(w.state)+'（计划未经正式批准）',
    '工作性质：'+lab(w.kind)+' / 阶段关联：'+w.phase,
    '目标交付：'+(deliverable(w.deliverable)?.title||w.deliverable),
    '期望成果：'+w.output,
    '完成条件：'+w.exit,
    '上游依赖：'+(w.depends.join(', ')||'无已登记的直接前置任务'),
    '已知限制：'+((w.blocking||[]).join('；')||'本计划未登记额外阻碍，需自行核查'),
    '依据：\n'+refs,
    '请先读取 GitHub 中最新的有效工程资料，核验实际代码及版本。区分事实、研究、正式批准和实现；不要自行宣布 Gate 通过、自动触发其他 ChatGPT 对话或把候选计划当实施授权。完成后把可复核成果及执行回执写入相应 GitHub 工作记录。',
  ].join('\n\n');
}
function decisionHandoff(q) {
  return ['请协助审查项目管理事项：'+q.title,
    'Git 仓库：'+homeUrl,
    '当前工作台提交：'+source().sha,
    '问题说明：'+q.description,
    '相关工作：'+q.work_ids.join(', '),
    '来源：'+q.source_ids.map(id=>sourceOf(id)).filter(Boolean).map(s=>s.url).join('\n'),
    '这是研究/审查请求，不代表已经批准。请明确需负责人确认的具体决定、依据和对项目范围及计划的影响。'
  ].join('\n\n');
}

function renderHome() {
  const p = data().project, d = data();
  heading('PROJECT OVERVIEW / 项目总览', p.name,
    '先掌握真正影响项目推进的事项，再进入交付范围、具体计划和需要作出的决定。');
  sourceBanner();

  const decision = d.decisions.find(x=>x.status==='NEEDS_OWNER');
  const hero = node('section','hero');
  const left = node('div');
  append(left,node('div','eyebrow','目前最值得你关注'),
    node('h2','', decision ? decision.title : '查看当前研发目标与下一步工作'),
    node('p','', decision ? decision.description : p.objective));
  const buttons=node('div','actions');
  append(buttons,
    button(decision?'查看审查事项':'查看工作计划',()=>navTo(decision?'decisions':'work')),
    button('查看近期工作',()=>navTo('work'),'button ghost'));
  left.append(buttons);
  const right=node('div','hero-side');
  append(right,node('div','caps','项目当前状态'),
    node('div','big','当前重点 · 系统需求审查'),
    node('p','',p.plan_note),
    chip('P0 定义已批准；不等于 P0 Gate 通过','green'));
  append(hero,left,right);
  $('#page').append(hero);

  const metrics=node('div','cols-3');
  const currentWork=d.work.filter(x=>x.horizon==='NOW').length;
  const ownerDecisions=d.decisions.filter(x=>x.status==='NEEDS_OWNER').length;
  const proposed=d.work.filter(x=>x.horizon==='NEXT'||x.horizon==='LATER').length;
  for(const [n,label,detail] of [
    [ownerDecisions,'需要负责人处理','正式审查请求，不包括内部草稿'],
    [currentWork,'当前计划中的工作','计划候选，执行状态需核对'],
    [proposed,'后续规划工作','没有虚构日期或执行授权']]){
    const card=node('div','card metric');
    append(card,node('div','label',label),node('strong','',n),node('div','fine',detail));
    metrics.append(card);
  }
  $('#page').append(metrics);

  const cols=node('div','cols-2');
  const planCard=node('div','card card-pad');
  append(planCard,node('div','caps','交付主线'),
    node('h2','',p.release),node('p','quiet',p.objective));
  const pipeline=node('div','');
  for(const [k,title,desc] of [
    ['CURRENT','当前：澄清和审查依据','G1 研究已形成，但需求尚未批准'],
    ['NEXT','下一步：受控整体设计','依据正式需求开展设计审查'],
    ['LATER','后续：实现及真实验收','根据有效设计与实测条件逐步实施']]){
    const item=node('div','progress-step'+(k==='CURRENT'?' current':''));
    const content=node('div');
    append(content,node('strong','',title),node('p','',desc));
    item.append(content);pipeline.append(item);
  }
  planCard.append(pipeline);
  planCard.append(button('查看交付结构',()=>navTo('delivery'),'button ghost small'));

  const tasksCard=node('div','card card-pad');
  append(tasksCard,node('div','caps','接下来有哪些工作'),node('h2','','近期工作安排'));
  for(const w of d.work.filter(x=>x.horizon==='NOW').slice(0,4)){
    const strip=node('div','strip'),body=node('div','strip-main');
    append(body,node('div','strip-title',w.title),
      node('div','strip-desc',w.owner+' · '+lab(w.state)));
    strip.append(body);
    strip.append(button('查看',()=>openWork(w.id),'button ghost small'));
    tasksCard.append(strip);
  }
  cols.append(planCard,tasksCard);
  const sec=part('项目推进一览','目标、工作、决定保持不同层次');
  sec.append(cols);
  const info=node('div','note-box',
    '这不是项目完成率展示：已有研究、候选源码、CI 和实际 ECU 验收是不同的工程证据。当前首页不展示 AI 内部草稿，也不会根据工作数量推断交付完成。');
  $('#page').append(info);
}

function renderDelivery(){
  const p=data();
  heading('DELIVERY STRUCTURE / 项目范围', '项目目标与交付结构',
    '从产品与版本目标出发组织交付成果。这个结构独立于 P0～G4 流程，也不把研究笔记直接当成交付。');
  sourceBanner();
  const hero=node('section','card card-pad');
  append(hero,node('div','caps','已确认的产品性质'),
    node('h2','',p.project.subtitle),
    node('p','',p.project.identity),
    chip('P0 文档已批准，阶段 Gate 另行审核','green'));
  const more=node('p','quiet',p.project.scope_note);
  more.style.marginTop='16px';hero.append(more);
  $('#page').append(hero);
  const sec=part('交付范围','下面的细化是待审计划建议，不冒充已批准需求');
  const tree=node('div','card');
  for(const d of p.deliverables.filter(x=>!x.parent)){
    const row=node('div','tree-parent'),h=node('div','tree-head');
    const headline=node('div');
    append(headline,node('div','caps',d.id),node('h3','',d.title));
    append(h,headline,chip(lab(d.scope),d.scope==='NOT_COMMITTED_FOR_NEAR_TERM'?'amber':'blue'));
    append(row,h,node('p','',d.description),node('div','fine','验收要点：'+d.acceptance));
    const children=p.deliverables.filter(x=>x.parent===d.id);
    if(children.length){
      const inner=node('div','tree-children');
      for(const child of children){
        const item=node('div','tree-child');
        item.append(button(child.title,()=>openDelivery(child.id),''));
        item.append(node('span','fine',child.id));
        inner.append(item);
      }
      row.append(inner);
    }
    tree.append(row);
  }
  sec.append(tree);
  const st=part('与研发计划的关系','交付目标不直接决定具体类、界面布局或代码文件');
  st.append(node('p','quiet','点击任一交付子项可以检查它对应哪些实际研发工作，以及引用了什么工程依据。'));
}

function renderWork(){
  heading('WORK & PLANNING / 开发计划', '工作分解与滚动计划',
    '当前工作可以详细规划；未来工作保留目标、依赖和准入条件。不要为尚未研究清楚的任务编造日期和实现方式。');
  sourceBanner();
  const bar=node('div','toolbar');
  const search=node('input','filter-input');search.type='search';search.placeholder='搜索工作、负责人或交付目标';
  search.value=filter;
  const horizon=node('select','filter-select');
  for(const [k,v] of [['ALL','全部计划层级'],['NOW','当前'],['NEXT','下一步'],['LATER','后续']]){
    const op=node('option','',v);op.value=k;horizon.append(op);
  }
  horizon.value=horizonFilter;
  const kind=node('select','filter-select');
  for(const [k,v] of [['ALL','全部工作类型'],['RESEARCH','调查研究'],['REVIEW','审查'],['DESIGN','设计'],['IMPLEMENT','实施'],['VERIFY','验证']]){
    const op=node('option','',v);op.value=k;kind.append(op);
  }
  kind.value=kindFilter;
  append(bar,search,horizon,kind);
  $('#page').append(bar);
  const list=node('div','');$('#page').append(list);
  const draw=()=>{
    list.replaceChildren();
    const matching=data().work.filter(w=>
      (horizonFilter==='ALL'||w.horizon===horizonFilter) &&
      (kindFilter==='ALL'||w.kind===kindFilter) &&
      (w.title+' '+w.owner+' '+(deliverable(w.deliverable)?.title||'')).toLowerCase().includes(filter.toLowerCase()));
    for(const [key,name,desc] of [
      ['NOW','当前工作','近期应推进、审查或核实的工作'],
      ['NEXT','下一步','依赖条件满足后可具体展开'],
      ['LATER','后续工作','只规划交付和依赖，不虚构实施日期']]){
      const items=matching.filter(w=>w.horizon===key);
      if(!items.length)continue;
      const section=node('section','work-group'),head=node('div','work-group-head');
      append(head,node('strong','',name),node('span','count',items.length),node('span','fine',desc));
      section.append(head);
      const cards=node('div','card');
      for(const w of items) cards.append(workRow(w));
      section.append(cards);
      list.append(section);
    }
    if(!matching.length)list.append(node('div','empty','没有符合筛选条件的工作。调整搜索或筛选条件即可。'));
  };
  search.addEventListener('input',e=>{filter=e.target.value;draw()});
  horizon.addEventListener('change',e=>{horizonFilter=e.target.value;draw()});
  kind.addEventListener('change',e=>{kindFilter=e.target.value;draw()});
  draw();
  $('#page').append(node('p','section-note','工作计划目前是候选版本。任务记录可以提出本地修改建议；只有经 GitHub 和负责人确认的正式变更才能成为下一版计划。'));
}

function workRow(w){
  const b=node('button','work-card');b.type='button';
  b.setAttribute('aria-label','查看工作：'+w.title);
  const logo=node('span','work-spot',({RESEARCH:'⌕',REVIEW:'✓',DESIGN:'◇',IMPLEMENT:'⌘',VERIFY:'◎'})[w.kind]||'•');
  const text=node('span','work-copy');
  append(text,node('strong','',w.title),
    node('span','',w.owner+' · '+lab(w.kind)+' · '+(deliverable(w.deliverable)?.title||w.deliverable)));
  const right=node('span','work-right');
  right.append(chip(lab(w.state),tone(w.state)));
  if(noteFor(w.id))right.append(chip('本地建议','amber'));
  b.append(logo,text,right,node('span','arrow','›'));
  b.addEventListener('click',()=>openWork(w.id));
  return b;
}

function renderProcess(){
  heading('PROCESS / 研发流程', '既定研发流程与螺旋修订',
    '这里展示适用阶段与当前工程记录的关系，不通过对象标题猜测 Gate，更不把历史代码存在当成阶段批准。');
  sourceBanner();
  $('#page').append(node('div','note-box',
    '流程规范与项目计划不是同一回事。既定阶段规定必须形成的工程判断和批准条件；项目计划负责安排具体交付工作。研发中的新问题会触发必要的局部再研究与修订，不能自动取消全部已有成果。'));
  const sec=part('阶段与实际核实情况','当前展示的是证据摘要，未录入的 Gate 一律不能当作已通过');
  const grid=node('div','flow-grid');
  for(const p of data().workflow.phases){
    const n=node('article','flow-node');
    append(n,node('small','',p.id),node('strong','',p.name),
      chip(lab(p.status),tone(p.status)),node('p','',p.summary));
    const related=data().work.filter(w=>w.phase===p.id);
    n.append(node('div','fine','关联当前计划工作：'+related.length+' 项'));
    grid.append(n);
  }
  sec.append(grid);
  const more=part('螺旋修订怎样影响计划');
  more.append(node('div','card card-pad',
    '发现新事实 → 核对有效范围及工程决定 → 确定受影响的交付和工作 → 重新研究/设计必要部分 → 修订计划与输入版本 → 继续受控实施。未受影响的工作不应无差别停止。'));
}

function impactPreview(deliverableId) {
  const affected = new Set([deliverableId]);
  let updated=true;
  while(updated){
    updated=false;
    for(const d of data().deliverables){
      if(d.parent && affected.has(d.parent) && !affected.has(d.id)){
        affected.add(d.id);updated=true;
      }
    }
  }
  const direct = data().work.filter(w=>affected.has(w.deliverable)).map(w=>w.id);
  const downstream = new Set();
  const seen = new Set(direct);
  updated=true;
  while(updated){
    updated=false;
    for(const w of data().work){
      if(seen.has(w.id))continue;
      if(w.depends.some(dep=>seen.has(dep))){
        downstream.add(w.id);seen.add(w.id);updated=true;
      }
    }
  }
  return {direct,downstream:[...downstream]};
}
function renderChangePreview(){
  const sec=part('范围变化与计划影响预览','基于已经登记的交付—工作—依赖关系推算；未登记关系仍然未知');
  const panel=node('div','card card-pad');
  const intro=node('p','quiet','如果产品范围、需求或新的硬件事实发生变化，先定位可能影响的交付成果与开发工作，再决定是否需要调整计划或重新研究。此处不会改写任何工程数据。');
  panel.append(intro);
  const controls=node('div','toolbar');
  const select=node('select','filter-select');
  select.setAttribute('aria-label','选择发生变化的交付成果');
  for(const d of data().deliverables){
    const opt=node('option','',d.id+' · '+d.title);opt.value=d.id;select.append(opt);
  }
  const kind=node('select','filter-select');
  kind.setAttribute('aria-label','选择问题性质');
  for(const [k,v] of [
    ['FACT','已知工程事实被纠正'],['REQUIREMENT','需求理解或验收要求变化'],
    ['SCOPE','当前版本交付范围变化'],['IMPLEMENT','仅实施存在缺陷']
  ]){
    const opt=node('option','',v);opt.value=k;kind.append(opt);
  }
  append(controls,select,kind);
  panel.append(controls);
  const results=node('div','');
  panel.append(results);
  function draw(){
    results.replaceChildren();
    const found=impactPreview(select.value);
    const expl={
      FACT:'先核对事实证据与适用范围；并不自动改变产品定义。',
      REQUIREMENT:'需要核对有效需求及设计输入，必要时按原流程重新审查。',
      SCOPE:'先审查版本范围变化、交付承诺和开发计划，不能无授权扩大范围。',
      IMPLEMENT:'优先核查实现缺陷及设计符合性，不自动回滚需求或 P0。'
    };
    results.append(node('div','note-box',expl[kind.value]));
    const all=found.direct.concat(found.downstream);
    if(!all.length){
      results.append(node('div','empty','目前没有登记直接关联的工作。这不是无影响的证明，应补充关系及证据。'));
      return;
    }
    const header=node('h3','', '已登记的可能受影响工作');
    results.append(header);
    const list=node('div','');
    for(const id of all){
      const w=work(id);
      const row=node('div','strip'),main=node('div','strip-main');
      append(main,node('div','strip-title',w.id+' · '+w.title),
        node('div','strip-desc',found.direct.includes(id)?'直接对应交付':'由已登记的任务依赖传递'));
      append(row,main,button('查看',()=>openWork(w.id),'button ghost small'));
      list.append(row);
    }
    results.append(list);
    results.append(node('p','section-note',
      '这里只显示显式登记的影响路径。需由工程总协调 AI 对照实际设计、代码和证据评估其他可能影响，并由负责人审查重大范围或基线变更。'));
  }
  select.addEventListener('change',draw);
  kind.addEventListener('change',draw);
  draw();
  sec.append(panel);
}

function renderDecisions(){
  heading('CONTROL & DECISIONS / 管理控制', '决策、问题与计划调整',
    '让需要你真正决定的事情显现出来；普通 AI 草稿和工作笔记不应作为日常待审批事项。');
  sourceBanner();
  const sec=part('当前需要处理的事项','决定必须说明依据和对交付范围、任务及计划的影响');
  for(const q of data().decisions){
    const box=node('article','card decision-card');
    append(box,chip(lab(q.status),tone(q.status)),node('h3','',q.title),
      node('p','',q.description));
    const acts=node('div','decision-actions');
    append(acts,node('span','fine','负责判断：'+q.owner),
      button('复制审查交接',()=>copy(decisionHandoff(q)),'button ghost small'));
    box.append(acts);sec.append(box);
  }
  renderChangePreview();
  const note=part('正式决定的边界');
  note.append(node('div','note-box',
    '当前重构版本不提供绕过正式审查的“批准”按钮。要批准需求、阶段 Gate、计划基线或任务执行，必须在正式审查机制中针对确切版本操作，不能用本地建议冒充。'));
}

function renderCollaboration(){
  heading('COLLABORATION / 协作与证据', 'ChatGPT、GitHub 与本地 Codex',
    '系统负责让你掌握任务和交接，不自动启动多 AI，也不假装读取了无法获取的本地执行情况。');
  sourceBanner();
  const c=data().collaboration;
  const sec=part('实际分工方式','按项目复杂度安排，不强制固定 Agent 数量');
  const grid=node('div','cols-3');
  for(const [label,value] of [
    ['工程总协调',c.coordinator],['专业方向研发',c.specialists],['集成验证及审查',c.verifier],
    ['本地执行',c.executor],['参与方式','负责人手动触发各 ChatGPT 对话或本地任务'],
    ['共享上下文','通过 GitHub 的准确提交、任务和证据交换']]){
    const box=node('div','card card-pad');
    append(box,node('div','caps',label),node('p','',value));
    grid.append(box);
  }
  sec.append(grid);
  const sec2=part('任务交接','复制任务合同，发送给相应 ChatGPT 或 Codex 对话');
  const worklist=node('div','card');
  for(const w of data().work.filter(x=>x.horizon==='NOW')){
    const row=node('div','strip');
    const t=node('div','strip-main');
    append(t,node('div','strip-title',w.title),node('div','strip-desc',w.owner));
    append(row,t,button('复制交接',()=>copy(taskHandoff(w)),'button ghost small'));
    worklist.append(row);
  }
  sec2.append(worklist);
  const sec3=part('可核实的来源','来源和实际回执不可由 AI 的一句话替代');
  const evidence=node('div','card card-pad');
  sourcesList(data().sources.map(s=>s.id),evidence);
  sec3.append(evidence);
  const receipts = snapshot.receipts || [];
  const section=part('本地 Codex 执行回执','仅显示已进入当前 Git 提交的记录，未提交文件不会冒充同步完成');
  if (!receipts.length) {
    section.append(node('div','empty','当前 Git HEAD 尚无已提交的 Codex 执行回执。没有记录不代表本地没有工作，也不能推断任何实体功能已验收。'));
  } else {
    const list=node('div','card');
    for (const r of [...receipts].reverse().slice(0,15)) {
      const line=node('div','strip'),details=node('div','strip-main');
      append(details,node('div','strip-title',(r.work_id||'未关联工作')+' · '+lab(r.outcome)),
        node('div','strip-desc',r.summary||'没有摘要'),
        node('div','fine','运行 HEAD：'+(r.actual_head_sha||'未知')+' · '+(r.reported_at||'')));
      line.append(details);
      line.append(chip('执行记录 ≠ 产品验收','amber'));
      list.append(line);
    }
    section.append(list);
  }
  $('#page').append(node('div','note-box','只有确切的 Git 提交、命令退出码、日志及可复核测试记录才能支持工程判断。回执 PASS 不自动等于集成验收、真实设备通过或负责人批准。'));
}

function openDelivery(id){
  const d=deliverable(id);if(!d)return;
  $('#drawerKicker').textContent='交付成果 · '+d.id;
  $('#drawerTitle').textContent=d.title;
  const body=$('#drawerContent');body.replaceChildren();
  const a=node('div','drawer-group');
  append(a,node('h3','','交付范围'),node('p','',d.description),
    node('h3','','成功条件'),node('p','',d.acceptance),
    metaRow('管理类别',lab(d.type)),metaRow('范围状态',lab(d.scope)));
  body.append(a);
  const s=node('div','drawer-group');
  s.append(node('h3','','对应开发工作'));
  const all=data().work.filter(w=>w.deliverable===d.id);
  if(!all.length)s.append(node('p','quiet','当前计划没有单独登记该交付子项的工作，不能推导已完成。'));
  else for(const w of all)s.append(button(w.id+' · '+w.title,()=>openWork(w.id),'button ghost small'));
  body.append(s);
  const ev=node('div','drawer-group');
  ev.append(node('h3','','依据与证据'));sourcesList(d.source_ids,ev);body.append(ev);
  if (!$('#drawer').open) $('#drawer').showModal();
}

function field(label,tag,value,choices){
  const div=node('div','form-field'),id='field-'+Math.random().toString(36).slice(2);
  const labEl=node('label','',label);labEl.htmlFor=id;
  const control=node(tag);control.id=id;
  if(tag==='select')for(const [v,t] of choices){
    const option=node('option','',t);option.value=v;control.append(option);
  }
  control.value=value||'';
  append(div,labEl,control);
  return {div,control};
}

function openWork(id){
  const w=work(id);if(!w)return;
  $('#drawerKicker').textContent='开发工作 · '+w.id;
  $('#drawerTitle').textContent=w.title;
  const body=$('#drawerContent');body.replaceChildren();
  const main=node('div','drawer-group');
  const facts=node('div','fact-list');
  append(facts,
    metaRow('工作性质',lab(w.kind)),
    metaRow('研发流程',w.phase),
    metaRow('所属交付',deliverable(w.deliverable)?.title||w.deliverable),
    metaRow('当前计划状态',lab(w.state)+'（候选计划）'),
    metaRow('预计负责人',w.owner),
    metaRow('优先级',lab(w.priority)),
    metaRow('直接依赖',w.depends.join('、')||'未登记'));
  append(main,node('h3','','已登记的工作信息'),facts,
    node('h3','','期望交付'),node('p','',w.output),
    node('h3','','退出条件'),node('p','',w.exit));
  if(w.blocking?.length){
    append(main,node('h3','','当前限制'),node('p','',w.blocking.join('；')));
  }
  body.append(main);

  const candidates=node('div','drawer-group');
  const notes=noteFor(w.id);
  append(candidates,node('h3','','提出计划修改建议'),
    node('p','quiet','这里不是直接修改正式任务和阶段。你的建议先保存在本地，必须经过核对及 GitHub 提交才能供其他对话使用。'));
  const owner=field('建议负责人','input',notes?.owner||w.owner);
  const state=field('建议状态','select',notes?.suggested_state||'NO_CHANGE',[
    ['NO_CHANGE','不改变状态'],['PLANNED','建议：计划中'],['IN_PROGRESS','建议：进行中'],
    ['BLOCKED','建议：阻塞'],['REPORTED_COMPLETE','建议：报告完成（仍需证据）']
  ]);
  const priority=field('建议优先级','select',notes?.suggested_priority||'NO_CHANGE',
    [['NO_CHANGE','不改变优先级'],['P0','最高'],['P1','高'],['P2','中'],['P3','低']]);
  const note=field('具体依据与调整理由','textarea',notes?.note||'');
  append(candidates,owner.div,state.div,priority.div,note.div);
  const warning=node('div','draft-warning',
    snapshot.local_stale?'本地存在基于旧 SHA 的建议。请先导出并检查，禁止覆盖。':
    '只写入隔离的本地建议文件，不会批准设计、更新 GitHub 或自动运行 Codex。');
  candidates.append(warning);
  const save=button('保存本地建议',async()=>{
    save.disabled=true;
    try{
      const resp=await fetch('/api/work-note',{
        method:'POST',headers:{'Content-Type':'application/json','X-CSRF-Token':snapshot.csrf},
        body:JSON.stringify({base_sha:source().sha,work_id:w.id,
          owner:owner.control.value,note:note.control.value,
          suggested_state:state.control.value,suggested_priority:priority.control.value})
      });
      const result=await resp.json();
      if(!resp.ok)throw Error(result.error||'无法保存');
      await reload();toast('本地候选建议已保存，尚未同步 GitHub');
      $('#drawer').close();
      render();
    }catch(e){toast('未保存：'+e.message)}
    finally{save.disabled=false}
  });
  save.disabled=!!snapshot.local_stale;
  const copyTask=button('复制 ChatGPT / Codex 交接指令',()=>copy(taskHandoff(w)),'button ghost');
  const actions=node('div','actions');append(actions,save,copyTask);candidates.append(actions);
  body.append(candidates);
  const ev=node('div','drawer-group');
  ev.append(node('h3','','相关正式来源及候选资料'));
  sourcesList(w.source_ids,ev);body.append(ev);
  if (!$('#drawer').open) $('#drawer').showModal();
}

function render(){
  const page=$('#page');page.replaceChildren();
  if(route==='home')renderHome();
  else if(route==='delivery')renderDelivery();
  else if(route==='work')renderWork();
  else if(route==='process')renderProcess();
  else if(route==='decisions')renderDecisions();
  else renderCollaboration();
}

async function reload(){
  const r=await fetch('/api/project',{cache:'no-store'});
  const value=await r.json();
  if(!r.ok)throw Error(value.error||'项目数据读取失败');
  snapshot=value;
  $('#sideProject').textContent=data().project.name;
  $('#sideRelease').textContent=data().project.release;
  $('#sourceIndicator').textContent='Git HEAD · '+source().sha.slice(0,9);
  $('#sourceIndicator').title='当前本地 Git HEAD；不代表已经与 GitHub 远端同步：'+source().sha;
}

async function downloadExport(){
  const r=await fetch('/api/export',{cache:'no-store'});
  if(!r.ok){toast('无法导出本地建议');return}
  const blob=await r.blob(),url=URL.createObjectURL(blob),a=node('a');
  a.href=url;a.download='management-suggestions.json';a.click();
  setTimeout(()=>URL.revokeObjectURL(url),1500);
  toast('本地建议已导出；尚未自动提交 GitHub');
}

document.querySelectorAll('[data-route]').forEach(n=>
  n.addEventListener('click',()=>navTo(n.dataset.route)));
$('#exportButton').addEventListener('click',downloadExport);
$('#drawerClose').addEventListener('click',()=>$('#drawer').close());
$('#drawer').addEventListener('click',e=>{if(e.target===$('#drawer'))$('#drawer').close()});
window.addEventListener('DOMContentLoaded',async()=>{
  try{await reload();navTo('home')}
  catch(e){
    $('#page').replaceChildren(node('h1','','当前项目无法加载'),
      node('p','quiet',e.message),
      node('div','note-box','请确认本地 Codex 已切换至新的重构分支，并使用 Git 提交中的项目资料启动工作台。本程序不会用旧 SQLite 快照填充页面。'));
  }
});
