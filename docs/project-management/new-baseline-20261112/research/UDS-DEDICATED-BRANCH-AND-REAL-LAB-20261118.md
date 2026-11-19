# AutoTest｜UDS 专用研发分支、独立 WPF 实测程序与验收计划（2026-11-18）

**负责人明确指令**：UDS 研究需要**专门开发分支**和**单独的上位机程序进行真实 ECU 实测验收**。  
**正式开发分支**：`feature/uds-lab-20261118`，已从 `main@1d6eeba8561a779640b20c5a3f384327a0018079` 建立，初始无 UDS 实现改动。  
**管理分支**：`Project_Management` 直接归档；无管理交付分支和管理 PR。  
**当前状态**：`BRANCH_CREATED / RESEARCH_AND_IMPLEMENTATION_SCOPE_ASSIGNED / UDS_LAB_NOT_BUILT / FIELD_VALIDATION_PENDING`。  
**G4 状态**：自动 TEST 仍待用户实测；UDS 独立工具实测**不依赖 G4 先通过**。UDS TEST 步骤最终集成仍依赖正式执行合同和 G4 真实运行基线。

## 一、交付目标：真正独立的 UDS 上位机

提供 **Windows WPF 独立可运行** `AutoTest.UdsLab.exe`（建议项目 `src/AutoTest.UdsLab/`，待执行专业核实现有 sln/引用后定点建项），而不是 G4 主窗口的新 Tab、无法连接真机的 UiPreview、静态设计 HTML、或另造协议的演示程序。

- 与 `AutoTest.Desktop`、`AutoTest.FlashLab` **独立启动、独立 UI、独立验收和独立运行记录**；不得同时抢占同一台真实主控/ECU 诊断会话。
- **尽量复用已有** `AutoTest.Specialists/Uds/{UdsClient,UdsSessionService,UdsEndpointProfile,IsoTpConversation,CanHubIsoTpLink,UdsTrace,UdsServiceCatalog,Workbench/UdsToolDocumentStore}`，及现有 `AutoTest.Devices` WinUSB/CanFrameHub/物理授权。不能创建第二套 WinUSB、CAN、UDS/ISO-TP 解释器。
- UDS 专项**负责诊断业务及独立实测程序**，不得代替 FLASH 专项决定 OEM 编程配置、Seed/Key 算法、擦写/校验/签名和 FLASH 结果；FlashLab 的真实烧录成功不自动替代 UdsLab 验收。
- 新程序默认浅色、中文、直接操作，左侧请求/配置、中间请求与响应/解码、右侧时间线/原始证据等比例以可实际操作为准，不要求复制冻结的自动 TEST IDE；上位机必须清楚区分“已发送/响应/期望匹配/协议错误/未验证”。

## 二、现有代码静态调查（专项目录应复核最新 HEAD）

`main` 包含 `src/AutoTest.Specialists/Uds/`、`src/AutoTest.Devices/Usb/` 及 Specialist.Tests，但**未见 `src/AutoTest.UdsLab` 独立项目**。

- `UdsClient`：请求/相关 ID、正响应 SID、NRC与0x78延时响应、P2/P2* 和异常日志；源码说明目前按风险等级限制非只读类请求。
- `UdsSessionService`：已有实际 CAN Lease、CAN Hub 与 ISO-TP Conversation 的会话抽象；必须审查设备独占与超时/取消。
- `UdsServiceCatalog`：已有只读、诊断状态变更、写入、编程等风险等级分类；**研究不能一键允许所有 SID 实体发送**。
- `UdsToolDocumentStore`：已有诊断端点/ECU 标识、服务模板的存取结构；须确认格式兼容、有效性、原件备份与配置冻结。
- `UdsStepHandler`：仅说明可与 TEST 适配，**不代表正式 TEST UDS 步骤已经真实执行通过**。

## 三、专门研究与详细设计成果（研究 AI 交项目管理审查）

1. 梳理每项 UDS 现有代码能力、被阻断能力、响应/错误/证据缺口与源码位置；不预设所有服务已接通。
2. 定义诊断 Profile：ECU 身份、请求/响应 CAN ID、帧类型、寻址、时间参数、P2/P2*、流控等与协议对应的字段；按现有真实后端合同复用，不借 FLASH 专项的编程 Profile 自动设通用默认。
3. 独立 UI 功能合同：连接/断开、真实会话状态、服务 SID 分类、请求 HEX、DID/子功能字段、响应原始 HEX/已解析字段、正负响应/NRC、0x78、超时/取消、可保存模板、可导出完整审计、明确不支持能力。
4. 安全合同：只读诊断先测；会话变更/复位/写入/例程/安全解锁/编程等应按风险等级、具体 ECU 与当前会话、授权限制决定，禁止简单放开目录；对有实体副作用的操作必须具备任务级授权和 UNKNOWN 状态。
5. TEST 诊断步骤合同：请求/期望、DID 数据解析、断言、变量/证据、StepId/Occurrence/取消/异常和会话所有权；只做定点 G3 增量，不新增第二 TEST DSL。
6. 分层验收用例和真实 ECU 证据格式：对每个已测 SID、正/负响应、时序、结果、ECU 固件身份、工程/配置/EXE/源码 SHA 进行追溯；如某 ECU 不支持一个 SID，记录 NRC 而不是自动“兼容成功”。

