# AutoTest｜G3-00 全量详细设计交付与准出审查候选 v0.1

**阶段：新基线 G3｜2026-11-15**  
**结论建议：`PASS_WITH_LIMITATIONS`（**仅候选，等待项目负责人审查批准**）**  
**原则：** 不以 HTML/Mock/纸面代码替代真正 WPF/实体 DUT 验证；可以按已获准的详细合同优先进入 G4 非实体 UI 工作包，再接分阶段真正硬件功能。
**前序 Gate：** P0/G0/G1/G2 已有批准记录；G3-DEVICE-01 v0.1、G3-WAVE-01 v0.1、G3-TECH-INTEGRATION-01 v0.1 已单独批准，**不能冒充整个 G3 已通过**。

## 一、本次精确审查对象

| 交付产品 | 作用 | 状态 |
|---|---|---|
| **G3-01 STRICT UI PARITY** | 对冻结 HTML 逐区域、逐操作到原生 WPF 的严格实现合同；不许实现者擅自改变 UI | 候审 |
| **G3-02 SOURCE CLEANUP** | `local` 旧 UI 删除/替换清单、保留后端边界、分叉 Git 处理和禁止兼容旧 UI | 候审 |
| **G3-03 TEST RUN CONTRACT** | 当前 TEST 领域模型、STEP/Flow/变量/等待/异常、项目级执行、所有权/证据/生产扫码 | 候审 |
| **G3-04 SYSTEM MODULES** | PROJECT/DBC/DEVICE/WAVE/SCAN/RECORD 和独立 Flash/UDS 先后顺序/接口 | 候审 |
| **G3-05 G4 WORKPACKAGES** | 文件与接口实施分切、Windows 验收、真实设备独立许可与用户实测回路 | 候审 |
| **UI-AUTO-G3-20261115-S01** | `docs/ui/archive/auto-test-20261115-ec75a9fe/AutoTest_UI.html`，完整 173155 字节、Git Blob `c1a55e29...` | **已冻结归档，自动测试操作方向用户已认可；不是整份 UI 全批准** |
| 已批准 G3-DEVICE/WAVE/TECH | 真实工位/波形证据与跨模块协调的上游详细合同 | 已批准、继续有效 |
| Flash F0 分支工作产品 | 独立烧录 Windows EXE 研究/配置/真实 UDS CAN1/安全阶段划分，独立开发节奏 | 研究执行中，G4/F2 实体权限未授权 |

**来源说明：** UI 工作源 `ui/ide-design@ec75a9fe69626d2a83e0c4ae6b23d187d5ccf5a2`；真实 HOST `local@a9a26ebfba1efcf032ff8ec8c435336a89469647`；`main` 与 `local` 分叉，不能整支合入；本次归档和技术文档独立提交至 main，不修改 HOST/fmw 源码。

## 二、产品定义与非谈判性边界

1. 通用 ECU 测试与开发环境，单 PC/单台架/同时一 DUT；不同 ECU 型号与硬件版独立工程。自动测试主线；工程师选择部分用例调试；生产扫码匹配与受控预检通过后自动执行批准方案。
2. UI 正式术语 `测试工程 → 测试项目 → 测试用例 → 步骤`；Package 只是底层文件对象；准备/测试/清理是可编辑阶段；不新建“测试程序”第三个业务对象。
3. 手动台架为多种真实外部测试激励+ DUT 观察的综合人工工具；KEY_SIM 20 开关/5 路电压是其中一项硬件实现；通用 UI 无 EPB“驻车/释放”默认动作。CAN 发送及日志在手动台架，不强制独立 CAN 工具。
4. ECU 主电源由外部电源控制，不要求当前 HOST/主控提供；CAN0 KEY_SIM、CAN1 DUT；IGN、夹具的手动与测试步骤需要真实连接和正确状态/授权。
5. DBC/UDS/Flash/WAVE 各自有专业功能，服务自动测试的同一资源/协议核心。烧录独立 EXE 研究/实现先行，独立 UDS 专业 UI 后研究；波形判定按未来自动测试现场实测迭代。
6. HTML 是 UI 视觉/交互唯一实施标准，不可作为硬件或真实测试完成证据。自动测试 UI 已获得用户正面使用反馈；其余专业 UI 仍归专项持续研究。

