/* Evidence-led technical investigation UI. Uses the host workbench's own
   project, modal, request and navigation helpers. All DOM text is escaped. */
'use strict';
let selectedStudyId = null;

function studyStatus(value) {
  return value === 'DECIDED' ? '技术路线已批准' : '技术问题待研究';
}
function studyLevel(value) {
  return ({ANALYSIS:'分析与资料论证', PROTOTYPE:'独立原型/软件实测', REAL:'真实环境实测'})[value] || value;
}
function studyResult(value) {
  return ({PASS:'满足验收', FAIL:'未满足验收', LIMITED:'受限', INCONCLUSIVE:'证据不足'})[value] || value;
}
function studyLine(label, content) {
  const row=el('div','meta-cell');
  add(row,el('label','',label),el('div','context',content||'未填写'));
  return row;
}
function studyTopic(node, title) {
  const row=el('div','sectionline');row.style.marginTop='17px';
  row.append(el('h2','',title));node.append(row);return row;
}
async function researchRefresh() {
  await refresh(false);
  if(state.view==='research') renderResearchView();
}
function researchAction(path, args, message) {
  return req(path,args).then(async result=>{
    closeModal();
    await researchRefresh();
    if(message)toast(message);
    return result;
  });
}
function researchModal(title, builder, submitText, submit) {
  showModal(title,(body,footer)=>{
    const fields=builder(body);
    modalActions(footer,()=>submit(fields),submitText);
  });
}
async function renderResearchView() {
  clean(root);
  heading('技术预研与可行性验证',
    '当系统需求已经明确，但实现方案、软件结构或设备能力仍不确定时，在正式架构批准前研究和实测；研究结论需要证据、人工审查和版本身份。');
  const actions=el('div','buttonrow');
  actions.append(button('+ 创建技术预研','primary',createStudyModal));
  actions.append(button('查看需求与架构','outline',()=>navigate('engineering')));
  root.append(actions);
  root.append(note('本工作区记录研究和试验，但不会替你执行设备操作、生成虚构测试结果或自动批准架构。纯编码 AI 不接受技术选型与研究任务。'));
  const work=el('div','work');work.style.marginTop='17px';
  const list=el('div','index'),detail=el('div','detail');
  work.append(list,detail);root.append(work);
  let studies;
  try{studies=await req('/api/studio/research?project='+qs(state.project));}
  catch(error){detail.append(el('div','reason',error.message));return;}
  if(state.view!=='research')return;
  if(!studies.some(x=>x.id===selectedStudyId))selectedStudyId=studies[0]?.id||null;
  const listHead=el('div','indexhead');
  listHead.append(el('strong','','待研究及已决定的技术问题'));
  listHead.append(el('div','tiny',studies.length+' 项 · 不等同于产品需求数'));
  list.append(listHead);
  for(const study of studies) {
    const b=el('button','indexitem'+(study.id===selectedStudyId?' active':''));
    const content=el('div');
    add(content,el('div','rowtitle',study.title),el('div','tiny',study.minimum_level+' · '+
      study.option_count+' 方案 / '+study.experiment_count+' 实验'));
    add(b,content,badge(study.status));
    b.onclick=()=>{selectedStudyId=study.id;renderResearchView();};
    list.append(b);
  }
  if(!studies.length){
    const body=el('div','blank');
    body.style.margin='20px';
    add(body,el('h2','','尚无技术预研事项'),
      el('p','','从明确的需求或设计中的未知问题开始。一个预研只回答一个可验证的技术问题，不要把整个产品立项写成一项实验。'),
      button('+ 创建第一项技术预研','primary',createStudyModal));
    detail.append(body);return;
  }
  try{
    const data=await req('/api/studio/research/detail?id='+qs(selectedStudyId));
    if(state.view!=='research'||selectedStudyId!==data.study.id)return;
    await renderStudyDetail(detail,data);
  }catch(err){detail.append(el('div','reason',err.message));}
}
async function renderStudyDetail(target,data){
  clean(target);
  const s=data.study, head=el('div','detailhead');
  const meta=el('div','buttonrow');
  add(meta,badge(s.status),badge(studyLevel(s.minimum_level)),el('span','tiny','研究修订 r'+s.revision));
  add(head,meta,el('h2','',s.title));
  head.append(el('div','context',s.question));
  const buttons=el('div','buttonrow');buttons.style.marginTop='13px';
  if(s.status==='OPEN'){
    add(buttons,
      button('+ 候选方案','outline',()=>createStudyOptionModal(s)),
      button('+ 实验计划','outline',()=>createExperimentModal(data)));
    if(data.options.length)buttons.append(button('技术选型评审','primary',()=>studyDecisionModal(data)));
  }else{
    add(buttons,button('把技术结论纳入架构','primary',()=>attachStudyModal(data)),
      button('重新开启研究','outline',()=>reopenStudyModal(s)));
  }
  head.append(buttons);target.append(head);
  const body=el('div','detailbody');
  const cols=el('div','meta-table');
  cols.append(studyLine('技术问题来源需求',s.requirement_id?itemName(s.requirement_id):'未直接指定来源'));
  cols.append(studyLine('研究证据最低要求',studyLevel(s.minimum_level)));
  cols.append(studyLine('技术约束与不确定性',s.constraints||'未明确约束'));
  cols.append(studyLine('正式架构准入',s.status==='DECIDED'?
    '已决定；绑定准确修订后可进入设计审查':'未通过；关联架构暂不得批准'));
  body.append(cols);
  if(data.decisions.length){
    const decision=data.decisions[0];
    const box=el('div','info');
    add(box,el('strong','','技术选型记录：'+
      (data.options.find(o=>o.id===decision.option_id)?.title||'未知方案')+
      (decision.state==='CURRENT'?'（当前）':'（已被后续研究替代）')),
      el('div','context',decision.rationale),
      el('div','tiny','审批人 '+decision.approver+' · '+decision.decided_at+
      ' · 审核证据 '+decision.reviewed_evidence.length+' 项'));
    if(decision.restrictions)box.append(el('div','context','适用限制：'+decision.restrictions));
    body.append(box);
  }
  studyTopic(body,'候选实现方案与比较');
  if(!data.options.length)body.append(el('p','muted','尚未记录候选方案。研究 AI 可以提出方案，但不能把未经实测的设想直接写成正式软件架构。'));
  for(const opt of data.options){
    const panel=el('div','panel');panel.style.marginBottom='11px';
    const title=el('div','buttonrow');
    add(title,el('h3','',opt.title),s.selected_option===opt.id?badge('已选定方案'):badge('候选'));
    panel.append(title);
    const grid=el('div','meta-table');
    add(grid,studyLine('实现思路',opt.approach),studyLine('代价、风险、取舍',opt.tradeoffs));
    panel.append(grid);
    if(opt.assumptions)panel.append(el('p','context','前提/假设：'+opt.assumptions));
    body.append(panel);
  }
  studyTopic(body,'实验、原始证据与审查');
  if(!data.experiments.length)body.append(el('p','muted','没有实验。真实设备相关的架构选型不能仅凭 AI 的文字研究结论放行。'));
  for(const exp of data.experiments){
    const panel=el('div','panel');panel.style.marginBottom='12px';
    const opt=data.options.find(x=>x.id===exp.option_id);
    const title=el('div','buttonrow');
    add(title,el('h3','',exp.title),
      badge(studyLevel(exp.level)),badge(exp.status==='AUTHORIZED'?'实验已授权':'待授权'));
    panel.append(title);
    panel.append(el('div','tiny','对应方案：'+(opt?.title||'未知')+(exp.required?' · 选型必须满足':' · 辅助验证')));
    const metadata=el('div','meta-table');
    add(metadata,studyLine('操作/实验方法',exp.method),
      studyLine('可判定的成功标准',exp.success_criteria),
      studyLine('实验环境及设备',exp.environment),
      studyLine('实验授权记录',exp.authorized_by?
       exp.authorized_by+' / '+exp.authorized_at:'尚未授权'));
    panel.append(metadata);
    if(s.status==='OPEN' && exp.status==='PLANNED')
      panel.append(button('审核并授权实验','outline',()=>authorizeExperimentModal(exp)));
    if(s.status==='OPEN' && exp.status==='AUTHORIZED')
      panel.append(button('登记实际实验结果','outline',()=>recordExperimentModal(exp)));
    for(const result of exp.results){
      const evidence=el('div','detailsection');
      const line=el('div','sectionline');
      add(line,el('strong','','实验报告 · '+studyResult(result.outcome)),
        result.reviewed_by?badge('证据已审核'):badge('待核实证据'));
      evidence.append(line);
      evidence.append(el('p','context',result.observations));
      const grid=el('div','meta-table');
      add(grid,studyLine('原始证据路径/编号',result.raw_reference),
        studyLine('适用配置',result.configuration),
        studyLine('软件/固件版本',result.source_revision),
        studyLine('执行者与时间',result.performed_by+' · '+result.performed_at));
      evidence.append(grid);
      if(result.reviewed_by)evidence.append(el('div','tiny',
        '审核：'+result.reviewed_by+' · '+result.reviewed_at+' · '+result.review_note));
      else if(s.status==='OPEN')evidence.append(button('审核这份实验报告','outline',
        ()=>reviewExperimentModal(result)));
      panel.append(evidence);
    }
    body.append(panel);
  }
  body.append(note('一项实验的“PASS”只针对记录的环境与配置。此工具不会验证设备确实被操作或日志未经篡改；只有审查过且符合研究证据等级的实验，才能支撑技术方案决定。'));
  if(s.status==='DECIDED'){
    const loc=el('div','detailsection');
    studyTopic(loc,'后续设计与修订');
    loc.append(el('p','context','现在可将选型结论关联到具体的系统架构、软件架构或接口详细设计，并锁定这次研究的修订。当技术研究重新打开时，受影响的架构编码准入将自动拒绝继续使用旧结论。'));
    loc.append(button('关联到工程设计','primary',()=>attachStudyModal(data)));
    body.append(loc);
  }
  target.append(body);
}
function createStudyModal(){
  researchModal('新增技术预研问题',body=>{
    body.append(note('先明确技术未知、适用需求与验证等级。不要把研发中已确定的技术再要求重复研究。'));
    const title=field(body,'研究问题名称');
    const question=field(body,'需要回答的技术问题','','textarea');
    const constraints=field(body,'已有工程约束/不得改变的边界','','textarea');
    const requirements=[['','可先不关联']].concat(state.data.objects.filter(x=>x.kind==='requirement').map(x=>[x.id,x.title]));
    const requirement=selectField(body,'来源系统需求',requirements);
    const minimum=selectField(body,'准入要求的最低实验等级',[
      ['ANALYSIS','书面分析与审查'],['PROTOTYPE','可复现实验原型'],['REAL','真实设备/环境验证']
    ],'PROTOTYPE');
    return {title,question,constraints,requirement,minimum};
  },'建立技术预研',async x=>{
    const ans=await req('/api/studio/research/create',{
      project_id:state.project,title:x.title.value,question:x.question.value,
      constraints:x.constraints.value,requirement_id:x.requirement.value,
      minimum_level:x.minimum.value});
    selectedStudyId=ans.study.id;
    closeModal();await researchRefresh();toast('已登记技术未知问题');
  });
}
function createStudyOptionModal(study){
  researchModal('技术候选方案 · '+study.title,body=>{
    const title=field(body,'方案名称');
    const approach=field(body,'技术实现路径','','textarea');
    const tradeoffs=field(body,'优缺点、资源消耗、风险与适用条件','','textarea');
    const assumptions=field(body,'未证实的假设','','textarea');
    return {title,approach,tradeoffs,assumptions};
  },'保存候选方案',x=>researchAction('/api/studio/research/option',{
    study_id:study.id,title:x.title.value,approach:x.approach.value,
    tradeoffs:x.tradeoffs.value,assumptions:x.assumptions.value},'已新增候选方案'));
}
function createExperimentModal(data){
  if(!data.options.length){toast('请先记录至少一个候选技术方案');return;}
  researchModal('设计可验证的技术实验',body=>{
    body.append(note('实验应定义可观察结果和成功条件。真实硬件实验在本工具里只登记计划，执行必须由独立的安全授权流程控制。'));
    const option=selectField(body,'对应技术方案',data.options.map(o=>[o.id,o.title]));
    const title=field(body,'实验名称');
    const method=field(body,'实验方法、输入、步骤及原始记录要求','','textarea');
    const criteria=field(body,'量化或可判定的成功标准','','textarea');
    const env=field(body,'设备、软件/固件、工具链及试验环境','','textarea');
    const level=selectField(body,'实验级别',[
      ['ANALYSIS','分析'],['PROTOTYPE','独立原型/软件实测'],
      ['REAL','真实设备/现场验证']],'REAL');
    const required=selectField(body,'是否为选型必需的实验',[
      ['true','是，必须通过'],['false','否，补充证据']],'true');
    return {option,title,method,criteria,env,level,required};
  },'建立实验计划',x=>researchAction('/api/studio/research/experiment',{
    study_id:data.study.id,option_id:x.option.value,title:x.title.value,
    method:x.method.value,success_criteria:x.criteria.value,
    environment:x.env.value,level:x.level.value,required:x.required.value==='true'
  },'实验计划已登记；执行前仍须授权'));
}
function authorizeExperimentModal(experiment){
  researchModal('实验授权 · '+experiment.title,body=>{
    body.append(note('确认实验对象、方法和环境已符合适用的硬件与人员安全要求。本操作不发送任何设备命令。'));
    const actor=field(body,'实验授权人','项目负责人');
    const noteField=field(body,'批准理由、试验条件与限制','','textarea');
    return {actor,noteField};
  },'确认授权',x=>researchAction('/api/studio/research/authorize',{
    experiment_id:experiment.id,actor:x.actor.value,note:x.noteField.value
  },'实验计划已授权'));
}
function recordExperimentModal(experiment){
  researchModal('录入实验实测结果 · '+experiment.title,body=>{
    body.append(note('如实记录观察数据。无需上传源码到 AI；原始记录必须有可恢复的本地文件路径或索引，以及对应软件/固件/设备版本。'));
    const outcome=selectField(body,'实际结果',[
      ['PASS','符合已定义的验收条件'],
      ['FAIL','未达到要求'],['LIMITED','受限可行'],['INCONCLUSIVE','证据不足']
    ],'INCONCLUSIVE');
    const obs=field(body,'实测观察与偏差','','textarea');
    const raw=field(body,'原始日志/测试报告/示波记录路径或证据 ID');
    const digest=field(body,'证据 SHA-256（可选，用于核对原始文件）');
    const config=field(body,'适用硬件、配置、软件及固件身份');
    const source=field(body,'实验源码/配置版本或提交');
    const performed=field(body,'现场实验执行者');
    return {outcome,obs,raw,digest,config,source,performed};
  },'登记原始结果',x=>researchAction('/api/studio/research/result',{
    experiment_id:experiment.id,outcome:x.outcome.value,observations:x.obs.value,
    raw_reference:x.raw.value,evidence_sha256:x.digest.value,
    configuration:x.config.value,source_revision:x.source.value,
    performed_by:x.performed.value
  },'实验结果已登记，仍待负责人审查'));
}
function reviewExperimentModal(result){
  researchModal('审查实验原始证据',body=>{
    const actor=field(body,'证据审查人','项目负责人');
    const noteField=field(body,'复核了哪些记录、证据适用范围与结论','','textarea');
    return {actor,noteField};
  },'确认审查',x=>researchAction('/api/studio/research/review',{
    result_id:result.id,actor:x.actor.value,note:x.noteField.value
  },'证据已登记人工审核结果'));
}
function studyDecisionModal(data){
  researchModal('技术选型与架构准入',body=>{
    body.append(note('先验证每个必需实验和研究所要求的成熟度；研究结果只对具体记录的设备、软件版本和环境有效。'));
    const option=selectField(body,'拟采用的方案',data.options.map(x=>[x.id,x.title]));
    const conditions=el('div','info');body.append(conditions);
    async function updateGate(){
      conditions.textContent='正在检查选型证据…';
      try{
        const assessment=await req('/api/studio/research/evaluate?study='+qs(data.study.id)+'&option='+qs(option.value));
        conditions.textContent=assessment.ready?'证据准入通过：'+assessment.accepted_evidence.length+' 项已审查且满足等级的实验记录':
          '尚不能批准：'+assessment.reasons.join('；');
      }catch(e){conditions.textContent=e.message;}
    }
    option.onchange=updateGate;updateGate();
    const approver=field(body,'架构/技术决定的批准人','项目负责人');
    const rationale=field(body,'技术选型理由和其他方案的取舍','','textarea');
    const restrictions=field(body,'适用边界、版本和必须继续验证的风险','','textarea');
    return {option,approver,rationale,restrictions};
  },'批准技术路线',x=>researchAction('/api/studio/research/decide',{
    study_id:data.study.id,option_id:x.option.value,approver:x.approver.value,
    rationale:x.rationale.value,restrictions:x.restrictions.value
  },'已形成带原始证据引用的技术决策'));
}
function reopenStudyModal(study){
  researchModal('重新开启技术研究',body=>{
    body.append(note('这会保留原有决定的历史，但使依赖旧研究修订的架构和工作包进入复审状态。'));
    const actor=field(body,'发起人','项目负责人');
    const reason=field(body,'新增事实、失败原因和需要重验的技术问题','','textarea');
    return {actor,reason};
  },'重新研究并触发复审',x=>researchAction('/api/studio/research/reopen',{
    study_id:study.id,actor:x.actor.value,reason:x.reason.value
  },'技术研究已重新开启；旧架构相关编码准入将被阻断'));
}
function attachStudyModal(data){
  if(data.study.status!=='DECIDED'){toast('请先形成经过批准的技术路线');return;}
  const targets=state.data.objects.filter(x=>['architecture','design','interface'].includes(x.kind));
  if(!targets.length){toast('请先在“需求与架构”中建立架构或详细设计草稿');return;}
  researchModal('将研究结论引用到架构修订',body=>{
    body.append(note('此操作只建立当前技术决策的精确版本引用；架构仍须经过独立设计评审才能批准。'));
    const target=selectField(body,'需要采用本研究结论的工程设计',
      targets.map(x=>[x.id,groupLabel(x.kind)+' · '+x.title]));
    return {target};
  },'绑定版本并生成待审查修订',async x=>{
    const target=item(x.target.value);
    if(!target)throw Error('目标设计不存在');
    const ids=[...new Set([...(Array.isArray(target.meta.study_ids)?target.meta.study_ids:[]),data.study.id])];
    const meta={...target.meta,study_ids:ids,
      study_revisions:{...(target.meta.study_revisions||{}),
        [data.study.id]:data.study.revision}};
    await req('/api/studio/object/'+target.id+'/update',{
      expected_revision:target.revision,meta
    });
    closeModal();
    await researchRefresh();
    toast('设计已关联技术决定 r'+data.study.revision+'，需要重新审批修订');
  });
}
