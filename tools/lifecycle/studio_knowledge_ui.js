/* Semantic engineering knowledge ledger — user-readable reasons, not metadata-first forms. */
'use strict';
const KNOWLEDGE_STATE={CANDIDATE:'候选，尚未确认',ACCEPTED:'已审查接受（不等于事实经独立验证）',
  RESEARCH_NEEDED:'需要进一步核查',REJECTED:'未接受'};
const KNOWLEDGE_REL={SUPPORTS:'支持',CONTRADICTS:'存在冲突',REFINES:'细化或纠正',
  CONTEXT_FOR:'提供上下文',IMPACTS:'可能影响',DERIVED_FROM:'由此推导'};

async function knowledgeWorkbench(){
  let data,guide;
  try{[data,guide]=await Promise.all([
    req('/api/studio/knowledge?project='+qs(state.project)),
    req('/api/studio/semantics?focus=all')]);
  }catch(e){toast('工程认识资料读取失败：'+e.message);return;}
  const titles=Object.fromEntries(guide.concepts.map(x=>[x.id,x.name]));
  showModal('工程认识与语义关系',(body,footer)=>{
    body.append(note('这是工程事实、疑问、解释和判断的可追溯记录。AI 提交的是候选，负责人确认后也不会自动更改产品定义、需求或源码。允许一条事实关联多个工程概念。'));
    const summary=el('div','detailsection');
    const accepted=data.items.filter(x=>x.state==='ACCEPTED').length;
    const pending=data.items.filter(x=>x.state==='CANDIDATE').length;
    add(summary,el('strong','','已审查 '+accepted+' 项 · 待确认 '+pending+' 项 · 总计 '+data.items.length+' 项'),
      el('div','tiny','已确认知识关系 '+data.links.filter(x=>x.state==='CONFIRMED').length+
      ' / '+data.links.length+' · 不表示正式工程对象已被修改'));
    body.append(summary);
    for(const item of data.items.slice().reverse()){
      const panel=el('div','detailsection');
      add(panel,el('strong','',item.title),
        el('div','tiny',KNOWLEDGE_STATE[item.state]+' · r'+item.revision+' · 来源 '+item.provenance),
        el('p','context',item.statement.slice(0,500)),
        el('div','tiny','涉及：'+item.concept_ids.map(x=>titles[x]||x).join(' / ')));
      const controls=el('div','buttonrow');
      controls.append(button('查看依据、解释和历史','outline',
        ()=>knowledgeEntryDetail(item,data,guide)));
      if(item.state==='CANDIDATE')controls.append(button('审查候选','outline',
        ()=>knowledgeReviewModal(item)));
      panel.append(controls);body.append(panel);
    }
    if(!data.items.length)body.append(el('p','context',
      '这里还没有工程认识记录。可以由项目管理 AI 根据对话、源码与真实实验提出候选，也可由负责人手动登记。'));
    if(data.links.length){
      const section=el('div','detailsection');
      section.append(el('strong','','已记录的语义关系'));
      for(const link of data.links){
        const a=data.items.find(x=>x.id===link.source_id);
        const b=data.items.find(x=>x.id===link.target_id);
        const p=el('div','item-row');
        const left=el('div');left.style.flex='1';
        add(left,el('strong','',(a?.title||link.source_id)+' — '+(KNOWLEDGE_REL[link.relation]||link.relation)+' — '+(b?.title||link.target_id)),
          el('div','context',link.rationale),
          el('div','tiny',link.state+' · 来源修订 r'+link.source_revision+' / r'+link.target_revision));
        p.append(left);
        if(link.state==='CANDIDATE')p.append(button('审查关系','outline',
          ()=>knowledgeLinkReviewModal(link,data)));
        section.append(p);
      }
      body.append(section);
    }
    footer.append(button('关闭','outline',closeModal));
    footer.append(button('登记工程认识候选','primary',()=>knowledgeCreateModal(guide)));
    if(data.items.length>=2)footer.append(button('提出知识关系','outline',
      ()=>knowledgeLinkProposeModal(data)));
    footer.append(button('查看概念说明','outline',()=>engineeringSemanticsModal('all')));
  });
}
function knowledgeEntryDetail(item,data,guide){
  showModal('工程认识 · '+item.title,(body,footer)=>{
    body.append(note('记录接受状态不意味着外部原始证据已经认证，亦不代表需求、设计或产品定义获得发布授权。'));
    const conceptNames=Object.fromEntries(guide.concepts.map(x=>[x.id,x.name]));
    const panel=el('div','detailsection');
    add(panel,el('strong','',item.statement),
      el('p','context','解释与边界：\n'+item.interpretation),
      el('div','context','概念作用：'+item.concept_ids.map(x=>conceptNames[x]||x).join(' / ')),
      el('div','context','原始来源/引用：\n'+item.source_refs.join('\n')),
      el('div','context','关联工程对象：'+(item.related_objects.join('、')||'暂无')),
      el('div','tiny','来源性质：'+item.provenance+' · 记录者 '+item.created_by+
           ' · '+item.origin_role+' · 当前 '+KNOWLEDGE_STATE[item.state]));
    if(item.review_reason)panel.append(el('p','context','负责人审查说明：'+item.review_reason));
    body.append(panel);
    const changes=data.revisions.filter(x=>x.item_id===item.id);
    const history=el('div','detailsection');history.append(el('strong','','不可覆盖的修订历史'));
    for(const rev of changes){
      history.append(el('div','tiny','r'+rev.revision+' · '+rev.operation+
                      ' · '+rev.actor+' · '+rev.created));
    }
    body.append(history);
    footer.append(button('返回工程认识','outline',knowledgeWorkbench));
    if(item.state==='CANDIDATE'){
      footer.append(button('修改候选','outline',()=>knowledgeCreateModal(guide,item)));
      footer.append(button('提交审查','primary',()=>knowledgeReviewModal(item)));
    }else footer.append(button('提出新的细化/纠正候选','outline',
      ()=>knowledgeCreateModal(guide,null,item)));
  });
}
function knowledgeCreateModal(guide,existing=null,refines=null){
  showModal(existing?'修改工程认识候选':'登记工程认识候选',(body,footer)=>{
    body.append(note('先写清楚原话或所观察的行为，再解释判断的适用条件。概念可以多选；不同层次的细节不要直接塞进产品定义，必要时通过“细化/纠正”关系保留。'));
    const title=field(body,'此项认识的主题',existing?.title||'');
    const statement=field(body,'原话、观察或需要研究的主张',existing?.statement||'','textarea');
    const interpretation=field(body,'为什么这样理解，适用范围、争议和不确定性',existing?.interpretation||'','textarea');
    const provenance=selectField(body,'这段材料是什么性质',[
      ['USER_INPUT','用户提供的输入（不等于外部验证）'],
      ['DOCUMENT','资料中的描述（需要核查来源）'],
      ['OBSERVATION','有条件的观察记录'],
      ['AI_INTERPRETATION','AI 的解释候选'],
      ['OPEN_QUESTION','尚未查明的问题']
    ],existing?.provenance||'AI_INTERPRETATION');
    const label=el('div','context');label.textContent='哪些工程概念与它有关？可以多选，不意味着只能属于一种类型';
    body.append(label);
    const conceptInputs=[];
    const group=el('div','detailsection');
    for(const concept of guide.concepts){
      const line=el('label','context'),checkbox=document.createElement('input');
      checkbox.type='checkbox';checkbox.checked=(existing?.concept_ids||['decision']).includes(concept.id);
      line.append(checkbox,document.createTextNode(' '+concept.name));
      group.append(line,document.createElement('div'));
      conceptInputs.push([concept.id,checkbox]);
    }
    body.append(group);
    const sources=field(body,'材料来源（每行一个文档、代码或对话引用）',
      (existing?.source_refs||[]).join('\n'),'textarea');
    const objects=field(body,'关联现有工程对象 ID（每行一个，可留空）',
      (existing?.related_objects||[]).join('\n'),'textarea');
    const author=field(body,'记录人','项目管理协作者');
    const error=el('div','error hidden');body.append(error);
    footer.append(button('返回','outline',knowledgeWorkbench));
    footer.append(button('保存为候选（不修改正式基线）','primary',async()=>{
      const entry={title:title.value,statement:statement.value,interpretation:interpretation.value,
        provenance:provenance.value,
        concept_ids:conceptInputs.filter(([,c])=>c.checked).map(([id])=>id),
        source_refs:sources.value.split('\n').map(x=>x.trim()).filter(Boolean),
        related_objects:objects.value.split('\n').map(x=>x.trim()).filter(Boolean)};
      try{
        let outcome;
        if(existing){
          outcome=await req('/api/studio/knowledge/revise',{
            id:existing.id,expected_revision:existing.revision,entry,author:author.value
          });
        }else{
          outcome=await req('/api/studio/knowledge/propose',{
            project_id:state.project,entry,author:author.value
          });
        }
        if(refines){
          await req('/api/studio/knowledge/link/propose',{
            project_id:state.project,source_id:outcome.id,target_id:refines.id,
            relation:'REFINES',rationale:'候选对既有记录进行细化或纠正；待负责人审查',
            author:author.value
          });
        }
        toast('已记录为工程认识候选，未改动正式产品定义');
        await knowledgeWorkbench();
      }catch(e){error.textContent=e.message;error.classList.remove('hidden');}
    }));
  });
}
function knowledgeReviewModal(item){
  showModal('人工审查 · '+item.title,(body,footer)=>{
    body.append(note('接受是对这一记录及其语义归属的人工判断，不证明来源真实，也不会自动更改下层设计。'));
    add(body,el('p','context','主张：'+item.statement),
      el('p','context','来源：'+item.source_refs.join('；')),
      el('p','context','解释及局限：'+item.interpretation));
    const decision=selectField(body,'本次审查结果',[
      ['REQUEST_RESEARCH','需要继续研究'],['ACCEPT','接受此记录及其解释'],['REJECT','拒绝这项判断']
    ],'REQUEST_RESEARCH');
    const reviewer=field(body,'审查人','项目负责人');
    const reason=field(body,'为什么作出该判断','','textarea');
    const error=el('div','error hidden');body.append(error);
    footer.append(button('返回','outline',knowledgeWorkbench));
    footer.append(button('保存审查决定','primary',async()=>{
      try{
        await req('/api/studio/knowledge/review',{
          id:item.id,expected_revision:item.revision,
          decision:decision.value,reviewer:reviewer.value,reason:reason.value
        });
        await knowledgeWorkbench();
      }catch(e){error.textContent=e.message;error.classList.remove('hidden');}
    }));
  });
}
function knowledgeLinkProposeModal(data){
  showModal('提出工程认识之间的关系',(body,footer)=>{
    body.append(note('两项认识可以支持、矛盾、细化或提供上下文。关系也必须讲明理由；不因对象都被接受就自动成立。'));
    const options=data.items.map(x=>[x.id,x.title+' · '+KNOWLEDGE_STATE[x.state]]);
    const source=selectField(body,'关系出发项',options,data.items[0].id);
    const target=selectField(body,'关联目标项',options,data.items[1].id);
    const relation=selectField(body,'关系含义',Object.entries(KNOWLEDGE_REL).map(([k,v])=>[k,v]),'REFINES');
    const rationale=field(body,'为什么存在这种关系','','textarea');
    const author=field(body,'提出人','项目管理协作者');
    const error=el('div','error hidden');body.append(error);
    footer.append(button('返回','outline',knowledgeWorkbench));
    footer.append(button('登记候选关系','primary',async()=>{
      try{
        await req('/api/studio/knowledge/link/propose',{
          project_id:state.project,source_id:source.value,target_id:target.value,
          relation:relation.value,rationale:rationale.value,author:author.value
        });
        await knowledgeWorkbench();
      }catch(e){error.textContent=e.message;error.classList.remove('hidden');}
    }));
  });
}
function knowledgeLinkReviewModal(link,data){
  showModal('审查工程认识关联',(body,footer)=>{
    body.append(note('关系确认需要两端工程认识当前都处于已接受状态，并且版本仍与提出关系时一致。'));
    body.append(el('p','context',link.rationale));
    const decision=selectField(body,'处理',[
      ['CONFIRM','确认关系'],['REJECT','拒绝关系']
    ],'CONFIRM');
    const reviewer=field(body,'审查人','项目负责人');
    const reason=field(body,'确认或拒绝的理由','','textarea');
    const err=el('div','error hidden');body.append(err);
    footer.append(button('返回','outline',knowledgeWorkbench));
    footer.append(button('记录决定','primary',async()=>{
      try{
        await req('/api/studio/knowledge/link/review',{
          id:link.id,decision:decision.value,reviewer:reviewer.value,reason:reason.value
        });
        await knowledgeWorkbench();
      }catch(e){err.textContent=e.message;err.classList.remove('hidden');}
    }));
  });
}
