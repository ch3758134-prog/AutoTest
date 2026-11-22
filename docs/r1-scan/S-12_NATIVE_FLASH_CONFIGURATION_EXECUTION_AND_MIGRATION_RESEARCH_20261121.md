# AutoTest｜S-12 原生烧录配置与真实 CanTool 后端：逐字段、执行合同及迁移研究

**日期** 2026-11-21｜**研究基准** `Project_Management/docs/project-management/current/00,01,02,03,05,06,07`；`R1@5f7a2cf42c1a30434431af18e2fb26741075ad2f`；SCAN 研究 S-09/S-10。  
**状态** `USER_CONFIRMED_NATIVE_CONFIG / DESIGN_PROPOSED_SCHEMA / SOURCE_REVIEWED / WINDOWS_NOT_VERIFIED / FIELD_NOT_VERIFIED`。**本稿只研究，不改 R1 物理烧录代码。**

## 1. 纠正两次历史混淆

**用户已确认且比 S-09、S-10 更新的业务要求**：
1. 生产每次**完整检测必烧录**（同号复检也一样），在 TEST 用例树执行之前完成；用户不需要手工将 Flash 加到 TEST 步骤。
2. 一个 ECU 型号工程只有**一份当前有效原生烧录配置、一份当前固件**；并非每只 DUT 一份 Recipe，也不在扫码时选择 XML/HEX/安全 DLL。
3. CanTool XML 的**参数含义要在 AutoTest 内原生可视编辑**、自动保存在型号工程的固定位置；不要求在每次运行时导入、引用或自动生成 XML。
4. 独立烧录工具**无工程可用**，使用工具自己明确的临时参数；可以主动读取/编辑工程资源，但不强制工具和生产工位永远共享同一组保存状态。两者共享**真实协议后端**，不是共享一个必须弹出的设置页。
5. 原有 CanTool 成功烧录证据、ISO-TP/UDS 和 ECU 特定算法保留；“把 XML 从界面藏起来”不是原生配置完成。

**S-10 仍然正确的技术判断**：强类型 NativeProfile → 直接冻结 CanToolFlashJob；工程固定相对路径 + 版本 SHA；固件/算法作为独立受控文件。  
**S-10 需要按最新 PM 更正**：不再预设“TEST Flash 步骤/SCAN Recipe/独立 FLASH 都强制引用同一份已发布 Profile”。正式生产用当前工程的配置；独立工具可独立草稿；开发 TEST Flash 步骤属于保留的技术/兼容性入口，不再是生产流程必须存在的动作。

## 2. 现有源码：看得见的字段不等于实际生效

### 2.1 真实链路（目前 R1）

```text
TEST StepNode Flash
  → ConfiguredRealTestFlashPort.FreezeJobs
    ├→ Get("programmingProfile") 从磁盘 XML 路径
    ├→ Get("settingsPath") 可选相关 JSON
    ├→ Get("firmwarePath") HEX/S19/BIN
    ├→ Get("securityLibraryPath") SafetyFile.dll
    └→ CanToolXmlProfileReader.Load(xml)
        → CanToolFlashJob.Freeze(config, firmware, binBase, security, target)
          → CanToolFlashWorkflow.ExecuteAsync
             → CAN/ISO-TP + 0x10/0x27/0x31/0x34/0x36/0x37/0x11
             → APP 10 03/50 03 恢复探测
```

`ProjectConfigurationDocuments.FlashSettingsDocument`（Schema 1）是**元数据草稿**，有请求/应答 ID、P2、SecurityLevel、EraseRoutine、VerifyRoutine 等字段，但并不被这条真实 Job 冻结链消费；别把它宣称为已实现的完整可执行烧录工艺。

`CanToolXmlProfileReader` 提供 12 个已知键，原 XML 兼容中文 `FLASH起始地址/FLASH结束地址/波特率`；`UnmappedEntries` 可以列出未映射的嵌套节点，**它们不能因为 XML 有值就被当作执行参数已支持**。

