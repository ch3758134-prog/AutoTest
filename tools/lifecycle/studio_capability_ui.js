/* V0.7 candidate: evidence-aware capability coverage, never a completion percentage. */
'use strict';
function capabilityTraceModel(snapshot){
  const objects=Array.isArray(snapshot.objects)?snapshot.objects:[];
  const relations=Array.isArray(snapshot.relations)?snapshot.relations:[];
  const index=new Map(objects.map(o=>[o.id,o]));
  const traceKinds=new Set(['derives','allocated','defines','implements','verifies']);
  const approved=relations.filter(r=>r.status==='APPROVED'&&traceKinds.has(r.kind)&&index.has(r.source_id)&&index.has(r.target_id));
  const candidates=relations.filter(r=>r.status!=='APPROVED');
  const neighbors=new Map();
  for(const r of approved){
    for(const [from,to] of [[r.source_id,r.target_id],[r.target_id,r.source_id]]){
      if(!neighbors.has(from))neighbors.set(from,new Set());
      neighbors.get(from).add(to);
    }
  }
  return objects.filter(o=>o.kind==='capability').map(cap=>{
    const visited=new Set([cap.id]),queue=[cap.id];
    while(queue.length){
      const id=queue.shift();
      for(const next of neighbors.get(id)||[]){
        if(!visited.has(next)&&index.get(next).kind!=='capability'){visited.add(next);queue.push(next);}
      }
    }
    const linked=[...visited].filter(id=>id!==cap.id).map(id=>index.get(id));
    const byKind=kind=>linked.filter(o=>o.kind===kind);
    return {cap,linked,requirements:byKind('requirement'),designs:linked.filter(o=>['architecture','interface','design'].includes(o.kind)),
      work:byKind('work_package'),tests:byKind('test'),
      pending:candidates.filter(r=>r.source_id===cap.id||r.target_id===cap.id).length,
      isolated:linked.length===0};
  });
}

/* A gap is a prompt to review known records, not an automatic acceptance verdict.
   The trace is restricted to confirmed semantic links; a missing link means UNKNOWN. */
function capabilityGapModel(model,snapshot){
  const objects=Array.isArray(snapshot.objects)?snapshot.objects:[];
  const relations=Array.isArray(snapshot.relations)?snapshot.relations:[];
  const byId=new Map(objects.map(o=>[o.id,o]));
  const all=new Set([model.cap.id,...model.linked.map(o=>o.id)]);
  const gaps=[];
  const put=(code,label,action,object=null)=>gaps.push({code,label,action,object});
  if(model.isolated)put('TRACE_MISSING','尚无已审查的能力追踪关系','检查能力来源，建立并审查需求/系统分配关系',model.cap);
  if(!model.requirements.length)put('REQUIREMENT_UNLINKED','没有通过已审查关系关联的需求','核对需求是否存在以及能力与需求的语义关联',model.cap);
  if(model.requirements.length&&!model.designs.length)
    put('DESIGN_UNLINKED','已关联需求，但没有已审查的架构、接口或详细设计关系','核对设计分配与关联审查',model.requirements[0]);
  for(const obj of [...model.requirements,...model.designs]){
    if(obj.state!=='APPROVED')
      put('DESIGN_REVIEW','需求/设计修订尚未批准：'+obj.title,'打开该修订，审查具体依据与状态',obj);
  }
  if(model.pending)put('RELATION_REVIEW','存在 '+model.pending+' 条直接相连的待审查关系','打开能力对象，逐项审查关系',model.cap);
  for(const rel of relations){
    if(rel.kind!=='impacts'||rel.status!=='APPROVED'||!all.has(rel.target_id))continue;
    const change=byId.get(rel.source_id);
    if(change&&change.kind==='change'&&change.state==='OPEN')
      put('CHANGE_OPEN','存在已审查影响关系指向此能力链的未关闭变更：'+change.title,
        '进入变更对象检查影响结论',change);
  }
  if(!model.tests.length)put('VERIFICATION_UNLINKED','没有通过已审查关系关联的验证对象','核对是否已有测试规格或原始验证记录；不推断实际测试结果',model.cap);
  if(model.work.length){
    put('READINESS_CHECK','存在 '+model.work.length+' 个实施工作包，尚未逐包执行实时准入检查',
      '打开工作包并调用现有 CODE_READY 检查',model.work[0]);
  }
  return gaps;
}
function capabilityGapDetails(model){
  showModal(model.cap.title+' · 工程缺口与行动',(body,footer)=>{
    body.append(note('只列出当前数据能确认的缺口或需要复核的事项；未建立追踪不等于未实现，验证对象不等于通过验收。'));
    const gaps=capabilityGapModel(model,state.data);
    if(!gaps.length)body.append(el('p','context','当前规则没有发现可判定的记录缺口；这不等于功能完成或通过实测。'));
    for(const gap of gaps){
      const line=el('div','detailsection');
      line.append(el('strong','',gap.label));
      line.append(el('p','context','建议核对：'+gap.action));
      if(gap.object){
        const actions=el('div','buttonrow');
        actions.append(button('查看相关对象','outline',()=>{closeModal();openItem(gap.object);}));
        if(gap.code==='READINESS_CHECK')actions.append(button('检查 CODE_READY','outline',()=>{closeModal();showReadiness(gap.object);}));
        line.append(actions);
      }
      body.append(line);
    }
    footer.append(button('关闭','outline',closeModal));
  });
}

