/* V0.8 governed product-definition workflow: review first, release by explicit owner action.
   This user-facing adapter cannot override server policy. No AI-specific approvals. */
'use strict';
async function definitionGovernanceModal(){
  let info, invalid;
  try{
    [info,invalid]=await Promise.all([
      req('/api/studio/governance/status?project='+qs(state.project)),
      req('/api/studio/governance/invalidations?project='+qs(state.project))
    ]);
  }catch(e){toast('无法读取产品定义治理状态：'+e.message);return;}
  showModal('产品定义 · 工程治理与变更审查',(body,footer)=>{
    body.append(note('正式定义必须先提出新版本、逐领域审查、核对证据及下游影响，然后才能由负责人发布。当前页面不会自动向 GitHub 发布或授权任何编码。'));
    const head=el('div','detailsection');
    add(head,el('strong','','当前工程定义状态：'+info.baseline_status),
      el('div','tiny','治理规则 '+info.policy_version),
      el('div','context','当前产品目标：'+(info.definition.summary||'未定义')+
        '\n产品版本 '+(info.definition.version||'未指定')+
        '；定义指纹 '+info.definition_digest.slice(0,20)));
    if(info.baseline_status==='UNBASELINED')
      head.append(note('现有内容是历史/导入的工作资料，还没有经过本治理内核签发。不能将其当作受控定义基线。'));
    if(info.baseline_status==='DRIFT')
      head.append(note('受控定义内容与现存项目记录不一致：禁止继续发布变更，须先处理数据库或历史来源差异。'));
    head.append(button('为什么这些信息不应混在产品定义里？','outline',()=>engineeringSemanticsModal()));
    body.append(head);
    const changes=el('div','detailsection');
    changes.append(el('strong','','定义修订申请'));
    if(!info.changes.length)changes.append(el('p','context','尚无正式的定义变更申请。可从现有产品定义创建新候选，再展开研究。'));
    for(const change of info.changes){
      const line=el('div','item-row'),textBox=el('div');
      add(textBox,el('strong','',change.reason.slice(0,90)),
        el('div','tiny',change.state+' · '+change.created+' · '+change.proposer));
      add(line,textBox,button('查看审查矩阵','outline',()=>definitionChangeModal(change.id)));
      changes.append(line);
    }
    body.append(changes);
    const suggestionPanel=el('div','detailsection');
    body.append(suggestionPanel);
    definitionSuggestionInbox(suggestionPanel,info);
    const work=invalid.filter(x=>x.status==='OPEN');
    if(work.length){
      const box=el('div','detailsection');box.append(el('strong','','已发布变更导致的实施复核：'+work.length+' 项'));
      for(const pending of work){
        const obj=item(pending.package_id);
        const line=el('div','item-row'),left=el('div');
        add(left,el('strong','',obj?.title||pending.package_id),
          el('div','tiny','实施包 r'+pending.pinned_revision+' · 受影响变更 '+pending.change_id.slice(0,10)));
        add(line,left,button('人工复核','outline',()=>definitionRevalidateModal(pending,obj)));
        box.append(line);
      }
      body.append(box);
    }
    footer.append(button('关闭','outline',closeModal));
    if(info.baseline_status!=='DRIFT')
      footer.append(button('+ 提出产品定义修订','primary',()=>definitionProposeModal(info)));
  });
}
function definitionProposeModal(info){
  const p=info.definition;
  showModal('提交新产品定义候选',(body,footer)=>{
    body.append(note('此处只创建候选，不立即替换现役定义。你可以和 AI 先充分讨论、保存调查结论，再逐领域审查。'));
    const summary=field(body,'产品身份的简洁候选表述（产品是什么）',p.summary,'textarea');
    const detail=field(body,'原有项目背景与上下文（保留原文，不等同产品身份）',p.body,'textarea');
    const version=field(body,'项目/交付版本元数据（旧版存储字段）',p.version);
    const variant=field(body,'配置/产品变体元数据（旧版存储字段）',p.variant);
    body.append(note('V1.0 先提供语义区分，现有 SQLite 仍将四个历史字段一起做版本校验；不要把背景、版本和变体当成产品的永久定义。更完整的存储拆分需另外迁移与验收。'));
    body.append(button('查看概念说明与反例','outline',()=>engineeringSemanticsModal()));
    const reason=field(body,'讨论的分歧、语义归属及需要进一步调查的事项','','textarea');
    const source=field(body,'原始依据（每行一个 GitHub/文档/实测证据引用）','','textarea');
    const actor=field(body,'提出人或协作参与者','产品负责人');
    const error=el('div','error hidden');body.append(error);
    footer.append(button('取消','outline',closeModal));
    footer.append(button('建立受控变更请求','primary',async()=>{
      try{
        const result=await req('/api/studio/governance/propose',{
          project_id:state.project,
          proposed:{summary:summary.value,body:detail.value,
            version:version.value,variant:variant.value},
          reason:reason.value,
          source_refs:source.value.split('\n').map(x=>x.trim()).filter(Boolean),
          proposer:actor.value,expected_digest:info.definition_digest
        });
        toast('变更候选已创建；现役产品定义未改变');
        await definitionChangeModal(result.change.id);
      }catch(e){error.textContent=e.message;error.classList.remove('hidden');}
    }));
  });
}
async function definitionChangeModal(id){
  let detail;
  try{detail=await req('/api/studio/governance/detail?id='+qs(id));}
  catch(e){toast('无法读取定义审查：'+e.message);return;}
  const c=detail.change,byDomain=Object.fromEntries(detail.reviews.map(r=>[r.domain,r]));
  showModal('产品定义变更 · 影响范围与研究门禁',(body,footer)=>{
    body.append(note('系统逐项核查已规定的影响领域；即使工程关系图缺少边，也不能直接跳过领域审查。AI 生成的说明仅是候选，证据与审查者仍须分别核实。'));
    const source=el('div','detailsection');
    add(source,el('strong','',c.reason),el('div','tiny',c.state+' · '+c.policy_version+
      ' · 基线指纹 '+c.base_digest.slice(0,20)),
      el('p','context','拟更新产品定位：'+c.proposed.summary),
      el('p','context','变更来源：'+c.source_refs.join('；')));
    body.append(source);
    const gate=detail.gate;
    const bar=el('div','detailsection');
    add(bar,el('strong','','工程准入：'+(gate.ready?'已满足全部准入条件':'不可发布')),
      el('div','tiny','必需研究 '+(gate.research_reviewed||0)+' / '+(gate.research_required||10)+
        '；领域影响 '+gate.reviewed+' / '+gate.required+'；工作包 '+
        gate.package_reviewed+' / '+gate.package_total));
    bar.append(button('查看必需研究与候选证据','outline',
      ()=>definitionResearchModal(c.id)));
    if(!gate.ready){
      for(const reason of gate.reasons.slice(0,14))
        bar.append(el('div','context','• '+reason));
    }else bar.append(note('全部领域已完成有效评估，但这不等于认证了证据真实性、设计正确性或真实设备可用。'));
    body.append(bar);
    for(const entry of [
      ['POSITIONING','产品定位与价值目标'],['STAKEHOLDERS','用户、角色与使用环境'],
      ['WORKFLOWS','端到端业务场景与操作流程'],['CAPABILITIES','产品能力与功能边界'],
      ['SYSTEM_BOUNDARY','系统分解及外部约束'],['ARCHITECTURE','系统与软件架构'],
      ['INTERFACES_HARDWARE','硬件、外部接口与通信'],
      ['VERIFICATION','验证范围、证据和验收'],
      ['IMPLEMENTATION','代码、工作包与集成'],
      ['DELIVERY_RISK','进度、风险和交付']
    ]){
      const [key,label]=entry,prior=byDomain[key],line=el('div','item-row');
      const left=el('div');left.style.flex='1';
      add(left,el('strong','',label),el('div','tiny',prior?prior.outcome:'尚未评估'));
      if(prior)add(left,el('div','context',prior.rationale.slice(0,260)),
                   el('div','tiny','证据 '+prior.evidence_refs.length+' 项 · 显式影响对象 '+Object.keys(prior.affected_revisions).length+' 项'));
      line.append(left);
      if(c.state==='REVIEW')line.append(button(prior?'重新评估':'评估此领域','outline',()=>definitionDomainModal(detail,key,label,prior)));
      body.append(line);
    }
    const implementation=el('div','detailsection');
    implementation.append(el('strong','','实施工作包逐项影响复核'));
    implementation.append(el('p','context','产品定义属于高层变更。即使关系图没有连接某个工作包，也必须独立判定该包是否受影响；无影响必须有证据和说明。'));
    const byPackage=Object.fromEntries((detail.package_reviews||[]).map(x=>[x.package_id,x]));
    if(!detail.packages?.length)implementation.append(el('p','context','目前无实施工作包；这不意味着没有尚未建模的工程实现。'));
    for(const pack of detail.packages||[]){
      const review=byPackage[pack.id],entry=el('div','item-row'),left=el('div');
      left.style.flex='1';
      add(left,el('strong','',pack.title+' · r'+pack.revision),
        el('div','tiny',review?review.outcome+' · '+(review.reviewed_revision===pack.revision?'当前修订':'修订已变更'):'尚未审查'));
      if(review)left.append(el('div','context',review.rationale.slice(0,180)));
      entry.append(left);
      if(c.state==='REVIEW')
        entry.append(button(review?'重新核对':'核对影响','outline',()=>definitionPackageReviewModal(detail,pack,review)));
      implementation.append(entry);
    }
    body.append(implementation);
    footer.append(button('打开研究任务与结论复核','outline',()=>definitionResearchModal(c.id)));
    footer.append(button('关闭','outline',closeModal));
    if(c.state==='REVIEW'){
      footer.append(button('拒绝候选','outline',()=>definitionDecisionModal(detail,false)));
      if(gate.ready)footer.append(button('进入正式发布审查','primary',()=>definitionDecisionModal(detail,true)));
    }
  });
}
function definitionPackageReviewModal(detail,pack,prior){
  showModal('实施包影响裁决 · '+pack.title,(body,footer)=>{
    body.append(note('系统不能因缺少依赖关系就断言无影响。必须逐包核对业务目标、设计输入、接口与代码。决定绑定当前实施包修订。'));
    const choice=selectField(body,'对该工作包的影响',[
      ['UNKNOWN','尚未查清，阻断产品定义发布'],
      ['IMPACT','受影响，发布后必须重新审核工作包'],
      ['NO_IMPACT','已证明不受影响，附事实依据']
    ],prior?.outcome||'UNKNOWN');
    const reason=field(body,'判断依据与继续行动',prior?.rationale||'','textarea');
    const evidence=field(body,'证据/版本来源（每行一个）',
      prior?.evidence_refs?.join('\n')||'','textarea');
    const reviewer=field(body,'审查人',prior?.reviewer||'项目负责人');
    const error=el('div','error hidden');body.append(error);
    footer.append(button('取消','outline',closeModal));
    footer.append(button('保存实施包影响判断','primary',async()=>{
      try{
        await req('/api/studio/governance/review-package',{
          change_id:detail.change.id,package_id:pack.id,
          expected_package_revision:pack.revision,
          outcome:choice.value,rationale:reason.value,
          evidence_refs:evidence.value.split('\n').map(x=>x.trim()).filter(Boolean),
          reviewer:reviewer.value,
          expected_base_digest:detail.change.base_digest
        });
        await definitionChangeModal(detail.change.id);
      }catch(e){error.textContent=e.message;error.classList.remove('hidden');}
    }));
  });
}

