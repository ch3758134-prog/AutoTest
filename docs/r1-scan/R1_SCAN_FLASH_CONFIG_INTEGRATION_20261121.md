# AutoTest R1｜SCAN × TEST × CanTool FLASH 配置联合接入说明（2026-11-21）

## 1. 研究来源与取舍

**直接回读的专项研究**：
- `research/r1-scan-configuration-20261120/docs/r1-scan/START_HERE.md`
- `S-07_CUSTOM_DUT_BARCODE_ENCODING_V01_20261121.md`（用户允许自定义编码，UT1 是候选编码）
- `S-08_END_TO_END_SCAN_PRODUCT_SYSTEM_RESEARCH_20261121.md`（产品变型与任务、配方、真实运行的区分）
- **`S-09_SCAN_FLASH_PRODUCTION_RECIPE_INTEGRATION_20261121.md`**（烧录配置与扫码匹配同一 Recipe，Flash 一律复用现有 CanTool/TEST）

核心判断：扫码是身份/方案索引，不是实体 CAN 指令。**FlashSettingsDocument 是元数据而不是能执行烧录的配置包**，只有 TEST `Flash` 步骤经 `ConfiguredRealTestFlashPort.FreezeJobs` 冻结的 `CanToolFlashJob` 才是真正可执行的程序。前期对普通 UI 错误强行附加物理 USB 联锁的过度设计，已在 R1 `295f4ea7f8` 撤销，保留原有硬件协议和烧录流程的具体权限控制。

## 2. 本轮 R1 新增配置与功能

### 2.1 工程资产／扫码配方

保留原 `ScanMatchSettingsDocument` SchemaVersion 1 / 原有构造及 JSON 格式，向 `ScanRecipeDraft` 追加**可选字段**，避免旧工程导入直接失败：

| 配置 | 字段 | 说明 |
|---|---|---|
| 产品 | `Model` / `HardwareCode` / `Customer` | 可匹配 UT1 的 ProductCode、HardwareCode；历史纯 SN 可不使用该字段 |
| 运行范围 | `TestProjectAssetId` / `TestProjectRevision` / `TestProjectFingerprint` | 已保存的测试项目、有序启用用例 |
| 单用例 | 原有 `TestProgramAssetId` / Revision / Fingerprint | 兼容旧扫码配方；与 TestProject 同时选择时只能引用该项目已启用用例 |
| 解析数据 | `DbcAssetId` + Revision/SHA | 跟随原始 DBC 资源冻结 |
| 烧录策略 | `FlashPolicy` | 第一版仅 `NoFlash` 或 `FlashRequired`，不静默启用条件跳过 |
| 软件目标 | `FirmwareAssetId` + Revision/SHA / `FirmwareReleaseLabel` | 工程固件版本与人类可读的软件目标，仍需 ECU 实际软件版本核验 |
| 烧录配置 | `FlashSettingsAssetId` + Revision/SHA | 引用已有工程烧录元数据，与真实 Flash 步骤中的 Job 作一致性核对 |
| ECU CAN | `TargetEcuCanKbps` | CAN 目标速率；Flash 执行仅接受 CanTool 代码当前支持的 250/500k |
| 可执行编程快照 | `FlashJobFingerprint` | 真实 TEST 的 XML/HEX/SafetyFile 冻结 Job SHA；自动发布时计算，扫码前复核 |

**配置员操作**：工程配置→“扫码与烧录”标签→“打开／新建扫码配方”和“打开／新建烧录配置”；在扫码配方右侧选择 TEST 项目/单用例、产品、固件、工艺、速率及烧录策略。

### 2.2 真实工作合同

