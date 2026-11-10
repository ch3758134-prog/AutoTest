/* GitHub execution console. All items are real REST snapshots or locally
   approved, revision-pinned work packages. This code does not run an AI agent. */
'use strict';
let executionBusy=false;
const execPill=(label)=>el('span','badge',label);
function safeGitLink(url,label){
  const a=el('a','link',label);a.target='_blank';a.rel='noopener noreferrer';
  if(typeof url==='string'&&/^https:\/\/github\.com\/[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+\/(issues|pull|actions)\//.test(url))a.href=url;
  else a.removeAttribute('href');
  return a;
}
function executionText(label,value){let n=el('div','meta-cell');add(n,el('label','',label),el('div','',value??'未知'));return n;}
function actionModal(title,message,inputs,run,label='确认'){
  showModal(title,(body,footer)=>{
    if(message)body.append(note(message));
    const fields={};for(const spec of inputs) {
      if(spec.options)fields[spec.key]=selectField(body,spec.label,spec.options,spec.value||'');
      else fields[spec.key]=field(body,spec.label,spec.value||'',spec.multiline?'textarea':'input');
    }
    modalActions(footer,()=>run(Object.fromEntries(Object.entries(fields).map(([k,v])=>[k,v.value]))),label);
  });
}
async function execChange(path,args,label){
 const result=await req('/api/studio/execution/'+path,args);
 closeModal();toast(label||'已保存');
 await refresh(false);
 if(state.view==='execution')await renderExecutionView();
 return result;
}
async function renderExecutionView(){
  clean(root);
  heading('GitHub · 研发执行中心',
    '直接读取仓库 Issue、Draft PR、精确 HEAD 和 GitHub Actions；把已批准的工程详细设计转成可供纯编码 AI 领取的工作包。');
  root.append(note('GitHub 是执行事实来源；工程设计审批来自本项目。Issue 存在、PR 合并、CI 绿色和现场验收是不同的状态。任务接单需要实际领取，不会把页面访问当成 AI 已开工。'));
  if(!state.project){root.append(el('p','','请先创建或选择工程项目。'));return;}
  const holder=el('div','stack');holder.append(el('p','muted','正在读取本地仓库关联与同步记录……'));root.append(holder);
  let data;
  try{data=await req('/api/studio/execution?project='+qs(state.project));}
  catch(e){clean(holder);holder.append(el('div','reason',e.message));return;}
  if(state.view!=='execution')return;
  clean(holder);
  const config=el('div','panel');
  const line=el('div','sectionline'),name=el('h2','',data.configured?'GitHub 仓库：'+data.repository:'尚未绑定 GitHub 仓库');
  line.append(name);
  line.append(button(data.configured?'切换/配置仓库':'连接 GitHub 仓库','outline',()=>executionRepoModal(data)));
  config.append(line);
  const action=el('div','buttonrow');
  if(data.configured){
    action.append(button('从 GitHub 获取真实状态','primary',()=>executionSync()));
    action.append(button('将工程项目归档到 Git 分支','outline',()=>executionArchiveProject()));
    config.append(el('p','muted','最近同步：'+(data.fetched_at||'从未同步')+' · 数据不自动视作当前状态；刷新会通过 GitHub API 获取新事实。'));
  }else config.append(el('p','muted','请先填写真实的 GitHub owner/repository，例如 ch3758134-prog/AutoTest。公开仓库可直接读取，私有仓库需要在服务进程配置 GITHUB_TOKEN。'));
  if(data.sync_error)config.append(el('div','reason','上次同步失败：'+data.sync_error+'；已有快照不应认作实时状态。'));
  if(data.snapshot?.truncated)config.append(el('div','reason','GitHub 查询只返回前 100 项，存在分页截断，不可据此宣称项目清单完整。'));
  if(data.snapshot)config.append(el('div','tiny','数据源：'+data.snapshot.source+' · '+data.snapshot.fetched_at));
  config.append(action);holder.append(config);
  if(!data.snapshot){
    const blank=el('div','blank');add(blank,el('h2','','尚无 GitHub 项目执行快照'),
      el('p','','连接仓库并读取 Issue、PR 和 CI 后，这里会显示真实项目情况。当前本地数据库的对象数量不代表 GitHub 工作量。'));
    holder.append(blank);
    return;
  }
  const snap=data.snapshot;
  const numbers=el('div','fourcol');const openIssues=snap.issues.filter(x=>x.state==='open');
  const openPrs=snap.pull_requests.filter(x=>x.state==='open');
  add(numbers,
    stat(openIssues.length,'公开待处理 Issue','不是当前计划完成度'),
    stat(openPrs.length,'未合并 PR','含 Draft 候选'),
    stat(openPrs.filter(x=>x.draft).length,'Draft PR','尚未通过审查'),
    stat(data.tasks.filter(x=>x.effective_state==='CLAIMED').length,'已实际领取的 AI 任务','仅本地工作队列领取记录')
  );holder.append(numbers);
  const advice=el('div','panel');
  advice.append(el('h2','','项目管理判断 · 基于当前已知事实'));
  const suggestions=[];
  if(!data.tasks.length)suggestions.push('GitHub 已有 Issue/PR，但尚无与批准设计关联的编码任务。需要先建立工作包映射，不能自动认定这些 Issue 已达到 CODE_READY。');
  for(const task of data.tasks)if(task.effective_state==='BLOCKED_BY_CHANGED_DESIGN')suggestions.push(task.title+' 的设计修订或代码基线已变化，已停止向编码 AI 发放旧上下文。');
  if(openPrs.some(p=>p.ci?.conclusion==='failure'))suggestions.push('存在最新匹配 HEAD 的 GitHub Actions 失败 PR，需要检查原始 CI 日志；不可将其标记为已验证。');
  if(openPrs.some(p=>p.draft))suggestions.push('仍有 Draft PR；需要与工程工作包、测试证据和具体负责人审批建立关系，不能只按提交数量推进。');
  if(!suggestions.length)suggestions.push('当前快照没有触发这些规则式提醒；不代表所有产品要求已经满足。');
  suggestions.slice(0,7).forEach(x=>{let p=el('p','context','• '+x);p.style.marginBottom='9px';advice.append(p)});
  advice.append(el('div','tiny','本区是确定性的项目审查提示，不是已经启动的自主 AI 项目经理。'));
  holder.append(advice);
  const grid=el('div','twocol');
  let issuePanel=el('div','panel'),prPanel=el('div','panel');
  const head1=el('div','sectionline');add(head1,el('h2','','GitHub Issues · 真实任务和研究事项'));
  issuePanel.append(head1);
  for(const issue of snap.issues.sort((a,b)=>b.number-a.number).slice(0,35)){
    let item=el('div','item-row'),left=el('div'),top=el('div','buttonrow');
    add(top,safeGitLink(issue.url,'#'+issue.number+' · '+issue.title),badge(issue.state));
    add(left,top,el('div','rowdesc',issue.labels.length?issue.labels.join(' / '):'未设置分类标签'));
    item.append(left);
    const bound=data.tasks.some(t=>t.issue_number===issue.number);
    if(bound)item.append(badge('已绑定工程工作包'));
    else if(issue.state==='open')item.append(button('创建编码工作包','outline',()=>executionImportIssue(issue)));
    issuePanel.append(item);
  }
  if(!snap.issues.length)issuePanel.append(el('p','muted','此快照没有 Issue。'));
  grid.append(issuePanel);
  const head2=el('div','sectionline');add(head2,el('h2','','Pull Requests · 交付候选'));prPanel.append(head2);
  for(const pr of snap.pull_requests.sort((a,b)=>b.number-a.number).slice(0,35)){
    let row=el('div','item-row'),content=el('div');
    const top=el('div','buttonrow');
    add(top,safeGitLink(pr.url,'#'+pr.number+' · '+pr.title),
      badge(pr.draft?'DRAFT':pr.merged_at?'MERGED':pr.state));
    add(content,top,el('div','rowdesc',
      'HEAD '+(pr.head_sha?.slice(0,12)||'未知')+
      ' · '+(pr.ci?'CI: '+pr.ci.status+'/'+(pr.ci.conclusion||'未结束'):'未找到匹配此 HEAD 的 Actions 记录')));
    row.append(content);prPanel.append(row);
  }
  if(!snap.pull_requests.length)prPanel.append(el('p','muted','此快照没有 PR。'));
  grid.append(prPanel);holder.append(grid);
  const work=el('div','panel');const header=el('div','sectionline');
  add(header,el('h2','','AI 实施工作包 · 真实可领取队列'),
    button('进入详细设计','outline',()=>navigate('engineering')));
  work.append(header);
  work.append(el('p','muted','任务从 GitHub Issue 和本地批准设计共同形成。研究/系统架构/详细设计仍在审查的工作，禁止被编码 AI 自动领取。'));
  if(!data.tasks.length&&!data.unlinked_work_packages.length)work.append(el('div','info','尚无工作包。可以从 GitHub 的编码 Issue 创建一份待设计任务，然后在“需求与架构”中完成软件详细设计并批准。'));
  for(const task of data.tasks){
    const card=el('div','relation'),left=el('div','stack');left.style.gap='3px';left.style.flex='1';
    const line=el('div','buttonrow');add(line,el('strong','',task.title),badge(task.effective_state));
    left.append(line);
    left.append(el('div','tiny','Issue #'+task.issue_number+' · 角色 '+task.agent_role+
      ' · '+(task.claim_actor?'由 '+task.claim_actor+' 领取':'尚未领取')+
      (task.pr_number?' · PR #'+task.pr_number:'')));
    if(!task.readiness.ready)left.append(el('div','rowdesc','编码尚未准入：'+task.readiness.reasons.slice(0,2).join('；')));
    card.append(left);
    const btns=el('div','buttonrow');
    if(task.state==='PLANNING'||task.state==='REVOKED'){
      btns.append(button('设置角色/Issue','outline',()=>executionBind(task,data)));
      if(task.readiness.ready)btns.append(button('审查后派发','primary',()=>executionDispatch(task)));
    }
    if(task.state==='DISPATCHED'){
      btns.append(button('归档到 Git 工作树','outline',()=>executionPublish(task)));
      btns.append(button('撤回','outline',()=>executionRevoke(task)));
    }
    if(task.state==='CLAIMED'){
      btns.append(button('关联 Draft PR','outline',()=>executionLinkPr(task,snap)));
      btns.append(button('归档任务合同','outline',()=>executionPublish(task)));
      btns.append(button('撤回任务','outline',()=>executionRevoke(task)));
    }
    card.append(btns);work.append(card);
  }
  for(const entry of data.unlinked_work_packages){
    let row=el('div','relation'),left=el('div');left.style.flex='1';
    add(left,el('strong','',entry.title),el('div','tiny','本地工作包 · 尚未连接 GitHub Issue'));
    row.append(left,button('关联 GitHub Issue','outline',()=>executionBind({object_id:entry.id,title:entry.title},data)));work.append(row);
  }
  holder.append(work);
  const tips=el('div','panel');
  tips.append(el('h2','','AI 接单方式与 GitHub 归档'));
  tips.append(el('p','context','已批准的编码任务派发后，本地 Codex 使用专用 CLI 查询并领取；服务端原子登记领取者、精确设计修订及代码 SHA。然后可以将只读任务合同导出到仓库 docs/engineering-control/tasks/，由 Git 提交和 Draft PR 交付。'));
  tips.append(el('pre','readout','py -3 tools/lifecycle/agent_tasks.py --server http://127.0.0.1:8766 --project '+state.project+' --role DEVICE --worker CODEX-DEVICE'));
  tips.append(el('div','tiny','命令中的 DEVICE 仅为示例角色；AI 不会因为工作包存在就自动启动 ChatGPT 对话。本阶段没有 GitHub Issue 自动分配/远程代理运行器。'));
  holder.append(tips);
}
function executionRepoModal(info){
  actionModal('GitHub 仓库配置','只接受 owner/name 的 GitHub 仓库。配置后同步是真实 REST API 读取，不会自动修改 GitHub，也不会向网页保存令牌。',
    [{key:'repository',label:'GitHub repository',value:info.repository||'ch3758134-prog/AutoTest'}],
    p=>execChange('configure',{project_id:state.project,repository:p.repository},'已绑定仓库，需要点击同步获取实际信息'),'保存仓库配置');
}
async function executionSync(){
 if(executionBusy){toast('已有同步正在进行');return;}
 executionBusy=true;toast('正在读取 GitHub Issue、PR 和 Actions 状态…');
 try{await execChange('refresh',{project_id:state.project},'已更新真实 GitHub 快照');}
 catch(e){toast('同步失败：'+e.message);if(state.view==='execution')renderExecutionView();}
 finally{executionBusy=false;}
}
function executionImportIssue(issue){
 if(/\[RESEARCH\]/i.test(issue.title)){
   toast('该 Issue 标记为技术研究，不应自动转成编码工作包。请在“技术预研与实验”中管理。');return;
 }
 actionModal('从 GitHub Issue 创建编码工作包',
   '仅导入 Issue 内容作为待设计工作包，不继承“已批准”状态；必须补充详细设计、代码基线与测试合同后才能派发。',
   [],async()=>{
     await execChange('import-issue',{project_id:state.project,issue_number:issue.number},
                      '已导入待设计编码工作包，请前往实施与验证补足 CODE_READY 条件');
   },'创建待设计工作包');
}
function executionBind(task,info){
 const issueOptions=(info.snapshot?.issues||[]).filter(i=>i.state==='open')
   .map(i=>[String(i.number),'#'+i.number+' · '+i.title]);
 if(!issueOptions.length){toast('请先同步 GitHub Issue 列表');return;}
 actionModal('关联工作包和 GitHub Issue',
  '绑定不等于批准任务。角色只决定哪个编码 AI 能领取此任务；公共接口、架构和验收标准仍由设计侧确认。',
  [{key:'issue_number',label:'对应 GitHub Issue',options:issueOptions,value:String(task.issue_number||issueOptions[0][0])},
   {key:'agent_role',label:'编码专业角色',options:[
     ['UNASSIGNED','暂不分配'],['DEVICE','DEVICE'],['UI','UI'],['TEST','TEST'],
     ['PROJECT','PROJECT'],['WAVE','WAVE'],['UDS','UDS'],['SCAN','SCAN'],
     ['RECORD','RECORD'],['INTEGRATION','INTEGRATION'],['HOST','HOST']],
     value:task.agent_role||'UNASSIGNED'}],
  p=>execChange('bind',{object_id:task.object_id,issue_number:Number(p.issue_number),agent_role:p.agent_role},
    'Issue 已绑定，只有通过编码准入后才能派发'),'确认关联');
}
function executionDispatch(task){
 actionModal('批准派发纯编码任务',
  '请先检查当前设计、输入修订、责任范围和 GitHub Issue。派发只建立真实可领取任务，不意味着 AI 已接单或 PR 已创建。',
  [{key:'actor',label:'派发批准人',value:'项目负责人'},
   {key:'note',label:'本次派发依据和适用条件',multiline:true}],
  p=>execChange('dispatch',{object_id:task.object_id,actor:p.actor,note:p.note},
     '已派发；待对应编码 AI 通过本地 CLI 真正领取'),'确认派发');
}
function executionRevoke(task){
 actionModal('撤回实施任务','撤回将取消后续领取资格，保留历史记录；不删除 GitHub Issue 或 PR。',
  [{key:'actor',label:'发起人',value:'项目负责人'},
   {key:'reason',label:'撤回理由',multiline:true}],
  p=>execChange('revoke',{object_id:task.object_id,actor:p.actor,reason:p.reason},
    '已撤回，必须重新通过准入才能再次派发'),'确认撤回');
}
function executionLinkPr(task,snapshot){
 const opts=(snapshot.pull_requests||[]).map(p=>[String(p.number),'#'+p.number+' · '+p.title]);
 if(!opts.length){toast('没有可绑定的 PR');return;}
 actionModal('绑定实际 GitHub PR',
   '绑定由 GitHub 返回的精确 PR HEAD 和 CI 事实；不得自动把绿色 CI 当成实物通过或合并许可。',
   [{key:'pr',label:'交付 PR',options:opts,value:String(task.pr_number||opts[0][0])}],
   p=>execChange('link-pr',{object_id:task.object_id,pr_number:Number(p.pr)},'PR 已关联'),'确认绑定');
}
async function executionPublish(task){
 try{
   const r=await req('/api/studio/execution/publish',{object_id:task.object_id});
   const paths=r.files?.join('，')||'';
   toast('已写入本地专业 Git 分支：'+paths+'。尚未提交和推送，需要受控 Git 交付。');
   await renderExecutionView();
 }catch(e){toast('归档失败：'+e.message);}
}

function executionArchiveProject(){
 actionModal('生成项目 Git 工程档案',
   '本操作会把当前项目工程需求、架构、研究、设计修订及任务领取记录写入当前非 main 的 Git 工作树。请先确认内容不含密钥、身份数据或未经脱敏的真实现场日志。文件不会自动 Commit/Push，也不会覆盖正式 SAEF。',
   [{key:'actor',label:'确认导出人',value:'项目负责人'},
    {key:'note',label:'确认信息已检查，后续受控审核提交',value:'已知这是本地 Git 工作树归档，不代表远端已入库'}],
   async fields=>{
     if(!fields.actor.trim()||!fields.note.trim())throw Error('归档确认说明不得为空');
     const result=await req('/api/studio/execution/archive-project',{project_id:state.project});
     closeModal();toast('本地 Git 工程档案已写入：'+result.path+'。尚未 Commit/Push，需要人工审查后按既定 PR 交付。');
     if(state.view==='execution')await renderExecutionView();
   },'生成待审查的 Git 归档');
}
