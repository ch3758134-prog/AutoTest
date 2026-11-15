# AutoTest｜G3-UX-01 v3 实测反馈接收与技术/产品边界复核

**记录编号：PM-G3-UX-V3-20261114 · v0.1**  
**来源：** G3-UX-01 独立 UI/UX 专项提交、由项目负责人在项目管理主对话转交的《G3-UX-01 v3 实测反馈与重大设计纠偏报告》；对应样本 `AutoTest_G3_UX01_IDE_Research_v3.html`。  
**来源性质：** 专项对用户 HTML 实际操作反馈的转述和专项的分析，不表示 PM 本人打开或验收过该 HTML。  
**当前状态：** `FEEDBACK_RECEIVED / PM_ACTIONS_CLASSIFIED / UX_DESIGN_NOT_APPROVED`。**不改变 P0～G2、已批准 G3 DEVICE/WAVE/TECH 原文；不授权 G4、Codex 代码修改或实体操作。**  
**先前业务基线：** [PM-BUSINESS-DECISIONS-20261114.md](PM-BUSINESS-DECISIONS-20261114.md) BIZ-01～12；G1-REVIEW-v0.3、G2 Gate、G3 TECH 专项批准记录。

## 一、专项结论及 PM 处理（六类问题逐项闭环）

| UX 专项错误 | 本轮结论 | 处理负责人 | G3/G4 验收与接口边界 |
|---|---|---|---|
| UX-ERR-01 通用产品内置 EPB“驻车/释放” | **业务/绑定错误，接受纠偏**。通用 AutoTest 没有预置 EPB 功能按钮；仅由当前工程有效 Controller 配置提供功能名称/状态、允许组合、动作时序。未绑定不编造。EPB 只能作为明确隔离的样例工程数据 | PM 提供领域约束；UI 专项修正原型与后续 WPF | 使用不同名称/不同配置的 ECU 工程切换，按钮随配置变化；无配置时不能产生可执行虚构按钮；20 Key 原始掩码不成为用户功能按钮 |
| UX-ERR-02 手动台架动线错误 | **UX 原型问题**。v3 专项报告记录的本轮用户操作偏好为“左 CAN 观察、中功能按键/IGN/夹具、右 CAN 发送”，而不是底部日志。本记录作为最新原型试用反馈转交 UI 专项，不提升为不许再调整的底层系统架构或 WPF 像素合同 | UI 专项独立迭代；PM 只保障共享数据/设备互斥 | 同屏操作与观察的用户走查；台架与自动测试实体写入必须互斥；按键短序列仅围绕配置功能键 |
| UX-ERR-03 用例编辑效率/密度不足 | **UI 专项欠缺**，不要求改 TEST DSL。行内关键参数与流程层级、断点/错误/状态对应、Inspector 高级项、减少无效说明，均由 UI 专项研究 | UI 专项 | 连续插入/修改步骤、嵌套 IF/SWITCH/WHEN、流程调试与保存恢复的真实 Windows 走查；主 PM 不冻结布局 |
| UX-ERR-04 配置仅暴露底层字段 | **业务对象组织与界面映射问题**。工程资源、ECU Controller/TCF、实际台架 TBC 与运行前检查是不同职责；不把“station、can0、can1、voltage、profile”等直接裸露字段表单冒充完整配置工作流 | PM 明确配置职责与资源约束；UI 专项设计可理解的操作流 | 真实工程中创建/修改 Controller、绑定 TCF、核对固定台架、预检资源失效；没有软件控制 DUT 外部主电源 |
| UX-ERR-05 UDS/波形缺专业研究 | **仍是 G3-UX 专项未闭合**，专业操作模型须研究成熟产品的实际任务，而不是为每个工具复制通用三栏。UDS/波形后端合同已独立存在，不因 UI 原型不成熟重建 | UI 专项继续专业研究；PM 分别复核 UDS/WAVE 与设备会话/证据接口 | 后续单独评估专业 UI 用户任务、NRC/请求历史/寻址和波形游标/参考/质量/比较；不得声称 HTML 有真实 ECU/波形能力 |
| UX-ERR-06 研究解释污染正式 UI | **UI 专项缺陷**。技术说明、原型依据、尚未完成项进入文档帮助、研究报告或示例工程，而不是长期占用正式用户工作区 | UI 专项 | 正式工作区仅显示操作、对象、必要错误/状态/帮助；演示数据不伪装成实时硬件事实 |

## 二、跨专业技术接口：按源码核对，而非凭 HTML 推定

核查依据：`local@a9a26ebfba1efcf032ff8ec8c435336a89469647`，**代码存在不等于 Windows/实体测试通过**。

