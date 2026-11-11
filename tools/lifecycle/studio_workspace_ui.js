/* Engineering Workbench V1.4 candidate: read-first project, stage and document workspace.
 * Pure presentation of existing engineering objects. Does not create approvals or change data.
 * Stage grouping is navigational and never implies that a formal Gate is passed.
 */
'use strict';

function wsObjects() { return state.data && Array.isArray(state.data.objects) ? state.data.objects : []; }

function wsPhaseOf(obj) {
  if (!obj) return null;
  const explicit = obj.meta && typeof obj.meta.phase === 'string' ? obj.meta.phase.trim().toUpperCase() : '';
  if (/^(P0|G[0-9]+)$/.test(explicit)) return explicit;
  const heading = /^(P0|G[0-9]+)\s*[｜|:：-]/.exec(obj.title || '');
  if (heading) return heading[1];
  const idMatch = /(?:^|-)(p0|g[0-9]+)(?:-|$)/i.exec(obj.id || '');
  return idMatch ? idMatch[1].toUpperCase() : null;
}

function wsStages() {
  const grouped = new Map();
  for (const obj of wsObjects()) {
    const phase = wsPhaseOf(obj);
    if (!phase) continue;
    if (!grouped.has(phase)) grouped.set(phase, []);
    grouped.get(phase).push(obj);
  }
  const compare = (a, b) => {
    if (a === 'P0') return -1;
    if (b === 'P0') return 1;
    return Number(a.slice(1)) - Number(b.slice(1));
  };
  return Array.from(grouped.keys()).sort(compare).map(id => {
    const all = grouped.get(id);
    const approved = all.filter(o => o.state === 'APPROVED');
    const researched = all.filter(o => o.meta && o.meta.research_complete === true);
    const top = researched[0] || (id === 'P0' ? approved[0] : null) ||
      all.find(o => o.kind === 'requirement') ||
      all.find(o => o.kind === 'system') || all[0];
    const phaseName = id === 'P0' ? '产品定义' : id === 'G0' ? '现有系统调查' :
      id === 'G1' ? '系统需求' : id === 'G2' ? '系统架构' :
      id === 'G3' ? '详细设计' : id === 'G4' ? '软件实施' : '研发阶段';
    const summary = researched.length ? '研究报告已整理，尚待正式审查' :
      approved.length ? '存在已批准的工程成果' : '已有研究资料，尚未正式批准';
    return {id, all, approved, researched, top, phaseName, summary};
  });
}

