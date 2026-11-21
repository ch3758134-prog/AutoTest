# AutoTest R1-SCAN S-05｜系统配置、操作产品方案与最小实测路径 v0.2

> 本设计是 2026-11-21 NI 对标后的**候审产品建议**，与 [S-04](S-04_NI_TESTSTAND_SYSTEM_RESEARCH_20261121.md) 配套；**S-01 的强制“每编号精确绑定”P0 建议被本报告修正**。用户已批准的是“外部唯一 DUT 编号”“匹配关系与条码采集独立”“研发可无扫码执行”“重复检测不重复计新增”；**四种关联模式、具体产品配置与出厂放行政策仍需评审**。
>
> R1 代码调查：`ProjectScanMatcher` 当前只有纯匹配状态；`RealTestWorkbenchService` 执行真实 TEST；R1 HEAD 最近已到 `02f18c65e5ba6038da10067c6285450de6cce911`。本稿不要求代码直接把 research 旧树覆盖 R1。

## 一、先决定“扫码识别的对象”和“配方从哪里来”

从现场操作者角度可以用两句话解释所有模式：
- **识别哪一台**：扫描枪获得唯一 DUT 编号，形成 `DutSerial`，仅用作身份与结果归属。
- **按什么方案测试**：来源可以是当前已选测试任务、条码里的产品编码、外部工单/批次或外部逐号档案中的任一种；经**独立配置关系**找到已发布 Recipe/实际 TestProject 与配置，完成预检后才调用 TEST。

**对于最初 EPB 出厂检测**，若同一批 DUT 配置一致，最简单实用流程是：

```text
工程管理员一次性准备:
  创建/导入 TestProgram + TestProject + TCF/DBC/Controller/TBC + Flash(若需)
  → 保存已批准的“功能检测方案 A”
  → 配置“编号规则 A”（允许按现有实际编号简单校验）
  → 建立“当前工位任务 A → 检测方案 A”显式关联
  → 选择生产/任务运行模式（不要求逐 SN 导入）

每只 DUT:
  装夹准备 → 扫码外部 SN → 展示“SN + 当前任务 + 型号 + 本次检测项目”
  → 设备/码率确认 → 用户确认开始 → 原 R1 TEST 执行、自动记录
  → PASS/FAIL/UNKNOWN 和报告 → 核对下一只
```

不允许因为“当前任务 A”存在就宣称已经**核实**该 SN 属于该型号；是否正式放行需外部/实物型号核验策略。若用户以后取得合法的“编号→型号/批次”的权威清单，可升级为严格核对而不改变 TEST 执行器。

## 二、建议的配置位置和操作层级（不把所有设置挤在扫码浮窗）

### 2.1 系统设置 → 扫码设备（本机工位配置）

设备设置**只管怎样正确取得输入**，不配置 DBC 或测试程序。

| 字段 | 默认建议 | 含义/限制 |
| --- | --- | --- |
| 输入方式 | `USB 键盘模拟` | 现场确认确为此类设备；未确认之前来源标记为 `KeyboardFieldUnverified` |
| 输入目标 | `仅扫码专用输入框` | 不全局 Hook，不拦截 TEST/DBC 编辑器键盘 |
| 结束符 | `Enter`，识别 CR/LF/CRLF | 必须实际枪扫一个样本确认；没有完整结束则不匹配 |
| 最大输入长度 | 由已发布规则/设备安全限制决定（系统可设上限） | 不能静默截断 |
| 前缀/后缀处理 | 默认仅剔除配置规定的传输结束符 | 不擅自 Trim/改大小写/移除业务字符 |
| 字符集/键盘布局 | 真实输入验证 | 验证符号、数字、字母、前导 0、大小写 |
| 输入超时/失焦/连扫 | 完整输入校验、清空半包、工位忙拒绝 | 不排队下一只，不串接两个 SN |
| 手工输入/粘贴 | 调试允许；生产是否允许代扫需要单独权限策略 | 从记录中能区分 `ManualTyped/Paste`，不伪称物理枪已验证 |
| 设备标识（未来） | 仅支持实际可验证的 HID POS/串口标识 | keyboard wedge 的普通 TextInput 不是设备证明 |

