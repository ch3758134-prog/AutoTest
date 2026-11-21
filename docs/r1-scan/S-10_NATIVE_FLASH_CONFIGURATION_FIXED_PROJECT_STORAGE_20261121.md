# AutoTest R1 SCAN S-10｜正式决策输入：原生手工烧录配置、工程内固定保存位置、消除运行时外部 XML 依赖

> 2026-11-21。Issue #72；研究分支 `research/r1-scan-configuration-20261120`。**用户明确提出需求变更**：“CanTool 的配置文件+固件+安全算法可以作为早期办法，但正式 AutoTest 不采用导入配置文件；烧录参数必须可在上位机手动配置编辑；配置保存到固定地址，不能依赖外部索引。”这是与 S-09 中“继续以 CanTool XML 作为正式 Flash Profile”直接冲突的新用户要求。
>
> 状态：`USER_REQUIREMENT_CONFIRMED / DESIGN_PROPOSED / R1_NOT_MODIFIED / FIELD_NOT_VERIFIED`。保留 R1 真实 CanTool 擦写实现作**后端算法/流程参考**，不再把 `Configuration.xml` / 可选 settings.json 当作正式工程运行必需的配置资产。当前最新已查看 R1 HEAD `5f7a2cf42c1a30434431af18e2fb26741075ad2f`，下一轮实施需重新回读实时 HEAD。
>
> **重要边界**：用户明确取消的是外部**烧录配置文件导入与引用**，不是取消烧录必须使用的固件镜像/安全算法资源。固件 HEX/S19/BIN 与经核实的安全 DLL 仍可能需要选取/导入，但建议复制进**工程受控存储**而非长期依赖外部路径。不能据此擅自承诺无需任何外部固件或算法文件。

## 1. 正式产品操作合同（不可通过“隐藏 XML 导入按钮”伪实现）

1. 打开 ECU 工程自动读取工程已存的原生烧录设置。**不用选择 XML 文件、导入 CanTool 配置、引用外部配置路径或搜索本地配置目录**。
2. 在上位机独立的 **FLASH 设置**里直接编辑 APP/BOOT CAN ID、寻址、波特率、诊断超时、会话参数、SecurityAccess 策略、存储区、擦写例程/下载/校验和复位参数。具有必填约束、范围校验、字段说明和已支持能力状态。对于当前仅有 CanTool 特定实现的参数，无法泛化时标记为 ECU 专用/不可随意编辑，不能做无效的“万能界面”。
3. 修改配置为**自动保存可编辑草稿**，同工程可重启继续，不能每次退出要求另存为或选择文件路径。草稿不等于已经批准用于生产。操作员可单独显式“发布/批准此版本”来固定不可变的运行快照。
4. 允许选固件镜像与明确已支持的安全算法；**导入后复制到工程内部资源目录并保存 SHA**，不使用 C:\外部目录、相对上次导入文件夹或无法随工程移动的绝对路径作为生产运行事实。
5. 独立 FLASH 工具、TEST 编辑器中的 Flash 步骤、SCAN 生产 Recipe、生产预检和 RECORD 必须引用**同一份原生工程烧录配置版本**，不得出现“FLASH 工具里修改成功、自动测试仍读另一份 XML”的双源事实。
6. 运行时从强类型原生 `FlashProfileDocument` 和固件/算法工程资产直接构建冻结 FlashJob；**不能先在幕后临时输出一个 XML 再要求老 `CanToolXmlProfileReader.Load(path)` 执行**，更不能运行时读取历史外部 XML/JSON。
7. 与当前真实 FLASH 安全边界保持一致：一个 TEST 项目运行范围至多一个真正写入步骤、独立 Flash Job 指纹授权、真机 CAN 速率确认/当前站点会话、断线失败不得静默重复擦写、APP 恢复与实际目标软件版本证据分开。

## 2. 一个 ECU 工程需要多少烧录配置？