### 2.2 字段证据对照表

| 参数 / 原始 XML 键 | R1 真正是否使用 | 目前准确语义 | 原生 UI/Schema 建议 |
|---|---|---|---|
| BootloadPhyID / BootRespond | **使用** | Boot 29/11bit 诊断端点；曾实测的 HydEPB 为 `18DA50F1/18DAF150` | 强类型 Boot Tx/Rx，自动推导 11/29bit 并限制 Request≠Response，不作为所有 ECU 默认 |
| APPPhyID / APPRespond | **使用** | APP 会话、复位与复位后核对的端点 | 强类型 APP Tx/Rx 与有效性约束 |
| BaudRate | **使用** | 目前 Job 只接受 250k/500k；主控另需 0x42 SET/0x43 GET readback | 对用户显示 kbit/s；Flash 有效选项只来自本 Job 的支持能力，不能把主控 8 个速率都标“烧录支持” |
| FlashStartAddr / FlashEndAddr | **使用** | 含端点，必须覆盖实际镜像，擦除区域按车型已验证 | uint 地址、区域预览，不能在 UI 随意改成陌生内存区 |
| BlockLength | **使用** | 默认 0x200，当前仅允许 64..4096 的 2 次幂；按此对齐写入及 FF 填充 | 单位字节、十六进制辅助、耗时/总下载字节预览，区别 ISO-TP FC BlockSize |
| SafetyFilePath | **XML 可展示，但不是实际文件选择权威** | Job 实际接收 `securityLibraryPath` 参数单独路径并核对 SHA | UI 中归于安全提供器**受控工程资产**，不能让 XML 文本遮蔽实际 DLL |
| VersionID / DiagnosticsID | **目前非 Job 核心执行输入** | 参考 CAN ID 字段存在，不证明软件版号读回已接 | 作为候选只读/高级字段，等明确 DID/报文判据才能启用软件版本验收 |
| ForceMinInterval | **Reader 有字段，核心执行未据此简单给固定等待** | 实际传输时序还受 ISO-TP FC/STmin 和 ECU 应答影响 | 未对接实际调度前标记“不支持编辑生效”，不能用假设置滑块 |
| 诊断 P2 / P2* | **实际 Job 端点目前固定 2s / 5s** | `FlashSettingsDocument.P2Milliseconds/P2StarMilliseconds` 不进入原 Job 冻结 | 若新 Profile 支持编辑，必须真正传入 UdsEndpointProfile 和相关请求处理并做时序回归 |
| EraseRoutine / VerifyRoutine | **旧元数据可填，但现有协议固定 FF00/FF01** | `CanToolProgrammingProtocol.Erase/Verify` 使用某 HydEPB 已验证流程 | 首版“CanTool-HydEPB”执行模式固定，显示实际命令；新增可选 Routine 需引擎能力+实测，不能仅写 JSON |
| SecurityAccess level / provider | **核心流程目前固定 27 01 / 27 02** | `CanToolSecurityProvider` 有多种 ABI 检测，但真正 `CanToolFlashJob.Freeze` 仅允许匹配原样本 SHA 的参考内置 LFSR 类型 | 选择受审 `ProviderId + AssetSHA + ABI`，显示哪些可用于现场、哪些仅静态识别，**不能**出现“任意 DLL 均支持 ECU” |
| 复位与 APP 恢复 | **固定真实步骤** | Boot 11 01 及 APP 10 03/50 03 等；`50 03` 是 APP 会话证据不是软件版号 | 将 `AppSessionRecovered` 与 `SoftwareVersionVerified` 独立状态显示和记录 |
| ECU 特定尾部 signature | **执行器有特定条件** | exact APP/Boot IDs + 0x10008000..0x1006FFFF + 0x200 时自动满足受限定参考的尾块行为 | 不伪装“所有 ECU 都通用”；版本化 `ProgramAlgorithmId` 并保留参考兼容回归和该算法的适用约束 |
| Firmware HEX/S19/BIN | **真实使用** | 解析实际镜像/填充、SHA、地址与边界核对；BIN 另需 BaseAddress | 独立导入固件**资源**到工程，不是“导入烧录配置” |
| CanTool settings.json | **可用作缺失 XML 值补充** | 并非独立必需的生产运行工艺来源 | 正式运行完全不用外部 JSON；历史迁移可一次性读取 |
| 未映射 XML 扩展字段 | **未知/不用于上述 Job** | 不等于执行器自动支持相应 OEM 业务 | 迁移报告列出“未映射/需人工核查”，不吞掉且不伪通过 |

