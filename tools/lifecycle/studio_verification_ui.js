/* Verified separation of test specification, run assertion and human evidence review. */
'use strict';
const VERIFICATION_LEVELS=[
  ['UNIT','单元测试'],['INTEGRATION','软件集成'],['BENCH','真实台架'],['USER_ACCEPTANCE','用户验收']
];
async function capabilityVerificationModal(model){
  let all;
  try{all=await req('/api/studio/verification?project='+qs(state.project));}
  catch(e){toast('验证证据加载失败：'+e.message);return;}
  showModal(model.cap.title+' · 版本化验证证据',(body,footer)=>{
    body.append(note('只列出此能力沿已批准追踪关系关联的测试规格及其执行记录。不以最新一条 PASS 覆盖历史 FAIL；不将人工证据确认当作整项能力验收。'));
    for(const test of model.tests){
      const panel=el('div','detailsection');
      const h=el('div','sectionline');add(h,el('strong','',test.title+' · 规格 r'+test.revision),badge(test.state));
      panel.append(h);
      const runs=all.runs.filter(x=>x.test_object_id===test.id);
      if(!runs.length)panel.append(el('p','context','无执行记录或尚未归档。'));
      for(const run of runs){
        const line=el('div','event');
        const changed=run.test_revision!==test.revision;
        add(line,el('strong','',run.level+' · '+run.outcome+' · '+(run.review_decision||'UNREVIEWED')),
          el('small','',run.created+' · 规格 r'+run.test_revision+(changed?'（历史规格）':'')+
            ' · '+run.configuration+' · '+run.source_sha.slice(0,12)));
        panel.append(line);
      }
      panel.append(button('打开测试执行与证据','outline',()=>verificationHistoryModal(test)));
      body.append(panel);
    }
    footer.append(button('关闭','outline',closeModal));
  });
}

async function verificationHistoryModal(test){
  let result;
  try{result=await req('/api/studio/verification?project='+qs(state.project));}
  catch(e){toast('执行记录加载失败：'+e.message);return;}
  const runs=result.runs.filter(r=>r.test_object_id===test.id);
  showModal('执行证据记录 · '+test.title,(body,footer)=>{
    body.append(note('测试规格 ≠ 已运行。执行人填写的结论尚不是独立认证的结果；只有经过人工审查的证据才有审查状态。实物验收还需对应硬件版本、配置与原始记录。'));
    if(!runs.length)body.append(el('p','context','暂无绑定该测试规格的执行记录。未执行或未归档，不等于通过或失败。'));
    for(const run of runs){
      const card=el('div','detailsection');
      const h=el('div','sectionline');add(h,el('strong','',run.level+' · '+run.outcome),
        badge(run.review_decision||'UNREVIEWED'));card.append(h);
      card.append(el('div','tiny','测试规格 r'+run.test_revision+' · '+run.created+
          ' · 执行人 '+run.actor));
      card.append(el('p','context','配置：'+run.configuration+
          '\n环境与设备身份：'+run.environment+
          '\nGit SHA：'+(run.source_sha||'未提供，需核对硬件/配置身份')+
          '\n证据引用：'+run.evidence_ref));
      if(run.review_decision){
        card.append(el('p','context','证据审查：'+run.review_decision+
          ' · '+run.reviewer+' · '+run.review_reason));
      }else{
        card.append(button('审查证据','outline',()=>verificationReviewModal(test,run)));
      }
      body.append(card);
    }
    footer.append(button('登记本次执行记录','primary',()=>verificationRecordModal(test)));
    footer.append(button('关闭','outline',closeModal));
  });
}
function verificationRecordModal(test){
  showModal('登记测试执行 · '+test.title,(body,footer)=>{
    body.append(note('只能记录已经实际执行的测试。必须填写真实证据引用和完整代码提交 SHA。系统不根据文字自动验证原始日志，更不会自动签发验收。'));
    const level=selectField(body,'执行层级',VERIFICATION_LEVELS);
    const outcome=selectField(body,'执行观察结论',[
      ['PASS','观察通过'],['FAIL','观察失败'],['INCONCLUSIVE','无法判断']]);
    const config=field(body,'产品配置/型号/软件版本');
    const sha=field(body,'软件源码 Git 40 位 SHA（单测/集成必填；无软件的实物测试可留空）');
    const environment=field(body,'测试环境、硬件/固件身份及关键条件','','textarea');
    const evidence=field(body,'原始日志、波形或证据文件引用','','textarea');
    const actor=field(body,'实际执行人','项目负责人');
    const error=el('div','error hidden');body.append(error);
    footer.append(button('取消','outline',closeModal));
    footer.append(button('保存不可覆盖的执行记录','primary',async()=>{
      try{
        await req('/api/studio/verification/record',{
          test_object_id:test.id,test_revision:test.revision,
          level:level.value,outcome:outcome.value,configuration:config.value,
          source_sha:sha.value,environment:environment.value,
          evidence_ref:evidence.value,actor:actor.value
        });
        toast('执行记录已保存，仍待独立证据审查');
        await verificationHistoryModal(test);
      }catch(e){error.textContent=e.message;error.classList.remove('hidden');}
    }));
  });
}
function verificationReviewModal(test,run){
  showModal('证据审查 · '+run.id.slice(0,12),(body,footer)=>{
    body.append(note('这是证据的人工审查，不自动修改需求或能力的验收状态；PASS 不代表已完成生产或物理验收。'));
    body.append(el('p','context',run.test_object_id+' / r'+run.test_revision+
      '\n'+run.level+' / '+run.outcome+
      '\n'+run.evidence_ref+'\n'+run.environment));
    const decision=selectField(body,'证据审查结论',[
      ['CONFIRMED','确认本条证据记录'],['REJECTED','证据不充分或不适用']]);
    const reviewer=field(body,'审查人','项目负责人');
    const rationale=field(body,'证据适用性、局限及审查理由','','textarea');
    const error=el('div','error hidden');body.append(error);
    footer.append(button('取消','outline',closeModal));
    footer.append(button('提交证据审查','primary',async()=>{
      try{
        await req('/api/studio/verification/review',{
          id:run.id,decision:decision.value,
          reviewer:reviewer.value,rationale:rationale.value
        });
        toast('证据审查已记录');
        await verificationHistoryModal(test);
      }catch(e){error.textContent=e.message;error.classList.remove('hidden');}
    }));
  });
}