**扫码枪真机首次配置步骤**：
1. 确认扫码枪实际型号、USB/串口连接及制造商编程指南。
2. 若为 USB 键盘模拟，按厂家手册设置 HID Keyboard、允许的条码类型、匹配的键盘布局和 Enter 结束符；不同厂商可能用“扫描配置条码”而非软件命令配置。
3. 在 Windows 记事本扫描**已存在的真实样本编号**，核对打印字符串、前导 0、大小写、结束符和是否意外 Tab 换焦点。
4. 在 AutoTest“设备输入测试”区域验证：只做采集，不操作 CAN/IGN/夹具/FLASH；显示完整原文、源模式/时间、结束符、是否成功、输入缺口；失焦和连扫分别验证。
5. 若要求区分物理扫描器与键盘，先确认 HID POS/特定厂商 SDK 支持；不通过“每秒 N 个字符”伪认证。

参照：
- https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/pos/barcodescanner-configure
- https://docs.zebra.com/us/en/scanners/general/sp72-prg/c-sp72-getting-started/c-sp72-host-interfaces.html

### 2.2 工程资源 → 编号规则（只判别编号）

独立工程资源 `IdentityRuleSet`，允许编辑草稿、真实样本测试、发布新版本、回滚查看旧版本。

配置字段：规则名称/Id、允许字符/长度/正则（可选）、可选校验位、可选命名字段（serial/productCode/lotCode）、唯一身份组合方法、是否允许归一化、Revision/SHA、发布/停用状态。实测预览对照表须显示“输入原文→解析序列号→提取的产品码/批号→合法/拒绝原因”。同一个编号不应因暗中大小写处理或去前导零与另一编号冲突。

**条码不含型号不成问题**：编号规则只验证/解析 `serial`，不必人为把 Regex 配成必有 model/customer 才能扫码。型号在“当前任务/产品映射/外部档案”层解析。

### 2.3 工程资源 → 测试方案（独立 Recipe）

用户先像普通工程操作一样保存并验证：
- 测试程序（准备/测试/清理）或测试项目（具有稳定 `ItemId` 的有序用例集合和本次选择子集）；
- 测试系统配置 TCF，固定 DBC + ECU 功能 Controller/20 Key；
- 工位配置 TBC（现硬件 ECU CAN / KEY CAN 端口角色固定，不做虚构重映射）；
- ECU CAN 目标 kbit/s（0x42/0x43 实体读回时校验）；KEY CAN 仍按固件固定值；
- 若需自动烧录，TEST 本身已有启用的 `Flash` 步骤，实际 XML Profile/HEX/安全文件及一致 SHA、目标 ECU/波特率完全满足单次烧录约束；**Recipe 不可以“只勾 RequiresFlash”就绕过 TEST 动态生成写入**；
- 报告/判定/正式放行是否启用分开；Recipe 发布时固定所有资源 Revision/SHA。
- 可预留同一 TestProgram 复用不同限值/参数 Profile 的能力，但 R1 不为模拟 NI Property Loader 而另建第二套测试程序模型。

**重要交互**：选择工程、保存测试程序、配置 TCF 与烧录依然走原工程文件；“扫码绑定设置”只是**引用已保存方案**，不得复制完整 DBC/测试用例编辑器进扫码设置页。

### 2.4 工程资源 → 配置关联（独立 TestAssignment）

`TestAssignment` 不是 Barcode 规则，也不是 Recipe 内嵌行，而是可维护、版本化、可发布的独立绑定资源。配置界面先选择一种**明确模式**：

| ResolutionMode | 绑定键 | 绑定值 | 必要证据/约束 |
| --- | --- | --- | --- |
| `CurrentTask` | `ActiveTaskId`（操作者显式选择） | `RecipeId/Revision/SHA` | 当前任务一对一选择；不能仅依据任意 SN 宣称识别型号；需任务绑定及可选实物核验 |
| `ProductCode` | 规则解析出的经外部规范确认的 `ProductCode` | 同上 | 未知前缀不进行“其他产品默认配方”；多命中拒绝 |
| `WorkOrder` | 已选择/扫码的 `WorkOrderId`、`ProductVariant` | 同上 | 可选权威 SN 成员表：强校验和弱校验要区分；不同工单冲突阻断 |
| `ExactDut` | `ExternalDutKey` | 同上 | 来自外部编号档案，支持导入/检索/启停/撤销/生效区间、唯一冲突拒绝 |

