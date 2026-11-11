/* Read-only plan queue driven by approved dependencies and actual CODE_READY status. */
'use strict';
function engineeringPlanModel(brief){
  const objects=Array.isArray(brief.engineering_objects)?brief.engineering_objects:[];
  const relations=Array.isArray(brief.engineering_relations)?brief.engineering_relations:[];
  const tasks=Array.isArray(brief.execution_tasks)?brief.execution_tasks:[];
  const readiness=brief.implementation_readiness||{};
  const indexed=new Map(objects.map(o=>[o.id,o]));
  const executions=new Map(tasks.map(t=>[t.object_id,t]));
  const milestones=objects.filter(o=>o.kind==='milestone').map(o=>({
    object:o,due:(o.meta||{}).due||'',state:o.state
  }));
  const packages=objects.filter(o=>o.kind==='work_package').map(o=>{
    const dependencies=relations.filter(r=>r.target_id===o.id&&r.kind==='depends')
      .map(r=>({source:indexed.get(r.source_id),relation:r}))
      .filter(d=>!!d.source);
    const unresolved=dependencies.filter(d=>d.relation.status!=='APPROVED');
    const check=readiness[o.id]||{ready:false,reasons:['准入状态未知；需要实时核查']};
    const execution=executions.get(o.id)||null;
    const state=execution?.effective_state||'NOT_LINKED';
    return {object:o,dependencies,unresolved,check,execution,state,
      action:(state==='BLOCKED_BY_CHANGED_DESIGN'||state==='BLOCKED_BY_PRODUCT_DEFINITION')?'REVALIDATE':
        unresolved.length?'REVIEW_DEPENDENCIES':
        !check.ready?'COMPLETE_DESIGN':
        !execution?'BIND_GITHUB':
        state==='PLANNING'||state==='REVOKED'?'REVIEW_DISPATCH':
        'INSPECT_EXECUTION'};
  });
  const priority={REVALIDATE:0,REVIEW_DEPENDENCIES:1,COMPLETE_DESIGN:2,
                  BIND_GITHUB:3,REVIEW_DISPATCH:4,INSPECT_EXECUTION:5};
  packages.sort((a,b)=>priority[a.action]-priority[b.action]||
    a.object.title.localeCompare(b.object.title));
  return {milestones,packages,
    note:'队列仅依据人工登记的计划、CODE_READY 和最近同步的执行事实；没有工期预测或自动完成判定。'};
}
const PLAN_ACTIONS={
  REVIEW_DEPENDENCIES:'审查任务输入关系',
  COMPLETE_DESIGN:'补齐软件设计/实施准入',
  BIND_GITHUB:'关联真实 GitHub Issue',
  REVALIDATE:'工程基线变化或失效，需复核旧派发',
  REVIEW_DISPATCH:'人工决定是否派发',
  INSPECT_EXECUTION:'核对任务、PR 和实际验证'
};
function renderEngineeringPlan(brief,mount){
  const plan=engineeringPlanModel(brief);
  const sec=el('div','panel');sec.style.marginTop='16px';
  sec.append(el('h2','','里程碑与工程任务队列'));
  sec.append(el('p','muted',plan.note));
  const milestones=el('div','detailsection');
  milestones.append(el('h3','','已登记里程碑'));
  if(!plan.milestones.length)milestones.append(el('p','muted','尚未记录里程碑。这不是项目没有计划的证明。'));
  for(const m of plan.milestones){
    const line=el('div','item-row'),inner=el('div');
    add(inner,el('strong','',m.object.title),el('div','tiny','日期 '+(m.due||'未指定')+
      ' · 登记状态 '+m.state+'；不代表交付完成'));
    add(line,inner,button('打开','outline',()=>openItem(m.object)));
    milestones.append(line);
  }
  sec.append(milestones);
  const queue=el('div','detailsection');
  queue.append(el('h3','','执行与阻塞队列'));
  if(!plan.packages.length)queue.append(el('p','muted','尚无正式实施工作包。需先完成需求、方案及详细设计。'));
  for(const t of plan.packages){
    const line=el('div','item-row'),inner=el('div');inner.style.flex='1';
    add(inner,el('strong','',t.object.title+' · r'+t.object.revision),
      el('div','context','下一步：'+PLAN_ACTIONS[t.action]));
    if(t.dependencies.length)inner.append(el('div','tiny','输入依赖：'+
      t.dependencies.map(d=>d.source.title+'（'+d.relation.status+'）').join('；')));
    if(t.check.reasons?.length)inner.append(el('div','tiny',
      '准入限制：'+t.check.reasons.slice(0,2).join('；')));
    inner.append(el('div','tiny','GitHub/派发：'+t.state+
      '；工作包批准不等于代码集成或实测验收'));
    line.append(inner);
    const buttons=el('div','buttonrow');
    buttons.append(button('查看工作包','outline',()=>openItem(t.object)));
    buttons.append(button('实施准入','outline',()=>showReadiness(t.object)));
    line.append(buttons);queue.append(line);
  }
  sec.append(queue);mount.append(sec);
}