- `ScanRecipePreparationService.FreezePublishedAsync`：正式保存发布的扫码配方前读取项目内启用用例、固定指纹、以及唯一 `Flash` 步骤；检查配方的 Firmware 与 `FlashSettingsDocument`、实际 CanTool HEX/S19 SHA、XML 波特率、APP 或 BOOT 请求/应答端点是否相符。冻结的是**真实 Job 的指纹**，不是“勾选了烧录”。**不会连接 USB 或刷写 ECU**。
- `NoFlash`：测试范围内有可达启用 `Flash` 步骤，则禁止发布/扫码准备；不会偷偷跳过 Flash 步骤。
- `FlashRequired`：整批已启用测试范围恰好一个 `Flash` 步骤；所需固件/工艺/码率/软件发布名称全有，并由实际 CanTool Job 冻结。物理执行仍由 R1 原有 TEST+CanTool +独立 Flash Consent 完成。
- `ProjectScanMatcher.MatchAsync`：匹配条码后重新检查已发布资源及真实 Flash Job 指纹，状态 `CONFIG_MATCHED` 只代表**配置准备通过**；失败返回解释和具体重新配置方向。工程变更后既有匹配不会被当作最新。
- `MainWindowViewModel.ApplyScannedRecipeAsync`：操作者在工程配置页选择“应用匹配到本次 TEST 选择”，用已有 `ProjectRunSelectionState` 勾选有序用例并填写 DUT 编号；**不触发真实 TEST、烧录或正式生产放行统计**，仍需在 TEST 页面手动预检和启动。开发者不扫码仍可用例/单步调试。
- 相同 Serial 的重复扫描不自动形成“新出厂”。目前并未完成 `InspectionSession/ReleaseDecision` 的唯一真实出厂统计接线，不能据此声称有正式重复去重结果。

### 2.3 自定义 UT1（可选）

新 `Ut1BarcodeCodec` 实现 S-07 的候选规则：`UT1-ProductCode-HardwareCode-外部唯一Serial-CRC16`；CRC-16/CCITT-FALSE，含首段原样 ASCII，算法向量 `UT1-EPBA-H02-000000123456-4372`。Serial 保留所有前导零；不将 Product/HW 拼进 Serial 造成重复注册。有效 UT1 数据还必须与已发布 Regex 中的 model/hardware/serial 提取一致；校验失败无 legacy 自动回退。历史条码可继续使用现有 `IdentityRuleSet` 发布规则。

## 3. 当前未做、不应宣称已交付的部分

1. **真实扫码枪设备接线**：现有键盘模式捕获已具备，USB/CDC COM 与 HID POS 专门驱动、扫描枪实例/前后缀、现场验证尚未交付；实际设备类型未证实，不写死。
2. **完整正式生产出厂模式**：目前应用扫描选择后的真实 TEST 是 Development/Diagnostic；Production 发布/复检/去重/ReleaseDecision 仍缺 UI 和统一记录。
3. **软件版本读回**：支持现有 CanTool APP 复位后恢复，但不等于核对实际软件版本 DID；不能凭版本文本或 APP 50 03 计正式版本 PASS。
4. **跨工程自动安装与切换**：本次只对当前已打开的工程和其中发布的方案生效。
5. **条件烧录、自动重新烧录**：本轮仅 NoFlash/FlashRequired。扫码事件不会触发 Flash；重复扫码不自动重复擦写。
6. **端到端 Windows 和真机验证**：当前仅提交源码和测试用例，尚无 R1 最新 SHA 的 Windows Debug/Release Build、Tests、WPF 启动以及真实扫描枪+ECU 烧录证明。

## 4. 验证门禁

- 纯源码回读：新 C# 文件基本结构（花括号）及 XAML 标签结构静态检查完成，**不是编译**。
- 新测试：`ScanFlashRecipeIntegrationTests`、`Ut1BarcodeCodecTests`、`Ut1ProjectRoutingTests`，以及旧 `ProjectConfigurationWorkflowTests` 必须在同一最新 HEAD 一起执行。
- Windows：运行 `HOST/scripts/r1-verify.ps1` 完成 Restore + Debug/Release Build + Tests，记录首次失败、提交号、日志。WPF 人工 UI 测试验证“扫码与烧录”页、配置保存、扫码预览、应用 TEST 选择。
- 真实硬件：核实扫描枪枚举/输入、ECU CAN 码率、已支持的 CanTool 安全算法/文件、烧录后 APP 和软件版本实际判定，最后整体 R1 集中验收。

### 审批范围

自定义 UT1 与默认生产路由是**候审选项**，不是默认强制更换现场全部条码。此次完成的是扫描配置、Flash 配置与 TEST 范围的真实源码准备闭环，不是新的生产执行器，也不是全量生产统计功能。
