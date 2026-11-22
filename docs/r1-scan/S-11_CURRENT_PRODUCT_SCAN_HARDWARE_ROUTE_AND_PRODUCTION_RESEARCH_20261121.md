# AutoTest｜S-11 扫码枪、型号工程匹配与生产站点的独立系统研究（按最新业务基线重做）

**2026-11-21｜研究分支** \`research/r1-scan-configuration-20261120\`｜**状态** \`USER_CONFIRMED_BASIS / RESEARCH_PROPOSED_DESIGN / R1_SOURCE_GAP / FIELD_NOT_VERIFIED\`  
**当前产品依据**：\`Project_Management@6d2a476de8eba752a227a02dc77b0fc5d30296aa\` 中的 \`docs/project-management/current/README.md\`、\`00_DECISIONS_AND_SUPERSESSION.md\`、\`01_PRODUCT_SYSTEM.md\`、\`02_ECU_PROJECT_TEST.md\`、\`03_PRODUCTION_STATION_AND_SCAN.md\`、\`07_SPECIALIST_WORKPACKAGES.md\`。**现行 R1 源码审查**：\`R1@5f7a2cf42c1a30434431af18e2fb26741075ad2f\`，不是完整 Windows/实物验证。

## 0. 关键纠错：从现行产品任务出发，不再从旧 Recipe 类出发

**已确认业务：** 
- 一个 ECU 型号一个工程。工程自己管理唯一当前生效 DBC、TEST 分类目录与有序用例、固件和生产原生 Flash 配置；
- 扫码编码包含型号和唯一编号，校验成功后当前同型号不弹窗，其他已知型号**确认后**切换已安装的工程，未知不猜；
- 正式每次完整生产检测（包括复检）都需要**本次实际烧录**，烧录在 TEST 之前，不依赖编辑一个 TEST Flash 步骤；
- 扫码正确后触发夹具请求，**等待实体启动键**才进入预检和烧录；正式连续生产无需 PC 逐台点击开始；
- 生产测试执行全部启用用例，失败处理遵从已编辑的 TEST 规则；结果/编号保留到下次合法扫码；
- 扫码重复允许形成不同的检测尝试，但绝不能累计成第二只新 ECU；研发单步、独立工具无需扫码；
- 工具、共享资源、工程师 TEST 与生产工位不是父子关系；主控夹具/按键的真实性要由硬件及固件专项验证。

**被取代的先前方案（只保留研究价值/历史兼容）**：S-01～S-09 候审的“每 SN 精确绑定”、“ProductVariant→Task→Recipe→TestProject”、“扫码后 PC 确认开始”、“NoFlash/ConditionalFlash”、“TEST 内手工插入 Flash 才能生产烧录”。2026-11-21 R1 代码里这些类确实存在，但**源码事实不能覆盖新的用户决定**。禁止继续在旧扫码规则页面增加必须操作的层级。

## 1. SCAN 不是一个功能；至少是五个明确分离的技术合同

| 合同 | 输入 | 只负责 | 不负责 |
|---|---|---|---|
| ScannerDevice | Windows 实际设备事件/USB HID 或串口流 | 可验证的设备身份、原始字符/字节、边界、连接/断线 | 认 ECU 型号、挑固件、执行设备 IO |
| BarcodeFormat | 完整原始标签 | 语法、型号代码、唯一序列号、校验码、编码版本及错误原因 | 工程路径/本次生产软件版本 |
| InstalledModelCatalog | 已批准安装/登记的多个 ECU 工程 | 唯一 \`ModelCode→ProjectId/ManifestPath\` 映射、路径/工程本身有效性 | 把未知型号猜成相似工程 |
| ProductionStation | 合法码/当前工程/实体按钮与夹具事件 | 从扫码进入完整工位状态机，串行单 DUT 处理 | 自己实现 CAN/UDS/烧录/TEST 底层 |
| InspectionIdentity | 实体唯一编号、原始 ScanEvent、时间 | 同一实体跨多次检测保持同一身份，与实际 Run/Report 关联 | 把扫码次数、运行次数和唯一合格件数混为一谈 |

**这里的“本机型号工程索引”是合法且必要的站点查找表**，其作用是定位本地安装的工程，不是被用户取消的“烧录配置外部索引文件”；生产 Flash 参数仍应**在每个工程内部的固定路径**保存。

## 2. 扫码枪设备：必须先区分 HID 键盘、HID POS 和 COM/CDC

### 2.1 当前代码是怎样的？

- \`AutoTest.Specialists/Scan/KeyboardWedgeCapture\` + \`BarcodeInputRouter\` + \`IdentityRuleSet\` + \`ProjectScanMatcher\` 存在。
- WPF \`MainWindow.xaml.cs\` 的 \`ScannerEntry_TextInput\`、\`ScannerEntry_KeyDown\` 依赖**输入框焦点**并对 Enter 完成采集；\`MainWindowViewModel\` 调用 \`MatchAsync\`。它不自动证明字符来自特定扫描枪。
- \`SerialBarcodeCapture.cs\` 只有 \`IVerifiedBarcodeLineSource\` 的抽象读取，源码明确没有打开 Windows SerialPort，**不构成真实 COM 枪完成**。
- 过去研究同时出现“必为键盘枪”和“必为 USB/COM 枪”的判断，都未见当前设备枚举/型号实测；不可武断指定一个。

### 2.2 可执行的设备适配设计（研究建议）

| 实际扫描设备模式 | Windows 实体获取 | 输入解析 | 主要注意 |
|---|---|---|---|
| USB HID Keyboard Wedge | 键盘字符经过正常焦点进入 WPF；必要时经 Win32 Raw Input 区分设备来源 | 原始字符流、终止键、超时和最大长度 | 普通 TextBox 无法证实设备身份，尤其人工键盘/粘贴/编辑焦点误入；不能全局 Hook 抢走工程师键盘 |
| USB HID POS Scanner | Windows POS BarcodeScanner API 枚举/Claim/Enable/DataReceived | 设备报出完整扫码数据及可用 symbology 字段 | 必须真实设备支持并配置为 POS 模式；不是所有 HID 枪都兼容 |
| USB CDC / 虚拟 COM 或传统串口 | Windows 枚举 COM 与绑定稳定设备实例，\`SerialPort\`/BaseStream | 增量字节缓冲、CR/LF/CRLF 或厂商协议分帧 | DataReceived 不是“每字节一个事件”；背压、断线、重连、重复尾缀、防止粘包/半包 |
| 已有未知/厂商模式 | 现场设备说明 + PnP/USB 实际枚举 | 先取得原始样本再设计 | 不为未有的设备写假驱动 |

**建议界面**：“设置 → 生产设备 → 扫码枪”只需：设备类型（自动识别候选/手选）、实例或 COM、串口速率/参数（仅在真实串口模式）、终止符、预览原始扫码、连接状态、测试一次。编码及型号工程绑定**另在生产配置**。HID POS/COM 作为实际支持的不同适配器，不在同一时刻抢占多个物理输入源。

**输入处理约束**：每次 ScanEvent 有 \`RawText\`、\`Origin/DeviceIdentity\`（真实可证明时）、\`CapturedAt\`、\`Terminator\`、\`DecodeStatus\`、\`BarcodeVersion\`、\`ModelCode\`、\`DutSerial\`。只删除已明确配置的传输结束符，**不对 serial 做 Trim/大小写转换/数值转换或去前导零**。键盘、粘贴和工程师调试录入可作为明确标记的 \`Manual\` 来源，但只有真实可验证设备和政策满足时才用于无人值守生产。输入长度有界；禁止控制字符混入业务编号；CRLF 不应产生两次扫描。

### 2.3 成熟产品的实际异常处理

- 设备掉线：显示“扫码枪未连接”，工程师操作仍可进行；待扫码可恢复，**不默默把纯键盘输入认作实体扫描**。
- 快速连扫：同一空闲会话只接收首个已完成扫码；扫码/夹具/烧录/测试活动中不排队不替换 DUT，不弹多个重叠窗口。
- 低质量码、CRC 错、未识别型号、多候选工程：显示原始码和明确原因，留在 WAIT_SCAN；未建立生产检验，不动作夹具。
- 焦点问题：若是真正 Keyboard Wedge，通过生产页受控焦点/可视状态或经过测试的设备识别，不让普通工程师编辑焦点接收到条码；禁止系统级全局键盘钩子造成 TEST 编辑问题。
- 不得仅用“输入间隔非常快”判定为真实扫码枪；这个条件既易误判又易被模拟。

**外部依据**：
- Microsoft barcode POS setup: https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/pos/barcodescanner-configure
- Microsoft Windows Raw Input: https://learn.microsoft.com/en-us/windows/win32/inputdev/about-raw-input
- Microsoft SerialPort.DataReceived: https://learn.microsoft.com/en-us/dotnet/api/system.io.ports.serialport.datareceived
- Zebra 的键盘/CDC 两种设备模式示例: https://support.zebra.com/article/000019965

## 3. 条码编码：用户确认的是“型号 + 唯一编号”，不是强制 UT1 细节

**业务必须有**：\`ModelCode\` 与不会因软件/硬件升级变化的 \`DutSerial\`。用户允许自主定义编码，但未批准特定长度、符号、CRC、H02 版字段和打印设备。

**当前候选可沿用并精简** \`UT1-EPBA-H02-000000123456-4372\`：
- \`UT1\` 是版本，\`EPBA\` 是工程型号编码，\`H02\` 是可选的有业务依据的 HW 版，\`000000123456\` 是序列号，末尾 CRC16 只检错；
- 如果真实生产不需要按硬件版本分工程，\`H02\` 不应迫使用户再建独立 ProductVariant 配方层。一个 ECU 工程可以定义允许或需警告的硬件修订范围，但实际只绑定**型号工程**。
- 如果客户标签已有其他格式，则在其实际合法定义里提取 \`ModelCode\` 与 \`DutSerial\`，不要通过任意 Regex 把旧 SN 解释为新型号。若旧码不携带型号，正式全自动跨工程就需要人工明确工程上下文；不能声称“扫码自动识别型号”。
- CRC 只能发现传输错误，**不能证明贴在板子上的 ECU 就是这个型号**。是否需要 ECU DID 二次核对，必须依据真实支持与生产策略确定。

**推荐下一步做法**：保留已实现 UT1 解析器作为试验候选，但不把它硬编码为唯一生产标签格式；以已知示例真实扫出结果、标签机器可读能力和编号发行规则，最终定 \`ModelCode/SN\` 取值、是否需要 HW、长度/校验。

## 4. 一个型号一个工程：生产扫码不再进行文件级配方选择

### 4.1 站点工程目录（研究最小方案）

\`InstalledModelProject\`：\`ModelCode\`、\`ProjectId\`、\`CanonicalManifestPath\`、\`ModelDeclaredInProject\`、\`LastVerifiedRevision/SHA\`、\`Enabled\`。由工程师安装/打开时注册和显式更新，不能从外部目录扫到类似文件就自动授权当成产品工程。

- \`ModelCode\` 全站点唯一对应一个当前有效工程，重复绑定必须先处理；
- 每次跨工程先核对目标路径、工程 ID/型号、文件 SHA、当前无运行、保存已完成/草稿可恢复；
- **当前同型号**：不弹窗，只准备当前工程；**已知异型号**：一次确认后切换并读取该工程固有的 DBC/TEST/固件/Flash/生产配置；**未知**：拒绝并指向工程安装/型号登记；
- 不做任意 Model+HW+WorkOrder+Recipe 的必填多级路由，不因当前测试者临时勾选了几个用例就改变生产执行范围；
- 工程切换应更新“当前工程”与所有 UI 的一致来源，不能只更改标签的模型名而仍使用上一工程的 Flash Job。

### 4.2 双模式不会互相误伤

| 行为 | 工程师/独立工具 | 正式生产 |
|---|---|---|
| 是否必须扫码 | 否 | 是 |
| 选运行范围 | 手动单步/用例/勾选范围 | 工程 TEST 树深度展开全部 Enabled |
| 是否每次自动烧录 | 否 | **是，含复检** |
| 触发启动 | 工程师使用自身运行入口 | 实体启动键有效边沿（待 HW 真机证明） |
| 资源 | 可用当前工程或独立工具临时文件 | 仅固定当前 ECU 工程的已发布快照 |
| 失败与数据 | 调试日志/诊断 | 本次真实检验失败/停止/无法核实均留历史，自动报告 |
| 记录性质 | Development/Diagnostic | Production Inspection；不得与研发统计混同 |

## 5. 生产状态机：已确认业务顺序与未证实硬件边界分开

\`\`\`text
WAIT_SCAN
  -> SCAN_COMPLETE/VALIDATE_MODEL_AND_SERIAL
      -> SAME_MODEL | OTHER_KNOWN_MODEL_CONFIRM_AND_SWITCH | REJECT
  -> IDENTITY_ACCEPTED / CREATE_INSPECTION_ID
  -> CLAMP_COMMAND_REQUESTED
  -> CLAMP_READY [需要可证明的到位判据；未知不得显示已到位]
  -> WAIT_START_EDGE [必须是压紧就绪后新收到的实体启动有效事件]
  -> STATION_PREFLIGHT / FREEZE_CONFIG_AND_ASSETS
  -> FLASH_RUNNING [每次检测新 FlashAttempt]
  -> FLASH_POSTCHECK [APP 恢复与目标版本证明分别判]
      -> failed/unknown: SKIP_TEST, preserve evidence
  -> TEST_RUNNING [唯一真实 TEST executor，全量 Enabled 顺序，失败由正式 TEST 逻辑处理]
  -> RECORD_FINALIZING / REPORT_PERSISTED
  -> CONFIGURED_FINISH / RELEASE_CLAMP_IF_CONDITIONS_MET
  -> RESULT_DISPLAYED / WAIT_SCAN [上一台结果保留至下一有效扫码]
\`\`\`

**物理验证缺口必须明确写给 DEVICE/FW**：真实启动键/停止键究竟接在哪个主控引脚、USB 如何上报？夹具 0x18/0x19 是动作请求还是有真实机械位置反馈？如果没有传感器，能够证明夹紧到位的是什么？上电已按住的启动键是否会误发？实体停止在 Flash 擦除过程中怎样传入 CancellationToken？用户没有授权 AI 假设存在 GPIO/USB 输入，故所有这些节点在软件设计中必须标记 \`HARDWARE_INPUT_NOT_VERIFIED\`，不能默认自动 PASS。

**故障原则**：普通 UI 编辑错误不等于设备危险电气状态；实际 FLASH 中断后 ECU 软件状态是否可运行是独立技术事实，不能无证据自动恢复或重复写入。FAIL 后夹具保持/释放尚未批准，不将脚本中的继续/清理语义替换成硬编码。

**站点恢复**：任一阶段出现错误，要记录处在哪一阶段、是否已发夹具请求/已接实体启动/已开始擦除、已有哪个 Run/FlashAttempt、是否已归档；清理与下一次扫码应受真实台架状态影响，不要遇错一律“自动安全归零”。

## 6. 批次/重复扫描/检验历史：严谨但不过度配置

至少定义：
- \`ScanEventId\`：一次真实扫描输入，有效/无效皆有设备事件结果；
- \`DutSerial\`：全局唯一实体号（需发行方确认唯一范围），不同次检测同一个 ID；
- \`InspectionId\`：扫码被站点接受后，本次单台检查身份；如果后续夹具/烧录失败，也应有本次记录；
- \`FlashAttemptId\`：本次烧录尝试，独立保存 JobSHA、阶段与结果（每次正式检测有一次新的尝试，不自动重试）；
- \`CaseAttemptId/RunId/StepOccurrenceId\`：实际 TEST 用例执行事实；多个用例归于同一 Inspection；
- \`ReportId\`：一次 Inspection 的单台报告/证据；
- \`ReleaseDecision\`：若存在正式出厂放行策略，另于 TEST PASS 与 Flash ACK，需明确质量规则。

**立即有效的用户原则**：同一 Serial 不因重复扫描/不同 HW 标签而变为新的实体；再次正式检测必须重新烧录；所有尝试独立报告，旧 PASS/FAIL 不被覆盖。  
**仍是研究而非政策**：首次合格、最终合格、复检统计、批次起止、首次已 PASS 再扫是否提示/限制、硬件身份 DID 是否必须通过，避免擅定为新工位弹窗。

生产不强制先创建工单/批次才能扫码；报表可按时间/操作班次或用户明确选择的报告范围生成，不将“报告范围”回写成设备运行许可。

## 7. 产品 UI 操作——把复杂性留在一次性配置，而不是每台扫描

**生产主界面**默认大面积显示“当前型号 / 扫码设备状态 / 夹具 / 等待实体开始 / FLASH 阶段进度 / TEST 当前用例 / 结果”。无工程时可等待扫描到合法型号后查已安装项目。上一台结果在下一个合法扫码之前保持。运行中不可覆盖 DUT 编号，不能隐藏烧录失败细节；有“查看本次 CAN/阶段日志”和“历史”但正常流程不要求打开。

**工程配置**提供“生产设置”集中入口，内容仅：该工程型号/允许的条码格式、固定 Flash Profile/当前固件、安全算法资源、必要工位设置/报告基础项。DBC/TEST 不在这里重复建立新选择表。工程师编辑自动保存；保存草稿不立即影响正在运行的已冻结版本。

**扫码设备设置**：设备类型/实例/采集测试与在线状态；不是条码规则/固件设置的混合大表。

**独立工具**：DBC、手动台架、UDS、Flash、波形无工程可启动，支持使用独立文件/临时配置；若选择编辑工程资源，则明显展示来源与保存归属。独立 Flash 参数不得暗改工程生产配置。

## 8. 接入最新 R1 的差距（源码事实，非新产品批准）

| 精确代码 | 当前 R1 能力 | 按新基线整改 |
|---|---|---|
| \`MainWindow.ScannerEntry_*\` / \`KeyboardWedgeCapture\` | 需要编辑框焦点的键盘文本+Enter | 真设备识别适配器 + 生产页面接收，单一可验证完整 ScanEvent |
| \`SerialBarcodeCapture\` | 无实际 COM 打开，接口抽象 | 由实物类型确认后实现 COM/USB POS/HID，半包/双尾缀/断连验证 |
| \`Ut1BarcodeCodec\` | CRC + Product/HW/SN 候选 | 编码格式仍候审；按工程 ModelCode 识别，不强迫 ProductVariant/配方层 |
| \`ProjectScanMatcher\` / \`ScanRecipePreparationService\` | 依当前工程扫描、多层 Recipe，冻结 TEST 内 CanTool XML Job，结果可应用人工 TEST 选择 | 迁移成 \`ModelCode→InstalledProject\` 查找/切工程 + 该工程**当前生产资源**检查，不再对生产扫描选择 TEST 子集 |
| \`ProjectRunSelectionState\` | 开发临时勾选用例 | 生产忽略临时勾选，读取工程有序 Enabled 树 |
| \`ProductionWorkflow\` | FLASH→TEST 的接口雏形，但未接真实现场且 Flash 阶段会记 \`RunFactKind.MockObservation\` | 接真正 \`ProductionStageExecutor\`、Flash/TEST 原始证据事实，绝不把 MockObservation 用作正式检验通过 |
| \`RealTestWorkbenchService\` | TEST Development/Diagnostic 执行与 RECORD 后端 | 需要统一 Production Inspection/CaseAttempt 映射，不复制一个新真实 TEST 执行器 |

## 9. 最低验证矩阵

1. 扫码枪实际 PnP/USB/COM 身份记录，三次连续扫相同码、一维/二维真实符号、Enter/CRLF、误扫/断线、人工键盘粘贴都能正确分类。
2. 扫当前型号无弹窗；扫另一个已安装型号只确认一次，完整切换工程 DBC/TEST/Flash；未知/双绑定/丢工程拒绝。
3. SCAN+夹具接线必须具备真实压紧命令及与状态语义一致的实物到位证据；没有证据时显示“夹具就绪未证实”，不能伪造实体启动事件。
4. 实体键只在 WAIT_START_EDGE 生效；在上电已按住/连续抖动/重复触发/Flash 期间按键不造成双烧录。
5. 每次完整检测（同号复检亦然）记录一次新 FlashAttempt→成功才 TEST 全部 Enabled→报告；失败不可隐式跳过后续烧录或伪造 PASS。
6. 运行中重扫拒绝且不排队，旧结果直到下一合法码才更新；数据恢复/无工程研发操作不中断。
7. 完整记录、原始 CAN 底本与简单单台报告、批量统计以同一 Inspection/Serial 索引，重复编号不会增加独立新 ECU 件数。

## 10. 研究状态与外部参考

**已经确认**：产品任务与流程由现行 PM 基线约束；目前 R1 有匹配候选源码但与新产线业务不一致。  
**尚待专业审定**：条码字节格式、设备模式、已安装工程目录 Schema、具体跨工程切换机制、实体键/夹具反馈、统计计算公式与 release policy。  
**Windows/真机状态**：本轮是研究，不提交 R1 产品代码；没有 Build 或现场 PASS。

NI TestStand Process Model:
https://www.ni.com/docs/en-US/bundle/teststand/page/process-models.html

NI Process Model customization:
https://www.ni.com/en/support/documentation/supplemental/08/teststand-process-model-development-and-customization.html

NI operator UI:
https://www.ni.com/en/support/documentation/supplemental/08/teststand-user-interface-development-best-practices.html

Microsoft POS scanner:
https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/pos/barcodescanner-configure

Microsoft Raw Input:
https://learn.microsoft.com/en-us/windows/win32/inputdev/about-raw-input

Microsoft SerialPort:
https://learn.microsoft.com/en-us/dotnet/api/system.io.ports.serialport.datareceived
