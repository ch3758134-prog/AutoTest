# AutoTest｜全产品 UX 系统研究连续档案

**维护角色：UI/UX 设计研究专项。研究分支：`ui/ide-design`。**

## 宗旨

先研究复杂工业级 Windows 工程软件的工作规律，再结合 AutoTest 已批准业务事实，最后进入具体页面交互原型。不以旧 WPF、旧 HTML 或当前 HOST 主窗口作为设计基线；不在研究分支写真实设备/TEST/PROJECT 业务代码。

每轮必须形成：研究问题、研究方法、原始来源、已核实的产品事实、跨产品设计判断、对 AutoTest 的待验证设计假设、正反面案例、完整任务验收脚本、限制、下一轮输入及提交 SHA。证据与意见绝不混用。

## 研究轮次

| 轮次 | 档案 | 主题 | 状态 |
|---|---|---|---|
| R01 | [R01_FOUNDATION_HANDOFF_20261119.md](R01_FOUNDATION_HANDOFF_20261119.md) | 现代复杂 PC/工业软件的六系统设计理念；既有独立报告交接 | 研究摘要与报告出处已关联 |
| R02 | [R02_WORKFLOW_AND_WORKSPACE_20261119.md](R02_WORKFLOW_AND_WORKSPACE_20261119.md) | 从成熟软件操作链推导产品会话、工程、文档、工具、运行与恢复架构 | RESEARCH_COMPLETE / DESIGN_CANDIDATE / USER_UX_NOT_VERIFIED |
| R03 | 待启动 | 以 R02 任务脚本为输入，深查完整对象操作链，并设计可验证的交互原型 | NOT_STARTED |

配套： [证据登记表](SOURCE_REGISTER_20261119.md) · [决策与待验假设](DECISION_LOG.md)

## 仓库与权限边界

- `ui/ide-design` 是 UI 研究设计的现有分支。临时 `feature/ui-rebuild-20261119` 已标 PAUSED，不以其历史实施计划作为最新决策。
- `main` 作为实际产品/领域事实来源；本分支不写 `main`。
- `feature/fluent-bench-test-ui-20261119` 是前次纯 UI 实验，`feature/host-fluent-integration-20261119` 是真后端接线候选；二者不能因已有代码就自动成为新的设计冻结版。
- 后端的 TEST/PROJECT/DEVICE/UDS/FLASH/WAVE/SCAN/RECORD 边界和硬件安全合同，由其专业团队与项目管理批准；UX 研究不得自行推翻。
- 具体页面需由用户在 Windows 可操作 WPF 上验收；文献研究/静态稿不能标 UI_ACCEPTED，更不能标 FIELD_PASS。

## 接手本系列的研究 AI 应如何工作

1. 按顺序读本目录的 R01、R02、SOURCE_REGISTER、DECISION_LOG，再读项目管理最新合同。
2. 分清已核实的一手产品事实、我们自己的设计候选和用户明确选择。
3. 下轮在同一分支新增完整 `R03_*.md`，同步索引与决策状态，不覆盖历史研究。
4. 应优先研究行业成熟做法；只有真正无法由研究和原有明确事实决定的用户场景，再集中提问。不要要求用户决定控件布局或格式技术约束。
