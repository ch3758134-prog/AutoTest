# AutoTest｜Flash Lab 专项范围纠偏：配置和独立前端优先，复用现有 UDS/ISO-TP 后端

**记录：FLASH-SCOPE-20261115-v0.2 · 用户最新明确纠偏**  
**状态：USER_SCOPE_CLARIFIED / F0 研究继续 / 不自动获得 ECU 实体刷写许可**  
**适用：** `feature/flash-lab-20261114` 及 Issue #25/#44/#49 中所有 Flash 专项任务。此记录优先于此前“重新建立一套独立 UDS/ISO-TP/设备架构”的错误理解。**此前 F0 原文保存为历史调查，不得再当成强制重构计划。**

## 一、用户真正要求

> 独立烧录工具**使用 AutoTest 已有的 UDS、ISO-TP、USB/CAN 后端**。专项主要研究 CanTool 的配置和烧录过程，形成一个**独立可操作的 Windows 上位机前端**；研究/实测通过的共享协议后端不应重复开发或产生第二套模型。完成烧录专项，再研究独立 UDS 专业工具。

**项目管理纠正**：之前把 Flash 任务讲成了全新诊断架构项目，重复研究协议和设备会话，显著扩大范围，属于 PM 任务定义错误。**G4 自动测试 WPF 分支不因此受阻**。

## 二、已有后端代码是默认唯一实现（根据本分支源码核对）

| 唯一代码来源 | 实际已具备什么 | Flash Lab 应做什么 |
|---|---|---|
| `AutoTest.Specialists/Uds/IsoTpFrameCodec.cs`、`IsoTpConversation.cs` | ISO-TP 编解码、多帧/FlowControl 交互 | **直接调用，不开发第二套 ISO-TP** |
| `UdsClient.cs`、`UdsSessionService.cs`、`UdsEndpointProfile.cs` | UDS 请求/响应、NRC/只读风险限制、会话与寻址基础 | **直接复用**；某服务确实缺失才做受控最小扩展 |
| `CanHubIsoTpLink.cs`、`CanFrameHub.cs`、`UsbSessionService` | CAN/ISO-TP 链接与 USB 会话接口 | 复用现成发送/接收事实；**只补独立 EXE 所必需的真实 TX 接线，不复制驱动/再建协议层** |
| `Flash/FirmwareImageParser.cs` | HEX/S19/BIN 离线解析与基本校验 | 直接作为固件导入功能后端 |
| `Flash/FlashTransferScheduler.cs`、`IProgrammingWorkflow.cs`、`ProgrammingProfile.cs` | 部分传输逻辑与编程接口/配置壳 | 先核对 CanTool 的具体流程与配置；**已有功能继承，实际缺的 OEM 阶段才补实现**，不得凭 UI 假装完整刷写 |

**必须承认的现存接线/许可缺口：**
- `HardwareConnectionService` 当前通过 `new CanFrameHub()` 构造 Hub，未提供 TX 委托；`CanHubIsoTpLink.SendAsync` 因而在这个现有调用路径上无法真正发送。应**查清并接入已有受控 CAN1 发送路径**，而非以此为由重写 CAN/UDS/ISO-TP。
- `UdsClient` 现在拒绝非 ReadOnly 服务，`ProgrammingProfile.Validate` 对 `!MockOnly` 有保护。这说明现有实体**完整烧录还没有实际打通**；不是前端可单独解决的事实，后续若需要真实擦除/下载，必须在 F2 已核查及批准条件下最小扩展现有服务，保留物理安全门禁。
- 同一主控不能被主 HOST/FlashLab 两个进程抢占；先**验证现有 USB 打开行为是否已提供独占**，有缺口才增加最小设备所有权协调。不要预设必须新建一整套跨进程框架。
- 只读 DID 0x22 等诊断发送也需要现有真实物理授权；不要凭“只读”绕过 `PhysicalCommandFence`。

## 三、当前专项优先工作：CanTool 配置 + 独立 WPF 操作

1. **CanTool 配置复原**：逐字段查 `Configuration.xml`、关联 .NET 调用及配置参加的烧录阶段；区分固定 ECU 参数、可编辑参数、实际请求/响应和未知配置；Seed/Key DLL 仅做原始 ABI/兼容性事实调查，不另造算法。
2. **复原操作顺序**：连接 USB、选择配置/固件、诊断寻址、ECU 身份、预检、操作阶段、错误处理、最终校验；先给出“操作→后端已有函数→需补参数/适配”的矩阵，不研究无关的泛用 UDS IDE。
3. **独立 WPF EXE**：建议 `src/AutoTest.FlashLab/`，有设备连接、配置选择/编辑/保存、固件导入与地址/校验、APP/Boot 参数、阶段状态、UDS/CAN 时间线、NRC/超时、证据导出。只调用共享底层服务，不依赖 `AutoTest.Desktop` 的 WPF UI DLL，不复制第二套协议。
4. **最小适配规则**：只在证明现成服务无法满足必要操作时，新增薄适配/补接线；每处写明原接口、缺口、实际改动与回归边界。不要因为架构美观重构 `AutoTest.Devices`、`AutoTest.Specialists`。
5. **先离线与只读，再实际刷写**：F0 研究可以立即做；F1 EXE 代码实施需按既有 G4 权限边界获批，实体 0x22 需适用许可；F2 的 SecurityAccess/擦除/下载/校验/复位需目标参数、恢复策略及**单独的实体写入批准**。不能混同“研究/界面完成”和“已证明 ECU 可刷写”。

## 四、不得再误解的几个问题

- **不是两个后端做对比**：FlashLab 实测的 UDS/ISO-TP 应来自主系统同一实现；因此本质上不需要解决“两套协议栈冲突”。
- **但是前端复用不等于已全链路可刷**：若受控真实 TX 或 OEM 特定烧录阶段确实未实现，必须直接指出并补齐对应唯一后端接口；不能因用户说“前端专项”就声称现有 F2 已测试通过。
- **不扩展产品边界**：不新建通用 UDS GUI，不重写 WinUSB，不研究新的通用 Flash 框架，不改自动测试 UI。
- **研究要围绕当前用户目标 ECU 与 CanTool 真实配置**：证明源材料与当前工程对应，不填猜测的 APP/Boot ID、Seed/Key、RID 和校验参数。

## 五、专项向 PM 的交付

第一份应是紧凑的 **“CanTool 配置字段/烧录步骤 ↔ 现有 AutoTest API” 对照表** 和独立 EXE 文件/调用清单，突出**需新增的前端内容**，把不可回避的最小后端缺口单列。后续形成可编译可运行的独立 FlashLab WPF 候选，用户现场实测并记录错误，再由 PM 检查共享资源改动而非重新审核整个 UDS/ISO-TP 架构。

**已有 Issue #49 属于存在条件性缺口时的安全接口候选，不再是必须先实施的新协议架构工作包。**