**源码真实性必须写在操作手册中**：目前仅对特定 HydEPB 参考流有实测限定，不能由于 GUI 允许输入其它型号/Routine/波特率就生成“支持所有 ECU”的假象。

## 3. 建议的最小原生配置对象，不是万能 UDS 编程编辑器

```text
NativeFlashProfileDocument
  SchemaVersion                 # 显式 schema 迁移
  ProfileId                     # 工程内稳定身份
  EcuModelCode / ProtocolFamily # 与工程绑定，先仅 CanTool-HydEPB 已验证模式
  EcuCanKbps                    # 枚举，支持集合来源于真实 Freeze 实现
  AppEndpoint: Request/Response/Addressing/P2/P2Star
  BootEndpoint: Request/Response/Addressing/P2/P2Star
  Memory: Start/End/BlockLength/BinBaseAddress?
  ProgrammingMethodId           # 型别为 "CanTool-HydEPB-v1"，非任意自由脚本
  Security: ProviderId/ProviderSha/AlgorithmCompatibility
  PrePost: AppProbePolicy / SoftwareVersionRule?
  Revision, ContentSha, State(Draft/Published), CompatibilityNotes
```

**文件与资源分别理解**：
- `NativeFlashProfile` 决定**怎么烧录**，不能把“当前 HEX 路径”硬写成可执行 Profile 的唯一不可变身份；
- `CurrentFirmware` 决定**本型号当前烧录哪版软件**（Firmware Asset ID/SHA/名称）；工程可保留历史版本用于审计，但日常入口只有“当前生效”一项；
- `SecurityProvider` 是原始库或已验证算法资源，必须记录 SHA、平台/ABI 与明确授权使用范围；
- `ProductionFlashSnapshot` 将**原生方法 + 固件 + 算法 + 实际校验结果**一次性冻结供本次检验；不是新的复杂用户 Recipe 编辑页；
- `FlashAttempt` 是每次真实物理烧录事实，任何取消/超时/已擦除状态必须可追溯。

这里推荐引入一个版本化原生 Profile + 一个工程当前固件指针 + 运行时生成的冻结 Job，**不**要求 ProductVariantRegistry、FirmwareRelease 管理后台、FlashPolicy 与 TestProject 项目子集作为正常生产步骤。

## 4. 固定存储：路径稳定，但资源内容与发布版本仍可审查

**候审物理布局**（`PROJECT` 最终选定并保留现有 `project.autotest-project.json`）：

```text
ECU工程/
  project.autotest-project.json
  assets/
    flash/
      profile.json                         # 本型号原生配置自动保存草稿，路径固定
      published/<SHA256>.profile.json     # 经确定的不可变生产版本
    firmware/
      <SHA256>.hex / <SHA256>.s19 / <SHA256>.bin
    security/
      <SHA256>.dll                       # 如果该 ECU 需要外部安全库
```

**特别注意**：固定文件名是**推荐技术方案，尚非用户批准的具体路径字节串**；用户确认的是工程内固定位置、无需在正式配置界面反复导入外部 XML。`ProjectWorkspaceService.AddAssetAsync` 当前仍按 GUID 生成 `assets/flash/{Guid}.settings.json`，不能说上述目录已实现。

