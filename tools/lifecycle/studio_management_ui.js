/* Real engineering management review; suggestions are not approvals or tasks. */
'use strict';
const CATEGORY_LABELS={PRODUCT_DEFINITION_REVIEW:'产品定义与影响再研究',
  REQUIREMENT_CLARIFICATION:'需求澄清', TECHNICAL_RESEARCH:'技术预研',
  ARCHITECTURE_REVIEW:'架构复核',DETAILED_DESIGN:'软件详细设计',
  CODING:'编码提案', INTEGRATION:'集成计划',VERIFICATION:'验证安排',
  SCHEDULE:'计划调整',BLOCKER:'阻塞处理'
};
async function renderManagementView(){
  clean(root);
  heading('研发管理与 AI 建议审查',
    '基于工程对象、设计准入、真实 GitHub 快照和实验结果来确定下一步；AI 只提出建议，关键设计与代码派发均须独立审批。');
  root.append(note('项目管理 AI 的建议不会自动成为正式需求、已批准架构或 CODE_READY 任务。所有建议都需要明确来源、修改范围与人类审查记录。'));
  const options=el('div','buttonrow');
  options.append(button('复制项目管理上下文','outline',async()=>{
    const brief=await req('/api/studio/management/context?project='+qs(state.project));
    await navigator.clipboard.writeText(JSON.stringify(brief,null,2));
    toast('项目上下文已复制：包含来源/修订/GitHub 同步时间。');
  }));
  options.append(button('核对 Git 工程版本','outline',managementGitBaselineModal));
  options.append(button('产品定义审查与版本门禁','outline',definitionGovernanceModal));
  options.append(button('工程概念说明与语义判断','outline',()=>engineeringSemanticsModal()));
  options.append(button('录入管理建议','primary',managementProposeModal));
  root.append(options);
  const mount=el('div','stack');
  mount.append(el('p','muted','正在汇总工程事实和待审查建议……'));root.append(mount);
  let result, proposals;
  try{
    [result,proposals]=await Promise.all([
      req('/api/studio/management/context?project='+qs(state.project)),
      req('/api/studio/management/proposals?project='+qs(state.project))
    ]);
  }catch(e){mount.textContent='管理资料加载失败：'+e.message;return;}
  if(state.view!=='management')return;
  clean(mount);
  const info=el('div','panel');
  const title=el('div','sectionline');
  add(title,el('h2','','当前项目的可信来源'),el('span','tiny','上下文指纹 '+result.content_sha256.slice(0,12)));
  info.append(title);
  const facts=el('div','meta-table');
  add(facts,
    managementFact('工程设计',result.engineering_objects.length+' 项对象（不代表已交付）'),
    managementFact('产品定义基线',result.product_definition_governance.baseline_status+
      ' · ' +(result.product_definition_governance.baseline?.revision||'无批准版本')),
    managementFact('产品定义待审变更',result.product_definition_governance.changes.filter(
      x=>x.state==='REVIEW').length+' 项'),
    managementFact('待确认的 AI 定义候选',result.product_definition_suggestions.filter(
      x=>x.state==='PENDING').length+' 项（不阻止正式研发）'),
    managementFact('产品定义研究待复核',result.product_definition_research_tasks.filter(
      x=>x.state!=='REVIEWED').length+' / '+result.product_definition_research_tasks.length+' 项'),
    managementFact('GitHub 状态',result.github.repository?
      result.github.repository+' · 最近同步 '+(result.github.fetched_at||'未知'):'尚未连接 GitHub'),
    managementFact('研究问题',result.technical_studies.length+' 项'),
    managementFact('执行证据',result.verification_runs.length+' 条（独立于测试规格）'),
    managementFact('待审证据',result.verification_runs.filter(x=>!x.review_decision).length+' 条'),
    managementFact('已确认失败',result.verification_runs.filter(x=>x.review_decision==='CONFIRMED'&&x.outcome==='FAIL').length+' 条'),
    managementFact('待处理阻塞',result.blockers.length+' 项'));
  info.append(facts);
  if(result.github.error)info.append(el('div','reason','GitHub 同步出错：'+result.github.error));
  if(!result.github.fetched_at)info.append(note('GitHub 未同步。AI 不得根据本地对象数量推断远端项目完成状态。'));
  info.append(el('p','tiny','执行证据由本地人工登记；证据审查与真实产品验收必须分别判断。'))
  if(result.product_definition_governance.baseline_status!=='BASELINED')
    info.append(note('产品定义尚未建立有效的受控基线；重大变更及其影响领域必须审查，编码派发被拦截。'));
  mount.append(info);
  const problems=el('div','panel');
  const ph=el('div','sectionline');add(ph,el('h2','','工程阻塞与需要补齐的条件'));
  problems.append(ph);
  if(!result.blockers.length)problems.append(el('p','muted','当前工程对象没有触发已知阻塞规则；这不表示项目全部完成。'));
  for(const problem of result.blockers.slice(0,30)){
    const row=el('div','item-row');
    const x=el('div');x.style.flex='1';
    add(x,el('strong','',problem.type),el('div','context',problem.reason));
    row.append(x);
    if(problem.object_id)row.append(button('查看对象','outline',()=>{
      const target=item(problem.object_id);
      if(target){navigate(target.kind==='work_package'?'delivery':'engineering');openItem(target);}
    }));
    if(problem.type==='PRODUCT_DEFINITION_RESEARCH'&&problem.change_id)
      row.append(button('处理研究','outline',
        ()=>definitionResearchModal(problem.change_id)));
    if(problem.type==='PRODUCT_DEFINITION_CHANGE'&&problem.change_id)
      row.append(button('查看变更门禁','outline',
        ()=>definitionChangeModal(problem.change_id)));
    problems.append(row);
  }
  mount.append(problems);
  const inbox=el('div','panel');
  const header=el('div','sectionline');add(header,el('h2','','AI 项目管理提案审查'),
    el('span','tiny',proposals.filter(x=>x.status==='PENDING').length+' 项待处理'));
  inbox.append(header);
  inbox.append(el('p','muted','AI 可以通过专用只读/提案接口协助整理计划和分工，但不得直接修改当前架构、执行授权或通过验收。'));
  if(!proposals.length)inbox.append(el('p','muted','当前无 AI 管理建议。可复制项目管理上下文给项目管理 AI，之后通过受限 CLI 提交待审查提案。'));
  for(const entry of proposals){
    const panel=el('div','detailsection');
    const h=el('div','sectionline');
    add(h,el('strong','',entry.title),badge(entry.status));
    panel.append(h);
    panel.append(el('div','tiny',(CATEGORY_LABELS[entry.category]||entry.category)+
      ' · '+entry.priority+' · '+entry.proposed_by+' · '+entry.created));
    panel.append(el('p','context','依据：'+entry.rationale));
    panel.append(el('p','context','建议行动：'+entry.next_action));
    if(entry.source_objects.length){
      const sources=el('div','tiny','引用工程对象：'+entry.source_objects.map(id=>itemName(id)).join('；'));
      panel.append(sources);
    }
    if(entry.status==='PENDING'){
      const controls=el('div','buttonrow');
      controls.append(button('纳入计划候选','primary',
        ()=>managementReviewModal(entry,'ACCEPT_FOR_PLANNING')));
      controls.append(button('需要补充依据','outline',
        ()=>managementReviewModal(entry,'NEEDS_INFORMATION')));
      controls.append(button('拒绝建议','outline',
        ()=>managementReviewModal(entry,'REJECT')));
      panel.append(controls);
    }else panel.append(el('div','tiny','审查：'+entry.reviewer+' / '+entry.review_note+
      ' · '+entry.reviewed_at));
    inbox.append(panel);
  }
  mount.append(inbox);
  renderEngineeringPlan(result,mount);
  mount.append(note('注意：纳入计划候选不会自动新增已批准需求、设计、实验或编码任务。正式实施仍须经软件详细设计、CODE_READY 和 GitHub 执行门禁。'));
}
function managementFact(label,text){
  const box=el('div','meta-cell');
  add(box,el('label','',label),el('div','context',text));return box;
}
function managementReviewModal(entry,decision){
  showModal('审查 AI 项目管理建议',(body,footer)=>{
    body.append(note('此处是负责人评估管理建议，不会自动修改工程设计或派发代码。'));
    body.append(el('p','context',entry.title+'：'+entry.next_action));
    const reviewer=field(body,'审查人','项目负责人');
    const reason=field(body,'审查判断与实际后续安排','','textarea');
    modalActions(footer,async()=>{
      await req('/api/studio/management/review',{
        id:entry.id,decision,reviewer:reviewer.value,note:reason.value
      });
      toast('管理建议已审查；仍未自动改变正式工程基线。');
      await renderManagementView();
    },'提交审查结论');
  });
}
function managementProposeModal(){
  showModal('记录项目管理建议',(body,footer)=>{
    body.append(note('这只登记待审查建议，不能批准详细设计或直接派发 AI 编码。'));
    const title=field(body,'建议标题');
    const category=selectField(body,'工作性质',
      Object.entries(CATEGORY_LABELS).map(([key,label])=>[key,label]));
    const priority=selectField(body,'建议优先级',[['P0','P0'],['P1','P1'],['P2','P2'],['P3','P3']],'P1');
    const rationale=field(body,'依据、风险及约束','','textarea');
    const nextAction=field(body,'下一步建议及预期交付','','textarea');
    modalActions(footer,async()=>{
      await req('/api/studio/management/propose',{
        project_id:state.project,category:category.value,priority:priority.value,
        title:title.value,rationale:rationale.value,next_action:nextAction.value,
        source_objects:[],proposed_by:'本地负责人'
      });
      toast('已建立待审查管理建议');
      await renderManagementView();
    },'登记建议');
  });
}

