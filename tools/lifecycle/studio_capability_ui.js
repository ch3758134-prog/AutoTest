/* V0.7 candidate: evidence-aware capability coverage, never a completion percentage. */
'use strict';
function capabilityTraceModel(snapshot){
  const objects=Array.isArray(snapshot.objects)?snapshot.objects:[];
  const relations=Array.isArray(snapshot.relations)?snapshot.relations:[];
  const index=new Map(objects.map(o=>[o.id,o]));
  const approved=relations.filter(r=>r.status==='APPROVED'&&index.has(r.source_id)&&index.has(r.target_id));
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
    return {cap,requirements:byKind('requirement'),designs:linked.filter(o=>['architecture','interface','design'].includes(o.kind)),
      work:byKind('work_package'),tests:byKind('test'),
      pending:candidates.filter(r=>r.source_id===cap.id||r.target_id===cap.id).length,
      isolated:linked.length===0};
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
    summary.append(header);
    const counts=el('div','tiny','已审查关系可达：需求 '+m.requirements.length+
      ' · 架构/接口/详细设计 '+m.designs.length+
      ' · 工作包 '+m.work.length+' · 验证对象 '+m.tests.length);
    summary.append(counts);
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
    summary.append(links);
    line.append(summary);table.append(line);
  }
  wrap.append(table);area.node.append(wrap);
}