- 新工程内 `profile.json` 可为空但保持合法 Schema 的**草稿**；自动保存采用原子替换、同工程唯一锁/备份、修订和内容 SHA，重启/换电脑拷工程后路径仍有效；
- “保存”是编辑状态，**不是**当前 DUT 的硬件授权；运行引用在检查成功后冻结为不可变发布快照，编辑过程中不影响进行中的一次检验；
- 当前固件导入时复制到工程中、校验 SHA 与格式/映像范围，当前有效版本指向受控 AssetId/SHA。外部原文件删掉不影响已受控的工程；
- 如果工程当前固件改变，新的生产检测必须核对一次新固件快照；不能因为文件名相同/时间更新悄悄套旧 SHA；
- 默认只有一个当前 Profile/固件；历史发布稿和历史固件存在是审计资料，**不要求正常操作员多级筛选**。

## 5. 正式执行器的重构：改数据源，不改经过验证的 CAN/UDS 流程

### 5.1 目标调用图

```text
ProductionStation (模型/扫码身份已核实 + Clamp/Start 真实事件)
  -> ProductionPreflight
       -> Project.CurrentPublishedNativeProfile + CurrentFirmware
       -> actual SecurityProvider asset by SHA
       -> hardware CurrentStationEpoch / ECU CAN 0x43 actual readback
       -> NativeFlashJobBuilder.Freeze(profile, firmware, provider, target)
       -> immutable JobFingerprint + InspectionId
  -> ProductionFlashStageExecutor.Execute(job, attemptId)
       -> ONLY existing HardwareConnectionService / device-session owner
       -> CanToolFlashWorkflow (ISO-TP/UDS/erase/download/verify/reset)
       -> raw CAN/UDS/phase progress, APP recovery, version rule when supported
  -> upon eligible success: existing RealTestWorkbenchService / RealRunCoordinator
  -> existing RECORD/Reports for this Inspection
```

独立 `FlashLab` 可用同一个 `NativeFlashJobBuilder` 构建 Job，但传入自己的**明确临时配置对象**与已选资源；如用户主动加载型号工程，界面标注“当前编辑工程版本/临时副本”，不能不经明确编辑就改生产使用的 `profile.json`。两个 UI 共用的是`JobBuilder + CanToolFlashWorkflow`，不是一定共用同一张 ViewModel 或同一用户保存状态。

### 5.2 建议的最低技术迁移步骤

1. 建立“旧 XML → 实际字段/类型/单位/可编辑性”逐项测试向量：HydEPB 原成功参数（APP+BOOT 四 ID、Range、Baud、BlockLength、Security SHA、尾块行为）。
2. 实现 `NativeFlashProfileDocument` 与 `NativeFlashJobBuilder`：**直接处理强类型值、镜像/Provider**，不隐式吐 XML 文件再回读；协议引擎既有 `CanToolFlashWorkflow` 保持一致。
3. 使用固定参考输入对比**真正的块地址、对齐 FF、末尾 signature、请求/响应端点、擦写/校验请求字节序、Checksum、Firmware SHA 与实际发送序列**。新 fingerprint 可升版本，但必须验证**业务上等价**，不能只比生成的字面 hash。
4. 先把**独立 Flash 工具**改成原生编辑运行并在真实 HydEPB 重测；保留阶段条/原始 CAN/NRC/STOP/进度导出。用户当前已有成功操作事实，升级不能让它倒退。
5. 在工程固定位置添加生产 Profile/固件及唯一当前选择；发布/自动保存/迁移/工程复制/引用 SHA 的完整文件测试。
6. 实现 `ProductionFlashStageExecutor` 直接调同一设备会话/后端，再接实体启动和 RECORD。**不需要绕回 TEST 内 `Flash` StepNode 来触发生产烧录**。原 TEST Flash 步骤源码保留并明确其开发/旧工程用途。
7. 在满足真实后验条件时进入生产 TEST；历史项目如果只有 XML 路径则提供**一次性显式迁移、差异说明和未发布草稿**，不自动授予生产资格。
8. 固定最新 Windows Build+真实 ECU 跑通后才将“已支持”从研究更新为产品功能。

