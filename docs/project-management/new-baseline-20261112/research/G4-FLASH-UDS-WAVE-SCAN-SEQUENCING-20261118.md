# AutoTest｜2026-11-18 G4 / FLASH / UDS / WAVE / SCAN 专项推进顺序与依赖

> **项目管理状态**：`OWNER_DIRECTION_RECORDED / IMPLEMENTATION_GATES_UNCHANGED / FIELD_G4_PENDING`。这是项目负责人在对话中明确的下一阶段工作顺序，不是 G4 Gate PASS、全功能实测通过、PR 合并批准或对具体实体操作的授权。  
> **唯一管理写入分支**：`Project_Management`，不新建管理交付分支/PR。  
> **负责人原意**：“准备将烧录接入上位机，但是G4的自动测试还没实测完成；接下来进行UDS研究；波形和扫码枪的完整研究需要G4的自动测试完成后才能接入实测。”

## 1. 执行顺序（确定的管理取舍）

| 工作线 | 当期工作 | 状态与依赖 | 放行边界 |
|---|---|---|---|
| **G4 自动测试（主线）** | 完成正式 WPF 编辑/保存/选测/预检/真实 TEST 运行、反馈证据和失败定位 | `ACTIVE / FIELD_VALIDATION_PENDING`；优先级最高 | 只有真实受测的功能标为 FIELD_PASS；不得以 UI Preview 或 Mock 替代 |
| **FLASH 接入上位机** | 复用现有已调通的 FlashLab 流程，在正式 HOST 提供受控独立工作台入口/界面集成 | `INTEGRATION_PLANNED`，**无需等待 G4 整体实测完成才可开展独立工具接入** | 不重写 CanTool 编程状态机；不把独立烧录成功误判为自动 TEST 已可执行 |
| **UDS 专项研究** | 研究正式 UDS 独立调试工作台、诊断会话/请求/响应/异常、TEST 步骤合同和配置 | `RESEARCH_START_NOW`；可与 G4/FLASH 接入并行 | 研究先形成完整 G3 定点技术设计与 G4 工作包，未经审查不得自造新协议栈或改变真实设备调用 |
| **WAVE 波形** | 保存现有研究/候选能力，针对 G4 TEST 执行证据和采样时序准备接口清单 | `FULL_INTEGRATION_AND_FIELD_TEST_DEFERRED` | 等 G4 自动测试真实执行基线经负责人检查；之后再做完整研究收口与接入实测 |
| **SCAN 扫码/生产** | 保存已有扫码、识别、匹配、报告等候选成果及接口需求 | `FULL_INTEGRATION_AND_FIELD_TEST_DEFERRED` | 等 G4 自动测试真实执行基线经负责人检查；之后才开展完整接入/生产工位实测 |

**解释**：这里“暂缓”针对 WAVE/SCAN 的完整集成与联动实测，不表示之前的研究作废，也不是要禁止必要的接口调查。用户已经明确先完成 G4 自动测试再接入实测，执行 AI 不得抢先将 WAVE/SCAN 的所有候选后端接线到不稳定的 G4。

## 2. FLASH 接入分层，不能混淆

### FL-I：独立专业烧录工具接入（现在允许规划并按既有职责实施）

- 现行候选：`feature/flash-lab-20261114@aafc2099793d2eb3c9b98e375168ff04aed4760d`，Draft PR #55，未合并 `main`。
- 依据：`research/FLASH-REFERENCE-VALIDATION-INTAKE-20261118.md`，指定 HydEPB 参考写入/校验/Boot `51 01` 已有现场专项成功汇报；APP 启动/软件版本后验未验证。
- 接入目标：正式上位机具备独立 FLASH 工作台入口、Profile/HEX/算法文件选择、冻结计划预检、明确的实体写入授权、可见阶段和失败审计。应**复用已有 FlashLab 编程流程**，不能复制一套不一致的业务代码，也不能因为合并 UI 修改 CAN/USB 通路。
- 先核对 FlashLab 与 HOST 最新准确 HEAD、共同引用的库和文件责任；独立工具可以先进入 HOST 导航/窗口，但不得把“能打开窗口”写成“真实 HOST 统一会话下可以烧录”。
- 如需要将 PR #55 的源码移入 HOST，先审查累计 PR 差异（先前 129 文件），按具体业务文件定点集成，**不执行整 PR 无差别合并**。

