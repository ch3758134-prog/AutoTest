# AutoTest UI V4｜FLASH × SCAN × WAVE 成熟产品专项研究与设计验收

日期：2026-11-21  
归属：AutoTest UI 独立研究；设计验收候选（Gate-Design）  
交付：`AutoTest_绿通HydEPB_产品体验版_v4_20261121.html`

## 一、研究基线与结论

本轮不是重新设计 CAN/UDS/Flash/Wave 算法，也不是把 R1 原 XAML 搬进原型。真实业务能力参考现有 R1/FLASH/SCAN/WAVE 的源码与研究合同；新 UI 独立决定导航、信息层级、工作任务和工具内部交互。原型继续使用绿通 HydEPB 的真实 DBC 内容和此前确认的按键/报文配置，但所有界面侧配置只作交互体验，**不能自动变为已发布的生产测试配方或实际设备执行**。

研究直接依据（原始 GitHub 分支）：

- `research/r1-scan-configuration-20261120/docs/r1-scan/S-07_CUSTOM_DUT_BARCODE_ENCODING_V01_20261121.md`
- `research/r1-scan-configuration-20261120/docs/r1-scan/S-08_END_TO_END_SCAN_PRODUCT_SYSTEM_RESEARCH_20261121.md`
- `research/r1-scan-configuration-20261120/docs/r1-scan/S-09_SCAN_FLASH_PRODUCTION_RECIPE_INTEGRATION_20261121.md`
- `research/r1-wave-judgement-20261120/docs/r1-wave/W-05_TIME_QUALITY_AND_JUDGEMENT_SEMANTICS_20261121.md`
- `research/r1-wave-judgement-20261120/docs/r1-wave/W-08_WAVE_LIMITS_MSA_AND_REFERENCE_QUALIFICATION_20261121.md`
- `research/r1-wave-judgement-20261120/docs/r1-wave/W-09_LIVE_WAVE_TOOL_CAPTURE_WORKBENCH_20261121.md`
- `feature/flash-lab-20261114/src/AutoTest.FlashLab/Shell/FlashLabWindow.xaml`
- `feature/flash-lab-20261114/src/AutoTest.FlashLab/Presentation/FlashLabViewModel.cs`

## 二、FLASH｜配置、烧录与放行不能混成一个成功灯

**源码事实**：FlashLab 的正式过程为十阶段：Boot/APP 模式探测、Boot 编程会话、Seed/Key 解锁、擦除 Flash、RequestDownload(0x34)、TransferData(0x36)、退出传输(0x37)、ECU 校验、ECU 复位、APP 启动/版本待核验。以实际 ECU 报文和确认字节更新状态；Boot 复位确认不代表 APP 启动，APP 会话恢复又不等于正确的软件版本。

**UI V4**：左栏为 CanTool Configuration.xml、固件 HEX/S19/BIN、SafetyFile.dll 和 ECU Profile；中央有十阶段状态与进度条、传输字节信息、独立结果（UDS 编程／APP 恢复／软件版本／出厂放行）；右栏专门展示 TX/RX 的时间、CAN ID、DLC、完整 HEX 数据和结果，允许导入既有真实日志分析。没有真实 CAN/USB 时进度显示 0%，没有启动伪烧录。

**后续真正集成必须补的约束**：源文件/安全库/受支持 ECU 配置的 SHA 与指纹冻结；R1 正式 TEST 中唯一的 Flash 步骤；显式安全授权；断开或取消之后的 `FLASH_UNKNOWN / RECOVERY_REQUIRED` 而不是自动重烧；版号独立 DID/合同验证。扫码匹配不能替代这些门禁。候选 V4 还没有真实运行和安全授权 UI 接线。

## 三、SCAN｜扫码设备配置与 DUT 测试方案匹配是两个责任域

**研究事实**：唯一编号由外部产生。候选条码格式是 `UT1-{ProductCode}-{HardwareCode}-{Serial}-{CRC16-CCITT-FALSE}`；标准向量 `UT1-EPBA-H02-000000123456-4372` 来自研究文档，**不是已发行的真实生产标签**。Serial 按字符串保留前导零。扫码只是识别入口，不能靠编号前缀、目录最新 HEX 或扫码本身触发工位动作。

**UI V4**：主窗口生产任务分为 `生产操作 / 产品与方案 / 扫码设备 / 记录`。产品变型和硬件版与当前生产任务一起路由到*草稿中的*测试方案，显示匹配与不匹配原因；支持 UT1 CRC 校验、错误型号阻断和重复事件区分。扫码设备原始测试区与生产匹配区分开。生产测试要已发布的配置和真实设备才能解锁。

