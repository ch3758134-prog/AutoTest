/* V0.7 change workbench: explicit impact approval, target-revision assessment and closure. */
'use strict';
async function changeReviewModal(changeId){
  let data;
  try{data=await req('/api/studio/change/review?id='+qs(changeId));}
  catch(e){toast('无法加载变更评估：'+e.message);return;}
  const change=data.change;
  showModal('变更影响评估 · '+change.title,(body,footer)=>{
    body.append(note('先创建“变更 → 目标对象”的 impacts 关系，审查关系，再逐项选择 REVISE / NO_CHANGE / INVESTIGATE。决定只记录影响评估，不自动改设计、不自动派发代码。'));
    body.append(el('div','tiny','变更 r'+change.revision+' · '+change.state+'；每次提交后重新读取版本，防止覆盖其他评估。'));
    if(!data.targets.length){
      body.append(el('p','context','当前没有明确的直接影响关系。先添加候选影响关系，再完成关系审查。'));
      body.append(button('增加影响关联','outline',()=>{closeModal();relationModal(change);}));
    }
    let incomplete=0;
    for(const t of data.targets){
      const panel=el('div','detailsection');
      const h=el('div','sectionline');
      add(h,el('strong','',t.target.title+' · r'+t.target.revision),badge(t.relation.status));
      panel.append(h);
      const a=t.assessment,valid=t.relation.status==='APPROVED'&&
          a.target_revision===t.target.revision&&!!a.rationale&&!!a.outcome;
      if(!valid)incomplete++;
      panel.append(el('div','context',valid?
        '当前评估：'+a.outcome+' · '+a.reviewer+' · '+a.rationale:
        (t.stale?'目标修订已变化，旧评估失效，必须重新确认':
        (t.relation.status!=='APPROVED'?'影响关联尚未批准，不能填写正式评估':'尚未评估此目标的实际影响'))));
      const buttons=el('div','buttonrow');
      buttons.append(button('打开目标对象','outline',()=>{closeModal();openItem(item(t.target.id));}));
      if(t.relation.status!=='APPROVED')
        buttons.append(button('审查此影响关系','outline',()=>{closeModal();relationApprovalModal(t.relation);}));
      if(t.relation.status==='APPROVED'&&change.state==='OPEN')
        buttons.append(button('评估此目标','primary',()=>changeAssessModal(change,t)));
      panel.append(buttons);body.append(panel);
    }
    if(change.state==='OPEN'&&data.targets.length&&incomplete===0)
      footer.append(button('完成评估并关闭变更','primary',()=>{closeModal();approvalModal(change,true);}));
    else if(change.state==='OPEN')
      footer.append(el('span','tiny','还有 '+incomplete+' 项尚未完成有效评估，不允许关闭。'));
    footer.append(button('关闭','outline',closeModal));
  });
}
function changeAssessModal(change,target){
  showModal('逐项评估影响 · '+target.target.title,(body,footer)=>{
    body.append(note('这是一个工程判断，不是自动推断。评估绑定目标 r'+target.target.revision+
      ' 和当前变更 r'+change.revision+'。目标设计随后修订时，此判断必须重新核对。'));
    body.append(el('p','context','目标：'+target.target.title+' · '+groupLabel(target.target.kind)));
    const outcome=selectField(body,'评估结论',[
      ['INVESTIGATE','需要继续调查'],['REVISE','需要修订'],['NO_CHANGE','确认不需修改']
    ],target.assessment.outcome||'INVESTIGATE');
    const actor=field(body,'评估人',target.assessment.reviewer||'项目负责人');
    const rationale=field(body,'依据与处理决定',target.assessment.rationale||'','textarea');
    const error=el('div','error hidden');body.append(error);
    footer.append(button('取消','outline',closeModal));
    footer.append(button('记录影响结论','primary',async()=>{
      try{
        await req('/api/studio/change/assess',{
          change_id:change.id,target_id:target.target.id,
          expected_change_revision:change.revision,
          expected_target_revision:target.target.revision,
          outcome:outcome.value,reviewer:actor.value,rationale:rationale.value
        });
        await refresh(false);
        toast('目标修订绑定的影响评估已记录');
        await changeReviewModal(change.id);
      }catch(e){error.textContent=e.message;error.classList.remove('hidden');}
    }));
  });
}
