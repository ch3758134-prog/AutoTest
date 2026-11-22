# AutoTest R1｜最新澄清下的全系统再研究、领域设计及 USB COM 第一轮可验收候选

日期：2026-11-22（UTC）  
实施基线：`R1@5f7a2cf42c1a30434431af18e2fb26741075ad2f`  
实施分支：`feature/r1-usb-com-scan-preflight-20261122` → Draft PR → `R1`  
状态：**业务合同已澄清 / 一部分源码已提交 / Windows Build、实机扫码、生产硬件、Flash 原生执行均未验收**。严禁改写成 FIELD_PASS。

## 0. 重新研究的依据与取代关系

主要依据：
- `Project_Management@6d2a476d`：`docs/project-management/current-20261121/01～09`、最新用户批准的单型号工程/专业工具/自动测试/生产检测分离；
- `research/r1-scan-configuration-20261120@68fb72b5`：S-10 原生烧录、S-12 字段映射、S-13 协同合同、S-14 最新确认与源码差异；
- `R1@5f7a2cf` 的真实 WPF/TEST/DEVICE/SCAN/FlashLab/RECORD 源码；
- 微软 System.IO.Ports 文档：串口 `DataReceived` 事件非“每一字节一个事件”；`SerialPort` 有缓冲/流读取行为差异。本候选采用独立短超时读取线程+显式字节分帧，而非将一次触发视作完整条码。

**最新业务规则优先于旧 S-01～S-09、旧 R1 ScanRecipe 方案**：
- 扫码枪是 USB 虚拟串口（COM）；**不是** keyboard wedge、HID POS 或经过 MK20 USB 的数据。
- 一 ECU 型号对应一份当前可用工程和其当前有效生产配置；工程显示名称不是型号身份。
- 扫码仅得出 ModelCode + 不丢前导零的唯一 Serial；硬件版号可提供兼容性证据，但不再是“多配方选择器”。
- 每次**完整正式生产检测（包括复检）**，先执行**一次必需烧录**，然后全部 Enabled TEST；用户不需要给每个生产 TEST 用例手工添加 Flash Step。
- 实际生产顺序：扫码 → 确认工程 → 请求夹紧 → 实体工位就绪证据 → 新实体 START 边沿 → 原生 Flash Job 冻结和执行 → APP/版本各自后验 → TEST 全部 Enabled 顺序执行 → RECORD/单台报告 → 受控收尾。
- 正常生产站不依赖 PC 逐台确认、手动挑 Recipe、每只 DUT 手输安全 SHA；工程师研发测试仍可不扫码运行，独立工具无工程也可工作。
- 独立 UDS/FLASH/DBC/WAVE/手动台架是**工具**；DBC/固件/参考波形/配置是**共享资源**；TEST 与 Production 是不同业务过程，复用唯一真实执行和设备服务。

## 1. 完整域模型与业务边界

| 域 | 当前职责 | 禁止的捷径 |
| --- | --- | --- |
| PROJECT | 单型号身份、manifest、DBC/TEST/原生 Flash/当前固件/算法资产、修订 SHA、原子保存 | 工程名冒充型号、旧 Recipe 的路径字符串冒充正式资源 |
| SCAN | 独立 COM 会话、原始数据来源、分帧、Codec/CRC、型号/SN 身份和拒绝证据 | 键盘输入当扫码枪、漏帧拼接、重复扫码立即烧录 |
| STATION/DEVICE | 主控独立 WinUSB、ECU/KEY CAN、IGN、夹具、物理 START 事件和有效工位证明 | USB ACK 冒充机械到位；假 GPIO/假 START；扫码串口误用主控 USB |
| FLASH/UDS | 强类型原生参数→不可变 Job、真实 CanTool/ISO-TP/UDS 流程、后验和原始证据 | 将 GUI XML 隐藏却仍运行期读 XML；APP 50 03 冒充目标版本匹配 |
| TEST | 唯一 StepNode DSL、全部 Enabled 用例树、实际执行和判定 | 工程师勾选改变生产范围、生产另写一套 TEST 解释器 |
| PRODUCTION | Inspection 生命周期、真实夹具/START/Flash前置/TEST/收尾/复检 | 通过旧 `NoFlash`/多 Recipe 跳过必烧；忽略旧 TEST 内 Flash 导致双烧 |
| RECORD | Inspection→FlashAttempt→CaseRunId→StepOccurrenceId→原始证据、单台/批次报告 | 数扫码次数当产量；没有硬件/证据仍出 PASS |
| UI/工具 | 工程师/操作员明确角色，浅色主应用、少点击、工具独立但共后端 | 生产按钮直接编辑诊断 XML、要求操作员熟悉内部哈希 |

