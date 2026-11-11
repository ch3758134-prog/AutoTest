/* Project Control V2: authenticated local project planning and human review.
 * Never calls ChatGPT or Codex; GitHub publishing remains an explicit Git action.
 */
'use strict';
window.projectActions=(()=>{
 let token='',actor='负责人',lastRemote=null;
 const types={RESEARCH:'研究与调查',DESIGN:'设计',IMPLEMENT:'编码实施',INTEGRATE:'集成',VERIFY:'验证',COORDINATE:'组织协调'};
 const stageNames={DRAFT:'计划草拟中',IN_REVIEW:'计划待审',BASELINED:'有已批准计划基线',REVISION_DRAFT:'有基线 · 修订草拟中'};
 const statusNames={NOT_STARTED:'未开始',IN_PROGRESS:'进行中',BLOCKED:'受阻',REPORTED_COMPLETE:'已报告完成（待核实）',VERIFIED_COMPLETE:'工作结果已核实',CANCELLED:'取消'};
 const changeNames={FACT_CORRECTION:'事实纠正',REQUIREMENT_CLARIFICATION:'需求澄清',SCOPE_CHANGE:'范围变更',DESIGN_CHANGE:'设计变更',IMPLEMENTATION_DEFECT:'实现缺陷',PLAN_CHANGE:'计划调整',OTHER:'其他问题'};
 const rp=s=>a(s);
 const control=()=>data?.control;
 const latest=()=>control()?.baselines?.slice(-1)[0]||null;
 const showState=()=>stageNames[control()?.planning?.status]||'状态未确认';
 const error=message=>{toast(message||'操作失败')};
 function formField(f) {
   const value=f.value===undefined?'':String(f.value);
   let element='';
   if(f.type==='select'){
     element='<select name="'+a(f.name)+'" '+(f.required?'required':'')+'>'+f.options.map(o=>'<option value="'+a(o.value)+'" '+(String(o.value)===value?'selected':'')+'>'+a(o.label)+'</option>').join('')+'</select>';
   }else if(f.type==='textarea'){
     element='<textarea name="'+a(f.name)+'" rows="'+(f.rows||4)+'" '+(f.required?'required':'')+' placeholder="'+a(f.placeholder||'')+'">'+a(value)+'</textarea>';
   }else if(f.type==='checklist'){
     element='<div class="pm-checklist">'+(f.options.length?f.options.map(o=>'<label class="pm-checkbox"><input type="checkbox" name="'+a(f.name)+'" value="'+a(o.value)+'" '+((f.value||[]).includes(o.value)?'checked':'')+'><span>'+a(o.label)+'</span></label>').join(''):'<span class="pm-muted">当前无可选对象</span>')+'</div>';
   }else{
     element='<input name="'+a(f.name)+'" type="'+a(f.type||'text')+'" value="'+a(value)+'" '+(f.required?'required':'')+' '+(f.readonly?'readonly':'')+' placeholder="'+a(f.placeholder||'')+' maxlength="'+a(f.maxlength||5000)+'">';
   }
   return '<div class="pm-field"><label>'+a(f.label)+(f.required?'<em>*</em>':'')+'</label>'+element+(f.help?'<p class="pm-help">'+a(f.help)+'</p>':'')+'</div>';
 }
 function dialogBox(title,description,fields,action,confirmText='保存') {
   const d=document.createElement('dialog');d.className='pm-dialog';
   d.innerHTML='<div class="pm-dialog-head"><div><div class="eyebrow">项目管理操作</div><h2>'+a(title)+'</h2><p>'+a(description)+'</p></div><button type="button" class="pm-dialog-close" aria-label="关闭">×</button></div>'+
     '<form method="dialog" class="pm-form">'+fields.map(formField).join('')+'<div class="pm-message" role="status"></div><div class="pm-dialog-actions"><button type="button" class="secondary pm-cancel">取消</button><button type="submit" class="primary">'+a(confirmText)+'</button></div></form>';
   document.body.append(d);
   const cleanup=()=>{d.close();d.remove()};
   d.querySelector('.pm-dialog-close').onclick=cleanup;
   d.querySelector('.pm-cancel').onclick=cleanup;
   d.addEventListener('cancel',e=>{e.preventDefault();cleanup()});
   d.querySelector('form').addEventListener('submit',async e=>{
     e.preventDefault();
     const form=d.querySelector('form'), fd=new FormData(form);
     const values=Object.fromEntries(fields.filter(f=>f.type!=='checklist').map(f=>[f.name,fd.get(f.name)||'']));
     fields.filter(f=>f.type==='checklist').forEach(f=>values[f.name]=fd.getAll(f.name));
     const submit=form.querySelector('[type=submit]');submit.disabled=true;
     const msg=form.querySelector('.pm-message');msg.textContent='正在校验并记录……';
     try{
       await action(values);
       cleanup();
     }catch(e){
       msg.textContent=e.message||'操作失败';submit.disabled=false;
     }
   });
   d.showModal();
 }
 function ownerAuth(){
   return new Promise((resolve,reject)=>{
     if(token){resolve();return}
     const d=document.createElement('dialog');d.className='pm-dialog pm-auth';
     d.innerHTML='<div class="pm-dialog-head"><div><div class="eyebrow">本地负责人授权</div><h2>确认管理操作权限</h2><p>从启动 Python 工作台的本地终端复制授权码。它不会上传到 GitHub，也不与 ChatGPT 共享。</p></div></div>'+
       '<form method="dialog" class="pm-form"><div class="pm-field"><label>操作者</label><input name="actor" value="'+a(actor)+'" required maxlength="140"></div><div class="pm-field"><label>本次运行的授权码</label><input name="token" type="password" required autocomplete="off"></div><div class="pm-dialog-actions"><button type="button" class="secondary pm-cancel">取消</button><button type="submit" class="primary">确认</button></div></form>';
     document.body.append(d);
     let done=false;
     const close=(ok)=>{if(done)return;done=true;d.close();d.remove();if(ok)resolve();else reject(new Error('已取消授权'))};
     d.querySelector('.pm-cancel').onclick=()=>close(false);
     d.addEventListener('cancel',e=>{e.preventDefault();close(false)});
     d.querySelector('form').onsubmit=e=>{
       e.preventDefault();
       const fd=new FormData(d.querySelector('form'));
       actor=String(fd.get('actor')||'负责人').trim();
       token=String(fd.get('token')||'').trim();
       close(true);
     };
     d.showModal();
   });
 }
 async function request(action,payload,endpoint='/api/control/action'){
   await ownerAuth();
   const r=await fetch(endpoint,{
     method:'POST',headers:{'Content-Type':'application/json','X-Owner-Token':token},
     body:JSON.stringify(endpoint==='/api/control/action'?{
       action,actor,payload:{...payload,reason:payload.reason||'负责人记录',expected_revision:control()?.revision}
     }:payload)
   });
   let body={};try{body=await r.json()}catch(_){}
   if(!r.ok){if(r.status===403)token='';throw new Error(body.error||'操作失败 HTTP '+r.status)}
   if(endpoint==='/api/control/action'){
     lastRemote=null;
     await refresh();
     closeDrawer();
     toast('已记录至本地工作记录；需 Git 提交才能供其他对话读取');
   }
   return body;
 }
 const reasonField=(value='')=>({name:'reason',label:'原因与判断依据',type:'textarea',required:true,rows:3,value,
    help:'重要工程决定必须有明确理由；记录会进入受控审计历史。'});
 function workOptions(except=''){
   return data.plan.work.filter(w=>w.id!==except).map(w=>({value:w.id,label:w.title}));
 }
 function sourceOptions(){
   const chosen=data.objects.filter(x=>x.state==='APPROVED'||x.id.startsWith('at-g1-')||x.id.startsWith('at-g0-')).slice(0,24);
   return chosen.map(o=>({value:o.id,label:o.title+' / r'+o.revision+(o.state==='APPROVED'?' · 已批准':' · 仅供研究引用')}));
 }
 function sourcesFor(ids,mode){
   return ids.map(id=>{
     const o=obj(id);
     return {object_id:id,revision:o?.revision||1,role:mode==='CONTROLLED'&&o?.state==='APPROVED'?'REQUIRED_BASELINE':'CONTEXT'};
   });
 }
 function workForm(id){
   const existing=id?workById(id):null;
   const fields=[
     {name:'title',label:'工作名称',required:true,value:existing?.title||''},
     {name:'deliverable_id',label:'所属交付目标',type:'select',required:true,value:existing?.deliverable_id||data.plan.deliverables[0]?.id,
      options:data.plan.deliverables.map(d=>({value:d.id,label:d.title}))},
     {name:'parent_work_id',label:'上级工作（工作分解）',type:'select',value:existing?.parent_work_id||'',
      options:[{value:'',label:'独立工作，无上级'},...workOptions(id)]},
     {name:'kind',label:'工作性质',type:'select',value:existing?.kind||'RESEARCH',options:Object.entries(types).map(([value,label])=>({value,label}))},
     {name:'mode',label:'工作类型',type:'select',value:existing?.mode||'EXPLORATORY',options:[{value:'EXPLORATORY',label:'探索研究（不需要预先批准所有结论）'},{value:'CONTROLLED',label:'受控工程工作（不能凭候选设计执行）'}]},
     {name:'horizon',label:'计划层次',type:'select',value:existing?.horizon||'CURRENT',options:[{value:'CURRENT',label:'当前'},{value:'NEXT',label:'下一步'},{value:'LATER',label:'后续规划'}]},
     {name:'priority',label:'优先级',type:'select',value:existing?.priority||'NORMAL',options:[{value:'LOW',label:'低'},{value:'NORMAL',label:'普通'},{value:'HIGH',label:'高'},{value:'CRITICAL',label:'紧急'}]},
     {name:'assignee',label:'负责方向 / 对话',value:existing?.assignee||'',placeholder:'工程总协调 / TEST / DEVICE / Codex'},
     {name:'expected_result',label:'应当交付什么',type:'textarea',rows:3,value:existing?.expected_result||'',required:true},
     {name:'exit_condition',label:'什么条件下才算工作完成',type:'textarea',rows:3,value:existing?.exit_condition||'',required:true},
     {name:'depends_on',label:'依赖哪些已有工作',type:'checklist',options:workOptions(id),value:existing?.depends_on||[]},
     {name:'source_ids',label:'引用的现有工程依据',type:'checklist',options:sourceOptions(),value:(existing?.sources||[]).map(x=>x.object_id),
      help:'未批准文件只作为参考；受控工程需要正式输入。选项并不等于编码授权。'},
     {name:'planned_start',label:'预计开始日期（可空）',type:'date',value:existing?.planned_start||''},
     {name:'planned_finish',label:'预计完成日期（可空）',type:'date',value:existing?.planned_finish||''},
     {name:'schedule_basis',label:'日期依据（仅填写日期时必须说明）',value:existing?.schedule_basis||''},
     reasonField()
   ];
   dialogBox(id?'调整开发计划中的工作':'添加一项开发工作',
     '变更计划内容会留下版本记录；已批准计划会进入待重新审查的候选修订。',fields,async v=>{
       const item={title:v.title,deliverable_id:v.deliverable_id,parent_work_id:v.parent_work_id||null,kind:v.kind,
         mode:v.mode,horizon:v.horizon,priority:v.priority,assignee:v.assignee,
         expected_result:v.expected_result,exit_condition:v.exit_condition,
         depends_on:v.depends_on,sources:sourcesFor(v.source_ids,v.mode),
         planned_start:v.planned_start,planned_finish:v.planned_finish,
         schedule_basis:v.schedule_basis};
       if(!item.planned_start)delete item.planned_start;
       if(!item.planned_finish)delete item.planned_finish;
       if(!item.schedule_basis)delete item.schedule_basis;
       if(id)await request('edit_work',{id,patch:item,reason:v.reason});
       else await request('create_work',{item,reason:v.reason});
     },id?'记录工作修订':'加入候选计划');
 }
 function deliverableForm(id){
   const existing=data.plan.deliverables.find(x=>x.id===id);
   dialogBox(id?'调整交付成果':'新增交付成果',
     '这是项目交付结构的一部分，不允许由工作任务自动扩充已批准的产品范围。',[
       {name:'title',label:'交付成果名称',required:true,value:existing?.title||''},
       {name:'success_condition',label:'交付成功条件',required:true,type:'textarea',rows:4,value:existing?.success_condition||''},
       {name:'commitment',label:'范围依据',type:'select',value:existing?.commitment||'PROPOSAL',
        options:[{value:'PROPOSAL',label:'候选范围（未经批准）'},{value:'IN_SCOPE_FROM_APPROVED_BASELINE',label:'已批准 P0 明确覆盖的范围'}]},
       reasonField()
     ],async v=>{
       const item={title:v.title,success_condition:v.success_condition,commitment:v.commitment};
       if(id)await request('edit_deliverable',{id,patch:item,reason:v.reason});
       else await request('create_deliverable',{item,reason:v.reason});
     });
 }
 function planDecision(which){
   const st=control().planning.status;
   const labels={submit_plan:'提交计划审查',withdraw_plan:'撤回送审计划',approve_plan:'批准当前开发计划',return_plan:'退回计划修订'};
   let message=which==='approve_plan'?'批准的是开发工作安排，不会批准 G1 需求、G2 设计、Gate 或 Codex 实施。未知的后续工作仍应保留为受约束的规划任务。':'操作将记录在本地 Git 跟踪工作记录中，其他 ChatGPT 对话需要在 GitHub 提交后才能看到。';
   dialogBox(labels[which],message,[
     {name:'confirmation',label:'确认操作',type:'select',options:[{value:'CONFIRM',label:'我已核对当前计划和依据，确认继续'}]},
     reasonField()
   ],async v=>{if(v.confirmation!=='CONFIRM')throw Error('请确认操作');await request(which,{reason:v.reason})},labels[which]);
 }
 function progressForm(id){
   const current=control()?.progress?.[id];
   dialogBox('登记实际工作进展','登记实际事实，不修改正式开发计划。报告完成不等于已经验证，更不等于整项产品交付通过。',[
     {name:'state',label:'当前执行状态',type:'select',value:current?.state==='VERIFIED_COMPLETE'?'REPORTED_COMPLETE':current?.state||'NOT_STARTED',
      options:Object.entries(statusNames).filter(([k])=>k!=='VERIFIED_COMPLETE').map(([value,label])=>({value,label}))},
     {name:'detail',label:'具体进展、结果和阻碍',type:'textarea',rows:5,required:true,value:current?.detail||''},
     {name:'evidence_refs',label:'来源链接或记录编号（每行一个）',type:'textarea',rows:3,
      value:(current?.evidence_refs||[]).join('\n'),help:'报告完成必须提供证据。不能以对话中的“已完成”代替真实记录。'},
     reasonField()
   ],async v=>await request('update_progress',{id,state:v.state,detail:v.detail,
       evidence_refs:v.evidence_refs.split('\n').map(x=>x.trim()).filter(Boolean),reason:v.reason}), '登记进展');
 }
 function verifyForm(id){
   dialogBox('核实工作结果','只核实该工作是否满足规定完成条件；不自动确认软件集成、现场验收或项目 Gate。',[reasonField()],
     async v=>await request('verify_work',{id,reason:v.reason}),'确认核实');
 }
 function changeForm(id){
   const ids=id?[id]:[];
   dialogBox('记录问题与变更','先分析这是什么问题、实际影响哪些工作；不因提出变更而自动修改正式 P0、需求或代码。',[
     {name:'title',label:'问题 / 变更标题',required:true},
     {name:'kind',label:'变化性质',type:'select',options:Object.entries(changeNames).map(([value,label])=>({value,label}))},
     {name:'description',label:'事实、发现及与原计划的差异',type:'textarea',rows:5,required:true},
     {name:'affected_work',label:'已知可能受影响的工作',type:'checklist',options:workOptions(),value:ids},
     {name:'source_refs',label:'证据链接（每行一个）',type:'textarea',rows:3},
     reasonField()
   ],async v=>await request('raise_change',{title:v.title,kind:v.kind,description:v.description,
       affected_work:v.affected_work,source_refs:v.source_refs.split('\n').map(x=>x.trim()).filter(Boolean),
       reason:v.reason}),'登记问题');
 }
 function changeAssess(id){
   const c=control().changes.find(x=>x.id===id);
   if(!c)return;
   const fields=c.affected_work.flatMap(wid=>[
     {name:'disposition_'+wid,label:'影响判断：'+(workById(wid)?.title||wid),type:'select',
      options:[{value:'REASSESS',label:'需要重新评估/修改计划'},{value:'INVESTIGATE',label:'证据不足，先研究'},{value:'NO_IMPACT',label:'经核对不影响'}]},
     {name:'rationale_'+wid,label:'该项判断的依据',type:'textarea',required:true,rows:2}
   ]);fields.push(reasonField());
   dialogBox('逐项评估影响','只判断真正受影响的范围，不自动全局回滚。不适用或不影响必须说明理由。',fields,async v=>{
     const assessments={};for(const wid of c.affected_work)assessments[wid]={
       disposition:v['disposition_'+wid],rationale:v['rationale_'+wid]};
     await request('assess_change',{id,assessments,reason:v.reason});
   },'保存影响评估');
 }
 function changeResolve(id){
   dialogBox('决定如何处理变化','接受重新规划不等于自动修改需求或计划；受影响工作将要求复核。',[
     {name:'decision',label:'处置决定',type:'select',options:[
       {value:'MORE_RESEARCH',label:'继续研究（暂不修改计划）'},
       {value:'ACCEPT_FOR_REPLANNING',label:'接受，要求调整受影响计划'},
       {value:'REJECT',label:'不采纳，记录理由'}]},
     reasonField()
   ],async v=>await request('resolve_change',{id,decision:v.decision,reason:v.reason}),'确认处置');
 }
 function reconcileForm(id){
   dialogBox('解除工作变更复核要求',
     '只有确认不受影响，或已批准新版工作计划后，才能解除此处阻碍。',[
       {name:'disposition',label:'核实结论',type:'select',options:[
         {value:'NO_CHANGE',label:'重新核对后确认本工作不需要修改'},
         {value:'REPLANNED',label:'已经批准包含本次调整的新版计划'}]},
       reasonField()
   ],async v=>await request('reconcile_work',{id,disposition:v.disposition,reason:v.reason}),'记录复核结论');
 }
 function receiptForm(id){
   dialogBox('登记 Codex / 本地执行回执',
     '只能记录实际执行命令与退出码；本页面录入属于“本地报告，待核实”，不能伪装成自动抓取日志。建议使用项目中的本地命令封装器生成原始回执。',[
       {name:'work_id',label:'对应开发工作',type:'select',value:id||'',options:data.plan.work.map(w=>({value:w.id,label:w.title}))},
       {name:'head_sha',label:'执行时完整 Git SHA（40 位）',required:true,placeholder:'必须是实际本地版本'},
       {name:'command',label:'实际执行命令',required:true,placeholder:'例如 dotnet test AutoTest.sln --configuration Release'},
       {name:'exit_code',label:'实际退出码',type:'number',required:true,value:'0'},
       {name:'note',label:'执行事实及限制',type:'textarea',required:true,rows:4},
       {name:'evidence_refs',label:'真实日志及测试报告引用，每行一个',type:'textarea',rows:3},
       reasonField()
     ],async v=>{
       if(!/^-?\d+$/.test(v.exit_code))throw Error('退出码应为整数');
       await request('record_receipt',{work_id:v.work_id,head_sha:v.head_sha,commands:[
         {command:v.command,exit_code:Number(v.exit_code)}],
         note:v.note,evidence_refs:v.evidence_refs.split('\n').map(x=>x.trim()).filter(Boolean),
         reason:v.reason});
     },'登记待核实回执');
 }
 function receiptReview(id){
   dialogBox('核实一条本地执行记录','核实准确的命令、SHA、结果及日志；即使被确认，也不能推导实体 ECU 验收通过。',[
     {name:'decision',label:'证据审查结论',type:'select',options:[
       {value:'EVIDENCE_REVIEWED',label:'已核对本条执行证据'},
       {value:'REJECTED',label:'证据不足或与原始记录矛盾'}]},
     reasonField()
   ],async v=>await request('review_receipt',{id,decision:v.decision,reason:v.reason}),'记录审查');
 }
 function artifactSubmit(id){
   const doc=obj(id);if(!doc)throw Error('未找到该文件');
   dialogBox('提交正式工程成果审查',
     '送审版本固定到当前内容指纹和修订。它不是普通 AI 草稿，也不会因为提交自动批准或进入下一阶段。',[
       {name:'revision',label:'本次提交的确切版本',value:'r'+doc.revision,readonly:true},
       reasonField()
     ],async v=>await request('submit_artifact',{
       object_id:id,object_revision:doc.revision,reason:v.reason
     }),'形成正式送审');
 }
 function artifactReview(id){
   const submission=control().artifact_submissions.find(s=>s.id===id);
   if(!submission)throw Error('未找到送审记录');
   dialogBox('负责人审查工程成果',
     submission.title+' / r'+submission.object_revision+'。批准仅使这一份确切工程文件成为有效成果，不能替代阶段 Gate、代码集成或实体 ECU 验收。',[
       {name:'decision',label:'审查结论',type:'select',options:[
         {value:'RETURN',label:'退回补充研究或修订'},
         {value:'APPROVE',label:'批准此正式工程成果版本'},
         {value:'REJECT',label:'不采纳该成果'}]},
       reasonField()
     ],async v=>await request('review_artifact',{id,decision:v.decision,reason:v.reason}),
     '正式记录审查决定');
 }
 function phaseReview(){
   const approved=data.objects.filter(o=>o.state==='APPROVED');
   dialogBox('记录阶段评审决定','流程 Gate 与单份文件的批准不同。通过 Gate 必须引用已经批准且属于该阶段的工程成果，不能把 G1 研究草稿当成依据。',[
     {name:'phase_id',label:'阶段',type:'select',options:data.process.stages.map(s=>({value:s.id,label:s.id+' · '+s.name}))},
     {name:'cycle_id',label:'研发轮次',type:'select',options:control().cycles.map(x=>({value:x.id,label:x.title}))},
     {name:'decision',label:'阶段决定',type:'select',options:[
       {value:'DEFER',label:'暂缓决定'}, {value:'RETURN',label:'退回补充'},
       {value:'PASS_WITH_LIMITATIONS',label:'附条件通过（须有正式依据）'},
       {value:'PASS',label:'通过（须有正式依据）'}]},
     {name:'evidence_ids',label:'已批准且属于当前阶段的证据',type:'checklist',
      options:approved.map(o=>({value:o.id,label:o.title}))},
     {name:'waivers',label:'附条件通过的例外说明（仅 PASS_WITH_LIMITATIONS，每行：工程对象ID | 具体理由）',
      type:'textarea',rows:3,help:'每个缺失的必要成果都应有明确理由；完全通过时不接受例外。'},
     reasonField()
   ],async v=>{
     const waivers=v.waivers.split('\n').map(x=>x.trim()).filter(Boolean).map(x=>{
       const split=x.indexOf('|');
       if(split<1)throw Error('每条例外必须写工程对象ID | 具体理由');
       return {object_id:x.slice(0,split).trim(),reason:x.slice(split+1).trim()};
     });
     await request('record_phase_review',{phase_id:v.phase_id,cycle_id:v.cycle_id,decision:v.decision,
       evidence_ids:v.evidence_ids,waivers,reason:v.reason});
   },'记录阶段评审');
 }
 function milestoneForm(id){
   const existing=data.plan.milestones?.find(x=>x.id===id);
   dialogBox(id?'修改项目里程碑':'添加项目里程碑',
    '里程碑表达交付目标和检查点，不等同于研发 Gate；未知日期应留空，不能为画甘特图编造排期。',[
      {name:'title',label:'里程碑名称',required:true,value:existing?.title||''},
      {name:'deliverable_id',label:'对应交付成果',type:'select',value:existing?.deliverable_id||data.plan.deliverables[0]?.id,
       options:data.plan.deliverables.map(d=>({value:d.id,label:d.title}))},
      {name:'exit_condition',label:'达成里程碑必须具备的结果和证据',type:'textarea',rows:4,required:true,value:existing?.exit_condition||''},
      {name:'target_date',label:'目标日期（可留空）',type:'date',value:existing?.target_date||''},
      {name:'date_basis',label:'目标日期的依据（设置日期时必填）',value:existing?.date_basis||''},
      reasonField()
    ],async v=>{
      const item={title:v.title,deliverable_id:v.deliverable_id,
        exit_condition:v.exit_condition,target_date:v.target_date||null,date_basis:v.date_basis||null};
      if(id)await request('edit_milestone',{id,patch:item,reason:v.reason});
      else await request('create_milestone',{item,reason:v.reason});
    },id?'登记里程碑修订':'添加里程碑');
 }
 function riskForm(id){
   dialogBox('登记项目风险','风险是未来可能发生的偏差，不应与已经发生的问题或技术研究草稿混同。',[
     {name:'title',label:'风险名称',required:true},
     {name:'description',label:'可能发生的情况及其影响',type:'textarea',rows:4,required:true},
     {name:'likelihood',label:'发生可能性',type:'select',options:[
       {value:'LOW',label:'低'},{value:'MEDIUM',label:'中'},{value:'HIGH',label:'高'},{value:'CRITICAL',label:'极高'}]},
     {name:'severity',label:'影响严重程度',type:'select',options:[
       {value:'LOW',label:'低'},{value:'MEDIUM',label:'中'},{value:'HIGH',label:'高'},{value:'CRITICAL',label:'严重'}]},
     {name:'mitigation',label:'计划采取的预防或应对措施',type:'textarea',rows:3,required:true},
     {name:'owner',label:'负责跟进的人或 AI 方向',required:true,value:'工程总协调'},
     {name:'affected_work',label:'可能受影响的工作',type:'checklist',options:workOptions(),value:id?[id]:[]},
     {name:'source_refs',label:'风险依据（每行一个）',type:'textarea',rows:2},
     reasonField()
   ],async v=>await request('raise_risk',{
     title:v.title,description:v.description,likelihood:v.likelihood,severity:v.severity,
     mitigation:v.mitigation,owner:v.owner,affected_work:v.affected_work,
     source_refs:v.source_refs.split('\n').map(x=>x.trim()).filter(Boolean),reason:v.reason
   }),'登记风险');
 }
 function riskReview(id){
   dialogBox('更新风险处理结果','风险关闭需要核实相应措施和实际证据，不能因为 AI 认为影响不大就关闭。',[
     {name:'status',label:'最新风险处理状态',type:'select',options:[
       {value:'MITIGATING',label:'正在落实应对措施'},
       {value:'MITIGATED',label:'已采取措施并有相关证据'},
       {value:'CLOSED',label:'风险已关闭，有明确证据'}]},
     {name:'evidence_refs',label:'对应的实际证据，每行一个',type:'textarea',rows:3},
     reasonField()
   ],async v=>await request('review_risk',{
     id,status:v.status,evidence_refs:v.evidence_refs.split('\n').map(x=>x.trim()).filter(Boolean),
     reason:v.reason
   }),'记录风险处理');
 }
 function cycleForm(){
   dialogBox('登记新一轮研发工作',
     '螺旋研发应从明确的新问题或变化开始，限定重新开展的工作范围；不自动推翻上一轮有效设计或批准文件。',[
       {name:'title',label:'迭代或工作轮次',required:true,placeholder:'例如：真实台架反馈引发的按键控制修订'},
       {name:'trigger',label:'为何需要新一轮工作',type:'textarea',required:true,rows:4},
       {name:'scope',label:'本次重点修订与不受影响的范围',type:'textarea',required:true,rows:4},
       reasonField()
     ],async v=>await request('start_cycle',{
       title:v.title,trigger:v.trigger,scope:v.scope,reason:v.reason
     }),'建立研发轮次');
 }
 function cycleClose(id){
   dialogBox('关闭当前研发轮次',
     '关闭轮次只是对这一轮工作的管理记录，不自动等于任何阶段 Gate 或产品验收通过。',
     [reasonField()],async v=>await request('close_cycle',{id,reason:v.reason}),'确认关闭');
 }
 const riskItem=r=>'<div class="pm-record"><div class="pm-record-head"><strong>'+a(r.title)+'</strong>'+
   pill(r.status==='OPEN'?'待处理风险':r.status==='MITIGATING'?'处理中':r.status==='CLOSED'?'已关闭':'已有措施',
     r.status==='CLOSED'?'approved':'pending')+'</div><p class="pm-muted">'+a(r.description)+'</p>'+
   '<p class="pm-muted">可能性 '+a(r.likelihood)+' / 影响 '+a(r.severity)+' / 负责 '+a(r.owner)+'</p>'+
   (r.status==='OPEN'||r.status==='MITIGATING'||r.status==='MITIGATED'?'<button class="ghost" data-pm="risk-review" data-id="'+a(r.id)+'">更新风险</button>':'')+'</div>';
 function wbsView(){
   let out='<div class="pm-record">';
   for(const d of data.plan.deliverables){
     out+='<h3>'+a(d.title)+'</h3>';
     const related=data.plan.work.filter(w=>w.deliverable_id===d.id);
     const byParent=(parent,depth,seen)=>{
       if(depth>10)return '';
       return related.filter(w=>(w.parent_work_id||null)===parent).map(w=>{
         if(seen.has(w.id))return '';
         const nested=new Set(seen);nested.add(w.id);
         return '<div class="pm-wbs-item" style="margin-left:'+Math.min(depth*18,126)+'px" data-work="'+a(w.id)+'"><span>'+
          (depth?'└ ':'• ')+a(w.title)+'</span><span class="pm-muted">'+a(types[w.kind]||w.kind)+'</span></div>'+
          byParent(w.id,depth+1,nested);
       }).join('');
     };
     out+=byParent(null,0,new Set());
   }
   return out+'</div>';
 }
 function ganttView(){
   const works=data.plan.work.filter(w=>w.planned_start&&w.planned_finish);
   if(!works.length)return '<div class="empty">当前尚无根据可靠日期形成的时间排期。先研究范围、依赖和工作量，再安排日期；不会生成虚构甘特图。</div>';
   const day=s=>Date.parse(s+'T00:00:00Z')/86400000;
   let first=Math.min(...works.map(w=>day(w.planned_start)));
   let last=Math.max(...works.map(w=>day(w.planned_finish)));
   const extent=Math.max(1,last-first+1);
   return '<div class="pm-gantt"><div class="pm-gantt-axis"><span>'+a(works.reduce((x,w)=>day(w.planned_start)<day(x)?w.planned_start:x,works[0].planned_start))+'</span><span>'+
      a(works.reduce((x,w)=>day(w.planned_finish)>day(x)?w.planned_finish:x,works[0].planned_finish))+'</span></div>'+
     works.map(w=>{
       const left=(day(w.planned_start)-first)/extent*100;
       const width=Math.max(1.5,(day(w.planned_finish)-day(w.planned_start)+1)/extent*100);
       return '<div class="pm-gantt-row" data-work="'+a(w.id)+'"><div class="pm-gantt-title">'+a(w.title)+'</div>'+
         '<div class="pm-gantt-track"><span style="left:'+left+'%;width:'+width+'%"></span></div>'+
         '<div class="pm-gantt-dates">'+a(w.planned_start)+' 至 '+a(w.planned_finish)+'</div></div>';
     }).join('')+'</div>';
 }
 const milestoneItem=m=>'<div class="pm-record"><div class="pm-record-head"><strong>'+a(m.title)+'</strong>'+
   pill(m.target_date?'计划日期 '+m.target_date:'尚无可靠日期','unknown')+'</div>'+
   '<p class="pm-muted">'+a(m.exit_condition)+'</p><button class="ghost" data-pm="milestone-edit" data-id="'+a(m.id)+'">调整里程碑</button></div>';
 function workButtons(w){
   const p=control()?.progress?.[w.id],invalid=control()?.invalidation?.[w.id];
   return '<div class="rule"></div><h3>工作推进和受控操作</h3>'+
    (p?'<div class="callout">当前实际进展：'+a(statusNames[p.state]||p.state)+' · '+a(p.detail||'')+'</div>':
       '<div class="callout">未登记实际进展；规划候选不等于已经启动工作。</div>')+
    '<div class="pm-work-actions"><button class="secondary" data-pm="work-edit" data-id="'+a(w.id)+'">修改计划</button>'+
    '<button class="secondary" data-pm="progress" data-id="'+a(w.id)+'">登记工作进展</button>'+
    (p?.state==='REPORTED_COMPLETE'?'<button class="secondary" data-pm="verify" data-id="'+a(w.id)+'">核实完成条件</button>':'')+
    '<button class="secondary" data-pm="change-work" data-id="'+a(w.id)+'">登记问题或变更</button>'+
    '<button class="secondary" data-pm="receipt-add" data-id="'+a(w.id)+'">登记本地执行回执</button>'+'<button class="secondary" data-pm="risk-new" data-id="'+a(w.id)+'">登记工作风险</button>'+
    (invalid&&!invalid.resolved?'<button class="primary" data-pm="reconcile" data-id="'+a(w.id)+'">变更后复核</button>':'')+
    '</div>';
 }
 const changeItem=c=>'<div class="pm-record"><div class="pm-record-head"><strong>'+a(c.title)+'</strong>'+
   pill(c.state==='RESOLVED'?'已处置':'待处理',c.state==='RESOLVED'?'approved':'pending')+'</div><div class="pm-muted">'+a(changeNames[c.kind]||c.kind)+' · '+a(c.description)+'</div><div class="pm-inline-actions">'+
   (c.state==='OPEN'||c.state==='ASSESSED'?'<button class="ghost" data-pm="change-assess" data-id="'+a(c.id)+'">评估影响</button>':'')+
   (c.state==='ASSESSED'?'<button class="ghost" data-pm="change-resolve" data-id="'+a(c.id)+'">决定处理</button>':'')+'</div></div>';
 const receiptItem=r=>'<div class="pm-record"><div class="pm-record-head"><strong>'+a(workById(r.work_id)?.title||r.work_id)+'</strong>'+
   pill(r.status==='UNVERIFIED'?'执行证据待核实':r.status==='EVIDENCE_REVIEWED'?'证据已核对':'证据驳回',
    r.status==='EVIDENCE_REVIEWED'?'approved':'pending')+'</div><p class="pm-muted">HEAD '+a(r.head_sha.slice(0,12))+' · '+a(r.note)+'</p><p class="pm-muted">'+r.commands.map(x=>a(x.command)+' (exit '+a(x.exit_code)+')').join(' / ')+'</p>'+
   (r.status==='UNVERIFIED'?'<button class="ghost" data-pm="receipt-review" data-id="'+a(r.id)+'">审查执行证据</button>':'')+'</div>';
 const phaseRecord=x=>'<div class="pm-record"><strong>'+a(x.phase_id)+' · '+a(x.decision)+'</strong><p class="pm-muted">'+a(x.reason)+' · 本地记录，待 GitHub 版本核实</p></div>';
 function renderExtras(){
   const root=$('#main');if(!root||!data?.control)return;
   const record=control();
   const baseline=latest();
   if(page==='overview'){
     root.insertAdjacentHTML('beforeend','<section class="section"><div class="panel"><div class="pane-title"><h2>研发管理事实</h2><button class="ghost" data-go="sync">核对 GitHub 与工作记录 →</button></div><div class="pm-stat-grid"><div><span>当前计划</span><strong>'+a(showState())+'</strong></div><div><span>实际完成验证</span><strong>'+a(Object.values(record.progress).filter(x=>x.state==='VERIFIED_COMPLETE').length)+' 项工作</strong></div><div><span>未处理变化</span><strong>'+a(record.changes.filter(x=>x.state!=='RESOLVED').length)+' 项</strong></div></div><div class="callout">工程计划、实际进展和研发阶段决定独立管理。项目是否总体完成不能用工作数或代码统计代替。</div></div></section>');
   }
   if(page==='plan'){
     const header='<section class="pm-management-head"><div><div class="eyebrow">当前计划版本 r'+record.planning.revision+'</div><h2>'+a(showState())+'</h2><p class="pm-muted">'+(baseline?'最近批准计划：'+a(baseline.id)+' / r'+baseline.revision:'当前尚无正式批准的计划基线')+'</p></div><div class="pm-inline-actions">'+
       '<button class="primary" data-pm="work-new">添加开发工作</button>'+'<button class="secondary" data-pm="milestone-new">添加里程碑</button>'+
       (record.planning.status==='DRAFT'||record.planning.status==='REVISION_DRAFT'?'<button class="secondary" data-pm="plan-submit">提交计划审查</button>':'')+
       (record.planning.status==='IN_REVIEW'?'<button class="secondary" data-pm="plan-withdraw">撤回修改</button><button class="primary" data-pm="plan-approve">批准计划</button><button class="secondary" data-pm="plan-return">退回</button>':'')+'</div></section>';
     const first=root.querySelector('.callout.warn');if(first)first.textContent=record.planning.status==='BASELINED'?
       '当前存在负责人签署的计划基线；若本地记录尚未提交到 GitHub，其他对话还不能把它当作已发布版本。':
       '计划目前未获得新的正式批准。可滚动完善规划，但不能用计划状态推断需求、设计或编码准入。';
     root.querySelector('.page-heading')?.insertAdjacentHTML('afterend',header);
     const foot='<section class="section">'+sectionHead('实际工作进展与变化')+
       '<div class="pm-two-cols"><div class="panel"><div class="pane-title"><h2>执行记录</h2></div>'+
       (Object.keys(record.progress).length?Object.entries(record.progress).map(([id,p])=>
         '<div class="pm-record"><strong>'+a(workById(id)?.title||id)+'</strong><div>'+pill(statusNames[p.state]||p.state,p.state==='VERIFIED_COMPLETE'?'approved':'unknown')+'</div><p class="pm-muted">'+a(p.detail)+'</p></div>').join(''):'<p class="pm-muted">尚未登记实际工作进展。计划任务不代表已经执行。</p>')+'</div>'+
       '<div class="panel"><div class="pane-title"><h2>问题与变更</h2><button class="ghost" data-pm="change-new">登记问题</button></div>'+
       (record.changes.length?record.changes.slice().reverse().map(changeItem).join(''):'<p class="pm-muted">尚未在新版管理记录中登记变化；不表示工程没有历史问题。</p>')+
       '</div></div></section>';
     root.insertAdjacentHTML('beforeend',foot);
     root.insertAdjacentHTML('beforeend','<section class="section"><div class="pm-two-cols"><div class="panel"><h2>按交付成果分解工作</h2>'+
       wbsView()+'</div><div class="panel"><h2>计划时间安排</h2>'+ganttView()+'</div></div></section>');
     root.insertAdjacentHTML('beforeend','<section class="section pm-two-cols">'+
       '<div class="panel"><div class="pane-title"><h2>交付里程碑</h2><button class="ghost" data-pm="milestone-new">添加里程碑</button></div>'+
       (data.plan.milestones?.length?data.plan.milestones.map(milestoneItem).join(''):'<p class="pm-muted">尚无里程碑。可以先建立无日期的检查点。</p>')+
       '</div><div class="panel"><div class="pane-title"><h2>项目风险</h2><button class="ghost" data-pm="risk-new">登记风险</button></div>'+
       (record.risks.length?record.risks.slice().reverse().map(riskItem).join(''):'<p class="pm-muted">尚未在此版本登记风险，不代表不存在技术未知。</p>')+
       '</div></section>');
   }
   if(page==='deliverables'){
     root.querySelector('.page-heading')?.insertAdjacentHTML('afterend','<div class="pm-top-actions"><button class="primary" data-pm="deliverable-new">新增交付成果</button><span class="pm-muted">新增内容首先是范围候选，不自动修改 P0</span></div>');
     root.querySelectorAll('.deliverable-card').forEach((card,i)=>{
       const d=data.plan.deliverables[i];if(d)card.querySelector('.actions')?.insertAdjacentHTML('beforeend',
         '<button class="secondary" data-pm="deliverable-edit" data-id="'+a(d.id)+'">修改交付定义</button>');
       if(d)card.insertAdjacentHTML('beforeend','<div class="pm-card-action"><button class="ghost" data-pm="deliverable-edit" data-id="'+a(d.id)+'">编辑成果与验收条件</button></div>');
     });
   }
   if(page==='deliverables'){
     root.insertAdjacentHTML('beforeend','<section class="section panel"><div class="pane-title"><h2>交付里程碑</h2>'+
       '<button class="secondary" data-pm="milestone-new">新增里程碑</button></div>'+
       (data.plan.milestones?.length?data.plan.milestones.map(milestoneItem).join(''):'<p class="pm-muted">尚无已登记里程碑</p>')+
       '</section>');
   }
   if(page==='process'){
     root.insertAdjacentHTML('beforeend','<section class="section"><div class="panel"><div class="pane-title"><h2>螺旋研发轮次</h2><button class="secondary" data-pm="cycle-new">登记新一轮工作</button></div>'+
       record.cycles.slice().reverse().map(x=>'<div class="pm-record"><div class="pm-record-head"><strong>'+a(x.title)+'</strong>'+
         pill(x.state==='ACTIVE'?'进行中':'已关闭',x.state==='ACTIVE'?'pending':'approved')+'</div>'+
         '<p class="pm-muted">触发原因：'+a(x.trigger)+'；范围：'+a(x.scope)+'</p>'+
         (x.state==='ACTIVE'?'<button class="ghost" data-pm="cycle-close" data-id="'+a(x.id)+'">关闭本轮</button>':'')+
         '</div>').join('')+'</div></section>');
     root.insertAdjacentHTML('beforeend','<section class="section"><div class="panel"><div class="pane-title"><h2>阶段审查与正式记录</h2><button class="secondary" data-pm="phase-review">记录阶段审查</button></div>'+
       (record.phase_reviews.length?record.phase_reviews.slice().reverse().map(phaseRecord).join(''):'<p class="pm-muted">本项目管理版本尚无新记录的正式阶段决定；历史审批需按来源单独核对。</p>')+
       '<div class="callout">阶段决定必须有真实的批准成果与负责人依据。系统不能由报告标题、草稿数量或对象状态自行认定 Gate 通过。</div></div></section>');
   }
   if(page==='reviews'){
     root.insertAdjacentHTML('beforeend','<section class="section"><div class="pm-two-cols"><div class="panel"><div class="pane-title"><h2>工程变化审查</h2><button class="secondary" data-pm="change-new">记录问题</button></div>'+
       (record.changes.length?record.changes.slice().reverse().map(changeItem).join(''):'<p class="pm-muted">当前无新版本的待处理变化</p>')+'</div>'+
       '<div class="panel"><div class="pane-title"><h2>Codex / 实际执行证据</h2><button class="secondary" data-pm="receipt-add">登记回执</button></div>'+
       (record.receipts.length?record.receipts.slice().reverse().map(receiptItem).join(''):'<p class="pm-muted">尚无新版执行回执，不声称已经取得本地构建或真机验收结果。</p>')+
       '</div></div></section>');
   }
   if(page==='handoff'){
     root.insertAdjacentHTML('beforeend','<section class="section"><div class="panel"><div class="pane-title"><h2>本地 Codex 执行回执</h2><button class="secondary" data-pm="receipt-add">登记回执</button></div>'+
       '<p class="pm-muted">推荐由 Codex 使用封装命令记录真实执行版本、命令、退出码及证据。回执上传 GitHub 后，其他 ChatGPT 对话即可读取核实。</p>'+
       (record.receipts.length?record.receipts.slice(-4).reverse().map(receiptItem).join(''):'<div class="callout">未收到执行回执。请勿因为某次对话称“构建成功”就将工作标为完成。</div>')+'</div></section>');
   }
 }
 function syncPage(){
   const s=data.sync,record=control(),base=latest(),r=lastRemote||s;
   return heading('工程资料 / GitHub 与本地','版本与同步','分清本地工作记录、已经提交的 Git 版本和 GitHub 远端事实。同步状态不由聊天文字推断。')+
   '<section class="pm-management-head"><div><div class="eyebrow">本地工作区</div><h2>'+a(s.branch||'未获取 Git 分支')+'</h2><p class="pm-muted">HEAD '+a(s.local_head||'UNKNOWN')+'</p></div>'+
   '<div class="pm-inline-actions"><button class="primary" data-pm="remote-check">核对 GitHub origin</button><button class="secondary" data-pm="receipt-add">登记 Codex 回执</button></div></section>'+
   '<section class="pm-two-cols section"><div class="panel"><h2>本地工作事实</h2><dl class="pm-facts">'+
     [['工作树修改',s.worktree_dirty===null?'未知':s.worktree_dirty?'存在未提交文件':'未发现未提交文件'],
      ['项目管理记录',s.control_file_modified===null?'未知':s.control_file_modified?'本地已修改，待 Git 提交':'本地文件未检测到修改'],
      ['记录版本',String(record.revision)],['计划状态',showState()],
      ['最近批准计划',base?base.id+' / r'+base.revision:'尚无批准计划']].map(([k,v])=>'<dt>'+a(k)+'</dt><dd>'+a(v)+'</dd>').join('')+
   '</dl><div class="callout">所有本地管理动作只修改 Git 跟踪的项目记录。需要你指示 Codex 提交和推送，其他 ChatGPT 网页对话才可能读取。</div></div>'+
   '<div class="panel"><h2>远端核实</h2><dl class="pm-facts">'+
     [['核实状态',r.remote_checked?'已请求 GitHub origin':'尚未进行实时核对'],
      ['远端关系',r.remote_relation||'REMOTE_UNVERIFIED'],
      ['远端 HEAD',r.remote_head||'未核验'],
      ['说明',r.remote_message||'远端状态未知']].map(([k,v])=>'<dt>'+a(k)+'</dt><dd>'+a(v)+'</dd>').join('')+'</dl>'+
     '<p class="pm-muted">远端核对不会自动拉取、合并、重置或推送代码；存在分歧时必须人工审查。</p></div></section>'+
     '<section class="section panel"><h2>项目决策与操作历史</h2><div class="pm-audit">'+
     record.events.slice(-25).reverse().map(e=>'<div class="pm-record"><strong>'+a(e.action)+'</strong><p class="pm-muted">'+a(e.reason)+' · '+a(e.actor)+' · '+a(e.at)+'</p></div>').join('')+
     '</div></section>';
 }
 async function syncCheck(){
   const res=await request('sync-check',{},'/api/sync/check');
   lastRemote=res;
   if(page==='sync')render();toast('已核对 GitHub origin；未更改本地仓库');
 }
 document.addEventListener('click',e=>{
   const b=e.target.closest('[data-pm]');if(!b)return;
   const action=b.dataset.pm,id=b.dataset.id;
   try{
     const fns={
       'auth':ownerAuth,'work-new':()=>workForm(null),'work-edit':()=>workForm(id),
       'milestone-new':()=>milestoneForm(null),'milestone-edit':()=>milestoneForm(id),
       'risk-new':()=>riskForm(id),'risk-review':()=>riskReview(id),
       'cycle-new':cycleForm,'cycle-close':()=>cycleClose(id),
       'deliverable-new':()=>deliverableForm(null),'deliverable-edit':()=>deliverableForm(id),
       'plan-submit':()=>planDecision('submit_plan'),'plan-withdraw':()=>planDecision('withdraw_plan'),
       'plan-approve':()=>planDecision('approve_plan'),'plan-return':()=>planDecision('return_plan'),
       'progress':()=>progressForm(id),'verify':()=>verifyForm(id),'change-new':()=>changeForm(),
       'change-work':()=>changeForm(id),'change-assess':()=>changeAssess(id),
       'change-resolve':()=>changeResolve(id),'reconcile':()=>reconcileForm(id),
       'receipt-add':()=>receiptForm(id),'receipt-review':()=>receiptReview(id),
       'phase-review':phaseReview,'artifact-submit':()=>artifactSubmit(id),
       'artifact-review':()=>artifactReview(id),'remote-check':syncCheck
     };
     const fn=fns[action];if(fn)Promise.resolve(fn()).catch(ex=>error(ex.message));
   }catch(ex){error(ex.message)}
 });
 return {renderExtras,workButtons,syncPage};
})();
