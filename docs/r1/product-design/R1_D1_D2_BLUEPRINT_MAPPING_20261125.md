# AutoTest R1｜成熟产品 D0/D1/D2 实施与差异审查（2026-11-25）

**正式依据**：《AutoTest R1｜成熟产品目标蓝图、交互合同与自主实施指令》全文第 0—14 节。  
**R1 实施起点**：e385071fd08148f00337c695c1b208e135378e83；仅更新 R1，不修改 main。  
**状态声明**：已完成 D1 独立离线交互原型及首组 D2 WPF 源码整改；未经 Windows 编译、真实浏览器 DPI 走查或真实硬件验收。正式生产 **NO-GO**。

## 1. 产品交互目标已独立冻结

四个一级任务：**生产检测、工程开发、生产准备、记录与报告**。

- 生产：操作员放入 ECU→扫码→受控夹紧→等待真实实体 START→原生 FLASH/APP 后验→一个有序连续 TEST 项目→同台 Inspection 证据归档→安全许可下释放→下一台。禁止 HEX/COM/用例编辑等生产界面控制。
- 工程：一个 ECU 型号工程、一个唯一 TEST 项目，含可选项目准备、有序用例调用实例和可选项目收尾；调用定义可复用；公共流程共享 TEST DSL。步骤编辑器以完整树和行间+为中心；复杂参数使用双击持久浮窗。
- 生产准备：四页（产品识别、工位设备、生产工艺、发布与验证），唯一关系是扫码原文→规则→编号→可信型号/硬件→唯一已发布工程→其中原生 FLASH 和完整 TEST；不得扫码重配资源。
- 记录：同一 DUT 有多次 InspectionAttempt，每次有 FlashAttempt、ProjectRun、CaseAttempt、StepOccurrence 与真实原始证据；复检不计新 ECU。
- 项目准备/收尾用于 65ms 整车 CAN 环境；不承包扫码、机械压紧、真实实体 START、安全释放或前置 FLASH。

## 2. D1 完整离线原型与两轮审查

由本轮对话提供 ZIP 制品 AutoTest_R1_D1_交互原型与评审.zip；内含可离线双击的 index.html、README、INTERACTION_CONTRACT、REVIEW_LOG。

交互覆盖：工程创建、唯一 TEST 用例树与重复调用/重排、任意节点行间插入、CAN ID/帧型/8 字节及 DBC 演示回写、IF/WHEN/SWITCH/LOOP/TRY、公共流程与参数、资源影响及采用、条码形制及批量编号表、夹具/START/工艺预览、分阶段生产演示、单台多次 Attempt 和报告钻取。所有演示状态显式 DEMO，不发起实体命令。

| 设计维度 | 第一轮 | 第二轮 |
|---|---:|---:|
| 产品路径 /20 | 18 | 19 |
| TEST 编辑与调试表达 /20 | 15 | 18 |
| 工程/公共资源 /15 | 12 | 14 |
| 生产与工位 /20 | 17 | 19 |
| RECORD /15 | 13 | 14 |
| UI/异常 /10 | 7 | 8 |
| **D1 设计自评** | **82** | **92** |

第一轮自主整改：补 TEST 调用重排/移除，CSV 批量编号，固定长度与字段长度分离、硬件版本字段；把模态遮盖编辑改成持久可移动浮窗，避免误关闭及草稿丢失。第二轮：多规则身份冲突、STOP/复检计数、机械状态 UNKNOWN、无数据不等于 ELSE，均在原型交互中明确区分。

实际仅执行本地 JS 语法检查、19 条原型基础操作检查、15 条产品场景检查，合计 34 条通过。浏览器人工可用性/DPI、Windows 运行和实体硬件均未检查。D1 设计 92 分不代表真实产品 92 分；核心安全阻断不能被设计评分抵消。

## 3. D2 已落 R1 的真实源码变更

