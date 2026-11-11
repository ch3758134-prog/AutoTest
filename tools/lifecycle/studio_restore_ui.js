/* Explicit recovery wizard; never overwrite or restore without server preflight. */
'use strict';
function restoreWorkspaceModal(){
  if(state.projects.length){toast('恢复只允许在空工作区进行；请启动独立数据文件');return;}
  showModal('从归档恢复到空工作区',(body,footer)=>{
    body.append(note('选择此前通过“导出项目”得到的工程 JSON。预检仅检查结构与关联；它不能恢复 Git、原始实验文件、设备状态，也不能重新认证历史审批。'));
    const pick=el('input');pick.type='file';pick.accept='.json,application/json';
    const group=el('div','field');group.append(el('label','','工程项目归档 JSON'));group.append(pick);body.append(group);
    const result=el('div','panel','尚未选择归档文件');body.append(result);
    const error=el('div','error hidden');body.append(error);
    let approvedArchive=null;
    const apply=button('确认恢复到当前空工作区','primary',async()=>{
      if(!approvedArchive)return;
      if(state.projects.length){error.textContent='工作区已非空，拒绝恢复';error.classList.remove('hidden');return;}
      apply.disabled=true;
      try{
        const restored=await req('/api/studio/restore/apply',{
          archive:approvedArchive,confirmation:'RESTORE_TO_EMPTY_WORKSPACE'
        });
        state.projects=await req('/api/studio/projects');
        state.project=restored.restored_project_id;
        await refresh(false);
        closeModal();navigate('dashboard');
        toast('历史工程档案已恢复；请核对证据、版本与原始文件');
      }catch(e){error.textContent=e.message;error.classList.remove('hidden');apply.disabled=false;}
    });
    apply.disabled=true;
    const precheck=button('检查归档结构','outline',async()=>{
      approvedArchive=null;apply.disabled=true;
      error.classList.add('hidden');
      if(!pick.files?.length){result.textContent='请选择 JSON 归档';return;}
      const file=pick.files[0];
      if(file.size>12_000_000){result.textContent='文件超过 12 MB，拒绝读取';return;}
      try{
        const parsed=JSON.parse(await file.text());
        const report=await req('/api/studio/restore/preview',{archive:parsed});
        clean(result);
        result.append(el('strong','','结构预检：'+report.status));
        result.append(el('p','context','项目：'+report.project_name+'\n原始项目 ID：'+report.project_id+
          '\n记录：'+Object.entries(report.records).map(([k,n])=>k+' '+n).join('；')+
          '\nGitHub：'+(report.repository||'未配置')));
        result.append(note(report.caution+'；应用会保留历史批准状态，不重新证明其真实性。'));
        approvedArchive=parsed;
        apply.disabled=false;
      }catch(e){result.textContent='预检失败：'+e.message;}
    });
    pick.onchange=()=>{approvedArchive=null;apply.disabled=true;result.textContent='文件已变更，请重新预检';};
    footer.append(button('取消','outline',closeModal),precheck,apply);
  });
}