## 2. 本轮源码调查：差距在何处

- `SerialBarcodeCapture` 原先只有 `IVerifiedBarcodeLineSource`，`MainWindowViewModel` 和状态栏只挂 `KeyboardWedgeCapture`，**完全不是实物 COM 接入**。
- `ProjectDefinition/ProjectRepository` 原先没有 `EcuModelCode`；同名/改名会让型号映射不可靠。
- `ProjectScanMatcher` 使用发布 Regex + 多 Recipe + TEST/Flash Pin，且 `ScanRecipePreparationService` 依旧依赖旧 TEST Flash 步骤与 XML。**保留历史兼容源码，但不得再把它当最新生产入口。**
- `ConfiguredRealTestFlashPort.FreezeJobs` 仍读取 `programmingProfile` (XML)、`firmwarePath`、`securityLibraryPath`；`FlashSettingsDocument` 是元数据而非真正 Job 参数。原生生产 Flash 尚未实现。
- `ProductionWorkflow` 仍有 MockObservation 风险；正式生产不能把 Mock 事实当烧录完成。
- `ProjectWorkspaceService` 已有 manifest 版本锁、原子保存和 `CurrentChanged`，应复用，不能另设不受控型号 JSON。
- `HardwareConnectionService` 的夹具 USB 命令目前不是机械到位证据；实体 START/STOP 的有效硬件输入/事件尚无实物确认。不得为演示绕过。
- WAVE 现有比较器与 RECORD 的判定证据类型有已确认合同冲突；正式质量门禁需要专项修复，不能用扫描成功冒充整机可生产。

## 3. 第一轮实际实现：严格限制为安全、可操作的前置能力

### A. 真 USB COM 数据输入
- 引入 `System.IO.Ports`（集中包版本）；独立 `SerialPortBarcodeLineSource` 打开操作员选择的 Windows `COMx`。
- 当前默认 8-N-1、无硬件流控、9600 baud；可选 1200/2400/4800/9600/19200/38400/57600/115200。**这些是首轮调试参数，实际枪的输出必须现场观察后确认；不宣称已支持厂商全部设置。**
- 真正以字节流 CR、LF、CRLF 分帧；排除 CRLF 双计数，限制长度，禁止不可打印字节，超过 2s 的帧内断续不把尾巴拼成新身份。严格保留 0 前导位。
- 读取线程每次短超时检查取消；断开/异常显示状态；不自动重连、不缓存后触发生产任务。
- COM 连接参数保存在本机 `%LocalAppData%/AutoTest/scanner-com.json`，不保存串码、不含任何硬件写入授权。没有主动连接按钮前不会打开设备。
- 用户关主窗口时停止读取/关闭 COM，之后才释放主控设备；扫描服务与主控 WinUSB 是完全不同的设备/会话。

### B. 正式工程型号身份
- `ProjectDefinition.EcuModelCode` 是与显示名分离的可选大写模型码（初版 2～16 位 A-Z/0-9），以旧工程 `null` 保持兼容。
- `ProjectRepository.StoredProject` 保存/读取 `EcuModelCode`，不把旧 `Name` 静默迁成型号。
- 工程工作区的 `SetEcuModelCodeAsync` 通过已有 manifest SHA 比对、修订递增、原子保存；真实 TEST 运行期间不可在扫码设置界面改变型号。
- 当前已打开工程与 UT1 型号相同时仅报告 **IDENTITY_MATCHED**；不同型号、未打开工程、型号未绑定、CRC 错误明确拒绝；**不调用旧 Recipe 匹配器**，不主动切工程/夹具/Start/Flash。

### C. WPF 可操作入口
- 主窗口移除键盘扫码框，改为“扫码枪”真实 COM 工作台入口。
- 设置/刷新实际 COM 列表、波特率、连接/断开；显示最近完整原始条码、型号、唯一 SN、型号匹配状态、错误；保存当前工程明确型号。
- 工具窗与主窗口同生命周期；关闭工具窗可让扫描在主程序中继续保持连接，主窗口统一退出才真正断 COM。
- 不存在生产运行或工位完成提示；只有“身份预检”这一合法观察态。