1. MainWindow.xaml 和 MainWindowViewModel.Production.cs：四个明确一级入口；生产准备的编辑入口受现有硬件忙碌、测试授权、FLASH/手动 CAN 租约约束。仅复用实际业务服务，不创造另一套 TEST。
2. RecordHistoryWindow.xaml/.cs：沿用真实只读 SQLite RECORD 查询，按同一个 DUT 编号分组显示多次运行；不会将重复扫码判定成新 ECU。
3. ProductionPreparationWindow.xaml.cs 与新 ProductionPreparationWindow.Navigation.cs：重组到产品识别、工位设备、生产工艺、发布验证四个任务页，复用已有扫码、发布和工装编辑服务；标准生产工艺明确区分未连接的 START、机械许可和安全释放。
4. ProductionInspectionView.xaml/ProductionInspectionViewModel.cs：空闲和无真实步骤观测时隐藏右侧大块无意义实测详情，实际 StepOccurrence 到来后才展开；完整用例表始终显示。
5. AutomaticTestResourceContract.cs、RealTestWorkbenchService.Project.cs、TestProjectWorkbenchViewModel.cs：开发临时勾选范围运行原本逐用例释放真实 CAN；现改为一个受限 ProjectExecutionSession，所选有序子集在运行开始前冻结，每用例独立 RunId/CaseAttempt，周期 CAN 跨用例保持、结束统一撤销。普通功能 FAIL 可继续并保留 FAIL；Prepare/Cleanup、NoData、STOP、失联和授权 UNKNOWN 均拒绝继续。只有单个明确 FLASH 开发调试仍可走原独立授权，不能混入连续范围。

所有变更属于 SOURCE_STATIC，未运行 Windows 编译/CI/实体操作。

## 4. 目标—现有源码差异（按实际产品风险排序）

| 目标 | 现有可复用资产 | 真实差异与下一步 | 状态 |
|---|---|---|---|
| 原生项目准备→唯一树→项目收尾 | TEST 项目 v2 ProjectSetup/ProjectTeardown、同一 TEST DSL 与连续物理 Scope | 编辑、资源冻结与真实整组执行源码已接通；旧 v1 只能显式迁移，现场未验收 | SOURCE_STATIC 已接通 |
| IF/WHEN/SWITCH/LOOP/TRY 可正式执行 | StepNode、PlanCompiler、RealRunCoordinator | 必须分别审计有效性、新鲜度、未知态、异常分支和 StepOccurrence；未接线类型不能装作可用 | P0 未完成 |
| 公共流程输入/输出/局部作用域 | PublicFlowDocument 和版本引用 | 参数、返回、递归安全、冻结调用栈、单步调试需真执行及归档证据 | 未完成 |
| 各开发运行入口同一物理生命期 | 已有 ProjectExecutionSession/CAN/KEY/IGN/PhysicalCommandFence | 本轮修正勾选范围；单步及所有工具场景仍需审查前置环境/停止规则；ACK 不代表 ECU 接收 | 代码已改，待 Windows/总线 |
| 完整发行包与资源影响图 | 工程资产 SHA/修订、原生 FLASH Publication、生产准备发布配置 | 依然欠独立不可变离线发行包/回滚与全图影响；不允许旧配置隐式兼容 | 部分实现 |
| 实体 START、夹具许可/互锁 | 两路夹具控制 GPIO、真实主控 USB | 未确认真实 START/STOP 物理信号、机械反馈与夹紧安全许可，不能由 UI 模拟 | PRODUCTION BLOCKED |
| 本台 FLASH→项目 TEST→Inspection | 原生 FLASH、连续 TEST 和 RECORD 技术底层 | 真正单台 InspectionAttempt 连贯提交、异常复检及安全收尾未接通 | PRODUCTION BLOCKED |
| DUT 多次完整报告 | RECORD SQLite、RunHistory、报告导出 | 目前新增同 DUT 分组；正式 Inspection 事实与 FLASH/TEST 聚合仍缺，不能混开发调试与生产通过 | 未完成 |
| 工程师 UI 稳定与真实体验 | 浅色 WPF、CAN UI 显示限频、部分自动保存 | 1366×768 / 1920×1080、多 DPI、快捷键、浮窗、真实 CAN 高载/关闭恢复必须 Windows 验证 | WINDOWS NOT_RUN |