用户已定义：**一个 ECU 工程绑定一个 ECU 型号**，其 DBC/自动测试/烧录及相关配置属于这个工程。因此首版建议：
- **一个默认可编辑 `FlashProfile`**，打开工程直接进入编辑，不要求用户先新建外部配置或一堆 Profile 文件；
- 软件版本与受控发布可有多个历史 `FirmwareRelease`（R5、R6），生产任务明确选择目标软件；
- 对确实有不兼容的 HW 变型或 Boot 协议，可在工程内扩展多个受审 `FlashProfileId`，但这不是首版默认 UI 必须展示的管理复杂度。**如不同硬件实际是不同型号、需用不同工程，则遵守一个型号一个工程的产品约束，不为了“万能混线”强行合并**；
- FLASH TEST 步骤应以固定工程资源 ID/版本引用原生 Profile，而不是持有 XML/固件/SafetyFile 三个外部路径。必要时仍支持离线独立 FLASH 工具中打开这个工程对其进行修改/调试。

## 3. 固定路径方案：明确“存储物理位置”和“版本引用”两件事

建议目录结构（**产品设计候选**，具体落盘路径需要 PROJECT 专项确认）：

```text
<ECU工程根目录>/
  manifest.json                       # 实际名称遵循 ProjectRepository 已有规范
  assets/
    flash/
      profile.json                     # 唯一默认烧录编辑草稿；固定项目相对路径
      releases/
        <revision-or-sha>.profile.json # 已发布不可变配置快照，按版本查历史
      security/
        <sha>.dll                      # 可选：经受控导入的硬件支持安全库
    firmware/
      <sha>.hex  或 <sha>.s19 / <sha>.bin # 固件镜像经批准复制至工程
```

选定的固定默认配置路径：`assets/flash/profile.json`。这是**相对工程根目录的确定路径**，不是 `C:\...\config.xml` 的机器绑定绝对路径。工程可以打包、迁移、备份，配置随工程走；用户不需要挑选路径。生产版本另保存只读快照，使草稿自动保存时不破坏已发布生产计划。

`ProjectManifest` 可以在**工程内部**登记受控资源 ID、Revision/SHA、相对固定路径，用于一致性检查与测试/扫码绑定；**项目内部资产清单不是用户反对的“外部索引配置”**。不要在漫无约束的外部文件夹搜索“最新配置”。

**现有代码差距**：`ProjectWorkspaceService.AddAssetAsync` 目前将 flash-settings 保存到 `assets/flash/{Guid}.settings.json`；它是**工程内副本**但**不是唯一固定路径**。原生默认 Profile 如果采用上述 `profile.json`，需要为这个**受控的单例资源**增加固定文件名分支、冲突检查、原子保存和项目 Manifest 指纹维护，而不是直接沿用 GUID 创建多份。保留其它资源原有 ID/Revision，不擅自修改全工程统一资源策略。

### 保存要求

- 新建/打开工程：自动初始化默认 Profile（可为草稿），自带有效 SchemaVersion、ProfileId，字段可为空/显示待配置；**没有导入 XML 的入口依赖**。
- 编辑中：主窗口约定的自动保存草稿、原子写入、校验后提交与可恢复备份，防止半写入造成配置损坏。
- 发布：先逐项验证所选硬件、Endpoint、目标内存范围、算法兼容、固件指纹和真实 TEST 计划；产生不可变 Release Revision/SHA 和引用关系，运行时不使用仍在编辑的 profile.json。
- 配置变更/换 ECU 工程：旧已发布快照不会被自动覆盖；若需要使用新内容，需重新发布配置及 ProductionRecipe；RUN 前比对 SHA 并拒绝漂移。
- **禁止**：硬编码至全局 `E:\Flash\Config.xml`、每次启动要求配置文件选择器、在别的盘符中保存而只把绝对路径写入工程、自动覆盖受控历史发布版本、在内存里改了但重启丢失。

## 4. 真正需要提供手工编辑的参数字段（而非 XML 字段编辑器）

### 4.1 目标与物理连接

`TargetEcuModel/CompatibleHardwareCodes`（明确允许的 ECU/HW）、`EcuCanKbps`（真机 0x43 核对）、ECU CAN 固定角色，不出现虚构的主控 CAN1/CAN2 映射。

### 4.2 APP/BOOT 诊断寻址

APP 请求/应答 CAN ID、BOOT 请求/应答 CAN ID（标准/扩展自动识别与显式校验）；ISO-TP addressing 模式、P2/P2*、FC/STmin 等有实测依据的参数。例：用户 HydEPB 的 Boot 请求 `18DA50F1`、应答 `18DAF150` 是**一个具体 ECU Profile**，不能当所有 ECU 默认值。