## 6. 如何兼顾生产无鼠标与现有 Flash Job 独立授权

目前 `ConfiguredRealTestFlashPort` 以及 `HardwareConnectionService.ProgramTestFlashAsync` 在每次 Job 要求准确 `AUTHORIZE ECU FLASH <SHA>` 文本；这是**现有工程师执行流程的技术事实**。用户新生产合同则是：**扫码→夹紧→实体启动键，不逐台在 PC 上输入一长串文本**。

研究建议由 FLASH/DEVICE 专业审查“工位已进入受控生产模式且已锁定有效 Profile/SHA 的连续任务许可”，在**每只 DUT 的新实体启动键事件和本次资源/设备会话复核之后**生成只限这一 `FlashAttempt` 的一次性真正执行许可，沿用现有底层单作业防重写/串行会话/证据；工作站工艺变更、设备重连、工程换型、停止/失败恢复必须重新核验。

这不是“删掉授权字符串检查就行”，也不是为了 UI 误报“危险电气状态”；是将**人工研究模式的确认方式**与**真实生产站的实体操作触发方式**做同等可审查的权限迁移。合同未审通过前，当前 R1 仍按照已有 Job 授权运行，不能把目标生产触发状态标“已能工作”。

## 7. 硬件与工况真实性，不应凭 UI 设计发明新状态

- 当前主控 CAN1/ECU 接口对外 A12(H)/A1(L)，主控 0x42/0x43 仅 ECU CAN；配置目标 250k/500k 必须与真实确认值一致；
- `0x18/0x19` 可用于夹具请求，但 USB ACK/GPIO 回读不等于机械到位；实体启动/停止键是否已有可用 GPIO+USB 上报未知；
- 烧录完成日志中 Boot 51 01、APP 50 03 和 `VersionMatched` 应分开，不能将“APP 会话出现”当已写正确固件；
- Flash 已擦除后的取消/断连：保留最后成功阶段和未知状态，**不要自动把本次烧录当没发生，更不要自动重新烧录**；下一次生产扫描前遵守经实测确认的工位恢复约束；
- 报告必须对烧录失败、停止、通信超时分别建立本次 FlashAttempt/Inspection；没有开始 TEST 就标“TEST 未执行”，不能标“TEST 失败”。

## 8. 完整产品 UI：一个生产设置入口，独立工具自己的工作台

### 8.1 工程师“生产设置 → 烧录配置”

**左侧一份当前生效配置**，不是多 Profile 清单：
1. ECU 通信：主控已确认的 ECU CAN 速率、APP/BOOT TX/RX、帧格式/真实支持寻址；
2. 编程参数：写入地址范围、块长、真实执行的 Routine 与操作模式（目前不支持任意编辑的字段仅显示有效实测默认与“受支持范围”）；
3. 安全算法：当前受支持 Provider/安全库 SHA，导入已验证资源；
4. 当前固件：HEX/S19/BIN 的版本/大小/地址/哈希及兼容性状态（直接选工程当前固件，不读旧外部路径）；
5. 后验：APP 会话和已实现的目标软件/功能判据（未知版号则明确显示未提供）；
6. 参数校验/预览：真实将擦除的地址范围、下载块数和 JobSHA、与当前 Profile 的偏差、原生草稿/生效版本。

字段默认不使用奇怪 0.0000000000001 浮点格式；CAN ID 是对齐 HEX（11/29 位清楚），容量/地址按 0x... 和人可读字节展示，Seed/Key 不记录明文。**编辑自动保存草稿，关闭不要求“另存为 XML”**。

### 8.2 独立 Flash 工具

可无工程打开；显示左侧配置+固件选择，中间十阶段烧录进度/停止/本次总字节，右侧实时 CAN/UDS 报文、阶段证据；提供“加载工程当前配置为本工具副本”但不默认覆盖生产原生 Profile。工具可直接使用自己管理的独立配置档，正常关闭主 HOST 时跟随退出，真实烧录仍单一设备会话。

### 8.3 操作员生产工作区

