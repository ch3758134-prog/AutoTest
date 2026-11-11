/* Canonical product identity lifecycle: one stable statement, not a four-field project dump. */
'use strict';
async function productIdentityModal(){
  let identity, invalidations;
  try{
    [identity,invalidations]=await Promise.all([
      req('/api/studio/product-identity?project='+qs(state.project)),
      req('/api/studio/product-identity/invalidations?project='+qs(state.project))
    ]);
  }catch(e){toast('读取产品身份资料失败：'+e.message);return;}
  showModal('产品身份 · 工程澄清与受控修订',(body,footer)=>{
    body.append(note('产品身份用于说明这是什么产品。R1、EPB、当前硬件通道、具体功能及实测结果应关联到对应工程资料，不能因为出现在历史讨论中就一并成为正式定义。'));
    const top=el('div','detailsection');
    add(top,el('strong','',identity.state==='BASELINED'?'受控产品身份 · r'+identity.revision:'产品身份尚未正式确认'),
      el('p','context',identity.baseline?.statement||'当前没有已批准的产品身份表述。'),
      el('div','tiny',identity.baseline?
        '审核人 '+identity.baseline.approved_by+' · 版本指纹 '+identity.digest.slice(0,16):
        '旧项目简介不会自动被认为已批准'));
    body.append(top);
    const changes=el('div','detailsection');
    changes.append(el('strong','','正在讨论的产品身份修订'));
    const pending=identity.proposals.filter(x=>x.state==='CANDIDATE'||x.state==='SCOPED');
    if(!pending.length)changes.append(el('p','context','暂无待审候选。可直接与项目管理 AI 讨论，由 AI 提出候选，再在这里审查。'));
    for(const p of identity.proposals){
      const line=el('div','item-row'),left=el('div');left.style.flex='1';
      add(left,el('strong','',p.statement.slice(0,180)),
        el('div','tiny',p.state+' · '+p.origin_role+' · '+p.created),
        el('p','context',p.reasoning.slice(0,180)));
      line.append(left,button('查看依据和影响', 'outline',()=>productIdentityProposalModal(p.id)));
      changes.append(line);
    }
    body.append(changes);
    const open=invalidations.filter(x=>x.state==='OPEN');
    if(open.length){
      const panel=el('div','detailsection');
      panel.append(el('strong','','因产品身份变化需要复核的工作包 · '+open.length));
      for(const x of open){
        const row=el('div','item-row'),left=el('div');left.style.flex='1';
        add(left,el('strong','',item(x.package_id)?.title||x.package_id),
          el('div','tiny','原修订 r'+x.pinned_revision+' · 变更 '+x.proposal_id.slice(0,10)));
        row.append(left,button('处理复核','outline',()=>productIdentityResolveModal(x)));
        panel.append(row);
      }
      body.append(panel);
    }
    const archive=el('div','detailsection');
    add(archive,el('strong','','旧工程信息（历史背景，不是产品身份）'),
      el('p','context',identity.legacy_project_context.body||
        identity.legacy_project_context.summary||'没有历史背景'),
      el('div','tiny','阶段：'+(identity.legacy_project_context.version||'未指定')+
        ' · 变体/配置：'+(identity.legacy_project_context.variant||'未指定')));
    body.append(archive);
    footer.append(button('关闭','outline',closeModal));
    footer.append(button('提出产品身份候选','primary',()=>productIdentityProposeModal(identity)));
    footer.append(button('阅读工程概念说明','outline',()=>engineeringSemanticsModal('product_identity,product_purpose,system_boundary,release_scope')));
  });
}
function productIdentityProposeModal(identity){
  showModal('提出新的产品身份候选',(body,footer)=>{
    body.append(note('这里只讨论产品最根本的身份。不要为了套模板填写长篇功能清单；系统也不按关键字或字数判断语义是否正确。当前提议不会自动成为正式产品定义。'));
    const statement=field(body,'产品是什么？（候选身份表述）',
      identity.baseline?.statement||'','textarea');
    const reasoning=field(body,'为什么这属于产品身份？哪些信息应在需求、组成、阶段与配置中继续展开？','','textarea');
    const sources=field(body,'本轮依据（每行一个对话、文档或工程资料引用）','','textarea');
    const knowledge=field(body,'支持此判断的工程认识 ID（每行一个，可留空）','','textarea');
    const actor=field(body,'提出人','项目管理协作者');
    const err=el('div','error hidden');body.append(err);
    footer.append(button('取消','outline',productIdentityModal));
    footer.append(button('保存候选，进入审查','primary',async()=>{
      try{
        const answer=await req('/api/studio/product-identity/propose',{
          project_id:state.project,statement:statement.value,
          reasoning:reasoning.value,source_refs:sources.value.split('\n').map(x=>x.trim()).filter(Boolean),
          knowledge_ids:knowledge.value.split('\n').map(x=>x.trim()).filter(Boolean),
          actor:actor.value,expected_digest:identity.digest
        });
        toast('已记录身份候选；当前产品身份没有变化');
        await productIdentityProposalModal(answer.id);
      }catch(e){err.textContent=e.message;err.classList.remove('hidden');}
    }));
  });
}
async function productIdentityProposalModal(id){
  let data;
  try{data=await req('/api/studio/product-identity/detail?id='+qs(id));}
  catch(e){toast('读取修订资料失败：'+e.message);return;}
  const p=data.proposal;
  showModal('产品身份修订 · 工程影响审查',(body,footer)=>{
    body.append(note('这里复核产品身份变化及对实际工程对象的影响，不要求机械完成十个固定领域。影响范围应有充分解释；未建立关系图的部分不可假装已经核查。'));
    const overview=el('div','detailsection');
    add(overview,el('strong','',p.statement),
      el('p','context','提出者的语义解释：\n'+p.reasoning),
      el('div','tiny',p.origin_role+' · '+p.state+' · 基线 r'+p.base_revision),
      el('p','context','依据：\n'+p.source_refs.join('\n')),
      el('p','context','关联工程认识：'+(p.knowledge_ids.join('、')||'未关联知识对象')));
    if(p.scope_rationale)
      overview.append(el('p','context','范围复核：'+p.scope_rationale+
        '\n证据：'+(p.scope_evidence||[]).join('；')));
    body.append(overview);
    const impacts=el('div','detailsection');impacts.append(el('strong','','已考察的工程对象 · '+data.impacts.length));
    if(!data.impacts.length)impacts.append(el('p','context','尚未建立具体对象影响判断。空白并不等于“无影响”；可先核对现有需求、系统组成、设计和工作包的关系。'));
    for(const decision of data.impacts){
      const line=el('div','item-row'),left=el('div');left.style.flex='1';
      add(left,el('strong','',(item(decision.object_id)?.title||decision.object_id)+' · '+decision.disposition),
        el('p','context',decision.rationale),
        el('div','tiny','对象修订 r'+decision.object_revision+' · '+decision.evidence_refs.join('；')));
      line.append(left);impacts.append(line);
    }
    if(p.state==='CANDIDATE'||p.state==='SCOPED')
      impacts.append(button('评估一个实际工程对象','outline',
        ()=>productIdentityImpactModal(data)));
    body.append(impacts);
    const gate=el('div','detailsection');
    add(gate,el('strong','',data.gate.ready?'已满足形式准入条件，仍需负责人签发':'尚不具备发布条件'),
      el('div','tiny','已评估 '+data.gate.assessed_objects+' 项，明确有影响 '+data.gate.impacted_objects+' 项'));
    for(const reason of data.gate.reasons)gate.append(el('p','context','• '+reason));
    gate.append(el('p','context',data.gate.note));
    body.append(gate);
    footer.append(button('返回产品身份','outline',productIdentityModal));
    if(p.state==='CANDIDATE'||p.state==='SCOPED'){
      footer.append(button('复核研究及影响范围','outline',
        ()=>productIdentityScopeModal(data)));
      footer.append(button('拒绝候选','outline',
        ()=>productIdentityDecisionModal(data,false)));
      if(data.gate.ready)footer.append(button('正式发布新身份版本','primary',
        ()=>productIdentityDecisionModal(data,true)));
    }
  });
}
function productIdentityScopeModal(detail){
  const p=detail.proposal;
  showModal('产品身份变化 · 语义范围复核',(body,footer)=>{
    body.append(note('应结合用户实际讨论、现有工程对象和关联关系解释：哪些内容是真正的身份变化，哪些仍属于当前工程背景；为什么选择考察这些对象？不要求把所有领域机械填成“无影响”。'));
    const why=field(body,'变化实质、研究范围及尚未排除的风险',p.scope_rationale||'','textarea');
    const refs=field(body,'支持当前判断的来源（每行一个）',(p.scope_evidence||[]).join('\n'),'textarea');
    const reviewer=field(body,'负责人','项目负责人');
    const err=el('div','error hidden');body.append(err);
    footer.append(button('返回','outline',()=>productIdentityProposalModal(p.id)));
    footer.append(button('保存范围复核','primary',async()=>{
      try{
        await req('/api/studio/product-identity/review-scope',{
          id:p.id,expected_digest:detail.current_digest,
          reviewer:reviewer.value,rationale:why.value,
          evidence_refs:refs.value.split('\n').map(x=>x.trim()).filter(Boolean)
        });
        await productIdentityProposalModal(p.id);
      }catch(e){err.textContent=e.message;err.classList.remove('hidden');}
    }));
  });
}
function productIdentityImpactModal(detail){
  const p=detail.proposal;
  showModal('具体工程对象 · 影响判断',(body,footer)=>{
    body.append(note('选择确实需要考察的对象，并根据实际需求、设计或代码依赖判断。没有被列入不代表已判定无影响；本次分析的边界要在范围复核中明确说明。'));
    const options=state.data.objects.map(x=>[x.id,x.title+' · '+x.kind+' · r'+x.revision]);
    if(!options.length){body.append(el('p','context','当前没有工程对象可供评估；先回到研究与系统分解。'));footer.append(button('返回','outline',()=>productIdentityProposalModal(p.id)));return;}
    const selected=selectField(body,'受影响或已考察的工程对象',options,options[0][0]);
    const outcome=selectField(body,'此对象的判断',[
      ['UNKNOWN','尚不明确，继续调查'],
      ['IMPACT','有影响，需要复核'],
      ['NO_IMPACT','经考察本次无直接影响']
    ],'UNKNOWN');
    const why=field(body,'判断理由及其限制','','textarea');
    const refs=field(body,'依据（每行一个）','','textarea');
    const reviewer=field(body,'负责人','项目负责人');
    const err=el('div','error hidden');body.append(err);
    footer.append(button('返回','outline',()=>productIdentityProposalModal(p.id)));
    footer.append(button('记录影响判断','primary',async()=>{
      const obj=item(selected.value);
      try{
        await req('/api/studio/product-identity/assess-impact',{
          id:p.id,object_id:selected.value,expected_object_revision:obj.revision,
          disposition:outcome.value,rationale:why.value,
          evidence_refs:refs.value.split('\n').map(x=>x.trim()).filter(Boolean),
          reviewer:reviewer.value
        });
        await productIdentityProposalModal(p.id);
      }catch(e){err.textContent=e.message;err.classList.remove('hidden');}
    }));
  });
}
function productIdentityDecisionModal(detail,approve){
  const p=detail.proposal;
  showModal(approve?'签发新的产品身份版本':'拒绝产品身份候选',(body,footer)=>{
    body.append(note(approve?
      '签发只更新独立的产品身份，不会改写历史背景、R1 版本或硬件配置。明确有影响的已批准下游工作包将进入重新验证状态。':
      '拒绝不会删除讨论资料；候选及其理由仍会留在审计历史中。'));
    body.append(el('p','context',p.statement));
    const reviewer=field(body,'负责人','项目负责人');
    const noteField=field(body,'决策理由与尚未解决的风险','','textarea');
    const err=el('div','error hidden');body.append(err);
    footer.append(button('取消','outline',()=>productIdentityProposalModal(p.id)));
    footer.append(button(approve?'确认发布产品身份':'确认拒绝','primary',async()=>{
      try{
        if(approve)await req('/api/studio/product-identity/publish',{
          id:p.id,expected_digest:detail.current_digest,
          reviewer:reviewer.value,decision_note:noteField.value
        });
        else await req('/api/studio/product-identity/reject',{
          id:p.id,reviewer:reviewer.value,reason:noteField.value
        });
        await productIdentityModal();
      }catch(e){err.textContent=e.message;err.classList.remove('hidden');}
    }));
  });
}
function productIdentityResolveModal(row){
  showModal('受影响实施工作包复核',(body,footer)=>{
    body.append(note('旧工作包不能因为文字说明就解除版本失效。必须已经生成新的、重新批准的工作包修订。'));
    const obj=item(row.package_id);
    body.append(el('p','context','实施包：'+(obj?.title||row.package_id)+
       '；原修订：r'+row.pinned_revision+'；当前：r'+(obj?.revision||'?')));
    const reviewer=field(body,'负责人','项目负责人');
    const why=field(body,'新设计和验证证据为何足以解除失效','','textarea');
    const err=el('div','error hidden');body.append(err);
    footer.append(button('返回','outline',productIdentityModal));
    footer.append(button('确认复核','primary',async()=>{
      try{
        await req('/api/studio/product-identity/resolve',{
          id:row.proposal_id,package_id:row.package_id,
          expected_revision:obj?.revision,reviewer:reviewer.value,
          rationale:why.value
        });
        await productIdentityModal();
      }catch(e){err.textContent=e.message;err.classList.remove('hidden');}
    }));
  });
}
async function renderProductIdentityBanner(host){
  if(!state.project)return;
  try{
    const status=await req('/api/studio/product-identity?project='+qs(state.project));
    if(!host.isConnected)return;
    const panel=el('div','detailsection');
    add(panel,el('strong','',status.state==='BASELINED'?
       '产品身份已批准 · r'+status.revision:'产品身份尚未签发'),
       el('p','context',status.baseline?.statement||
          '旧工程项目简介只是历史材料，不会自动成为新的产品身份。'));
    panel.append(button('研究与审查产品身份','outline',productIdentityModal));
    host.append(panel);
  }catch(e){host.append(el('p','context','产品身份读取失败：'+e.message));}
}