**不允许混合猜测**：一只 DUT 只能根据此生产任务已发布的 `ResolutionMode` 确定一份方案，不“先找 SN，找不到退化型号前缀，再退化默认任务”。如果确需复合策略，必须经业务审批明确严格优先/交叉检查，且结果足够可解释。

### 2.5 生产工作任务（工位操作状态，不是每次扫码自动派号）

推荐轻量 `ActiveTestTask` 记录：
`TaskId`、`ProjectPin`、`RecipePin`、`ResolutionMode`、`ExpectedProductVariant`、`OptionalWorkOrderId/LotId`、`OperatorId`、`StationId`、`Publication/Review`、`StartedUtc`、`TaskFingerprint`。

对于模式 A：准备工作任务一次，连续扫描多只 DUT；退出/更改任务需要确认，工作任务与已完成记录不一起修改。对于模式 C：导入外部工单清单后切换当前任务。对于模式 D：可以不固定具体型号，但必须能从权威档案唯一查出且当前工位支持。

## 三、扫码界面显示和用户任务（UI 专项按此设计，不在此重画）

**默认研发模式**：主界面打开当前工程→TEST 编辑器→直接运行单步/用例/勾选范围；没有扫码配置也完全可用。运行记录为 `Development` / `Diagnostic`，绝不进入“新增出厂件数”。

**轻量生产模式**：主窗口显示“当前任务/方案 + 扫码框 + DUT 状态/匹配详情 + 预检/开始”。点击匹配详情可以查看当前任务和完整有序用例列表，不应覆盖主工程树或打开多个无法管理的编辑窗口。

示例界面字段（纯示例，不代表现有项目真的存在这些配置）：

```text
模式：生产任务          工程：HydEPB 示例工程
当前任务：功能测试-A     解析模式：CurrentTask
工作单：示例-WO-01       测试方案：示例-Recipe-A@r3

扫码状态：等待扫码
扫码编号：[ SN000123________________ ]      [清空] [查看规则]
匹配结果：外部编号 SN000123
  关联来源：当前任务 → Recipe-A@r3
  产品声明：HydEPB A／硬件 r2（来自任务，非实体读回验证）
  本次用例：1. 上电检查  2. 功能判定  3. 清理验证
  TCF/DBC/KEY：已关联并固定 SHA
  ECU CAN：目标 500k / 当前读回 500k
  烧录计划：不烧录     RECORD：可写／复检状态：首次检测候选
预检：PASS / BLOCKED / NEEDS_CONFIRMATION
[重新扫码] [查看完整运行计划] [运行前检查] [确认开始]
```

任何字段缺失不能用“匹配成功”掩盖，尤其要区分：
- `InputReceived`：确实读取到文本；
- `IdentityParsed`：编号格式合法；
- `AssignmentResolved`：唯一找到已发布任务→方案；
- `ProductAssurance`：受限声明/外部档案核验/ECU 非破坏性读回交叉核验；
- `PlanPrepared`：资源/用例/配置/设备和码率全部预检；
- `ReadyToRun`：操作员明确确认、安全授权就绪；
- `TestVerdict` 与 `ReleaseStatus`：实际 TEST/正式出厂不同维度。

**扫码成功不立即执行**。操作者必须看清 DUT 编号、产品、方案、目标速率、是否烧录和复检状态；点击开始才复用原 TEST。可支持专用生产视图精简操作，但不能把准备/配置变成多个难以关闭的独立编辑弹窗。

## 四、数据关系/接口建议（无新增执行器）