### 4.3 下载和擦写方法

FLASH Start/End、内存区允许范围、块对齐、`RequestDownload/TransferData/TransferExit`、实际擦除与校验服务/Routine IDs、Reset/APP 恢复策略、异常/超时处理、只对特定 OEM 验证的尾块行为。**区分可安全通用的 UDS 字段与 ECU 私有步骤**，每一种能力应关联支持等级与真机实测证据，不要因 UI 能编辑就允许任意组合刷写。

### 4.4 安全访问

SecurityAccess 子功能、Seed/Key 受支持 Provider 标识、安全库 SHA、平台架构/接口约束。可选择内置已验证算法或经批准导入的 DLL；不能明文在配置里保存 Seed、Key、授权码，也不能将安全提供器自动回退为测试算法。

### 4.5 固件/软件版本与验证

目标镜像工程 AssetId+SHA（导入镜像不是导入配置）、固件发布标签、ExpectedSoftwareVersion / DID/响应判定（若 ECU 真有支持）；APP 诊断恢复与实际目标版本读回是**两条独立质量结果**。无合法 DID 不能硬说“烧录的固件版本已确认”。

## 5. 代码迁移必须做什么（R1 真实函数对照）

### 5.1 不仅是改一个 UI，必须真正移除 XML 的执行依赖

| 当前 R1 实现 | 当前问题 | 正式产品变更 |
| --- | --- | --- |
| `FlashSettingsDocument` | JSON 工程配置已可编辑，但明确 `Not a ProgrammingProfile and not a flash execution authorization`；不是可执行 FlashJob 的完整事实 | 升级/新增 `FlashProfileDocument V2`，强类型字段、严格校验、项目内固定路径、草稿/已发布分离 |
| `CanToolXmlProfileReader` | 从磁盘 XML 加载 Profile，并可读取相关 JSON | 仅供历史 CanTool 研究/迁移参考（如仍保留），**正式执行入口不依赖 XML** |
| `CanToolFlashJob.Freeze(CanToolXmlProfileReader,...)` | 冻结 Job 需要传入外部 XML 解码器 | 新增 `Freeze(NativeFlashProfileSnapshot, FirmwareAsset, SecurityProvider)`；直接冻结字段/镜像/算法生成相同类型的真实 Job |
| `ConfiguredRealTestFlashPort.FreezeJobs` | 按 TEST Flash 步骤的 `programmingProfile`/firmwarePath/securityLibraryPath/settingsPath 从外部路径恢复 Job | 将 Flash 步骤引用改为 `FlashProfileAssetId + PublishedRevision/SHA + FirmwareReleasePin`，从项目内部文件读取已发布快照；不能把 XML 路径当主合同 |
| `ScanRecipePreparationService`（R1 最近新增） | **当前仍通过 `ConfiguredRealTestFlashPort.FreezeJobs` 核验原始 CanTool XML 三件套，并把 JobSHA 回写配方**；不能用它证明新方案已满足 | 发布和扫码预览必须改用**工程内原生 Profile**重建同一 Job SHA，核对 Recipe/Test Flash 步骤和软件发布号 |
| `FlashLab` 独立工具 | 原三文件导入是实测研究入口，用户不想正式软件继续这样操作 | 原生编辑器直接读工程 Profile；独立工具保留调试/阶段日志/进度/CAN，但消费相同 `FlashProfileDocument` + `CanToolFlashJob` |
| `CanToolFlashWorkflow` | 真 ECU 参考烧录流程已实现、用户曾实际烧录成功 | **保留**已验证的 UDS/ISO-TP/块传输/安全锁/阶段证据算法，避免为取消 XML 导入而重写物理后端 |
| `ProjectWorkspaceService.AddAssetAsync` | `FlashSettingsKind` 保存于 `assets/flash/{GUID}.settings.json`，虽在工程内但非唯一默认文件名 | 对一个 ECU 工程的默认 FlashProfile 建立稳定 `assets/flash/profile.json` 路径；资源 ID/发布版本继续由工程内部 Manifest 严格管理 |

