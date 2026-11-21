# AutoTest R1-SCAN S-02｜研发/生产用户流程、扫码输入与重复复检策略

**任务**：Issue #72；状态 `UX_AND_POLICY_SPECIFIED / UI_NOT_IMPLEMENTED / FIELD_NOT_VERIFIED`。此文件定义**业务动作、可见状态、错误处理**，不是 WPF 布局稿；视觉和独立窗口仍归 UI 专项。

## 1. 两种运行模式——绝对分离

| 项 | 研发/调试模式（默认） | 生产扫码模式（显式启用） |
| --- | --- | --- |
| 入口 | 打开工程→测试用例/项目→直接运行或单步 | 明确选择“生产扫码”，显示当前已发布规则、绑定表、配方、目标工程 |
| DUT 编号 | 可留空；可手工填写以便查日志，来源标明未验证 | 必须有外部产生的编号和**单一有效绑定**；不得自动派号 |
| 运行范围 | 工程师本次临时勾选、单步/用例/项目可运行 | 只使用**发布配方冻结的有序项目 EntryId 子集**，不采信编辑器临时勾选 |
| TCF/DBC/KEY | 工程师手动选择或编辑器已确定的资源 | 从绑定配方固定到 SHA 并与所有启用步骤一致性检查 |
| CAN 速率 | 工程师选择→明确应用与读回 | 匹配计划得目标码率→操作员确认应用与读回；只有匹配才可 Ready |
| FLASH | 配置了合法 Flash 步骤时仍须实体 SHA 授权 | 除满足配方绑定和唯一 Flash 限制外，**同样独立人工完整指纹授权** |
| 结果 | `Development` 或 `Diagnostic`；不可加入新增出厂数量 | 正式匹配证明+批次关联+真实 `Production` 记录；**需独立出厂放行判定** |
| 是否依赖扫描枪 | **完全不依赖**；扫描模块配置不完善也可以执行 TEST | 必须通过对应生产身份准入；手工录入/粘贴不可默认冒充扫描枪 |
| 重复运行 | 正常调试；每次新 RunId | 同 DUT 可按受控复检策略再次执行，每次新 AttemptId/BatchId/RunId，**不新增第二个出厂件** |

**强不变量**：在研发模式，`ScanRuleSet` 不存在/扫描器未连接/码格式不匹配，**不得**被放进 `TestEditorViewModel.RunRealAsync`、`TestProjectWorkbenchViewModel.RunSelectedAsync` 的额外前置条件。执行器仍保留真实 USB/物理操作/FLASH 安全检查，不等于允许离线 Mock。

## 2. 生产准备阶段（配置员/工程师）

1. 在当前 ECU 工程创建或打开 **SCAN 编号规则**：用客户真实编号样例编辑、验证、预览命中字段；规则必须显式发布并具有 Revision/SHA；拒绝多命中、短码、控制字符。
2. 新建**编号绑定表**资源：导入客户权威的 DUT 编号清单或逐条编辑 `DutKey→RecipeRef`。提供搜索、编辑、冲突检测、停用/撤销、版本说明、有效时间；导入动作不可跳过完整校验。不要将此表内嵌在正则规则里；导入 CSV 文件本身不是运行依赖。
3. 为可重复使用的方案维护 **Production Recipe**：选择 **一个测试项目**，在项目已保存的 EntryId 中选择执行子集与顺序（或者指定单个测试程序）；明确 TCF、其 DBC/Controller 按键资源、TBC、ECU CAN 波特率、是否烧录、绑定的真实 Flash profile/firmware/security、报告与复检规则。
4. 编辑器逐层显示依赖关系：项目 → 项目条目 → 测试程序 → 启用步骤映射 → TCF 的 DBC/KEY → 固定工位 → Flash。提示 `未发布/旧 Revision/无唯一绑定/缺少真实 Flash job`；只有所有 P0 字段有效才可发布。
5. 显式“发布”规则/绑定/配方：提交新的不可变快照，保留旧快照用于历史运行重建。任何后续修改只能生成新 revision。生产开始时必须显示本机加载的 **规则版本、绑定表版本、配方版本、工程版本**，不是仅显示文件名。
6. 生产工位连接真实 USB 主控、校验设备身份和当前 epoch。尚未准备就绪可保持研发模式，但不能以未应用的生产配置“开始生产”。

### 首版最短生产路径