默认不展示 XML、DLL 选择、Job SHA 长文本输入、TEST Flash Step 编辑入口。屏幕显示当前型号/固件版本标签、扫描编号、夹具/实体启动、S1～S10 各阶段及下载进度、功能 TEST 进度和最终结果；需要时展开/查看 CAN 明细。更新 Profile 或固件属于工程师配置任务，不是每只 DUT 操作员任务。

## 9. 本轮特意不纳入首版的“通用功能”

不实现所有 OEM 的任意动态 Routine 编辑、不自动猜 DID、不自动导入未知 DLL、不支持未经证实的速率/FD 帧、不让用户配置任何自定义安全访问脚本就认为 ECU 通用。明确在 UI 中标“尚不支持该 ECU/该算法”，比给一个无效 TextBox 成熟。

**将来扩展**：经第二个实测 ECU 才提炼 `ProgrammingMethodId` 插件接口/受控方法模板；不会为了未来多型号预先增加强制复杂配方管理层。

## 10. 关键负向/正向验收

| 场景 | 必须观察到的事实 |
|---|---|
| 完全不提供 CanTool XML，新工程手动填参数 | 原生草稿自动保存；再打开有同样配置；真正冻结 Job 不读取/生成 XML |
| 删除原始来源文件夹 | 生产仍使用已复制进工程且 SHA 一致的固件和安全算法，不访问旧盘符 |
| 编辑 Profile 尚未发布/正在执行 | 本次已冻结 Job 不改变；下次新批次按明确的生效版本 |
| 切换工程/ECU CAN 码率不符 | 烧录预检明确拒绝/要求正确读回，不偷偷用上台的 Job |
| 输入非 250/500k、非法地址/块长或不支持 DLL | 源头提示真实能力限制，不执行 |
| 当前 HydEPB 固定实测资料 | 原生构建产出的 Boot/APP/擦除/传输/尾块/校验与先前成功日志逐阶段对齐 |
| 软件传完但 APP 未恢复 | 后续正式 TEST 不启动；本次结果与证据留存 |
| APP 50 03 恢复但软件版本未有已实证判据 | 只记录 APP 恢复，不伪造软件版本 PASS |
| 同一 SN 重新扫码复检 | 重新产生 FlashAttempt，不因历史已 PASS 或历史软件版本而跳过 |
| Flash 中段断线/取消 | 不进行自动破坏性重试；保留确定阶段及未知结果 |
| 独立 Flash 无工程、工程师 TEST 不扫码 | 原有可用路径继续存在，不强制走生产设置 |

## 11. 正式设计/实现 Gate（需要额外专业接口评审）

- FLASH：提供真实支持的 `ProgrammingMethodId` 与代码中准确使用字段、已支持 SecurityProvider、复位后验证、生产授权迁移。
- PROJECT：原生 Profile 的精确 Schema、固定工程相对路径、单例资产与 Manifest Pin、自动保存/历史迁移。
- DEVICE：唯一 USB 会话、0x42/0x43、真实夹具/START/STOP 事件、任务所有权和断连恢复边界。
- Production/TEST：Flash 前置状态机→固定 Enabled 树、失败后 TEST 控制流、无重复真实引擎。
- RECORD：FlashAttempt、JobSHA、原始 CAN/阶段、APP 恢复、功能 TEST/报告共用 Inspection。
- UI：现代浅色原生编辑器，独立工具参数和工程生产设置分离，真正取消生产 XML 导入入口。

**上述还未实施，也没有 Windows/真机验收**；这是可审查的设计研究，不是要求用户立即接受新 Schema。

### 外部技术依据

- ISO 14229 功能参考（UDS 0x10、0x31、0x34、0x36、0x37）：https://www.autosar.org/fileadmin/standards/R22-11/AP/AUTOSAR_SWS_Diagnostics.pdf
- NI TestStand Process Models（区别生产前后过程与具体 TEST）：https://www.ni.com/docs/en-US/bundle/teststand/page/process-models.html