## 三、已确认的落地可行性

**可行：** `local` 中已有 WPF Prism Shell、`ProjectWorkspaceService`、工程项目/用例、`StepNode`/草稿事务/Mapping、DBC/Controller、WinUSB/CAN 实体服务、单包测试运行、波形基础、Scan 和 Record 基础。因此**原生 WPF 严格模仿冻结 HTML 在架构上可实现**，无需整体推倒后端，更不需要 WebView2 执行演示脚本。

**现状不能直接通过：** 旧 `MainWindow.xaml`/TEST `TreeView` 与 HTML 的布局和高密度步骤交互明显不同；旧 `MainWindowViewModel`/编辑器 VM 把 UI 与部分业务逻辑混合；`StepDescriptorCatalog` 的 SWITCH/WHEN/TRY/CALL/CanWait/UDS 等并未全部具备真实可执行语义；`RealTestWorkbenchService` 单用例并在结束时 Cleanup，不能直接当多包项目运行；`HardwareConnectionService` 的 CAN Hub TX 目前缺正式诊断适配；旧生产工作流有 `MockObservation/SimulationOnly`；`StationControlService` 目前明确拒绝部分物理输出。这些事实阻止将“UI 可操作”宣称为“R1 已可出厂”，但**不阻止独立开展严格 WPF 界面及真实工程编辑实施**。

## 四、G1 需求（REQ-01～47）到 G3 的完整覆盖关系

| G1 范围 | G3 设计覆盖 | 本次准出限定 |
|---|---|---|
| REQ-01～09 工程/资源/DBC | G3-01 U-01/02/09、G3-02 保留 Projects、G3-03 资源绑定 | 真实保存/DBC 编辑与项目引用须 G4 Windows 验证 |
| REQ-10～22 TEST/Flow/编辑/真实判定 | G3-01 U-03～14、G3-03 全套步骤/运行、G3-05 TEST-FLOW/A2 | IF/SWITCH/WHEN/异常/调试尚未全部真实执行；G4 不得退化 UI 或伪执行 |
| REQ-23～31 真实设备/台架/输入 | 已批 G3-DEVICE-01 + G3-03/04 工位合同 | 设备状态/夹具/IGN 依真实硬件验证，实体自动运行另行许可 |
| REQ-32～38 DBC/UDS/WAVE/Flash 专业 | G3-04 + 已批 WAVE + Flash F0/F1 专项 | G1 独立 CAN 工具需 CR-CAN-01 定点文本批准；UDS 专业 UI 后置；F2 刷写未获许可 |
| REQ-39～47 Scan/Production/Record | G3-03 第 5 节 + G3-04/05 的 SCAN/RECORD | 当前 ProductionWorkflow 模拟残留必须更换，扫码生产需真实项目 Run 与去重证据 |
| Q-01～08 可靠性/可用性/维护 | G3-01 视觉对照、G3-02 清理、G3-03 数据质量、G3-05 本地验收 | 本轮**没有**新 Windows Build 或实体 DUT 结果，不能关闭质量限制 |

## 五、G3 本轮准出限制（必须显示在审批记录与 G4 工作包）