function managementGitBaselineModal(){
  showModal('Git 工程基线差异审查',(body,footer)=>{
    body.append(note('读取的是 --repo 对应工作树的 Git 已提交 HEAD 快照，而不是未提交的文件。只比较，不覆盖任何本地工程记录，也不自动相信 Git 提交等于负责人批准。'));
    const result=el('div','panel','正在检查已提交 Git 快照……');
    body.append(result);
    const revision=field(body,'指定提交 SHA（可选；默认 HEAD）','HEAD');
    const check=async()=>{
      result.textContent='正在从 Git 精确提交读取工程档案……';
      try {
        const data=await req('/api/studio/baseline/preview?project='+
          qs(state.project)+'&revision='+qs(revision.value.trim()||'HEAD'));
        clean(result);
        result.append(el('h3','','Git 对照：'+data.state));
        result.append(el('div','tiny',data.repository+' · '+data.git_commit));
        result.append(el('p','context','Git 工程文件：'+data.source_path));
        result.append(el('p','context','两边对象：Git '+data.object_count_git+
          ' 项；本地 '+data.object_count_local+' 项；差异 '+data.differences.length+' 项'));
        if(!data.on_local_main_history)
          result.append(el('div','reason','此提交不在本地已知 main 历史中，不能视为正式批准的工程基线。'));
        if(!data.differences.length)result.append(el('p','','对象内容未发现差异；这不等于完成实测验收。'));
        for(const difference of data.differences.slice(0,40)){
          result.append(el('p','context',difference.comparison+' · '+difference.title+
            ' · Git r'+(difference.revision_in_git??'-')+
            ' / 本地 r'+(difference.revision_local??'-')));
        }
        result.append(el('div','tiny','比较只读，未自动同步。'+data.note));
      }catch(error) {
        clean(result);result.append(el('div','reason',
          '暂时无法对照：'+error.message+
          '。应先在非 main 的专业分支执行“项目归档”，经审查 Commit/Push 后再对照。'));
      }
    };
    footer.append(button('重新比较','outline',check));
    footer.append(button('关闭','primary',closeModal));
    check();
  });
}
