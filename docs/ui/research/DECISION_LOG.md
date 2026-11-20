# AutoTest｜UX 研究决策/假设台账

此表收纳研究方向的候选，不代替 PM 的正式产品批准。每一轮只能新增或明确更改状态，不能把研究意见悄悄写成“用户已确认”。

| ID | 决策/假设 | 来源 | 当前状态 | 验收/下一步 |
|---|---|---|---|---|
| UX-H001 | 六系统（工作空间/对象/命令/上下文/状态/恢复）先于页面与控件 | R01 | RESEARCH_PRINCIPLE | R03 原型逐项检查 |
| UX-H002 | 软件会话/工程/编辑/工位/执行五种上下文分层 | R02 E01,E04,E05,E09 | DESIGN_CANDIDATE | 绘制生命周期与跨窗口状态图 |
| UX-H003 | 没有正式工程也能进入合适的编辑体验；正式资源保存另行确认 | R02 E04,E05 与用户反馈 | DESIGN_CANDIDATE | T01、T04；需 TEST/PROJECT 评估正式临时对象支持 |
| UX-H004 | 自动测试运行和编辑工作区重心不同，共享正式用例/工程上下文 | R02 E09 与用户早前确认 | USER_DIRECTION / DETAILS_OPEN | T02、T11、T12 |
| UX-H005 | 工具与文档分属不同容器；工具可独立窗口且保留上下文 | R02 E04,E11 与用户要求 | USER_DIRECTION / DETAILS_OPEN | T09、T10、T14 |
| UX-H006 | 专业对象具有不同编辑器；不强制三栏/通用 Inspector | R02 E06,E07,E08 | DESIGN_CANDIDATE | R04 各对象任务实测 |
| UX-H007 | 本地编辑缓冲、领域事务、正式持久保存、Run 快照明确分层 | R02 E05,E06 与业务边界 | DESIGN_CANDIDATE | T06、T07、T15 |
| UX-H008 | 全产品命令统一语义，不把高频操作仅放右键或弹窗 | R02 E12,E15 | DESIGN_CANDIDATE | 操作步骤/鼠标键盘验证 |
| UX-H009 | 设备 ACK/真实反馈/结果与证据必须分层显示 | R01/R02 E14 + 已确认安全边界 | DOMAIN_BOUNDARY | TEST/DEVICE 审查，不由 UI 修改 |
| UX-H010 | 首版不默认上完整任意停靠框架，先实现布局恢复、多屏、有效缩放 | R02 E03,E04 成本推论 | DESIGN_CANDIDATE | T16 及长时间使用反馈 |
| UX-H011 | 优先完成跨对象连续任务原型，再冻结视觉和组件 | R01/R02 E13 + 用户明确要求 | UX_RESEARCH_DIRECTION | R03 先 T01~T06、T09、T15 |

| UX-H012 | 有正式上次工程直接恢复运行任务，无工程进入轻量工作入口，恢复失败提供明确修复 | R03 E17~E21 + 用户已确定场景 | DESIGN_CANDIDATE | P1~P4 可操作 WPF 原型 |
| UX-H013 | 同一软件内“应用 Shell / 任务工作区 / 文档对象 / 观察反馈”分层，不强制四层导航 | R03 E01,E17,E23,E24 | DESIGN_CANDIDATE | P1~P4 和跨工具任务 |
| UX-H014 | 恢复未完成会话与正式资源保存、真实 Run 快照必须独立 | R03 E05,E16,E22 | DESIGN_CANDIDATE | T07、T15、P4 |
| UX-H015 | 工程师常用入口不每次弹出欢迎页；第一次和错误状态明确任务引导 | R03 E18,E19,E20 | DESIGN_CANDIDATE | 对比入口三种原型 |
| UX-H016 | 实体命令风险分级、不将本地撤销机制套给真实输出 | R03 E25+已确认 DEVICE/TEST 边界 | DOMAIN_BOUNDARY / UX_CANDIDATE | 专业合同审查 |

| UX-H017 | 直接编辑/持续对象详情/结构画布/编辑-执行四种交互模型按对象匹配，不固定三栏 | R04 E26~E39 | DESIGN_CANDIDATE | O01~O12 |
| UX-H018 | NavigationSelection、EditorSelection、RunSelection、StationTarget、TraceSelection 各自独立 | R04 E28,E29,E36 | DESIGN_CANDIDATE | O01/O09 与 T11 |
| UX-H019 | 字段缓冲/对象草稿/领域事务/正式保存/真实 Run 快照分层，半成品草稿可跨选择恢复 | R04 E32,E39 + R03 | DESIGN_CANDIDATE / BACKEND_REVIEW_REQUIRED | O03/O04/O10 |
| UX-H020 | 命令需明确目标、作用域、可用条件、副作用、撤销、错误与证据，右键非唯一入口 | R04 E38 | DESIGN_CANDIDATE | O01/O06/O07 |
| UX-H021 | 编辑与实体发送分离，普通快捷键不直接执行高风险物理动作 | R04 E38 + BIZ-10 | UX_CANDIDATE / DOMAIN_SAFETY | O07/O08 |

