/* V0.9 product change research queue — a human-reviewable research ledger, not an AI chat pane. */
'use strict';
async function definitionResearchModal(changeId){
  let data, detail;
  try{
    [data,detail]=await Promise.all([
      req('/api/studio/governance/research?change='+qs(changeId)),
      req('/api/studio/governance/detail?id='+qs(changeId))
    ]);
  }catch(e){toast('研究任务加载失败：'+e.message);return;}
  const tasks=data.tasks||[],reviewed=tasks.filter(x=>x.state==='REVIEWED').length;
  const excluded=tasks.filter(x=>x.state==='NOT_APPLICABLE').length;
  showModal('产品定义变更 · 研究范围与证据复核',(body,footer)=>{
    body.append(note('内置领域是帮助 AI 不遗漏重要问题的研究视角，不是十篇固定报告。你可以根据具体变更决定哪些需要调查、哪些不适用，并补充新的研究问题；理由、证据与版本必须保留。'));
    const summary=el('div','detailsection');
    add(summary,el('strong','','已复核研究 '+reviewed+' 项 · 不适用并说明 '+excluded+' 项 · 待澄清 '+(tasks.length-reviewed-excluded)+' 项'),
       el('p','context','范围由工程事实和问题性质决定。将某方向判断为不适用不等于断言相关工程实现没有影响；证据引用仍须核查。'));
    body.append(summary);
    for(const task of tasks){
      const panel=el('div','detailsection'),line=el('div','sectionline');
      add(line,el('strong','',task.domain+' · '+(task.state==='REVIEWED'?'已复核':task.state==='NOT_APPLICABLE'?'已说明不适用':'待研究')),
        el('span','tiny','任务 r'+task.revision+' · 候选 '+task.submissions.length+' 份'));
      panel.append(line);
      panel.append(el('p','context',task.question));
      const active=task.submissions.find(s=>s.id===task.accepted_submission_id);
      if(task.state==='NOT_APPLICABLE'){
        const scoped=task.scope_decisions?.slice(-1)[0];
        if(scoped)panel.append(el('p','context','范围裁决：'+scoped.rationale+'\n依据：'+scoped.evidence_refs.join('；')));
      }
      if(active){
        panel.append(el('p','context','确认的研究结论：'+active.finding.slice(0,380)));
        panel.append(el('div','tiny','局限：'+active.limitations.slice(0,180)+
          ' · 来源 '+active.evidence_refs.length+' 项'));
      }else if(task.submissions.length)panel.append(el('p','context',
        '已有候选材料但尚未获得确认；不能自动视为该领域研究完成。'));
      const controls=el('div','buttonrow');
      if(detail.change.state==='REVIEW'){
        if(task.state==='OPEN'){
          controls.append(button('提交研究材料','outline',
            ()=>definitionResearchSubmitModal(changeId,task)));
          controls.append(button('说明本方向不适用','outline',
            ()=>definitionResearchScopeModal(changeId,task)));
        }
        else controls.append(button('重新开启研究','outline',
          ()=>definitionResearchReopenModal(changeId,task)));
        controls.append(button('查看候选与审查','outline',
          ()=>definitionResearchCandidateModal(changeId,task)));
      }
      panel.append(controls);body.append(panel);
    }
    footer.append(button('返回定义影响审查','outline',()=>definitionChangeModal(changeId)));
    if(detail.change.state==='REVIEW')
      footer.append(button('增加实际研究问题','outline',()=>definitionResearchNewTopicModal(changeId)));
  });
}
function definitionResearchSubmitModal(changeId,task){
  showModal('提交研究发现 · '+task.domain,(body,footer)=>{
    body.append(note('填写的是候选材料，不是结论审批。可链接已经经实验结果审查并作出 DECIDED 决定的技术研究 ID；系统会固定其研究修订。'));
    body.append(el('p','context','本次研究问题：'+task.question));
    const finding=field(body,'已查到的事实、不同观点与判断过程','','textarea');
    const refs=field(body,'可追溯来源（每行一个）','','textarea');
    const limits=field(body,'未解决问题、适用范围与局限','','textarea');
    const study=field(body,'已批准技术研究 ID（可留空）','');
    const author=field(body,'提交人','研究协作者');
    const err=el('div','error hidden');body.append(err);
    footer.append(button('返回','outline',()=>definitionResearchModal(changeId)));
    footer.append(button('提交候选材料','primary',async()=>{
      try{
        await req('/api/studio/governance/research/submit',{
          task_id:task.id,expected_revision:task.revision,
          finding:finding.value,
          evidence_refs:refs.value.split('\n').map(x=>x.trim()).filter(Boolean),
          limitations:limits.value,linked_study_id:study.value||null,
          author:author.value
        });
        toast('候选研究材料已保存；待负责人复核');
        await definitionResearchModal(changeId);
      }catch(e){err.textContent=e.message;err.classList.remove('hidden');}
    }));
  });
}
function definitionResearchCandidateModal(changeId,task){
  showModal('研究候选与复核 · '+task.domain,(body,footer)=>{
    body.append(note('这是研究材料的审查记录。确认前应自行检查来源、配置、实验结论和遗漏的问题；AI 提交的材料默认不可信，不会直接进入正式设计基线。'));
    if(!task.submissions.length)body.append(el('p','context','尚无提交材料。先收集资料或复用专业技术研究结果。'));
    for(const candidate of task.submissions){
      const panel=el('div','detailsection');
      add(panel,el('strong','',candidate.origin_role+' · '+candidate.author),
        el('div','tiny','提交于 '+candidate.created+' · task r'+candidate.task_revision),
        el('p','context','发现：'+candidate.finding),
        el('p','context','已知局限：'+candidate.limitations),
        el('p','context','来源：'+candidate.evidence_refs.join('；')));
      if(candidate.linked_study_id)panel.append(el('div','tiny',
        '技术调查 '+candidate.linked_study_id+' / r'+candidate.linked_study_revision));
      if(task.state==='OPEN' && candidate.task_revision===task.revision)
        panel.append(button('复核这份候选','outline',
          ()=>definitionResearchReviewModal(changeId,task,candidate)));
      body.append(panel);
    }
    footer.append(button('返回任务列表','outline',()=>definitionResearchModal(changeId)));
  });
}
function definitionResearchReviewModal(changeId,task,candidate){
  showModal('确认研究证据 · '+task.domain,(body,footer)=>{
    body.append(note('只有负责人可在本地提交审查。选择“继续研究”会开启下一修订，原候选保留历史但不能在新修订中直接复用。'));
    body.append(el('p','context',candidate.finding));
    const verdict=selectField(body,'复核结论',[
      ['REQUEST_MORE','证据不足，继续研究'],['ACCEPT','核对后接受本次研究结论']
    ],'REQUEST_MORE');
    const reviewer=field(body,'复核人','项目负责人');
    const rationale=field(body,'已核实内容、冲突处理和结论依据','','textarea');
    const err=el('div','error hidden');body.append(err);
    footer.append(button('返回','outline',()=>definitionResearchCandidateModal(changeId,task)));
    footer.append(button('保存复核决定','primary',async()=>{
      try{
        await req('/api/studio/governance/research/review',{
          task_id:task.id,submission_id:candidate.id,
          expected_revision:task.revision,decision:verdict.value,
          reviewer:reviewer.value,rationale:rationale.value
        });
        toast(verdict.value==='ACCEPT'?'研究结论已记录；仍需领域影响判断':'已要求继续研究');
        await definitionResearchModal(changeId);
      }catch(e){err.textContent=e.message;err.classList.remove('hidden');}
    }));
  });
}
function definitionResearchReopenModal(changeId,task){
  showModal('重新调查 · '+task.domain,(body,footer)=>{
    body.append(note('新的证据或更高层级工程变更可能推翻既有研究。重新开启将使本领域既有影响评估失效，必须重新评估。'));
    const reviewer=field(body,'提出重新调查的负责人','项目负责人');
    const why=field(body,'新的事实、冲突或旧研究不足之处','','textarea');
    const err=el('div','error hidden');body.append(err);
    footer.append(button('取消','outline',()=>definitionResearchModal(changeId)));
    footer.append(button('重新开启并失效旧结论','primary',async()=>{
      try{
        await req('/api/studio/governance/research/reopen',{
          task_id:task.id,expected_revision:task.revision,
          reviewer:reviewer.value,rationale:why.value
        });
        toast('研究任务已重新开启；相关领域判断必须重新审查');
        await definitionResearchModal(changeId);
      }catch(e){err.textContent=e.message;err.classList.remove('hidden');}
    }));
  });
}

