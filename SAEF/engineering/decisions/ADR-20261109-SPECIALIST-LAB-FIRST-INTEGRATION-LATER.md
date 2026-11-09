# ADR-20261109｜AutoTest 分支分层验证与统一集成后置（修订 v0.2）

- **类别**：用户明确确认的研发、UI 所有权与实测顺序决定
- **来源**：2026-11-09《交接 AutoTest 进展整理》延续讨论；用户分别明确“各专业自己做可实测界面，自己实测后再集成”以及**“总体 UI、测试与报告分支原本无法直接实测，应等统一集成后实测”**。
- **状态**：USER_CONFIRMED / RECORDED；未执行 SAEF 原生 registry/check，不冒充已批准 G3/G4 工作产品修订或任何实机 PASS。
- **修订说明**：本 v0.2 **纠正 v0.1 把所有专业不加区别要求独立实测的过度扩张**。Git 历史保留 v0.1，现行规则以本版为准。
- **执行说明**：`docs/r0-lab/05_专项独立实测优先于统一集成_20261109.md`
- **R1 UI 既有设计参考**：`docs/ui-r1/reference/AutoTest_UI_Interactive_Prototype_V0.1.html`、`docs/ui-r1/reference/02_逐页面全元素设计.md`；为历史设计候选/参考，非已通过 WPF 人工验收的成品。
- **R1 UI 任务**：`docs/ui-r1/00_R1总体UI开发与接入边界_20261109.md`

## 一、两个验证轨道

### A. 可独立验证的专业实测分支

DEVICE、WAVE、UDS/Flash、BENCH、PROJECT/DBC 中确实可以脱离正式 HOST 验证的功能，以及 SCAN 的扫码枪输入/规则等，**由各自分支自行实现本专业功能、专业可运行 WPF/诊断入口、日志、最小受控适配、自动测试**。Windows Build/Test → 用户实际操作/必要的物理试验 → 原专业对话连续修正 → 达到该专业适用的验收条件之后，再进入统一 HOST 的正式接线/全系统回归。

注意：**可实测的功能先测，不代表所有功能都能脱离平台单独验证。** 实际要求多个专业共同参与的能力可以采用受控最小测试装配，但不能强迫先把完整 R1 Shell/生产闭环集成出来才能测自己的独立能力。

**DEVICE vs BENCH**：DEVICE 专业窗口用于底层 WinUSB/CAN/20-bit KeyMask/五路人工测量/电气安全诊断；BENCH 专业窗口用于 ECU/Profile 中的**真实按键**、按下/保持/释放与操作证据。20 Key 是模拟电压合成工具，不是正式台架用户面对的 20 个真实按键。二者不得互相冒充，依据 `ADR-20261109-REAL-KEY-VOLTAGE-SEMANTICS.md`。

### B. 后置统一集成验收的分支——用户特别确认

**总体 UI（R1 UI）、TEST（测试程序/引擎）、RECORD/报告（正式运行记录、报告/统计）**：现在就继续研发并保证有独立的软件单元/组件验证、契约验证、Mock/回放回归、需要时 WPF 冒烟启动；但**不要求它们分别开发额外独立可现场实测的上位机，也不安排用户在统一集成前逐个验收**。

- **总体 UI**：负责完整产品级 WPF/Prism IDE Shell、全局视觉/交互/主题、工程树、工具窗/多文档/停靠/菜单/导航/命令/上下文、专业页面容器与集成规则。现在即按 R1 的完整产品需求开发；等专业能力进入统一 HOST 后，用户在整体软件中评估 UI。不能把简单 Shell 或模块占位页冒充 R1 UI 已完成。
- **TEST**：负责编排/编辑器内部功能、控制流、执行引擎、调试与结果展示所需 UI/VM 及自动测试；完整 RunCoordinator→共享资源/设备→StepFact 的使用体验在集成后验证，不另造独立专业 EXE 或第二套正式 TestDocument。
- **RECORD/报告**：负责 SQLite/OperationAudit/Trace 聚合、出厂/复测去重统计、报告生成及自身专业展示/测试；依赖正式 RunId/StepFact/生产数据的完整用户验收在集成后，不另建一个临时生产/记录体系。
- **SCAN/Production**：扫码枪识别、规则/匹配/诊断可先做独立台架验证；正式扫码→TEST→RECORD→报告→放行/复测在后置集成中验证。
- 各分支不因用户尚未验收而停下无关的专业开发；用户无需反复解释已在 SAEF 确认的需求。

## 二、专业 UI 所有权与最终接线

各能独立实测的分支自行完成完整的**专业页面与临时诊断/测试启动入口**，原专业对话持续改进；总体 UI 分支负责**统一产品 Shell 和全局交互**，不能越权接手或重做成熟专业业务 UI。R1 UI 可设计所有 P01–P13 页面的统一信息架构/视觉规范和专业布局接口，具体专业功能仍由拥有该页面的分支实现；争议由主对话裁决。

INTEGRATION 在后置阶段负责正式 Shell/Prism 导航、DI、唯一共享 USB/CAN/Station Session、Project/Resource Revision Context、跨专业适配、记录关联和系统回归。R1 UI 分支与 INTEGRATION 在 Shell/Bootstrap 共享文件上需指定单一责任人并经主对话协调，不应并行覆盖。**专业独立启动入口不进入正式产品时，必须可无损移除而不影响业务能力**。

## 三、现在就需要的接口，不需要现在就全接上

每个专业的交付注明 View/VM/后台入口、资源/Revision/Station 输入、业务操作和取消、错误/结果/TraceRef、来源 Mock/Replay/Physical、长生命周期会话/订阅的所有权以及后续单一 Prism DI 接线方式。专业可以用私有 Mock/Replay 适配验组件；不得修改公共 Contracts、设备核心、ISO-TP/CAN Hub、另一专业私有文件或绕开真实物理授权。

真实 USB/CAN/按键/IGN/夹具/UDS Tx/Flash 始终分别遵守已有安全门禁；**独立运行≠自动物理授权**。RequestIssued、TransportAccepted、PhysicalObserved、EcuResponded、BusinessVerified 的证据等级不能相互冒充。

## 四、状态和审核

A 轨：专业源码就绪 → Windows Build/Test → 专项可操作 → 用户反馈/复测 → 专项验收 → 正式集成 → 全系统验收。

B 轨：R1 UI/TEST/RECORD 开发与自动/组件验证 → 待正式集成 → 合并组件/服务/页面 → 用户在完整 HOST 中操作验收 → 修订收敛到 R1。

上述状态分别记录；一条轨道的 PASS 不证明另一轨道已完成。原有 G3/G4 ACCEPTED、已冻结物理事实与安全边界不因本流程修订自动改变。
