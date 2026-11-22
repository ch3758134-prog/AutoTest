# AutoTest｜S-13 扫码 × 生产前置烧录 × TEST × RECORD 一体化合同与 R1 差异总审查

**研究日期** 2026-11-21｜**产品权威** \`Project_Management/docs/project-management/current/\`｜**对照源码** \`R1@5f7a2cf42c1a30434431af18e2fb26741075ad2f\`。  
**状态** \`INTEGRATED_RESEARCH_COMPLETE / BUSINESS_DECISIONS_INHERITED / DESIGN_RECOMMENDATIONS_OPEN / R1_CODE_NOT_CHANGED / WINDOWS_FIELD_UNVERIFIED\`。  
**依赖细节**：[S-11｜扫码设备与型号工程](S-11_CURRENT_PRODUCT_SCAN_HARDWARE_ROUTE_AND_PRODUCTION_RESEARCH_20261121.md)；[S-12｜原生 FLASH Profile](S-12_NATIVE_FLASH_CONFIGURATION_EXECUTION_AND_MIGRATION_RESEARCH_20261121.md)。

## 一、只需一眼看懂的正式业务流程

\`\`\`text
生产工作区（正常无需鼠标）
  放 ECU / WAIT_SCAN
    -> 扫码设备读完整字节，BarcodeCodec 提取 ModelCode + DutSerial
    -> StationModelCatalog 查本地已安装的唯一型号工程
         同型号：直接继续
         已知异型号：确认切换完整工程
         未知/多义/资源不一致：拒绝，不选历史“最近一份”
    -> 创建 InspectionId（绑定 RawScanEvent / 真实设备来源）
    -> 夹具压紧请求 -> 真实夹具就绪判据 [当前 HW 未验证]
    -> 实体启动键的有效新触发 [当前主控输入接口未验证]
    -> 冻结当前工程的 DBC/TEST Enabled 树/原生 FLASH Profile/
       当前固件/安全算法/主控码率/记录条件
    -> FLASH 前置阶段：每次完整检测**重新烧录**
         新 FlashAttemptId -> CanToolFlashJob -> CanToolFlashWorkflow
         -> APP Session 恢复和必要真实软件版号判据
         FAIL/UNKNOWN: 不运行下游正式 TEST，完整记录原因
    -> 真实 TEST：一次按树顺序展开全部已启用用例
         每用例自己的 RunId/StepOccurrence，失败处理遵守 TEST 可编辑逻辑
    -> RECORD：原始 CAN + Flash/UDS + TEST 关键测量 + 单台报告
    -> 已证实的工位收尾/夹具释放条件
    -> 上一台结果持续显示，回到待扫码
\`\`\`

**必须独立存在的工程师路径**：工程师可以无扫码单步/用例/勾选测试；无工程可以使用手动台架、DBC、UDS、Flash、WAVE 专业工具。独立 Flash 允许临时配置，不强迫其从当前生产工程加载设置。

## 二、六个产品数据对象已经足够——避免套旧的多级 Recipe

| 数据对象 | 作用及字段最小集合 | 谁拥有 | 谁不能改 |
|---|---|---|---|
| \`ModelProjectBinding\` | 本机 ModelCode、ProjectId、工程规范路径、启用/唯一性、最近已验证 ManifestSHA | Station/PROJECT | 单次扫码不能修改 |
| \`BarcodeFormat\` | 编码版本、型号/SN 提取、可选校验、终止符处理后文本 | SCAN | Flash 不能解析条码 |
| \`ProductionSetup\` | 型号工程当前生产版本指针：TEST 全树、DBC、当前固件、NativeFlashProfile、必要工位设定 | PROJECT | 扫码不手选一套 XML/HEX |
| \`FrozenProductionRunPlan\` | 单次 \`InspectionId\`、DutSerial、工程/资产 SHA、OrderedEnabledCases、Flash Job SHA、StationEpoch | Production/PROJECT | 正在编辑的草稿不能修改已冻结对象 |
| \`InspectionRecord\` | 本次身份/扫码/夹具/START/FlashAttempt/所有 CaseRun/报告状态 | RECORD | UI 不自行造 PASS |
| \`ToolFlashWorkspace\` | 独立工具临时/用户明确选择的 Profile/镜像、诊断日志 | FLASH 工具 | 不自动覆写工程 ProductionSetup |

以上是**推荐概念合同**，不代表必须逐个创建复杂的公开 JSON 资源/可见配置页面。一个型号一个工程时，ProductionSetup 可是现有 ProjectManifest 对当前资源的统一只读投影，不需要一个新 Recipe 编辑器。

## 三、必须停止沿用的 R1 旧生产假设

| 已有 R1 源码/界面 | 直接沿用会怎样 | 正确迁移方式 | 状态 |
|---|---|---|---|
| \`ScanMatchSettingsDocument.ScanRecipeDraft\` 多 TEST/DBC/Firmware/FlashPolicy 选择项 | 用户仍需维护一套叠床架屋的 Recipe；与“一个型号一个工程”冲突 | 扫描配置仅保存条码规则、型号代码和工程内部当前资源的引用；已有 Recipe 迁移只读、不给正式生产默认使用 | **REWORK** |
| \`ProjectScanMatcher\` 当前打开工程匹配 | 无法根据另一已知型号找到并完整切工程 | 加 StationModelCatalog，与稳定 ProjectId/Manifest SHA 关联；当前同型号直接继续 | **REWORK** |
| \`ScanRecipePreparationService\` 使用 TEST 内唯一 Flash 步骤、XML/HEX/SafetyFile 冻结 | 生产必须要求工程师给 TEST 插 Flash；不满足原生配置 | 改 ProductionPreflight 从型号工程的 NativeProfile/当前 Firmware 冻结一次 FlashJob，TEST 树不含前置生产 Flash | **REWORK** |
| \`ApplyScannedRecipeAsync\` 只把勾选范围转给开发 TEST | 扫码后还要 PC 点击运行；不管夹具、实体 START/每台必烧 | 只在开发兼容入口保留；生产独立状态机自动调度，生产用全树 Enabled | **REWORK** |
| \`FlashSettingsDocument\` (v1) | 能填参数却不影响真机 Flash Job | 建强类型 NativeProfile + 固定工程路径；旧数据显式迁移为未发布草稿 | **REWORK** |
| \`CanToolXmlProfileReader.Load\` / \`CanToolFlashJob.Freeze(xml,...)\` | 正式运行仍依赖 XML；改个 UI 不解决 | 原生 JobBuilder 直接接受 Profile 值/工程固件/算法，历史 XML 仅读迁移 | **REWORK** |
| \`CanToolFlashWorkflow\`、\`CanToolProgrammingProtocol\`、\`CanToolSecurityProvider\` | 这是有特定实测的核心烧录技术资产 | 保留并做新旧 on-wire 等价回归；真机 re-verify | **KEEP** |
| \`ConfiguredRealTestFlashPort\` | TEST 内已有真实 Flash 技术能力 | 保留开发/旧工程能力；生产前置使用公共 Flash service，不再强制 TestNode Flash | **KEEP / ROLE_CHANGED** |
| \`HardwareConnectionService.ProgramTestFlashAsync\` | 单一 HOST 主控会话、现有精确 Job 授权 | 为生产真实 START 和本次新 Attempt 提供经审查的授权迁移；保留设备会话独占 | **KEEP / EXTEND** |
| \`FlashLabDeviceSession\` 独立 WinUSB 打开 | 独立工具合理，但与 HOST 同时连接同主控需协调 | 共享同一物理会话所有权/工具移交，不关闭窗口来“解决”并发 | **VERIFY/EXTEND** |
| \`ProductionWorkflow\` | 有 FLASH→TEST 的接口雏形，但记录 Flash 时有 \`RunFactKind.MockObservation\` | 必须将真实 FlashAttempt/RawCan/实际 CaseRunId 映射进 RECORD，MockObservation 不得支持正式放行 | **BLOCKED** |
| \`RealTestWorkbenchService\` / \`RealRunCoordinator\` | 已有真实 TEST 后端 | Production 复用真执行器，不建另一个测试 DSL/Mock executor | **KEEP** |
| \`RealTestRecordSession\` / SQLite/HTML/JSON/CSV | 有实体测试证据链 | 增加 Inspection/FlashAttempt 映射，连续完整 CAN 与固定单台/批量报告证据，不重建 DB 真相源 | **KEEP / EXTEND** |

注意：以上是当前 SHA 的静态源结论。**此前 R1 扫码+烧录接线已经提交源码，但与新业务存在矛盾，后续整改应定点迁移；不能因为它“曾提交”就强制保留旧操作。**

## 四、职责边界：不新造第二个执行引擎

- SCAN **只有**设备采集/编号校验/工程型号识别；合法扫码可发给 Production 一个“身份确认事件”，而不是自己调用 CAN/夹具或 Flash。
- Production 负责“生产前置工序的顺序”并调用 DEVICE 的夹具请求/真实 START、FLASH 现有服务、TEST 现有服务、RECORD 现有事实 API。业务状态机不是第二个 TEST DSL。
- FLASH 负责“怎么把这一份真实镜像写入特定 ECU”，包括 UDS/ISO-TP/擦除/下载/复位；输入冻结 Job，输出分阶段的实际过程/完整证据，而不是自己挑 TestProject。
- TEST 仍执行开发和正式生产的用例树，生产传入整棵已启用顺序计划，运行结果与失败处理通过现有语义记录。
- PROJECT 持有“这个型号当前有效资源是什么”和资源 SHA；生产开始前获取不可变快照，自动保存中途编辑不会改变当前执行。
- RECORD 只有一个真实事实体系；FlashAttempt 不和 TestRunId 合并，二者都在同一个 InspectionId 下。

## 五、设备授权与用户操作：必须经过专业审查而非僵硬重复弹窗

**已批准用户体验**：实体启动键启动本次烧录+自动 TEST，常规生产不需要鼠标；但用户没有要求“绕过 USB/烧录已有所有安全检查”。

**研发建议**：工位配置稳定时可以有一个明确生效的生产操作许可（绑定 Station/ProjectId/ProfileSHA/FirmwareSHA/设备会话），每收到夹具已就绪后的**新实体启动事件**，根据本次编号/最新冻结 Job 与工位状态产生**只能用于当前 FlashAttempt**的短生命操作令牌。物理写入仍经同一 WinUSB 会话和底层 Flash fencing；失败/工程换型/设备重连/版本变更时许可失效。是否需要操作员登录/权限级别由产品实际场景决定，**不能为研究目的额外强制每台扫码登录**。

现行 \`AUTHORIZE ECU FLASH <fingerprint>\` 是实验/研发源码的已实现合同，生产无需逐台手动输入长串；调整其输入方式需 FLASH/DEVICE 和真实硬件门禁审查，**不能先简单删除检查并宣布已符合业务**。

## 六、可直接交给研发的不可变 RunPlan 草案

\`\`\`text
FrozenProductionRunPlan {
  InspectionId, ScanEventId, DutSerial, ModelCode,
  ProjectId, ProjectManifestSha, ProductModelAtStart,
  TestTreeSha, OrderedEnabledCasePins[(EntryId, PackageAssetId, Revision, SHA)],
  DbcPin, OptionalWaveReferencePins,
  NativeFlashProfilePin, FirmwareAssetPin, SecurityProviderPin,
  EcuCanBitrateKbps, StationEpoch, FlashJobFingerprint,
  CreatedAt, ProductionPolicyRevision
}
\`\`\`

**必须在本次 START 前重检**：Scan/型号工程对应不变、没有正在运行的上一台、夹具就绪的来源有效（不能仅 GPIO ACK）、实体按钮新边沿、ECU CAN 码率读回、当前 Profile/固件/安全算法的 SHA/受支持范围、Report 存储可用、TEST 已启用树编译成功。

若任一对象改变，冻结结果作废、不会在半次生产执行中套用另一个固件或新的 DBC。不同型号切工程的确认只有在工位空闲且原来的未完成数据可恢复之后才能发生。

## 七、整套生产记录与报告（明确四个层级）

1. **Raw CAN 底本**：真实 TX/RX、时间、通道、ID、DLC、所有 DATA、丢失/截断元数据；Flash/UDS 阶段保留敏感信息脱敏规则。
2. **TEST 关键日志**：StepOccurrence、测量值、判据、失败原因、引用原帧范围；无 UI 文本制造正式 PASS。
3. **一次 Inspection 的单台报告**：SN/Model/工程版本/固件 SHA/FlashAttempt/真实结果/TEST 已运行或未运行用例/最后结果；即使烧录失败、停止、未知也有独立报告或明确“不完整待恢复”事实。
4. **独立批量报告**：按可查询范围汇总不同 ECU 数、检测尝试数、通过/失败/中止/复检；**首次合格、最终合格、批次起止**是待批准定义，不能自作口径。正常生产不要求先手工创建批次。

一个 Serial 被复检三次是**一只 ECU、三次完整检测，三次必烧录尝试**；不能因为 CountDistinct(Serial)==1 就推断第一次放行成功，也不能因为三次 PASS 就算三只新出厂。

## 八、优先实施门禁与顺序（研究建议，不擅自改 R1 准出范围）

| 优先顺序 | 工作包 | 正向验收与错误场景 | 前置条件 |
|---|---|---|---|
| P0-A | 原生 FLASH 字段及 JobBuilder | 无 XML 运行、文件复制后无外部引用、HydEPB 真机流程等价，校验/终止日志有证据 | FLASH 专项真实 XML/固件/安全算法向量 |
| P0-B | 单型号工程资源与 TEST 树 | 型号唯一、当前固件/Flash/DBC/Enabled树可冻结，自动保存不破坏运行 | PROJECT+TEST |
| P0-C | 真实扫码枪采集与型号项目路由 | 实体扫码、CRLF/连扫/失焦、同型号无弹窗、异型号确认、未知拒绝 | 设备接口已核对 |
| P0-D | 夹具/START 真实 FW 事件与 Production 状态机 | 扫码合法可请求压紧，有独立就绪事实，只有实体新 START 边沿开跑 | HW/DEVICE 实测，不接受 Mock 冒充 |
| P0-E | FLASH→TEST→RECORD 统一生产闭环 | 每次必烧录、失败不进入 TEST、用例全量 Enabled、FlashAttempt/RunId 同源单台报告 | A/B/C/D，真实 ECU |
| P1 | 批量报告与异常/重复策略完善 | 同号多次报告与去重不重计、可追溯统计，报告界面简洁 | 负责人最终确认统计口径 |

**不要求用户每完成一个按钮就做一次实测**；Windows 编译/单测/静态 UX 审核先做，最后提供明确场景集中实机验收。R1 哪个阶段必须完整包含生产闭环，现行项目管理尚未取得用户最新版本准出审批，不得在此自行立新版本 Gate。

## 九、正式审查最少要提出哪些未决问题

用户已明确的 18 条业务决定**不重复问**。保留给真实研究/现场的仅：
1. 扫码枪实际型号、USB 枚举/HID 或 COM、真实前后缀和真实标签文本；UT1 是否需要 HW 字段/CRC，唯一 SN 的发行范围。
2. 主控当前是否存在可读实体 START/STOP 输入、夹具真实到位反馈或经用户核准的替代就绪证明。
3. Flash 原生字段 Schema 和必须支持的第二个 ECU/OEM 流程是否已有材料；不支持算法必须显式阻断。
4. 实际 ECU 目标软件版本是否能读到真实 DID/应用层特征，若不能如何描述“程序已写入”与“生产已验证”。
5. FAIL 后是否保留夹具/IGN 现场；复检/批次的具体统计口径；当前 R1 是否要求完整产线闭环作准出。

这些**不能**由阅读源码或网页替用户判定。

## 十、外部对标

NI Sequential Process Model 将 UUT 识别、运行前后及结果报告从具体测试序列中抽离，适合 AutoTest 的 Production 编排；不要求复制 NI 的软件结构。  
https://www.ni.com/docs/en-US/bundle/teststand/page/process-models.html  
https://www.ni.com/en/support/documentation/supplemental/08/teststand-process-model-development-and-customization.html

微软扫描设备和 SerialPort 文档说明 HID Keyboard、HID POS、USB CDC/串口输入必须各按真实设备类型处理。  
https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/pos/barcodescanner-configure  
https://learn.microsoft.com/en-us/windows/win32/inputdev/about-raw-input  
https://learn.microsoft.com/en-us/dotnet/api/system.io.ports.serialport.datareceived