### FL-II：正式 TEST 烧录步骤（后续独立验收）

- 正式 TEST DSL 中创建受控的 Flash 调用/任务步骤（如仍未完成精确合同，先保持 `UNSUPPORTED/PREFLIGHT_BLOCKED`）；唯一 Run Context、实体授权、执行证据、失败终态及未知副作用。公共流程不得绕过 FLASH 授权或独立产生生产 PASS。
- 需要与 G4 真实自动测试运行器的取消、异常、清理、权限、工程资源 Revision 和结果状态兼容。
- 不把 Boot `51 01` 当作 APP 软件版本匹配。APP 身份/版本后验是 FLASH 专项独立待验证工作。

## 3. UDS 研究范围（新研究任务；非新增执行器）

研究现有：
`src/AutoTest.Specialists/Uds/{UdsClient,UdsSessionService,UdsEndpointProfile,IsoTpConversation,CanHubIsoTpLink,UdsStepHandler,UdsTrace}`（按真实仓库 HEAD 复核），及既有 DEVICE/TEST 服务，不重写物理总线、IsoTp 或 USB 驱动。

要求产出：
1. **独立 UDS 工作台使用流程**：目标 ECU/APP/Boot Profile 的参数配置；请求 SID/子功能/DID/数据编辑，执行一次请求、显示响应、解码/原始 PDU、NRC、`0x78`、多帧、取消、超时、日志/导出和必要安全提示。
2. **TEST UDS 步骤模型**：单次请求及响应断言、期待 SID/DID/值、超时/等待、NRC 结果、变量/数据提取、失败/异常区分、StepId/Occurrence 证据；运行中可否切换会话、会话状态由谁持有、如何与其它 CAN/Flash 请求共享占用管理。
3. **诊断服务覆盖表**：按协议标准及目标 ECU 实际证据分别列出可支持、待研究、禁止缺省执行的服务。编程/写入/复位/SecurityAccess 等可能改变 ECU 状态的服务必须审查物理操作授权，不得在普通 UI 请求框中无限制执行。
4. **与 FLASH 的复用与边界**：共享已经存在的 UDS/ISO-TP/实际会话能力；FLASH 特定序列、OEM `0x74`、FF00/FF01、尾签名和安全算法仍由 FLASH Profile 决定；不合并为通用诊断默认。
5. **G3 增量技术合同及 G4 分阶段任务**：对象/配置、UI Inspector、运行端口、错误/追踪、兼容、独立工作台实测与自动 TEST 真实验证分别验收。用户先看设计，再安排实现。

先做研究设计，未获相应明确实现批准前不新增模拟/虚拟设备成功路径，不修改 G4 自动测试主区与 FLASH 已验证流程。

## 4. G4 自动测试基线放行 WAVE/SCAN 的条件

“G4 自动测试完成”在本安排中指**用户对实际自动测试工作流的可用基线完成实测认可**，不是单靠代码编译、自动化单测或界面效果。

最低检查应能追溯：
- 工程资源/DBC/测试项目、测试用例、步骤和参数可新建编辑、保存重开；
- 用户选测、预检、单步骤及完整运行状态按已实现合同有效，错误/中断/未执行不伪装为 PASS；
- 当前真实可用硬件刺激至少形成一个用户确认的自动测试成功与失败样例，附测试对象、固件/工程源版本、原始日志和截图；
- 唯一真实设备会话和对应 Run Context 可追踪，不能由独立工具破坏当前工位状态；
- 设备专项尚未完成的能力可以明确阻断，不得因部分未验证功能就篡改其安全结论。

**上述是后续 WAVE/SCAN 集成研究的准入核对清单，并不代替正式 G4 Gate 审批。** G4 若仍有局部未完成项，负责人可明确按已验证的核心运行链决定对应联动测试窗口，执行 AI 不得自行宣称已完成。

## 5. 职责