### D. 测试与代码门禁
- `SerialBarcodeFrameDecoderTests`：CR/LF/CRLF、连续两帧、字节分段、0 前导、超长、不可打印字节、帧内超时。
- `ProductionBarcodePreflightTests`：已有/缺失/不符型号、坏 CRC、不允许旧 Recipe 兜底、SN 精确保留。
- 项目 Manifest 的 ModelCode 新旧文件往返读写、修订及跨工程隔离需要 Windows 实际测试；首轮源码仅作候选。
- **尚未运行** Windows Restore、Build、Tests、WPF 启动及真实 USB 扫码；Linux 当前未配置 .NET10 编译工具链。不得标 Build PASS。

## 4. 第二轮：原生烧录配置必须实际驱动真实 Flash

`NativeFlashProfileDocument` 至少有：
- `SchemaVersion/ProfileId/EcuModelCode/ProgrammingMethodId`；
- `EcuCanKbps`（当前已验证烧录模式限定 250k/500k）、`App/Boot Tx/Rx`、11/29位、P2/P2*；
- `FlashStart/FlashEnd/BlockLength/BinBaseAddress?`、编程 Routine 和算法选型（只展示真正执行器支持的字段）；
- 当前 `FirmwareAssetId/Revision/SHA` 与 Security Provider 的 `AssetId/ABI/SHA`；
- APP 会话后验与**另行明确配置且真正实现的**软件版号判定；
- 草稿/当前生效状态、修订/内容指纹、相容性限制与失败说明。

工程内固定位置建议 `assets/flash/profile.json`，必要历史发布快照按 SHA 保管；固件/算法导入后复制进工程受控目录。用户只批准“固定工程位置”，此精确文件名仍是技术候选。

真正的 `NativeFlashJobBuilder` 必须**直接**从强类型对象冻结已有 `CanToolFlashJob` 参数，不在运行时生成临时 XML 再回读。需以实测 HydEPB CanTool 流程逐段比对 APP/Boot TX/RX、擦除、0x36 块、末尾特殊处理、校验、复位与最终真实报文。独立 FlashLab 可无工程运行自身草稿，主动加载工程资源时才复用。

正式生产 Flash 作为前置 `FlashAttempt`，成功且必要后验满足才转入 TEST；若生产 TEST 树还有可达的 Flash Step，**明确阻断双烧录**。工程师开发 TEST 的旧 Flash 步骤保留兼容，不用于推断正式生产是否必烧。

## 5. 第三轮：真正的生产执行合同

推荐状态机：
```
WAIT_SCAN -> MODEL_RESOLVED -> PROJECT_READY
    -> CLAMP_REQUESTED -> CLAMP_READY_REAL_EVIDENCE
    -> START_NEW_PHYSICAL_EDGE -> FINAL_PREFLIGHT_FROZEN
    -> FLASH_ATTEMPT -> FLASH_VERIFIED -> TEST_RUNNING
    -> RECORD_FINALIZING -> RESULT_HELD -> STATION_RECOVERED -> WAIT_SCAN
```
- 每个 Inspection 有新 `InspectionId`、来源 ScanEventId、ModelCode、原始 Serial、工程/资源SHA、主控 epoch；记录开始时间不代表 Flash 已开始。
- 夹具只有 USB ACK 时停在 `CLAMP_REQUESTED`，不可谎称 `CLAMP_READY`；START 需要真实输入、去抖和新上升沿，未接线不得替代为软件按钮。
- 生产每次 Inspection（复检亦然）需要新的真实 FlashAttempt，防止重放旧 START 或重复扫码导致双擦写；正在运行时新的扫码丢弃，不覆盖现场身份。
- 只使用已发布**当前型号工程**的全部 Enabled 用例；不读取开发人员临时勾选；目录节点不运行，执行统一 `RealRunCoordinator`。
- Flash 失败时 TEST 不是 FAIL 而是 NOT_EXECUTED；任何未完成原始证据、不受控复位或未知电气状态保持 UNKNOWN/恢复要求，不能放行下一个 DUT。
- 报告统计区分 `ScanEvent` 数、`Inspection` 次数、唯一 DUT、复检次数、PASS/FAIL/STOP。首次合格/正式放行政策待用户对样例验收，不自行定义。
- 独立工具不受生产扫码前置限制，但共用唯一物理独占会话和真实事实系统。