**兼容策略**：旧 FlashSettings（Schema v1）、旧 Scan Recipe、现有 TEST Flash 路径仅作为旧工程识别和显式迁移的输入；迁移时展示缺少字段/不支持项，生成**未发布**新草稿，不默默让旧配置获得物理擦写权限；迁移完成并验收后，正式 UI 不显示“导入 CanTool XML 配置”。用户说的是“不需要导入配置文件”，因此不能要求每次迁移都让用户提供一份 XML 文件；遇到旧工程需要的信息可从原步骤和已存在内部文件读取并人工补全。

## 6. 扫码、正式方案和烧录怎样绑定（新修订）

```text
外部实体条码（UTF1/legacy）
  -> DutSerial + 产品代码 + HW [标签声明]
  -> 当前已批准 ProductionTask
  -> 绑定到该 ECU 工程唯一已发布 ProductionRecipe
     ├─ TestProject + 有序用例集
     ├─ TCF/DBC/KEY/TBC + ECU CAN
     ├─ FlashPolicy(NoFlash/FlashRequired)
     ├─ FirmwareReleaseId + IMAGE SHA （工程内固件资源）
     ├─ NativeFlashProfileId + ReleasedProfileSHA （固定位置保存的原生配置之发布快照）
     └─ ExpectedVersionVerification（有真实 ECU 校验合同才启用）
  -> 编译 FrozenUutRunPlan 与真实 FlashJob，核对 TestFlash Step 引用与 Recipe 完全一致
  -> 显示“这只 ECU 会烧什么固件、按哪个协议/区域/ID、要测哪些用例”
  -> 原有实体 Flash 安全门禁及唯一 TEST 执行器
  -> RECORD 的真实 Flash Attempt/CaseRun/软件版本判定/出厂状态
```

**首版 NoFlash/FlashRequired 明确分离；只有一个已发布默认 profile。** 不能将用户当前正在编辑的草稿配置用于正在执行的生产任务，亦不能由扫码事件直接授权 FLASH。

## 7. 最小验收（不接受“配置编辑了但还要导 XML”）

- 新建工程，进入 FLASH 工具，直接看到 Profile 编辑器，全部字段能合理输入并保存；**不出现选择 Configuration.xml 的强制路径**。
- 关闭上位机、重新启动自动打开上次工程，Flash 参数逐项保留且路径为工程根目录下稳定文件，换电脑完整复制工程后仍有效。
- 固件镜像/必要安全算法文件导入后被复制入工程资产目录；删除/断开原始外部目录不影响完整已发布工程使用，所有 SHA 与 Provider 兼容性核验仍严格。
- 修改配置未发布，生产仍使用先前已发布 ProfileSHA（若策略规定草稿更改不影响当前发布）；新发布后新 RunPlan 需全量重新冻结。
- 扫码产品/HW+任务→唯一 Recipe，包括固件发布标签与 ProfileSHA；FlashPolicy=NoFlash 但 TEST 存在 Flash 必须阻断，FlashRequired 没有或多于一次也阻断。
- 真正执行 TEST 内唯一 Flash：**不访问 XML，不写临时 XML，不从历史文件夹寻找 XML**；冻结 Job SHA 与预检完全一致；仍使用真实 CAN/UDS 后端及单次独立授权。
- FLASH 独立调试工具与 TEST 读取同一工程 Profile，对同一固件/算法得到一致的 Job 指纹；需设置的特定 ECU 参数均可从上位机字段表达，不依赖厂商配置文件。
- 真机验收仍需真实 ECU、真实软件版本读回/功能证据；无真机证据不得宣布行为 PASS。

## 8. 研究范围与待 R1/FLASH/PROJECT 接口评审

- 用户明确的**业务要求**可作为下一阶段产品设计约束：原生手工配置、工程内稳定持久路径、正式运行不导入外部配置。
- 文件名 `assets/flash/profile.json`、双版本保存/发布、各字段 Schema、兼容迁移步骤、固件/算法托管形式属于**工程建议**，仍需项目管理/PROJECT/FLASH 评审后固化；不应将建议路径冒称已冻结文件格式。
- R1 最近新增 `ScanRecipePreparationService` 依然从 TEST 旧 XML Profile 构建 Job；**若直接将旧 S-09 的合同交给 R1 编码，会违背本次用户明确要求**。请项目管理在审批台账明确 S-10 对 S-09 XML 运行态提议的修订优先级。
- 不改历史真实烧录证据和已经验证的 CanTool 参考实现本身；正式 AutoTest 的产品运行入口必须从原生 Profile 直接调用经核验后端。