| UX-H022 | 全应用任务中心区分可后台编辑任务与不可绕过独占的实体长任务 | R05 E40,E41 | DESIGN_CANDIDATE | S01/S05/S10 |
| UX-H023 | ResourceSaved/ResourceChanged 与未保存草稿互不替代，跨工具共享修订、显式确认 | R05 E45 | DESIGN_CANDIDATE / PROJECT_REVIEW | S02 |
| UX-H024 | 请求/主控 ACK/ECU 响应/测量/判定/报告分层，状态与实际事件关联 | R05 E42,E43 + DOMAIN | DOMAIN_BOUNDARY / UI_CONTRACT | S03~S06 |
| UX-H025 | CAN/报告大数据使用虚拟化和后台处理，避免 WPF 主线程阻塞 | R05 E49,E50 | ENGINEERING_DESIGN_CANDIDATE | S01/S09 |
| UX-H026 | 多 DPI/多屏窗口、焦点/键盘/对比为产品质量要素，不是视觉补丁 | R05 E47,E48 | UX_QUALITY_CANDIDATE | S08 |

| UX-H027 | 任务导向入口 + 可持续工程环境 + 专业对象编辑器 + 真状态与恢复 | R06 / R01~R05 | UX_IMPLEMENTATION_CANDIDATE | UX-I01~UX-I03 |
| UX-H028 | 无工程编辑重用 TestDocument.Create 与 StepEditTransaction，不伪造 ProjectId/AssetRevision/Hash | R06 main 代码核查 | DOMAIN_ADAPTER_REQUIRED | UX-I01 领域绑定设计 |
| UX-H029 | WPF-UI Light + Prism 唯一生命周期 + 原生窗口/DPI | R06 E51~E55 | TECHNICAL_DIRECTION_SELECTED / BUILD_NOT_RUN | UX-I01 编译、窗口、键鼠验收 |
| UX-H030 | 全产品专业交互不能降成统一表格/Inspector，也不能用禁用空壳冒充全功能实现 | R06 / PM-BIZ / PM-INPUT | PRODUCT_WIDE_DESIGN_CANDIDATE | UX-I02 逐工具任务验收 |

## R06 研究准出结论

- R01～R06 已覆盖软件整体架构、用户入口、对象编辑、命令体系、跨工作区状态、恢复、性能与 Windows 专业体验。
- UX-I01 首批 WPF 实施的核心领域接口已明确：可创建正式的内存 TestDocument + StepEditTransaction，但现有 TestEditorSession 要求正式工程身份，需要 Unbound/Bound 会话展示适配或专业接口协作。
- 当前没有必须先由用户回答的控件布局问题；允许进入首轮代码实施并通过真实体验微调。
- 这不是 UI 设计最终冻结，也不是项目 Gate 审批和 Windows/ECU 实测通过。

## R05 阶段结论

- 已提出跨工具状态和错误恢复合同、真实工位独占 UX 呈现与 S01~S10 验收脚本。
- R06 将汇总为首轮真正可实现的 WPF 原型合同；未修改业务源码。

## R04 阶段结论

- 已提交对象类型与交互类型映射、统一命令合同、草稿/保存区分、O01~O12 可操作任务。
- 没有修改 WPF 或后端；R05 继续跨任务与异常研究。

## R03 阶段结论

- 完成启动、空工程、工程恢复、编辑/运行、跨工具和失败追溯的产品级候选。
- 入口采用“同一主窗口状态化任务引导”作为优先验证方向，**仍未经过真实 Windows 用户操作验收**。
- 下一轮 R04 深入对象命令与连续编辑，不再要求用户回答抽象布局偏好；避免开发第二套领域模型。

## R02 阶段结论

- 研究源与设计推论已分开记载；没有任何新的产品页面被“批准”。
- 已确认旧实现路径 `feature/host-fluent-integration-20261119` 不可因沿用了真实服务就自动视作新版 UX 成果。
- 下轮继续在同一分支建立 R03，不需要用户先回答控件布局问卷。
- 研究完原型、完成真实操作验收，再由 PM 审查 UI 设计版本及集成界面合同。

**本记录不授权 HOST 代码修改、不改变主控硬件、无任何 CI/实体测试结果。**

## 2026-11-19 用户明确更新：纯 UI 完整重建与统一验收（优先于旧 UX-I01）

| ID | 决策 | 来源 | 状态 | 约束 |
|---|---|---|---|---|
| UX-H031 | 旧 G4 UI 表现层完全废弃；新 UI 不继承旧 Shell、旧编辑器、旧导航或风格 | 用户在本对话多次明确要求全部删除、重新设计 | USER_CONFIRMED | 仅在独立 feature 分支删旧 UI；`main` 暂不合并 |
| UX-H032 | 当前只完成独立纯 UI，不保留接真实 TEST/PROJECT/DEVICE 的实施依赖；真实服务接线由后续阶段安排 | 用户否定“保留真实业务服务再重建”路线 | USER_CONFIRMED | UI 会话是交互验证资料，不是第二套可执行领域模型 |
| UX-H033 | 所有 P/A/B/C/D/E 研究、设计与代码批次内部连续推进和逐批留档，用户只在全部 UI 完成后统一 Windows 实际验收 | 2026-11-19 用户明确改变审批节奏 | USER_CONFIRMED | 不要求每批停下请用户批准；仍须写准确的 Gate 与未验证项 |
| UX-H034 | 保留软硬件真实事实层级，UI 不接后端时不能伪造 USB ACK、ECU 响应、波形采样、运行结果或 PASS | R01~R06 / 用户禁止假运行 | HARD_BOUNDARY | 纯 UI 可以展示用户手工曲线和状态结构，但必须标注非实测 |

旧 `UX_I01_WPF_IMPLEMENTATION_START_CONTRACT_20261119.md` 的“首批真实模型适配与 PROJECT/TEST 接线优先”安排已由 UX-H032 **SUPERSEDED**；文档原文保留为历史，不得当作当前实施指令。当前实施候选：Draft PR #69，`feature/ui-product-rebuild-20261119`。Windows Build、UI 实际验收尚未执行。