function wsPlainText(content, limit) {
  const plain = String(content || '')
    .replace(/^#{1,6}\s+.*$/gm, '')
    .replace(/\*\*/g, '')
    .replace(/\|/g, ' ')
    .replace(/https?:\/\/\S+/g, '')
    .replace(/\s+/g, ' ').trim();
  return plain.length > limit ? plain.slice(0, limit).trimEnd() + '…' : plain;
}

function wsTag(text, tone) {
  return el('span', 'ws-tag'+(tone ? ' ws-tag-'+tone : ''), text);
}

function wsHeading(kicker, title, subtitle) {
  const h = el('header', 'ws-page-header');
  if (kicker) h.append(el('div', 'ws-eyebrow', kicker));
  h.append(el('h1', 'ws-page-title', title));
  if (subtitle) h.append(el('p', 'ws-subtitle', subtitle));
  root.append(h);
  return h;
}

function wsSetNavigation(view) {
  const target = view === 'reader' ? 'stages' : view;
  document.querySelectorAll('[data-view]').forEach(b => b.classList.toggle('active', b.dataset.view === target));
}

function wsChoosePhase(phase) {
  state.phaseFocus = phase;
  state.view = 'stages';
  state.selected = null;
  wsSetNavigation('stages');
  renderView();
  window.scrollTo({top: 0, behavior: 'instant'});
}

function wsOpenDocument(obj, returnTo) {
  if (!obj) return;
  state.readerBack = returnTo || (state.view === 'reader' ? state.readerBack : state.view);
  state.selected = obj.id;
  const phase = wsPhaseOf(obj);
  if (phase) state.phaseFocus = phase;
  state.view = 'reader';
  wsSetNavigation('reader');
  renderView();
  window.scrollTo({top: 0, behavior: 'instant'});
}

function wsBackToStage() {
  if (state.phaseFocus) return wsChoosePhase(state.phaseFocus);
  navigate('dashboard');
}

function wsAction(label, action, prominent) {
  return button(label, prominent ? 'primary' : 'outline', action);
}

function wsDocumentRow(obj, context) {
  const wrapper = el('button', 'ws-document-row');
  const leading = el('div', 'ws-document-icon', obj.kind === 'test' ? '✓' :
    obj.kind === 'requirement' ? '§' : obj.kind === 'reference' ? '▤' : '□');
  const content = el('div', 'ws-document-row-copy');
  content.append(el('strong', '', obj.title));
  content.append(el('div', 'ws-row-summary', wsPlainText(obj.body, context === 'compact' ? 110 : 170)));
  const status = el('span', 'ws-row-status', obj.state === 'APPROVED' ? '已批准' :
    obj.meta && obj.meta.test_state === 'NOT_RUN' ? '尚未验证' : '待审查');
  wrapper.append(leading, content, status);
  wrapper.onclick = () => wsOpenDocument(obj, context === 'home' ? 'dashboard' : 'stages');
  return wrapper;
}

function wsSourceNote() {
  const source = state.gitSource;
  const noteBlock = el('div', 'ws-source');
  const left = el('div');
  left.append(el('strong', '', source && source.project_id === state.project ?
    '当前工程资料来自 Git 提交 ' + source.commit.slice(0, 12) :
    '当前资料来自本地工作区'));
  left.append(el('p', '', source && source.project_id === state.project ?
    '这是本次恢复的版本。拉取新代码不会自动更新已有 SQLite 数据。' :
    '未提供当前工作区的 Git 恢复来源，不能据此判断远程版本。'));
  noteBlock.append(left);
  return noteBlock;
}

function wsHomeHero(stage) {
  const hero = el('section', 'ws-hero');
  const lead = el('div', 'ws-hero-lead');
  lead.append(el('div', 'ws-eyebrow', '当前需要关注'));
  if (stage && stage.top) {
    lead.append(el('h2', '', stage.id + ' · ' + stage.phaseName));
    lead.append(el('p', '', '已有阶段研究成果。' + (stage.researched.length ?
      '完整报告已整理，等待你阅读全文并决定是否需要修订。' :
      '仍有工程内容等待研究或审查。')));
    const actions = el('div', 'ws-actions');
    actions.append(wsAction(stage.researched.length ? '阅读完整研究报告' : '查看阶段资料',
      () => stage.researched.length ? wsOpenDocument(stage.researched[0], 'dashboard') : wsChoosePhase(stage.id), true));
    actions.append(wsAction('查看阶段全部成果', () => wsChoosePhase(stage.id), false));
    lead.append(actions);
  } else {
    lead.append(el('h2', '', '从已有工程资料开始'));
    lead.append(el('p', '', '当前没有可以明确识别为已整理研究的阶段；先查看实际工程对象，不虚构进度。'));
    lead.append(wsAction('浏览工程资料', () => navigate('engineering'), true));
  }
  const right = el('div', 'ws-hero-aside');
  right.append(el('div', 'ws-kicker', '审批边界'));
  right.append(el('strong', '', '研究完成 ≠ 正式批准'));
  right.append(el('p', '', '所有需求、设计和阶段 Gate 必须分别经过负责人审查。此页面只展示已有记录，不改变审批状态。'));
  hero.append(lead, right);
  return hero;
}

function renderWorkspaceHome() {
  clean(root);
  const project = state.data.project;
  wsHeading('工程项目 / 概览', project.name || '未命名项目',
    '在一个页面看清当前进展、可阅读的成果，以及需要你决定的事项。');
  const phases = wsStages();
  const pending = [...phases].reverse().find(x => x.researched.length && !x.researched.some(o => o.state === 'APPROVED'));
  const approved = wsObjects().find(o => o.state === 'APPROVED' && wsPhaseOf(o) === 'P0');
  root.append(wsHomeHero(pending || [...phases].reverse().find(x => !x.approved.length) || null));
  const section = el('section', 'ws-section');
  const hdr = el('div', 'ws-section-title');
  hdr.append(el('h2', '', '研发阶段'));
  hdr.append(wsAction('查看完整阶段资料', () => wsChoosePhase(pending ? pending.id : (phases[0] ? phases[0].id : 'P0'))));
  section.append(hdr);
  const row = el('div', 'ws-phase-grid');
  for (const phase of phases) {
    const card = el('button', 'ws-phase-card');
    card.append(el('small', '', phase.id + ' · ' + phase.phaseName));
    card.append(el('strong', '', phase.researched.length ? '研究已整理' :
      phase.approved.length ? '存在已批准成果' : '研究与候审'));
    card.append(el('span', '', phase.summary));
    card.onclick = () => wsChoosePhase(phase.id);
    row.append(card);
  }
  if (!phases.length) row.append(el('p', 'ws-muted', '尚无按阶段归档的研究成果。'));
  section.append(row);
  root.append(section);

  const columns = el('section', 'ws-home-columns');
  const primary = el('div', 'ws-white-block');
  primary.append(el('div', 'ws-eyebrow', '已形成的正式依据'));
  primary.append(el('h2', '', approved ? '已批准的产品定义' : '正式产品身份待核对'));
  if (approved) {
    const description = state.data.project.summary || wsPlainText(approved.body, 330);
    primary.append(el('p', 'ws-quote', description));
    primary.append(wsTag('对象已批准 · 阶段 Gate 另行审查', 'neutral'));
    primary.append(wsAction('阅读批准文件', () => wsOpenDocument(approved, 'dashboard')));
  } else {
    primary.append(el('p', '', '当前工程快照没有明确可核验的 P0 已批准对象。'));
    primary.append(wsAction('查看产品与系统', () => navigate('product')));
  }
  const secondary = el('div', 'ws-white-block');
  secondary.append(el('div', 'ws-eyebrow', '可核验的下一步'));
  secondary.append(el('h2', '', '从研究成果进入审查'));
  secondary.append(el('p', '', '先阅读完整研究报告，再根据内容提出修改意见或进行正式审查。不要从零散的对象数量判断项目是否完成。'));
  const latest = pending && pending.researched[0];
  if (latest) secondary.append(wsDocumentRow(latest, 'home'));
  else secondary.append(el('p', 'ws-muted', '当前快照尚未标记完整研究报告。'));
  columns.append(primary, secondary);
  root.append(columns);
  const advanced = el('details','ws-home-advanced');
  advanced.append(el('summary','', '按需展开：能力追踪与技术预研'));
  const moreActions = el('div','ws-actions');
  const trace = wsAction('查看能力与需求追踪', () => {
    showWorkspaceCapabilityDetails();
    trace.disabled = true;
  });
  const research = wsAction('查看专业技术研究概况', () => {
    showWorkspaceResearchDetails();
    research.disabled = true;
  });
  moreActions.append(trace, research);
  advanced.append(el('p','ws-muted','这些是完整工程管理功能，平时不占据项目首页。展开后仍可查看原始依据和受控关系。'));
  advanced.append(moreActions);
  root.append(advanced);
  root.append(wsSourceNote());
}

function renderPhaseWorkspace() {
  clean(root);
  const phases = wsStages();
  if (!phases.length) {
    wsHeading('工程项目 / 阶段与成果', '尚无可识别的阶段成果', '请先核对数据来源，并从现有工程资料开始。');
    root.append(wsSourceNote()); return;
  }
  const selected = phases.find(p => p.id === state.phaseFocus) ||
    [...phases].reverse().find(p => p.researched.length) || phases[0];
  state.phaseFocus = selected.id;
  wsHeading('工程项目 / 阶段与成果', selected.id + ' · ' + selected.phaseName,
    '完整阅读成果、检查审查状态，再进入必要的工程细节。');
  const tabs = el('div', 'ws-stage-tabs');
  for (const p of phases) {
    const tab = button(p.id + ' ' + p.phaseName, 'ws-stage-tab' + (p.id === selected.id ? ' active' : ''),
      () => wsChoosePhase(p.id));
    tab.setAttribute('aria-current', p.id === selected.id ? 'page' : 'false');
    tabs.append(tab);
  }
  root.append(tabs);

  const summary = el('section', 'ws-stage-summary');
  const main = el('div');
  main.append(el('div', 'ws-eyebrow', '阶段内容与审批'));
  main.append(el('h2', '', selected.summary));
  main.append(el('p', '', '本阶段共有 ' + selected.all.length + ' 条关联工程记录，其中 ' +
    selected.approved.length + ' 条对象已批准。对象批准不等于阶段 Gate 通过。'));
  if (selected.top) main.append(el('p', 'ws-stage-excerpt', wsPlainText(selected.top.body, 280)));
  const actions = el('div', 'ws-actions');
  if (selected.top) actions.append(wsAction(selected.researched.length ? '阅读全文' : '打开主要成果',
    () => wsOpenDocument(selected.top, 'stages'), true));
  main.append(actions);
  const aside = el('div', 'ws-stage-summary-aside');
  aside.append(el('div', 'ws-kicker', '这表示什么'));
  aside.append(el('p', '', selected.researched.length ?
    '研究文本已经整理并归档，不代表你已批准需求，也不表示 Gate 通过。' :
    selected.approved.length ?
    '已有经过批准的对象，但其它工作产品和 Gate 仍需独立判断。' :
    '目前只有未批准的工作内容；这里不会把草稿伪装成正式成果。'));
  summary.append(main, aside);root.append(summary);
  const sec = el('section', 'ws-section');
  const title = el('div', 'ws-section-title');
  title.append(el('h2', '', '本阶段的研究与工作产品'));
  title.append(el('span', 'ws-count', '选择文档可进入完整阅读模式'));
  sec.append(title);
  const list = el('div', 'ws-document-list');
  const score = o => o.meta && o.meta.research_complete ? 0 :
    o.state === 'APPROVED' ? 1 : o.kind === 'requirement' ? 2 : o.kind === 'test' ? 3 : 4;
  for (const obj of [...selected.all].sort((a,b) => score(a) - score(b) || a.title.localeCompare(b.title,'zh-CN')))
    list.append(wsDocumentRow(obj));
  sec.append(list);root.append(sec);
  root.append(wsSourceNote());
}

function renderDocumentWorkspace() {
  clean(root);
  const obj = wsObjects().find(o => o.id === state.selected);
  if (!obj) {
    wsHeading('工程项目 / 文档', '文档不存在或已变更', '请返回阶段列表重新选择；当前内容不会凭空补齐。');
    root.append(wsAction('返回阶段列表', wsBackToStage, true));return;
  }
  const phase = wsPhaseOf(obj);
  const back = el('div', 'ws-reader-back');
  back.append(button('← 返回' + (phase ? phase + ' 阶段' : '项目'), 'link', wsBackToStage));
  root.append(back);
  const header = el('div', 'ws-reader-header');
  const above = el('div', 'ws-reader-context');
  above.append(el('span', '', (phase ? phase + ' / ' : '') + groupLabel(obj.kind)));
  above.append(wsTag(obj.state === 'APPROVED' ? '已批准' :
    obj.meta && obj.meta.test_state === 'NOT_RUN' ? '未执行' : '未批准',
    obj.state === 'APPROVED' ? 'approved' : 'neutral'));
  header.append(above);
  header.append(el('h1', '', obj.title));
  header.append(el('p', '', '修订 r' + obj.revision + ' · ' +
    (obj.state === 'APPROVED' ? '该对象有正式批准记录，历史仍可复核。' :
     '这是研究或候审内容，尚未获得负责人正式批准。')));
  const actions = el('div', 'ws-actions');
  actions.append(wsAction('历史修订', () => historyModal(obj)));
  actions.append(wsAction('查看工程属性与关联', () => openItem(obj)));
  if (obj.state !== 'APPROVED') actions.append(wsAction('记录问题或修改意见', () => captureFindingModal(obj)));
  header.append(actions);
  root.append(header);

  const layout = el('div', 'ws-reader-layout');
  const toc = el('nav', 'ws-reader-toc');toc.setAttribute('aria-label', '文档目录');
  toc.append(el('div', 'ws-kicker', '本文目录'));
  const paper = el('article', 'ws-reader-paper');
  const doc = el('div', 'doc ws-readable');
  renderReadable(doc, obj.body || '');
  const headings = [...doc.querySelectorAll('h2,h3')];
  headings.forEach((h,i) => {
    h.id = 'ws-section-' + i;
    const link = button(h.textContent, 'ws-toc-link' + (h.tagName === 'H3' ? ' sub' : ''), () => {
      h.scrollIntoView({behavior:'smooth',block:'start'});
    });
    toc.append(link);
  });
  if (!headings.length) toc.append(el('p', 'ws-muted', '本文无章节标题'));
  paper.append(doc);
  const rail = el('aside', 'ws-reader-rail');
  rail.append(el('div', 'ws-kicker', '审查信息'));
  const meta = el('div', 'ws-review-facts');
  const facts = [
    ['文档性质', groupLabel(obj.kind)],
    ['当前状态', obj.state === 'APPROVED' ? '已批准' : '未批准'],
    ['当前修订', 'r' + obj.revision],
    ['运行/测试', obj.meta && obj.meta.test_state === 'NOT_RUN' ? '尚未执行' : '以原始证据为准']
  ];
  for (const f of facts) {
    const cell = el('div');
    cell.append(el('small','',f[0]),el('strong','',f[1]));meta.append(cell);
  }
  rail.append(meta);
  rail.append(el('p','ws-review-guidance','正式批准仅能在有权限的审查流程中完成。本页阅读、打开或查看历史均不会自动批准。'));
  const related = state.data.relations.filter(r => r.source_id === obj.id || r.target_id === obj.id)
    .map(r => wsObjects().find(o => o.id === (r.source_id === obj.id ? r.target_id : r.source_id)))
    .filter(Boolean).slice(0,4);
  if (related.length) {
    rail.append(el('div','ws-kicker','关联工程内容'));
    for (const o of related) {
      const b = button(o.title,'ws-related-link',() => wsOpenDocument(o,'reader'));
      rail.append(b);
    }
  }
  const more = el('details','ws-reader-more');
  more.append(el('summary','', '来源及完整工程属性'));
  more.append(el('div','ws-raw-meta',obj.id));
  if (obj.meta) for (const entry of Object.entries(obj.meta)) {
    const label = metaLabels[entry[0]] || entry[0];
    const item = el('div','ws-meta-line');
    item.append(el('strong','',label));
    item.append(el('span','',typeof entry[1]==='string' ? entry[1] : JSON.stringify(entry[1])));
    more.append(item);
  }
  rail.append(more);
  layout.append(toc,paper,rail);
  root.append(layout);
  const footer = el('div','ws-reader-footer');
  footer.append(el('span','', '已阅读到文档末尾 · 本页不执行审批'));
  footer.append(button('返回阶段成果','outline',wsBackToStage));
  root.append(footer);
}
