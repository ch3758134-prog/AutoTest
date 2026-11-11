/* Explanatory engineering concepts: help people and AI reason, not classify by keywords. */
'use strict';
async function engineeringSemanticsModal(focus='product_identity,product_purpose,system_boundary,release_scope,configuration,verification_evidence,decision'){
  let guide;
  try{guide=await req('/api/studio/semantics?focus='+qs(focus));}
  catch(e){toast('工程概念说明读取失败：'+e.message);return;}
  showModal('工程概念如何理解与使用',(body,footer)=>{
    body.append(note('这些说明与案例用来辅助 AI 和负责人判断信息应当处于什么工程层次，不是禁词表、字数规则，也不是对项目事实的自动裁决。'));
    add(body,el('div','tiny','指导版本 '+guide.edition+' · SHA '+guide.sha256.slice(0,16)));
    for(const concept of guide.concepts){
      const panel=el('div','detailsection');
      add(panel,el('strong','',concept.name),
        el('p','context','作用：'+concept.purpose),
        el('p','context','包含：'+concept.includes),
        el('p','context','不替代：'+concept.not_substitutes));
      panel.append(el('strong','','判断时可以追问'));
      for(const q of concept.questions)
        panel.append(el('div','context','• '+q));
      if(concept.examples?.length){
        panel.append(el('strong','','语境示例'));
        for(const x of concept.examples)
          panel.append(el('p','context','“'+x.text+'”\n'+x.judgment));
      }
      if(concept.counterexamples?.length){
        panel.append(el('strong','','容易混淆的情况'));
        for(const x of concept.counterexamples)
          panel.append(el('p','context','“'+x.text+'”\n'+x.judgment));
      }
      panel.append(el('div','tiny','相关概念：'+concept.related.join('、')));
      body.append(panel);
    }
    const process=el('div','detailsection');process.append(el('strong','','建议 AI 如何工作'));
    for(const step of guide.ai_workflow)process.append(el('div','context','• '+step));
    body.append(process);
    footer.append(button('关闭','outline',closeModal));
    if(focus!=='all')footer.append(button('查看全部工程概念','outline',()=>engineeringSemanticsModal('all')));
  });
}