function definitionResearchScopeModal(changeId,task){
  showModal('研究范围判断 · '+task.domain,(body,footer)=>{
    body.append(note('这是对本次问题研究范围的解释，不是自动判定任何代码“无影响”。不要因为缺少关联对象就轻易宣布不适用。'));
    body.append(el('p','context','本方向的启发问题：'+task.question));
    const why=field(body,'为什么这个方向与当前变更无直接关系？','','textarea');
    const refs=field(body,'支持该判断的工程事实或讨论来源（每行一个）','','textarea');
    const reviewer=field(body,'负责人','项目负责人');
    const err=el('div','error hidden');body.append(err);
    footer.append(button('返回研究','outline',()=>definitionResearchModal(changeId)));
    footer.append(button('记录范围判断','primary',async()=>{
      try{
        await req('/api/studio/governance/research/scope-out',{
          task_id:task.id,expected_revision:task.revision,
          reviewer:reviewer.value,rationale:why.value,
          evidence_refs:refs.value.split('\n').map(x=>x.trim()).filter(Boolean)
        });
        await definitionResearchModal(changeId);
      }catch(e){err.textContent=e.message;err.classList.remove('hidden');}
    }));
  });
}
function definitionResearchNewTopicModal(changeId){
  showModal('增加当前工程真正需要研究的问题',(body,footer)=>{
    body.append(note('不要求只能从预置领域中选择。研究问题应来自当前事实、争议或尚未解决的工程关系，并解释为什么需要。'));
    const question=field(body,'需要弄清楚的问题','','textarea');
    const reason=field(body,'此问题为什么对当前工程变更有意义','','textarea');
    const reviewer=field(body,'负责人','项目负责人');
    const err=el('div','error hidden');body.append(err);
    footer.append(button('返回研究','outline',()=>definitionResearchModal(changeId)));
    footer.append(button('加入研究范围','primary',async()=>{
      try{
        await req('/api/studio/governance/research/add-topic',{
          change_id:changeId,question:question.value,
          rationale:reason.value,reviewer:reviewer.value
        });
        await definitionResearchModal(changeId);
      }catch(e){err.textContent=e.message;err.classList.remove('hidden');}
    }));
  });
}