**禁止误迁移**：旧“准备”用例不可自动转成项目准备；旧历史 EXCLUDED 项不可自动改为启用；老扫码配方不再决定 FLASH/TEST；来源未核验的 HydEPB 参数不准随意补齐成“可执行工程”。

## 5. 下一轮实施 Gate

D2：完成原生 TEST 项目级准备/收尾，仍调用唯一 StepNode 解释器并保持可复用调用身份；完成流程控制/公共流程正式能力矩阵；工程资源改版影响预览和显式旧工程迁移。

D3：物理 START 接线调查与事件、夹具安全许可和真正机械证据、已发布原生 FLASH/APP 后验、完整项目 TEST、单台 Inspection/复检 SQLite 原子结案；真实硬件未知时必须阻断，不用软件 START 冒充。

D4：逐页落实原型 WPF 编辑体验和独立工具，不新增第二份业务配置；主窗关闭/异常收尾、CAN 8–10Hz UI 刷新、报告海量历史性能独立验收。

D5：Windows build/GUI/DPI、实物扫码枪、WinUSB/ECU CAN、独立 CAN 分析仪 65ms 周期、KEY_SIM、夹具/安全互锁、FLASH/UDS、HydEPB 驻车释放和报告复检全链路。

**最终结论**：设计原型已完成两轮并达设计分 92；真实 WPF 仅第一组 SOURCE_STATIC 变更；Windows 未验、真机未验、生产始终 NO-GO。不能把 D1 DEMO 或代码提交当成成熟产品通过。


## 7. 本轮 D2 第二次源码审查补记（更新，优先于上面旧状态）

2026-11-25：新加入原生 TEST 项目准备/收尾。TestProjectDocument v2 各有可选稳定调用 ID、资产 Revision/SHA；旧 v1 只可按原义读取，不能暗自把旧“准备”功能用例改写成项目级阶段。TestProjectWorkbenchViewModel 和 TestProjectWorkbenchView.xaml 允许工程师绑定/编辑/移除已存在的 TEST 定义，日常自动保存保留阶段身份。AutomaticTestResourceContract 对两个阶段执行固定 TEST 资源、DBC、波形与重复 FLASH 风险预检；ProductionPreparationService 把两阶段固定资源加入预检指纹。AutomaticTestHomeViewModel.Run 在同一 ProjectExecutionSession 运行 项目准备→有序功能用例→项目收尾；项目阶段各自写真实 Run/CaseAttempt 及 StepOccurrence，不宣称产品功能 PASS。工程师选择范围继续保持只执行其冻结有序子集，而非暗中执行未选择阶段。

新增主动发现：整组物理授权、CAN/KEY 和 DBC 映射原来依赖本地显示 Name 作为全局键。不同 TEST 用例可合法复用显示名称，但指向不同 CAN ID 或 DBC 资源。已将 RealTestWorkbenchService.Project、ConfiguredStationActionPort、LiveDbcSignalService 的跨用例绑定改为完整 TestTargetMapping 及其冻结资源的值身份，防止把另一个用例的同名信号当作观测来源；物理发送仍受既有 PhysicalCommandFence 和 CAN ID 白名单、USB 连接世代约束。

回归源码：TestProjectPlanWorkflowTests 新增 v2 阶段序列化/重开、v1 未显式升级拒绝、用例与阶段重复调用 ID 拒绝等测试（NOT_RUN）。已有项目资源和设备实测未由本轮执行。

下一阶段差异：条件分支与公共流程参数/返回值/递归的完整真实能力矩阵仍未准出；项目阶段 WPF 调试状态与报告优先级需 Windows 验证；实体 START、工装机械反馈/互锁、正式 InspectionAttempt 的 FLASH→TEST→收尾事实链依然 P0 阻断。因此本轮产品原型 D1 设计仍自评 92/100，**绝不能据此给 R1 实际生产产品或 WPF 验收评 92 分**。正式状态 PRODUCTION NO-GO。