1. `src/AutoTest.Projects/ControllerConfig/ControllerModels.cs`：`EcuControlProfile` 包含多个 `ControllerDefinition`，各自有 `ControllerState(Name, KeyMask, Windows, Measurements, ActionTiming)`；`HardwareKeyPolicy` 约束现有板卡组合与硬件配置。功能名称来自工程资产，不能由 WPF ViewModel/HTML 预置“驻车/释放”。配置的 5 路电压约束和测量证据也不能由 USB ACK 推断为电气事实。
2. `src/AutoTest.Projects/ControllerConfig/ButtonActionPlanner.cs`：`ButtonActionPlanner.Resolve` 结合 `StateReference`、`ButtonActuation(Pulse/Hold/Release)` 生成 `ButtonActionPlan`。**UI 使用配置的功能语义和意图，底层适配器才使用 20-bit mask 经 CAN0 执行。**
3. `src/AutoTest.Desktop/Projects/TestSystemConfigurationDocuments.cs`：`TestSystemConfigurationDocument`（TCF）负责 ECU 模型/硬件修订及 DBC、Controller 资源绑定；`TestBenchConfigurationDocument`（TBC）描述单一固定测试台架与 CAN0/1 事实，**不是要求用户配置一个任意网络工位拓扑**。运行前检查验证实际连接、资源与能力，不能被字段编辑替代。
4. `src/AutoTest.Desktop/Projects/ProjectConfigurationDocuments.cs`：项目资产包括 Controller/Wave/Flash/Scan/TCF/TBC，具有各自身份和验证，编辑器应区分“编辑工程资源、绑定 DUT 配置、查看台架事实、运行前检查”四项用户任务。
5. `src/AutoTest.Desktop/Configuration/ConfigurationWorkbenchViewModel.cs`：已有配置编辑入口和字段视图，是否能支持用户易理解的完整任务尚需 Windows 体验验证；**UI 专项不得因此建第二份 Controller/Profile 或物理设备模型**。
6. G3-DEVICE-01 v0.1 已批准的实体授权、命令 ACK、GPIO/物理证据分层继续有效；手动台架与自动测试不能同时写同一工位。G3-WAVE-01 v0.1 已批准的真实来源、参考修订、三态判定及 TEST 关联继续有效。UI 重新设计不撤销这些合同。
7. G1 的独立 CAN 工具表述已在 [PM-BIZ](PM-BUSINESS-DECISIONS-20261114.md) 标成 `CR-CAN-01`：当前用户决定取消强制独立 CAN 工具、将人工 CAN 发送/日志放在手动台架；旧 G1 正文尚待局部正式变更审批，底层 CAN/DBC/UDS/FLASH 功能不能因此被删除。

## 三、PM-01～06 处理决定

- **PM-01 / ACCEPTED：** 以 `EcuControlProfile` 的配置身份作为唯一 UI 功能键来源；无配置则没有虚构功能键。旧 PM-BIZ 中“驻车、释放”只是具体 ECU **示例名称**，不是平台内置功能命名或默认按钮。
- **PM-02 / ACCEPTED_AS_UX_FEEDBACK：** 记录 v3 报告的左观察/中控制/右发送操作偏好，交由 UI 专项按真实操作继续完善；不把页面区域顺序升级为跨模块 API 或新 Gate 要求。
- **PM-03 / UX_G3_OPEN：** 测试用例高密度、连续编辑与流程控制体验尚未达标，按 UI 专项反复试用闭环，不以“HTML 可操作”作为 WPF 准入证据。
- **PM-04 / DOMAIN_CONTRACT_SET：** TCF、TBC、Controller、资源、预检职责按已核实源码与 G1/G2 确定；UI 专项负责完整用户编辑工作流，不直接修改物理设备领域模型。
- **PM-05 / UX_G3_OPEN：** UDS 与波形独立深化成熟产品的专业操作研究；PM 审核 UDS/ISO-TP、设备独占、WAVE 来源/参考/证据之间的一致性。
- **PM-06 / REJECT_V3_AS_G3_FINAL：** v3 HTML 不是正式 G3 UI 详细设计，当前没有用户验收或 G4 WPF 实施授权，不发布、不合并、不要求 Codex 实施。

## 四、交接 UI 研究对话的边界

请 UI 专项直接读取本记录、现行业务决策 BIZ-01～12 和真实当前 WPF 文件，对六类错误形成版本化研究和实测反馈。可以深化 UDS、波形等专业工具操作模型，也可调整工作区布局，但不改变已批产品/设备/真实运行语义；不把 EPB 专用样本按钮、K01～K20 底层硬件或历史研究说明做成通用产品默认界面。

**本记录不代表 PM 指挥 UI 专项具体像素或视觉设计；也不意味着 G3-A2/F1/DEVICE/WAVE 其他技术设计必须等待 UX 原型完成。**

## 五、下一阶段管理行动

1. 继续为 G3-A2 的项目级运行、工程师选择性执行及扫码生产自动运行，收敛统一工位授权和实际运行事件合同。
2. G3-F1 独立只读 UDS 通路与真实 CAN1 TX、设备独占另行设计；实体刷写 F2 需要单独授权与现场证据。
3. 对 G1 `CR-CAN-01` 组织**定点需求变更候审**，不直接修改 G1 已批正文，不要求用户重复解释取消独立 CAN 工具的原因。
4. 本轮报告由用户转交的原文应由 UI 专项继续保留；主线归档本 PM 审查决定和跨专业约束。跟踪 Issue #46/#45 及本轮 UX 专项 Issue。

**批准状态：** `G3-UX-01 V3=NOT_APPROVED`、`G3_GATE=NOT_YET_PASSED`、`G4_NEW_IMPLEMENTATION=NOT_AUTHORIZED`。