- **G4 WPF/INTEGRATION/TEST**：持续完成自动测试实测与回归；FLASH 首先作为独立工具受控接入，TEST Flash 步骤后续另行验收；禁止 UI Preview 混入真实程序。
- **FLASH**：已有 HydEPB 参考流程与配置、擦写证据、复位/APP 后验边界；协助 HOST 复用，不扩权硬件、通用 CAN/USB/ISO-TP 专业。
- **UDS 研究 AI**：只负责 UDS 诊断业务设计与可执行合同调查，研究完先交 PM 审查，不擅自开始 G4 共享协议重构。
- **WAVE、SCAN**：继续保管已有研究成果，列出对 TEST Run Context / 证据与资源的依赖；完整接入和真实生产联调等待 G4 自动测试实测基线。
- **项目管理**：持续记录依赖、版本、负责人认可、缺陷和阶段性限制。管理变更只写本分支，无项目管理交付分支或 PR。

## 6. 不能由本记录推断

- 不等于 G4 已 PASS、FLASH 已合入 main、UDS 已完成研究/执行、WAVE/SCAN 已接入；
- 不等于 FLASH APP 版本后验已通过或可以自动触发真实烧写；
- 不等于将现有 UDS、WAVE、SCAN 的旧专项代码大批量直接合入当前 HOST；
- 不因为 WAVE/SCAN 延后，就删除之前 G3 需求、报告或研究资产。

## 7. 当前交付位置（登记时核对）

- `Project_Management@c9bd3f1531be0f9ba1e815e630b8a1e7ecaffe7b`（登记前）。
- `main@1d6eeba8561a779640b20c5a3f384327a0018079`。
- `feature/g4-html-parity-20261115@9599688c5519b28efb1aeae9562c990251da1056`（G4 仍待实机验收）。
- `feature/flash-lab-20261114@aafc2099793d2eb3c9b98e375168ff04aed4760d`，PR #55 open/draft/未合并。

后续各专业开始时仍须回读最新 SHA，不把本文件的静态记录误认成永远不变的 HEAD。

## 8. 2026-11-18 UDS 专项分支与独立实测程序补充（后续决定优先）

负责人进一步明确：**UDS 研究必须有专用开发分支，且必须开发独立 WPF 上位机进行真实 ECU 实测验收**，不能仅形成研究文档、依赖 G4 主界面的 UDS 工具或等待 G4 TEST 全链完成后才实测。

- 已创建 `feature/uds-lab-20261118`，从 `main@1d6eeba8561a779640b20c5a3f384327a0018079` 建立；开发分支入口 `docs/uds-lab/START_HERE.md`。
- 独立验收可执行目标 `AutoTest.UdsLab.exe`，通过现有真实 DEVICE/UDS/ISO-TP 服务做诊断实测，**不得以静态 UI 或 Mock 验收**。
- UDS 研究和独立工具实现可先于 G4 自动测试整体现场通过；而 UDS 的正式自动 TEST 步骤接入仍要求遵守 G4 已批准运行合同及相应实测结果。
- 详细正式合同：[UDS 专用分支和独立 WPF 实测验收](UDS-DEDICATED-BRANCH-AND-REAL-LAB-20261118.md)。本节是对原推进顺序的**补充与优先解释**，不重置 G4 或 WAVE/SCAN 的依赖。

## 10. 2026-11-19 G4 受控源码接收与 Fluent UI 专项接管（最新决策优先）

- 用户认可 G4 阶段成果、要求优先组织 PR #61 受控接入 HOST main；这不是 G4 实体全功能 PASS。PR 当前 dirty，正式 INTEGRATION 跟踪 [Issue #62](https://github.com/ch3758134-prog/AutoTest/issues/62)，冲突和 Windows 构建审查前不得合并。
- 独立 UI/UX 活动设计分支为 ui/ide-design，采用现代 Windows Fluent Light，先在 UDS 交互中验证编辑模式与组件，再用于手动台架/自动 TEST；需用户操作验收后才派发正式 WPF 实施。
- feature/ui-rebuild-20261119 只是较早创建的临时准备分支，现已暂停，不取代 ui/ide-design；未来若使用必须以获批设计与已接收 main 重建起点。
- WAVE/SCAN 原“等待 G4 自动测试通过”现在解释为等待**新的 UI 与正式 TEST 真实执行主线集成并获用户实际验收**；独立 FLASH/UDS 继续按专项实测和交付边界推进。
- 详见 [最新基线接收和 UI 专项合同](G4-PR61-BASELINE-ACCEPTANCE-20261119.md)。