```text
已批准工程 + 发布编号规则 + 发布独立编号绑定表 + 发布配方
  → 显式切换生产扫码模式（可随时退出到研发）
  → 扫码输入外部唯一编号
  → 解析并查唯一绑定、冻结 TestProject EntryId/TCF/DBC/KEY/码率/Flash
  → 显示该 DUT 的实际“将执行什么”和是否是复检
  → 用户明确检查、准备设备码率并预检
  → 用户确认开始（实体操作仍过 TEST/Flash 安全授权）
  → R1 逐用例执行 + 每用例原始证据与正式 RunId
  → RECORD 事务完成、批次聚合、按独立规则给出放行/待审结果
  → 操作员核对结果并准备下一只 DUT
```

## 3. 扫码后用户应看到什么（不是只有“匹配成功”）

**匹配预览必需字段**：
- 扫到的 DUT 编号（可按策略显示部分，查看完整需权限）、输入来源（键盘模拟枪/人工键入/粘贴/串口已验证）、采集时刻、RuleSet 版本、命中 RuleId；
- 编号绑定表名称/版本/RowId、当前绑定状态；工程名称/ID/型号/硬件版本；
- Recipe 名称/ID/Revision/SHA；测试项目名/修订，**本次按顺序执行的用例数与名称**（EntryId 稳定，显示顺序与真实序列一致）；
- TCF、DBC、Controller/按键配置、TBC 的名称/修订、匹配/冲突情况；
- 目标 ECU CAN 码率与当前主控实际 0x43 读回，设备在线/epoch；KEY_CAN 为固定配置，不让配置映射虚构新硬件；
- 烧录：`不烧录` 或 `需要烧录`、目标 ECU、固件标识/SHA 前缀、真实 Flash Profile 状态、当前是否已另行授权（**永不显示或保存 Seed/Key 原始内容**）；
- 该编号历史：未检测 / 曾 FAIL / 曾 PASS 未放行 / 已放行 / 复检待审批 / 历史不可读取；
- Gate 清单：规则/绑定/资产/用例/TCF/设备/速率/Flash/RECORD/复检；每项 `通过 / 待确认 / 阻断`；
- 操作按钮语义：`重新扫码`、`查看匹配配置`、`应用并读回 ECU CAN 码率`（受控实体操作）、`运行前检查`、`确认开始`、`取消本次`。无需大面积占用测试编辑页面。

**严禁**：扫码 Enter 立即触发 IGN/夹具/KEY/CAN/FLASH；扫描匹配状态用绿色 PASS 隐含“测试 PASS/生产已出厂”；在来源未验证时把纯文本标成“已验证扫码枪”。

## 4. 状态机与拒绝策略

建议与 `ProductionSession` 对齐，扩展 `AwaitingOperatorConfirm / AwaitingBitrate / Preflighted / AwaitingReleaseReview` 等业务状态；在这几步没有 `Execute` 调用。

| 状态 | 用户可做什么 | 系统必须保证 |
| --- | --- | --- |
| `Idle / Development` | 编辑工程、选择用例/单步运行；随时进入扫码 | 未配置扫描规则不影响研发 TEST |
| `ProductionIdle` | 扫码；切回研发（若工位不忙） | 已发布配置可显示、扫描框有明确焦点 |
| `Captured` | 预览原始输入、取消、重新扫码 | 不执行任何物理操作；同次扫描有唯一 ScanEventId |
| `Matched` | 查看编号绑定和完整计划 | 未冻结/不完整仍不能 Ready；不隐式选中 UI 勾选范围 |
| `NeedsConfiguration / Blocked` | 查看缺项、修正规则/绑定/版本后**重新扫** | 明确错误码/资源名/恢复动作；不能“仍然开始” |
| `NeedsBitrate / Preflight` | 设备空闲时明确应用/读回、运行预检 | 预检后任何引用/设备/编号变化使计划失效 |
| `ReadyForExplicitStart` | 审核 DUT、完整运行计划、风险确认，点击开始 | 冻结计划 hash 和授权上下文，不自动烧录 |
| `Running` | 查看步骤/进度/停止 | **任何新扫码立即拒绝且不加入队列**；当前 DUT 身份不可替换 |
| `AwaitingRecord / AwaitingRelease` | 查看各用例 RunId、报告、证据和放行阻断 | 缺持久记录不可假 PASS，生产计数不得抢先增加 |
| `Completed/Failed/Aborted/Unknown` | 归档、复核、经允许开始新扫码/复检 | Unknown/Flash 未知副作用必须人工恢复/硬件核查后才能解除忙状态 |

### 核心异常矩阵（必须给出行动，不是只返回代码）