function definitionDomainModal(detail,domain,label,prior){
  showModal('影响评估 · '+label,(body,footer)=>{
    body.append(note('必须先在同次变更的研究队列确认本领域证据，才能保存影响结论。若研究后来重新开启，旧领域判断自动失效。缺失工程对象时需先研究和补建追踪关系。'));
    const outcome=selectField(body,'此变更对该领域的影响',[
      ['UNKNOWN','尚未查清（阻断发布）'],
      ['IMPACT','存在影响，须复核下游对象'],
      ['NO_IMPACT','已确认无影响，并有依据']
    ],prior?.outcome||'UNKNOWN');
    const rationale=field(body,'分析结论、候选方案、遗漏关系与判断理由',prior?.rationale||'','textarea');
    const evidence=field(body,'资料与证据引用（每行一个，必须可追溯）',
      prior?.evidence_refs?.join('\n')||'','textarea');
    const picked=new Set(Object.keys(prior?.affected_revisions||{}));
    const group=el('div','detailsection');group.append(el('strong','','确认受影响的工程对象'));
    group.append(el('p','context','当结论为 IMPACT 时选择对象；系统将从已审查关系向下游追踪，并阻止受影响的旧实施包继续执行。候审关系不会自动算确定影响。'));
    const filter=field(group,'筛选本项目对象');
    const listings=el('div');listings.style.maxHeight='230px';listings.style.overflowY='auto';group.append(listings);
    const renderList=()=>{
      clean(listings);let shown=0;
      for(const obj of (state.data?.objects||[])){
        if(filter.value&&!obj.title.toLowerCase().includes(filter.value.toLowerCase())&&!obj.id.includes(filter.value))continue;
        const l=el('label','');l.style.display='block';l.style.padding='4px';
        const box=el('input');box.type='checkbox';box.checked=picked.has(obj.id);
        box.onchange=()=>box.checked?picked.add(obj.id):picked.delete(obj.id);
        l.append(box,document.createTextNode(' '+groupLabel(obj.kind)+' · '+obj.title+' · r'+obj.revision));
        listings.append(l);if(++shown>=100)break;
      }
      if(!shown)listings.append(el('p','context','未找到对应对象；应先补齐工程关系。'));
    };
    filter.oninput=renderList;renderList();body.append(group);
    const reviewer=field(body,'审查人',prior?.reviewer||'项目负责人');
    const error=el('div','error hidden');body.append(error);
    footer.append(button('取消','outline',closeModal));
    footer.append(button('保存领域评估','primary',async()=>{
      try{
        await req('/api/studio/governance/review',{
          change_id:detail.change.id,domain,outcome:outcome.value,
          rationale:rationale.value,
          evidence_refs:evidence.value.split('\n').map(x=>x.trim()).filter(Boolean),
          affected_ids:outcome.value==='IMPACT'?[...picked]:[],
          reviewer:reviewer.value,expected_base_digest:detail.change.base_digest
        });
        await definitionChangeModal(detail.change.id);
      }catch(e){error.textContent=e.message;error.classList.remove('hidden');}
    }));
  });
}
function definitionDecisionModal(detail,approve){
  showModal(approve?'签发新产品定义基线':'拒绝产品定义变更',(body,footer)=>{
    body.append(note(approve?
      '这是影响广泛的工程决定。将替换当前项目定义文本、保存可追溯基线并把已识别受影响的旧实施包标记为待复核。本地批准不是 Git/GitHub 发布。':
      '拒绝候选将保留完整原因和审查历史，现役定义不变。'));
    const reviewer=field(body,'决策负责人','项目负责人');
    const reason=field(body,approve?'批准依据、接受的局限与后续审查约束':'拒绝理由','','textarea');
    const error=el('div','error hidden');body.append(error);
    footer.append(button('返回审查','outline',()=>definitionChangeModal(detail.change.id)));
    footer.append(button(approve?'确认发布本地定义新基线':'确认拒绝',
      approve?'primary':'outline',async()=>{
        try{
          const result=await req('/api/studio/governance/'+(approve?'publish':'reject'),{
            change_id:detail.change.id,reviewer:reviewer.value,
            ...(approve?{decision_note:reason.value,
                          expected_base_digest:detail.change.base_digest}
                      :{reason:reason.value})
          });
          await refresh(false);
          toast(approve?'本地定义版本已发布，受影响工作包待复核':'候选已拒绝');
          await definitionGovernanceModal();
        }catch(e){error.textContent=e.message;error.classList.remove('hidden');}
      }));
  });
}
function definitionRevalidateModal(row,obj){
  if(!obj){toast('工作包在本地视图中不存在，请先刷新');return;}
  showModal('产品定义变更后复核实施工作包',(body,footer)=>{
    body.append(note('仅清除这一次产品定义变更产生的复核阻塞；设计输入、CODE_READY、Git SHA、真实物理授权等其他检查不会放宽。'));
    add(body,el('strong','',obj.title+' · r'+obj.revision),
      el('p','context','受控定义变更：'+row.change_id));
    const reviewer=field(body,'复核负责人','项目负责人');
    const rationale=field(body,'核对设计与源码、接受/修订依据及有效版本','','textarea');
    const error=el('div','error hidden');body.append(error);
    footer.append(button('取消','outline',closeModal));
    footer.append(button('记录复核结论','primary',async()=>{
      try{
        await req('/api/studio/governance/resolve',{
          package_id:obj.id,change_id:row.change_id,
          expected_package_revision:obj.revision,reviewer:reviewer.value,
          rationale:rationale.value
        });
        await refresh(false);await definitionGovernanceModal();
      }catch(e){error.textContent=e.message;error.classList.remove('hidden');}
    }));
  });
}
async function renderGovernanceBanner(host){
  if(!state.project)return;
  try{
    const p=await req('/api/studio/governance/status?project='+qs(state.project));
    if(!host.isConnected)return;
    const panel=el('div','detailsection');
    const status=p.baseline_status==='BASELINED'?'有受控定义基线':
      p.baseline_status==='DRIFT'?'当前定义与批准版本不一致':'定义尚未经治理审查';
    add(panel,el('strong','',status),
      el('div','tiny','已批准版本 '+(p.baseline?.revision||'无')+
        ' · 当前在审请求 '+p.changes.filter(c=>c.state==='REVIEW').length));
    panel.append(button('打开定义与影响审查','outline',definitionGovernanceModal));
    host.append(panel);
  }catch(e){host.append(el('p','context','定义治理状态读取失败：'+e.message));}
}
