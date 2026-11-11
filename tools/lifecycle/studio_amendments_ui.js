/* AI engineering amendments — review against the actual controlled object. */
'use strict';
const AMEND_STATE={CANDIDATE:'等待工程审查',READY_TO_APPLY:'已复核，待生成新草稿',
 RESEARCH_NEEDED:'需继续研究',REJECTED:'未采纳',APPLIED:'已形成对象新修订'};
async function amendmentsWorkbench(){
 let data;
 try{data=await req('/api/studio/amendments?project='+qs(state.project))}
 catch(e){toast('读取修订建议失败：'+e.message);return}
 showModal('工程修订建议 · AI 候选',(body,footer)=>{
   body.append(note('AI 的建议只指向已存在的真实工程对象及固定修订，不会另建一套需求或直接修改已批准设计。'));
   add(body,el('strong','','待处理 '+data.items.filter(x=>['CANDIDATE','READY_TO_APPLY'].includes(x.state)).length+
       ' 项 · 历史 '+data.items.length+' 项'));
   for(const r of data.items){
     const panel=el('div','detailsection');
     add(panel,el('strong','',(r.target?.title||r.target_id)+' · '+(AMEND_STATE[r.state]||r.state)),
       el('div','tiny',r.target?.kind+' · 基于 r'+r.base_revision+
          ' / 当前 r'+r.target?.revision+(r.stale?' · 过期，需重新提出':'')),
       el('p','context',r.reasoning.slice(0,450)));
     panel.append(button('查看原文、候选及审查','outline',()=>amendmentDetailModal(r.id)));
     body.append(panel);
   }
   if(!data.items.length)body.append(el('p','context','暂无修订候选。项目管理 AI 可通过受限接口按对象 ID 和当前修订提交建议。'));
   footer.append(button('关闭','outline',closeModal));
   footer.append(button('查看工程认识','outline',knowledgeWorkbench));
 });
}
function amendmentDiff(body,field,before,after){
 const panel=el('div','detailsection');
 panel.append(el('strong','',({title:'标题',body:'工程内容',meta:'设计合同与元数据',parent_id:'上级对象'})[field]||field));
 const cols=el('div','twocol');
 for(const [label,value] of [['原始版本',before],['候选版本',after]]){
   const cell=el('div','');cell.style.minWidth='0';
   cell.append(el('div','tiny',label));
   const pre=el('pre','');pre.style.cssText='white-space:pre-wrap;word-break:break-word;max-height:370px;overflow:auto;border:1px solid var(--line);border-radius:8px;padding:10px';
   pre.textContent=typeof value==='string'?value:JSON.stringify(value,null,2);
   cell.append(pre);cols.append(cell);
 }
 panel.append(cols);body.append(panel);
}
async function amendmentDetailModal(id){
 let r;
 try{r=await req('/api/studio/amendments/detail?id='+qs(id))}
 catch(e){toast('读取候选失败：'+e.message);return}
 showModal('工程修订候选 · '+r.target.title,(body,footer)=>{
   body.append(note('来源性质是提交者的声明，不代表证据已认证；AI 不能在这里自行审批，也不能替负责人改写实际工程对象。'));
   const head=el('div','detailsection');
   add(head,el('strong','',AMEND_STATE[r.state]||r.state),
       el('div','tiny',r.origin_role+' · '+r.target.kind+' · 基于 r'+r.base_revision+
          ' · 当前 r'+r.target.revision),
       el('p','context','建议理由：\n'+r.reasoning),
       el('p','context','来源：\n'+r.source_refs.join('\n')),
       el('p','context','知识引用：\n'+(r.knowledge_pins.map(x=>x.id+' / r'+x.revision).join('\n')||'无')));
   if(r.scope_rationale)head.append(el('p','context','负责人处理理由：\n'+r.scope_rationale+
     '\n核查来源：'+(r.scope_evidence||[]).join('；')));
   if(r.stale)head.append(el('p','reason','目标对象修订已变化，本候选不能直接应用。'));
   body.append(head);
   for(const [field,value] of Object.entries(r.proposed_patch))
     amendmentDiff(body,field,r.base_snapshot[field],value);
   const related=el('div','detailsection');
   related.append(el('strong','','相关工程对象（仅提供影响审查线索）'));
   for(const x of r.related_candidates){
     const orientation=x.direction==='source_to_target'?'本对象 → 关联对象':'关联对象 → 本对象';
     related.append(el('p','context','已记录关系：'+orientation+' · '+x.relation+
       ' · '+x.title+' · '+x.kind+' r'+x.revision+
       '（关系方向不等于已确定的工程影响方向）'));
   }
   if(!r.related_candidates.length)related.append(el('p','context','现有受控关系图中无邻近对象；这不证明不存在间接影响。'));
   body.append(related);
   footer.append(button('返回列表','outline',amendmentsWorkbench));
   if(r.state==='CANDIDATE'&&!r.stale)
     footer.append(button('记录负责人审查','primary',()=>amendmentReviewModal(r)));
   if(r.state==='READY_TO_APPLY'&&!r.stale)
     footer.append(button('生成新草稿修订','primary',()=>amendmentApplyModal(r)));
   footer.append(button('打开对应工程对象','outline',()=>{
     const target=item(r.target_id);if(target)openItem(target);else toast('请刷新当前工程对象');
   }));
 });
}
function amendmentReviewModal(r){
 showModal('复核修订 · '+r.target.title,(body,footer)=>{
   body.append(note('需要根据原工程设计和证据判断是否修改。选择继续研究不会产生任何对象修订；无需为了流程而填满固定领域。'));
   const decision=selectField(body,'审查结论',[
     ['RESEARCH','尚需补充研究'],
     ['REVISE','同意进入修订确认步骤'],
     ['REJECT','本次不采纳']], 'RESEARCH');
   const rationale=field(body,'理由、实际影响范围和仍需验证的地方','','textarea');
   const refs=field(body,'人工核对的依据（每行一个）',r.source_refs.join('\n'),'textarea');
   const reviewer=field(body,'审查人','项目负责人');
   const error=el('div','error hidden');body.append(error);
   footer.append(button('取消','outline',()=>amendmentDetailModal(r.id)));
   footer.append(button('保存决定','primary',async()=>{
     try{
       await req('/api/studio/amendments/review',{
         id:r.id,expected_target_revision:r.base_revision,decision:decision.value,
         reviewer:reviewer.value,reason:rationale.value,
         evidence_refs:refs.value.split('\n').map(x=>x.trim()).filter(Boolean)
       });
       await amendmentDetailModal(r.id);
     }catch(e){error.textContent=e.message;error.classList.remove('hidden')}
   }));
 });
}
function amendmentApplyModal(r){
 showModal('确认：生成工程对象的新草稿',(body,footer)=>{
   body.append(note('修改将按精确修订号写入真实工程对象。旧批准不会延续到新版本，相关实现任务仍需重新检查设计输入与 CODE_READY。'));
   add(body,el('strong','',r.target.title+' · r'+r.base_revision+' → r'+(r.base_revision+1)),
     el('p','context','变动字段：'+Object.keys(r.proposed_patch).join('、')),
     el('p','context','审查理由：'+r.scope_rationale));
   const reviewer=field(body,'确认人','项目负责人');
   const rationale=field(body,'为什么现在能够形成此修订','','textarea');
   const error=el('div','error hidden');body.append(error);
   footer.append(button('取消','outline',()=>amendmentDetailModal(r.id)));
   footer.append(button('创建新草稿','primary',async()=>{
     try{
       const result=await req('/api/studio/amendments/apply',{
         id:r.id,expected_target_revision:r.base_revision,
         reviewer:reviewer.value,reason:rationale.value
       });
       await refresh(false);
       toast('已形成 r'+result.applied_revision+'，状态 '+result.object_state+'，未自动批准');
       await amendmentDetailModal(r.id);
     }catch(e){error.textContent=e.message;error.classList.remove('hidden')}
   }));
 });
}