```text
ScanCapture {EventId, RawText/Hash, Origin, Utc, CompletedByTerminator}
  -> IdentityParse {ExternalDutKey, ParsedProductCode?, RuleId, RulePin}
  -> AssignmentResolution {
       ResolutionMode, SourceKey, BindingPin/RowId?,
       DeclaredProductVariant, ProductAssurance,
       RecipePin, Explanation/Conflicts
     }
  -> FrozenUutRunPlan {
       ExternalDutKey, InspectionSessionId,
       ActiveTaskPin?, WorkOrderId?, LotId?,
       ProjectPin, RecipePin, TestProjectPin?,
       OrderedCaseEntries[{EntryId, TestProgramPin, CompiledSHA}],
       Tcf/Dbc/Controller/TbcPins, RequiredEcuCanKbps,
       FlashJobPins?, PolicyPins?, CanonicalSHA
     }
  -> StationPreflight (no physical writes from ScanResolve)
  -> Existing RealTestWorkbenchService (explicit operator safety authorizations)
  -> Actual RunRecord(s) and Evidence via RealTestRecordSession
  -> InspectionSession ↔ actual CaseRunId(s) ↔ reports
  -> Independent release/retest event if production policy later approved
```

`InspectionSessionId` 表示同一只 DUT 一次工位检查，`CaseRunId` 表示真实 R1 的每个已执行 TEST 用例的 RunId；`LotId` 表示外部生产批号，互不混同。已经收到一次扫描但还没有开始运行，不应新建“检测通过”的 RECORD。允许重复扫码不新增出厂，但按复检策略产生独立 `InspectionSessionId` 和每用例新 RunId。

目前 `TestProjectWorkbenchViewModel` 的勾选状态是工程师临时 UI 状态，不是生产任务授权；新 `FrozenUutRunPlan` 以确定的 Project `ItemId` 顺序冻结，不得覆盖 `Enabled/RunSelected`。TCF/TBC 应由 ScopedRunContext 显式传入并再次校验，避免扫描在全局 UI 切换时跑错配置。

## 五、首版与后续边界：按最小真实闭环交付，而不是先建 MES

| 阶段 | 必做 | 明确不做 | 完成证据 |
| --- | --- | --- | --- |
| **S-M0 真实设备输入** | 现有 keyboard wedge 专用输入区＋真实枪 CRLF/焦点/输入来源、原样保存、阻断连扫 | 不做默认全局 Hook，不宣称验证了物理枪身份 | 真实型号/条码/终止符操作日志 |
| **S-M1 固定任务映射** | 选一个已保存的任务/Recipe；扫码外部编号→显示方案/用例、TCF/DBC/码率/Flash、PlanSHA；开发入口不变 | 不强制预先逐 SN 导入；不打通正式出厂自动放行 | 与 R1 真工程文件匹配，歧义/失效拒绝 |
| **S-M2 真实 TEST 接线** | 操作员确认后按有序范围运行 `RealTestWorkbenchService`，保留 Flash 独立授权，每用例正式 RunId/原始证据 | 不造第二 TEST/FLASH，引入模式 B/C/D 不作为基础测试前置 | 真实 ECU 执行、报告复盘、断连安全停止 |
| **S-M3 生产身份与统计合同** | 与 RECORD/项目管理确认外部身份验证等级、复检、首次放行的真正定义，持久关联会话→用例 Run | 不把 `COUNT(DISTINCT dut_id)` 当出厂量；不因扫码成功自动批准放行 | 同编号复检、成功/失败、放行幂等可查询 |
| **S-M4 复杂混线与生产任务** | 按真实业务数据扩展 `ProductCode/WorkOrder/ExactDut` 模式、成员表导入与撤销 | 不在没有业务源的情况下凭 Regex 推断 BOM/型号/固件 | 多产品正/负样例和带版本的历史报告 |

**兼容前述 S-03**：原 P0-A 的“逐 SN 绑定表”作为 M4/严格生产候选；M1 优先以当前任务→Recipe 的已发布关系实现最小真实操作路径；M2 调用原 TEST。若用户确认现场要求逐编号严审，再调整顺序，但**不要默认为全部用户必须逐个登记编号才能测试**。

## 六、验证场景与要向实际业务核实的最低数据