## 6. 跨模块全量整改顺序与切分

| 顺序 | 模块 | 必须交付 | 当前第一轮 |
|---|---|---|---|
| G0 | Windows Build/Test/WPF | 精确 SHA、Debug/Release、全量测试、启动报告 | 待 Windows Codex |
| G1 | COM 扫码 | 真实 COM 读字节、完整帧、拒绝、状态 UI | 源码候选已实现 |
| G2 | PROJECT/型号 | 显式 ModelCode、manifest 保存、单型号多工程冲突目录 | 当前工程身份已实现；跨工程唯一索引未做 |
| G3 | FLASH 原生 | 固定工程配置、Job Builder、独立工具对齐 | 研究完成，未改协议代码 |
| G4 | DEVICE/STATION | 夹具机械到位证据、真实实体 START/STOP、会话恢复 | 被真实硬件证据阻断 |
| G5 | Production | 无鼠标过程、每次必烧录、全 Enabled TEST、防双烧 | 未接入真实运行 |
| G6 | RECORD | Inspection/FlashAttempt/CaseRun/StepOccurrence，同源报告 | 基础 TEST RECORD 有，生产关联待做 |
| G7 | WAVE/UDS/工具 | 修 Wave→Record、独立工具共享真实资源 | 需专项交叉审查 |
| G8 | UI/产品验收 | 自动保存/清晰交互/关闭/错误及 100–150% DPI | 首轮扫码工作台候选，完整 UI 仍在专项 |

## 7. 第一轮操作者验收（当前不会擦写 ECU）

1. 在 Windows 从这个候选分支构建并启动 `AutoTest.Desktop`，打开已有 ECU 工程；确认左下/底栏没有键盘模拟扫码框。
2. 点“扫码枪”，刷新实际 COM 口，选择扫码枪的端口/速率，点“连接”；如果端口被其他应用占用，显示清晰失败而不假连接。
3. 先将工程的型号明确设为 `EPBA` 并保存；关闭/重开工程检查 manifest 仍保留型号（工程显示名无需叫 EPBA）。
4. 用真实 USB 串口扫码枪扫描已配置终止符 CR、LF 或 CRLF 的合法 UT1 标签：`UT1-EPBA-H02-000000123456-4372`；预期完整原文、型号 EPBA、序列号 `000000123456`，仅显示匹配不触发实体操作。
5. 错误 CRC、未设工程型号、其他型号、重复扫描、扫描过程 COM 断线与重新连接：检查解释、状态与无硬件动作。
6. COM 连接后关闭工作台窗口、再打开应看到同一连接状态；关闭主窗口应停止串口而不要求逐个关独立窗口。
7. 这轮不应在 UI 中看到“Production PASS”“已夹紧”“已执行烧录”“已新增出厂一台”等没有硬件证据的结果。

## 8. 已知限制、是否发布

- 当前只识别 UT1 候选格式；实际扫码枪的真实数据与串口输出尚未获取，**不把 UT1 默认编码冻结为现场全部标签标准**。实际厂商条码不同，下一轮依据字节样本做项目可编辑 Codec/前后缀，而非先冒险自动兼容。
- 真实 USB COM 是本轮唯一正式扫码输入；旧 KeyboardWedge 类可作为历史读取/单测兼容代码留存，但主 UI 不再接入。
- 本轮只比对**当前已打开工程**；跨工程索引、异型号自动切换/确认与工程级唯一性还没实施。
- 旧 ScanRecipe/NoFlash/FlashRequired 源码尚在 R1 作兼容，不能以它作为最新正式生产执行入口。
- 生产 Flash 前置、原生配置、夹具/START 与正式 Inspection/报告仍未实际连接，不可作为生产首版发布。
- **候选结论：SOURCE_CANDIDATE / BUILD_NOT_VERIFIED / SCANNER_FIELD_NOT_VERIFIED / PRODUCTION_NO_GO。** 不更改 `main`、固件或已有已通过的 CanTool 技术流程。