| 场景 | 反馈 | 恢复操作 |
| --- | --- | --- |
| 扫码输入无焦点/截断/无终止符/粘贴/失焦 | `SCAN_FOCUS_DENIED/INCOMPLETE/SOURCE_POLICY` | 焦点重入、清空本次缓冲、重新扫描；拒绝跨窗口粘接 |
| 连续扫码（上一编号还在预检/运行/等待归档） | `SCAN_STATION_BUSY`；保留正在处理的 DUT | 不排队下一 DUT，不替换当前编号；操作员完成/安全取消后再次扫描 |
| 规则空/未发布/过期/多规则命中 | `SCAN_RULE_INVALID/STALE/IDENTITY_AMBIGUOUS` | 配置员修订并重新发布规则；操作员不得忽略 |
| 合法条码无独立编号绑定 | `BINDING_NOT_FOUND` | 查权威编号清单/绑定表，补充并发布，重新扫码 |
| 一码多绑定/跨工程/同编号不同硬件版 | `BINDING_AMBIGUOUS/PROJECT_MISMATCH/ECU_VARIANT_CONFLICT` | 阻断，人工修正发布记录，不能“选第一条” |
| 配方指向旧项目、被删用例/旧 DBC/KEY/TCF | `RESOURCE_STALE`（显示 ID、旧/新修订） | 配置员明确采用新版本并重发布；不能静默跟随最新 |
| 扫码得到 FLASH=true 但测试无 Flash 步骤/或相反 | `FLASH_POLICY_MISMATCH` | 修订已保存 TEST 和 Recipe，再发布；禁止运行时注入 |
| 目标 ECU CAN 码率与主控读回不一致 | `ECU_CAN_BAUD_UNCONFIRMED` | 工位空闲、人工点击应用/读回，再预检；不因扫描自动切换 |
| FLASH XML/HEX/DLL/SafetyFile 变更或授权不匹配 | `FLASH_JOB_STALE/FLASH_CONSENT_REQUIRED` | 重新冻结，人工输入完整授权；**不自行重试擦除/下载** |
| 同编号已曾出厂 | `RETEST_APPROVAL_REQUIRED` / `ALREADY_RELEASED` | 提供复检理由/操作者/审批，产生独立尝试；新增出厂计数不变 |
| 设备断连、报告归档失败、运行被取消 | `STATION_CHANGED/RECORD_UNAVAILABLE/ABORTED/UNKNOWN` | 尝试硬件清理，保留每个已有 RunId；人工核查设备、恢复证据，不算 PASS |
| 复检进行中再次扫描同号 | `SCAN_STATION_BUSY` | 不覆盖原 Attempt/Run，按原批次完成处理 |

## 5. 重复扫码、复检、重新出厂——三种不同业务事件

**A. 重复扫码，尚未实际启动：** 同一 DUT 在 `Captured/Matched/Ready` 阶段被重复输入，不新建 Attempt、不新建 RunId、也不计检测；可以显示“当前编号已经在准备中”，仅经显式取消才能重做匹配。不同编号更不能替换当前已冻结的计划。

**B. 同一 DUT 再次真实检测（含首检失败后重试）：** 操作员选择复检，填写理由；按政策可能需授权。新 `AttemptId`、`BatchId`，各用例全新 RunId；历史原始 PASS/FAIL/Unknown 与报告全部保留、不可覆盖。可以单独统计复检次数与复检结果，但“不等于新增生产件”。

**C. 已出厂/已放行 DUT 重新检测：** 保留原 `ReleaseId` 和其成立时的计划/证据；后续运行标记 `RetestOfReleaseId`，即便新一次真实 PASS，也不可再次增加原 ReleaseScope 的“首检出厂件数”。如果出现质量返工/撤销放行/重新放行，需由专门审核事件说明原因和作用域，不能由扫码模块自行推断。

统计建议显示四类量：生产扫码事件、生产测试批次/实际用例执行次数、按批准范围首次放行的**唯一 DUT 数**、复检/返工次数；另独立显示失败/不确定/待复核。现有 RECORD 的 `COUNT(DISTINCT dut_id)` **含未通过 DUT，不能当出厂数量**。

## 6. 扫码硬件与 WPF 输入操作研究

**优先 USB 键盘模拟枪（keyboard wedge）**，沿现有专用 `ScannerEntry` 捕获，不安装系统键盘 Hook，不吞测试编辑器、DBC 和日志检索的键盘输入。扫描设备需现场核验型号、USB HID 模式、扫描前后缀、Enter/CRLF、是否发送 Tab、中文输入法和按键速度。