| 场景 | 必须结果 |
| --- | --- |
| 扫描合法编号（不含型号），当前任务明确且合法 | 只用任务选择 Recipe；显示“型号来自任务声明”，不能伪称条码自动识别型号 |
| 扫描合法编号但当前无任务/无来源映射 | 研发可手选 TEST；生产自动配置阻断，不猜默认 |
| 两个已发布任务同样可用、没有明确当前任务 | 生产阻断歧义，明确选任务 |
| 条码有可用产品码，映射不存在或两条有效映射冲突 | 不默认匹配任何 Recipe |
| 码率设置与工位 0x43 读回不符 | 等待人工应用/读回，绝不因扫描立即设置 0x42 |
| 已批准 Recipe 无 Flash，TEST 出现启用 Flash 步骤 | 预检拒绝；不能扫码后临时插入/忽略危险动作 |
| 已批准 Recipe 需要 Flash 但 Security/XML/HEX SHA 错 | 预检拒绝；不以 `RequiresFlash=true` 视为授权 |
| R1 中单步/用例、勾选范围不扫码直接测试 | 正常经过原实体安全门禁执行；RunMode=Development/Diagnostic |
| 同号连续扫码、运行中再扫第二台 | 忙拒绝不排队；不把同一输入计新测试，也不覆盖当前 DUT |
| 同号再检测 / 历史 PASS 后复检 | 新 InspectionSession/各自新 RunId；历史证据不变，不再计新增出厂 |
| 修改规则、绑定、项目、DBC、固件或 USB epoch 后启动 | 已冻结计划作废；重新匹配/预检 |
| 实物枪输入带 Tab/CRLF、失焦/IME/粘贴 | 不拼接、不丢字符，无法确证源时如实标来源 |

**尚需真实产品输入信息（研究不擅自猜）**：
- 真实已存在外部编号是否仅是随机唯一 SN，还是包含客户/产品/硬件版本字段？提供至少 3 个无敏感信息的示例；
- 现场是固定同型号连续检测，还是多个型号交替？是否存在工单/生产批次？业务是否已经给出 SN→产品的清单；
- 是否需要把“人工核对型号”升级为 ECU DID 等实际读回，且 Flash 前具体必须核对哪些字段；若读不出型号应如何处理；
- 什么才算正式“出厂新增”和“返修复检”？目前只是已批准的不重复计新增，不自动推导完整放行权限模型。

**这些数据只决定后续启用何种可选绑定方式，不影响 R1 先实现无扫码开发测试或固定任务的扫码采集与真实 TEST 最小路径**。

## 七、明确结论

- NI 证明值得借鉴的是**身份识别与 TEST 序列分层、扫码可以换不同产品方案或装载参数、SinglePass/生产连续入口分离，以及序列号/产品编码/批次/工位/结果分别管理**；不是要求每只编号必须有一行复杂关系库。
- AutoTest 的最佳产品方向是**“运行任务 + 已发布配置绑定 + 编号采集 + 预检计划 + 唯一真实执行器 + 记录追溯”**。
- 正式绑定模式由真实业务输入决定；首版默认固定任务可支持单一产品连续检测，严格 SN/型号映射作为可扩展能力。不在没有来源证据时凭条码猜测硬件版本，不使开发运行受扫描枪配置阻断。
- **交付仍为研究/候审设计**；R1/项目管理/RECORD 与用户认可之后，才能实施生产 Schema、身份确认和出厂政策，且必须通过真实 scanner/ECU 验收。

---

> **2026-11-21 用户补充后续说明**：用户表示“编码可以我们自己来设计”。本文件设计的四种路由仍可用，但“随机唯一 SN 不含产品信息”和“先以 CurrentTask 固定型号为默认”是**旧前提下的最短路径建议**，对新标签不再是唯一首选。请先阅读 [S-07 自定义 UT1 编码候选](S-07_CUSTOM_DUT_BARCODE_ENCODING_V01_20261121.md)：新标签可以包含 ProductCode/HW + 独立序列号，通过已发布 ProductVariant→Recipe 映射选测试方案；当前工位任务用于限定允许产品，防止错型混入。仍不能因扫码或 CRC 自动放行/烧录。