## 四、开发顺序（唯一真实实测程序）

| 阶段 | 工作 | 是否能宣称验收 |
|---|---|---|
| UDS-R0 研究与合同 | 全面现状调查、服务矩阵、实机测试步骤、风险/授权合同 | `DESIGN_REVIEW`，不宣称运行通过 |
| UDS-L1 独立 WPF | 建立 `AutoTest.UdsLab.exe`、真实设备连接/断开/日志、配置、只读请求/响应和导出；复用既有协议服务 | Windows 编译/启动与服务单测独立记录，不等于现场通过 |
| UDS-L2 真实 ECU 验收 | 用户现场运行程序，至少完成真实连接、只读 DID/服务请求、有效正响应、可控 NRC/超时、长报文 ISO-TP/取消（视 ECU 支持而定）与证据导出 | 依据实际观察的服务逐项 `FIELD_PASS`；不支持部分 `UNSUPPORTED/NOT_VERIFIED` |
| UDS-L3 状态改变类验证 | 仅在具体 ECU 明确授权和条件满足后测试扩展会话、复位或其它有副作用服务 | 单独真实授权与证据；不随只读 L2 自动开放 |
| UDS-G4 复用接入 | 将已验证 UDS 业务接入 G4 的独立工具入口与正式 TEST UDS 步骤 | 须在 G4 单一 Run Context、设备会话/实体 Fence、异常和证据合同下另行验收 |

**用户明确要求 UDS 独立实测。没有完成 L2 实机验证，不得用 Mock/静态源码/测试绿灯来签收 UDS 专项。** 本地 Codex 仅按任务执行 Windows Build/Test 和记录实际启动；真实 ECU 请求由用户现场受控实测。

## 五、职责边界与提交制度

- **UDS 专业 AI**：专用分支开发 UdsLab 独立 WPF、UDS 诊断业务、专用测试/证据；按专业文件责任提交。非本专业共享接口变更先提供精确差异与影响，不擅改主控固件、FlashLab 程序或 G4 私有 UI。
- **INTEGRATION/TEST**：研究成果通过 UdsLab 真实验收后，再按正式批准合同将对应服务与配置接入 G4/TEST。不能用重复拷贝把源码复制进主窗口。
- **FLASH**：继续独立处理 CanTool 编程相关 OEM 状态机和 APP 版本后验，不受 UDS 研究替代。
- **项目管理**：研究/实现/Windows 可启动/真实设备验证/自动 TEST 接入分别记状态。专业短期分支可以向 main 提交 Draft PR 供技术审查，但 **不自动合并**。这与 **管理分支没有 PR/交付分支** 不冲突。

## 六、验收阶段最低输出

**UDS 专项向项目管理 AI 汇报**：Git 分支与精确 SHA、项目与 EXE、共用代码/改动代码列表、可实际启动界面及截图、用户现场 ECU 设备身份和请求/应答 IDs、源 PDU + 正/负响应证据、NRC 与 P2/P2*、长帧与取消、失败/UNKNOWN 处理、诊断导出原始文件、当前服务矩阵和无法确认项。

**不得认定**：新分支创建=UDS Lab 已实现；独立界面显示=UDS 真实可用；只读 DID 成功=UDS 所有服务已验证；UDS Lab 验收=G4 自动 TEST UDS 步骤已完成。

## 七、与近期项目决策的关系

- [G4/FLASH/UDS/WAVE/SCAN 原推进顺序](G4-FLASH-UDS-WAVE-SCAN-SEQUENCING-20261118.md)：此次补充强调专用分支和独立可执行 WPF，UDS 的开发和单独验收可以先于 G4 主链完成；TEST 诊断步骤仍需正式集成验证。
- [FLASH 指定 ECU 阶段验收](FLASH-REFERENCE-VALIDATION-INTAKE-20261118.md)：Boot 复位应答成功、APP 后验未知，不得推断 UDS 已验收。
- `main` 保持稳定；新分支初始基线 `1d6eeba8561a779640b20c5a3f384327a0018079`。 
