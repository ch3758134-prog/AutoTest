/* AI can propose product-definition text but this inbox is owner-controlled. */
'use strict';
async function definitionSuggestionInbox(host,info){
  host.append(el('strong','','AI 产品定义候选（未触发工程变更）'));
  const loading=el('p','context','正在读取候选建议……');host.append(loading);
  let suggestions;
  try{suggestions=await req('/api/studio/governance/suggestions?project='+qs(state.project));}
  catch(e){loading.textContent='候选读取失败：'+e.message;return;}
  if(!host.isConnected)return;
  clean(loading);
  const awaiting=suggestions.filter(x=>x.state==='PENDING');
  host.append(el('p','context','待审 '+awaiting.length+' 项。AI 生成的文本不会改写现役定义，也不会自动暂停研发；只有负责人确认进入正式研究后，才生成十项必需研究任务并触发重大变更门禁。'));
  if(!suggestions.length)host.append(el('p','context','暂无候选建议。可通过受限项目管理 AI 接口提交完整产品定义草案，或直接从本页创建负责人变更申请。'));
  for(const suggestion of suggestions.slice(0,12)){
    const line=el('div','item-row'),left=el('div');left.style.flex='1';
    add(left,el('strong','',suggestion.proposed.summary.slice(0,100)),
      el('div','tiny',suggestion.state+' · '+suggestion.origin_role+' · '+suggestion.proposed_by),
      el('div','context',suggestion.reason.slice(0,150)));
    line.append(left);
    line.append(button('核对建议','outline',
      ()=>definitionSuggestionDetailModal(suggestion,info)));
    host.append(line);
  }
}
function definitionSuggestionDetailModal(suggestion,info){
  showModal('产品定义候选 · 是否启动全面研究',(body,footer)=>{
    body.append(note('这是未经批准的建议，并非工程定义。接受只会开启正式变更请求和十项研究任务，不会自动签发产品新版本。'));
    const detail=el('div','detailsection');
    add(detail,el('strong','','原始候选来源：'+suggestion.origin_role),
      el('p','context','提出人：'+suggestion.proposed_by+'\n原因：'+suggestion.reason),
      el('p','context','候选定位：'+suggestion.proposed.summary),
      el('p','context','关联项目背景（不是产品身份）：\n'+suggestion.proposed.body),
      el('p','context','候选版本：'+suggestion.proposed.version+' · 适用变体：'+suggestion.proposed.variant),
      el('p','context','待查来源：\n'+suggestion.source_refs.join('\n')));
    if(suggestion.semantic_notes)
      detail.append(el('p','context','AI 对各项内容语义归属的说明：\n'+suggestion.semantic_notes));
    detail.append(button('查看工程概念指导','outline',()=>engineeringSemanticsModal()));
    body.append(detail);
    if(suggestion.base_digest!==info.definition_digest)
      body.append(note('建议基于旧定义版本，当前项目定义已变化；必须重新提交并审查差异，不能直接接受。'));
    const reviewer=field(body,'负责人','项目负责人');
    const rationale=field(body,'接受研究的理由或拒绝说明','','textarea');
    const error=el('div','error hidden');body.append(error);
    footer.append(button('返回','outline',definitionGovernanceModal));
    if(suggestion.state==='PENDING'){
      footer.append(button('拒绝候选','outline',async()=>{
        try{
          await req('/api/studio/governance/suggest/reject',{
            id:suggestion.id,reviewer:reviewer.value,rationale:rationale.value
          });
          toast('候选已拒绝；工程定义未变化');
          await definitionGovernanceModal();
        }catch(e){error.textContent=e.message;error.classList.remove('hidden');}
      }));
      if(suggestion.base_digest===info.definition_digest)
        footer.append(button('确认启动全面研究','primary',async()=>{
          try{
            const result=await req('/api/studio/governance/suggest/accept',{
              id:suggestion.id,reviewer:reviewer.value,
              rationale:rationale.value,expected_base_digest:info.definition_digest
            });
            toast('已形成正式定义变更；必需研究任务自动建立');
            await definitionChangeModal(result.change_id);
          }catch(e){error.textContent=e.message;error.classList.remove('hidden');}
        }));
    }
  });
}