**重要对象必须区别**：`ScanEvent` 只是一次识别事件；`InspectionSession` 是一次获准的真实 DUT 检测；`CaseRunId` 由 R1 实际执行器产生；`ReleaseDecision` 是正式出厂放行。重复扫码不得新增检测，也不得新增首次出厂量。

**后续接线需要**：真实 HID/COM 枚举实测；已批准 ProductVariant/ProductionRecipe/Binding 的版本、SHA、当前任务与工单；RunPlan 的冻结及全部资源指纹；FlashPolicy `NoFlash` / `FlashRequired`；生产释放权限及 RECORD 的幂等合同。V4 页面草稿没有发布权限、不能冒充真实生产生产记录。

## 四、WAVE｜采集、参考、测量质量与判定分层

**研究事实**：WAVE 是独立采集与分析工具，真实源仍来自唯一 USB/CAN/DBC 链。离散枚举/布尔应画阶梯曲线或状态轨，不可平滑出不存在的中间状态。真正的响应时间必须明确 host-observed 与物理事件的差别；没有已标定时序上界不能随意判 PASS。不能因“CSV 两条线形似”通过生产检测。

**UI V4**：工程当前 DBC 信号选择、参考与测量 CSV 导入、两层曲线叠加、离散阶梯/数值曲线切换、缩放、双游标 Δt；显示样本数、观察时窗、相邻采样中位数/p95/最大 Gap 和部分数据问题。非法/非递增时间行会被拒绝。参考候选状态、时间可信度和正式判定清楚分开，CSV 文件不自动晋升为已批准生产参考。

**重点改变**：取消一刀切默认 `0.05` 容差、`95%` 覆盖率。要求由工程配置提供触发来源、响应时限、保持时长、最大样本年龄、最大缺帧间隔、参考批准范围与算法版本。数据不足是 `INCONCLUSIVE`，不是 PASS 或产品 FAIL；运行中采集或真正的 `APP`/手动动作仍需接到 R1 真实链路。

## 五、产品信息架构与操作链

- 自动测试、工程资源与生产任务在**同一主工作区**；工具 DBC / UDS / FLASH / WAVE / 手动台架为主窗口拥有的独立窗口，不提供独立 CAN 工具。
- 研发：工程→用例/步骤→DBC 资源选择→参数编辑→保存→正式预检/真实执行→RECORD；扫码设置缺失不得卡住研发。
- 生产：先批准产品与任务→发行测试与烧录配置→扫描编号→解释匹配→冻结 Plan→只读预检→需要 Flash 时单独授权→R1 TEST 执行→分别形成 Flash/APP/版本/功能/放行结果。
- 波形：选信号→实时监视→受控录制与触发→质量检验→参考候选→审核批准→测试引用→报告可复核来源。

## 六、验收清单与诚实的限制

| 交互/检查 | 本轮状态 | 禁止误称 |
|---|---|---|
| UT1 CRC 有效与无效、Product/HW 冲突 | 浏览器交互已测 | 真实扫码枪来源认证 |
| 同编号重复识别与正式检测统计分离 | 浏览器交互已测 | 实际产线首件放行逻辑已接线 |
| FLASH 10 阶段、进度 0 空态、CAN 日志布局 | 浏览器交互已测 | 已真实烧录／已接 R1 编程流程 |
| 三文件路径/存在性检查，日志只读导入 | 浏览器交互已测 | 可用于真实 ECU 擦除授权 |
| WAVE 真实 CSV 导入、双游标/缩放、质量字段、非法时间序列拒绝 | 浏览器交互已测 | CAN 真采样、时间精度已标定 |
| 已批准生产参考与正式 PASS | 明确阻断或空态 | 已验证质量/参考/判定 |
| 主窗与工具窗口联动 | HTML 中通过关闭事件与 owner 监视做体验模拟 | 浏览器弹窗机制相当于 WPF Owner 实体生命周期 |

本次交付依旧是 **Gate-Design 可操作 HTML 原型**，不是正式 WPF 构建或现场测试。硬件发送/波形录制/扫码设备枚举/Flash 传输/RECORD 写入仍由现有真实后端和后续审查实现。正式现场准出必须分别通过 Windows 构建、实际设备通信、错误与断电恢复、证据完整性和用户验收。