function renderCapabilityTrace(){
  const area=section('产品能力 · 工程追踪');
  area.header.append(button('查看产品能力','link',()=>navigate('product')));
  const wrap=el('div','panel');
  wrap.append(el('p','muted','仅沿已审查的工程关系展示可达对象。数量不是完成率；测试对象不代表测试通过，未发现关系也不代表功能不存在。'));
  const model=capabilityTraceModel(state.data);
  if(!model.length){
    wrap.append(el('p','context','尚未建立产品能力。请先在“产品与系统”定义真实用户能力，再关联需求、设计和验证。'));
    wrap.append(button('+ 建立产品能力','outline',()=>editorModal('capability')));
    area.node.append(wrap);return;
  }
  const table=el('div','rowlist');
  for(const m of model){
    const line=el('div','item-row');
    line.style.alignItems='flex-start';
    const summary=el('div');summary.style.flex='1';summary.style.minWidth='0';
    const header=el('div','buttonrow');
    header.append(button(m.cap.title,'link',()=>openItem(m.cap)));
    header.append(badge(m.cap.state));
    header.append(button('记录此能力的问题','outline',()=>captureFindingModal(m.cap)));
    summary.append(header);
    const counts=el('div','tiny','已审查关系可达：需求 '+m.requirements.length+
      ' · 架构/接口/详细设计 '+m.designs.length+
      ' · 工作包 '+m.work.length+' · 验证对象 '+m.tests.length);
    summary.append(counts);
    const gaps=capabilityGapModel(m,state.data);
    const gapActions=el('div','buttonrow');
    gapActions.append(button('查看工程缺口与行动（'+gaps.length+'）','outline',()=>capabilityGapDetails(m)));
    summary.append(gapActions);
    if(m.isolated)summary.append(el('div','reason','尚无已审查的追踪关系。请检查需求分配和关联审查。'));
    if(m.pending)summary.append(el('div','tiny','直接相连的待审查关系：'+m.pending));
    const links=el('div','buttonrow');
    for(const [label,items] of [['需求',m.requirements],['设计',m.designs],['工作包',m.work],['验证',m.tests]]){
      if(items.length)links.append(button('查看'+label+'（'+items.length+'）','outline',()=>{
        showModal(m.cap.title+' · '+label+'追踪',(body,footer)=>{
          body.append(note('这里只显示从该能力经已审查关系可达的对象。请逐项查看原始依据、状态和版本；不推断物理验收。'));
          for(const obj of items){
            const entry=el('div','item-row'),inner=el('div');
            add(inner,el('strong','',obj.title),el('div','tiny',groupLabel(obj.kind)+' · '+obj.state+' · r'+obj.revision));
            add(entry,inner,button('打开对象','link',()=>{closeModal();openItem(obj);}));
            body.append(entry);
          }
          footer.append(button('关闭','outline',closeModal));
        });
      }));
    }
    if(m.tests.length)links.append(button('查看全部验证证据','outline',()=>capabilityVerificationModal(m)));
    summary.append(links);
    line.append(summary);table.append(line);
  }
  wrap.append(table);area.node.append(wrap);
}