| 限制 | 必须怎样管理 | 可否先开展严格 UI 实现 |
|---|---|---|
| **G3-L01** 当前版本自动测试 UI 有完整 HTML 快照，但未做相同场景下的 WPF 对照实测 | 精确 HTML SHA + UI-DIFF 回归 + 用户 Windows 验收 | **可**，这是首要 G4 工作包 |
| **G3-L02** `main/local` 已分叉，旧 UI 源码与业务 VM 耦合 | 指定 local HEAD、逐文件移除旧 UI、保留核心；不整支合并 | **可**，实施前须确认引用与短分支 |
| **G3-L03** TEST Flow 的所有 HTML 步骤尚无完整真实解释执行 | G3-03 AST/Slots/参数映射与 TEST-FLOW #52 补齐；未支持步骤清楚阻断 | **可**，但不能宣称已执行/通过 |
| **G3-L04** A2 项目级 Run/包间 Cleanup/工位授权未完整 | 按 G3-03/#48 真实服务/资源 pin/证据补齐 | **可**，Run 留为不可虚报的实际接口状态 |
| **G3-L05** 实际 DEVICE/IGN/SOL/KEY_SIM/USB 现状须现场验证 | 已批 G3-DEVICE，独占、UNKNOWN 和真实控制权限 | **可**，物理执行另审 |
| **G3-L06** 生产扫码与旧 MockObservation/SimulationOnly 没闭环 | 真正生产项目 Run、资源与记录、复测去重 | **可**，不允许假生产 PASS |
| **G3-L07** WAVE 时间/判定及 UI 需要用户真实测试后的优化 | 已批 G3-WAVE 保持，真实 TEST 采集/判定后再改参数 | **可**，不得预定虚假物理容差 |
| **G3-L08** 独立 Flash Lab 未真实接 ECU/算法/刷写，UDS UI 后置 | 独立分支 F0/F1，F2 安全/恢复/身份批准，不影响自动测试 UI | **可** |
| **G3-L09** 用户最新取消独立 CAN 工具要求与旧 G1 REQ-32/33 文本不一致 | 按 G1-CR-CAN-01 #50 局部变更审批，不恢复旧 CAN UI | **可**，新候选不得引用旧 CAN 产品义务 |
| **G3-L10** 专业 UI 全部未统一用户验收 | UI 专项继续更新同一工作 HTML；已冻结自动测试版本不漂移 | **可**，不得宣告全部专业界面已通过 |

## 六、正式准出与下一审批请求

### 建议 G3 `PASS_WITH_LIMITATIONS` 的准确含义
**G3 的总体系统设计方向、严格自动测试 UI 实现标准、业务/硬件边界与受控工作包交付方法已可支持第一批 G4 非实体工程实现**，而不是“已完成所有实际 ECU 功能”或“所有 TEST/Flash/Device/Wave 接口已验证”。这些差距以 G3-L01～10 显式挂在后续 G4 的硬性验收和实体门禁上。违反既有安全/实体边界时不能靠 `PASS_WITH_LIMITATIONS` 绕过。

### 如负责人批准本交付，建议直接允许的后续工作
- 首批**G4-UI-00～03（严格按冻结 HTML 原生 WPF 重建 + 真实工程资源编辑）**：不调用新实体输出，不保留旧 UI 兼容层，由用户打开本地 Windows 可执行程序实测；
- **Flash F0 研究和 F1 离线 XML/固件/独立 EXE 结构**可并行推进；接真实 CAN1/只读 DID 仍需通过物理 Fence 和当前现场授权；
- 其他 G4-TEST-FLOW/A2/SCAN/DEVICE/WAVE/RECORD/Flash F2 的实际实体能力，按 G3-03/04/05 与各自真实证据逐工作包审查，不能因 UI 获准就默认全允许。

### 本次**不包含**的授权
- 不自动合并 `local` 整支或将未实测候选直接并入 `main`；
- 不批准 Flash F2 实体编程、未知恢复流程、烧录 DLL 猜测、永久放开 USB 写；
- 不批准以 HTML 演示高亮/旧 Mock 测试通过来声明真实 TEST/生产合格；
- 不替用户验收波形/专业 UDS/Flash/DBC/手动台架完整 UI；
- 不擅自恢复旧 UI 或改变已冻结 HTML 的自动测试交互。

**需要负责人的正式选择：** 同意/驳回 **G3-00～05 全量详细设计 v0.1 和 G3 Gate `PASS_WITH_LIMITATIONS`**，另确定是否同时批准首个 G4 非实体 UI 实施包。未明确批准前，以上仅是完整交付候选，记录应留在 GitHub，不能假装 Gate 已通过。
