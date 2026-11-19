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