现有源码：`KeyboardWedgeCapture` 按 Enter/CR/LF 完成、2 秒键间超时、512 默认长度；`MainWindow.xaml.cs` 使用 TextInput 收集字符，Enter 调用 `CompleteScan`；`MainWindowViewModel` 用本地 router，仅后台 drain 队列，ScanStatus 显示“仅匹配”。**其 `Origin=keyboard` 无法区分真人键入和真实 HID 扫描器**，生产界面不能伪称已识别真实设备身份。

最低可操作要求：
1. 编辑器拥有焦点时**不能**被扫描器路由全局接管；只在显式选中的扫描输入区接收扫码并给清晰焦点/忙状态。
2. 在生产扫码模式中键盘模拟枪作为`KeyboardWedgeUnverified`来源；现场验收后可按工位业务准入该来源，但保存“不可区分人工键盘”的限制。需真正设备绑定时另做 HID 身份/专用 SDK 验证。
3. 严格处理 CR、LF、CRLF 为一个扫描终止；没有结束符应有“待输入/超时清空”反馈，不能将两次扫描拼接。带 Tab 结尾需配置枪或单独授权支持，不默认当完整编号。
4. `Ctrl+V`/WPF 粘贴与键盘输入不一定经 `PreviewTextInput` 同一路径；生产入口须拦截并识别 `ManualPaste`，默认拒绝当作正常物理扫码；调试入口允许手工输入但来源明确。
5. 输入验收应测试特殊字符、前导零、边界长度、无焦点、后台窗口、连扫、扫码中窗口切换、Windows 输入法/布局、字符串误带 CR、扫码运行中锁定。不将字符串录入成功等同设备验证。
6. 单消费者处理扫码，不能“排队执行下一只 DUT”；现有 `BarcodeInputRouter` 可先复用，接入真实站点 Admission Gate；UI 必须解除当前测试 busy 后才能再采集。
7. 串口/COM 仅有 `IVerifiedBarcodeLineSource` 抽象；没有实际枪型号/协议/线缆前，不承诺自动打开端口或现场通过。

**参考边界**：Zebra 官方扫码产品文档支持键盘模拟输入、配置前/后缀、可添加 Enter，说明这类接口依赖输入焦点/终止符配置；但其 Android DataWedge 功能不等于 AutoTest Windows 已集成的驱动。https://www.zebra.com/ 、https://techdocs.zebra.com/datawedge/latest/guide/programmers-guides/how-do-i/

## 7. 待联调的真实现场操作用例（不能用纯 Mock 报 PASS）

| ID | 操作 | 验收重点 |
| --- | --- | --- |
| S-F01 | 用户以当前真实 USB 枪、外部正式条码，在焦点扫描区扫描 | 精确原始文本、终止行为、Rule/Binding/Recipe、正确匹配预览 |
| S-F02 | 扫描长度/规则/型号合法但未绑定的外部编号 | 必须拒绝且给可修复原因，不选择默认型号配方 |
| S-F03 | 发布后修改绑定/DBC/TEST 文件，再对旧匹配计划点击开始 | 旧计划 SHA 失效、必须重新预检、不能写 ECU |
| S-F04 | 将计划码率设为非当前主控码率 | 不自动切速；必须先由人应用/0x43 读回，匹配才可执行 |
| S-F05 | 在实体生产测试中连扫/扫描第二个 DUT | `STATION_BUSY`，当前 DUT 身份、RunId、证据不变；无潜伏队列 |
| S-F06 | 无扫码进入开发模式执行整个用例及单步 | 不触发 SCAN 规则校验；严格不计出厂 |
| S-F07 | 真实配置 requiresFlash，故意不给匹配的实体授权 | 实体 FLASH 不发生，拒绝原因可理解，运行/日志可追溯 |
| S-F08 | 相同 DUT 再次真实执行（有批准理由） | 新 AttemptId 与逐用例 RunId，关联原报告，新增出厂量不变 |
| S-F09 | 两用例批次第二个失败/取消/报告证据不可用 | 批次无出厂放行，已有用例证据仍可查，状态 FAIL/INCONCLUSIVE/UNKNOWN |
| S-F10 | 真实扫描枪断开/恢复、窗口失焦/CRLF/重复 Enter | 不篡改正在运行的身份，不丢错误反馈，不重复启动 |

**现场 PASS 条件**：R1 指定 HEAD 的 Windows Build/Test 通过 + 用户真实枪/真实工位/真实 ECU 操作结果 + RunId/原始扫码/匹配指纹/报告证据闭环均实测可查。本文及单元测试均不构成现场 PASS。
