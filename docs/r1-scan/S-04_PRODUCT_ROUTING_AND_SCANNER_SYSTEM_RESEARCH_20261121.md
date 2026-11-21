# S-04｜扫码枪、DUT 唯一编号与生产测试配置的全局产品方案研究 v0.2

- 日期：2026-11-21
- 任务：Issue #72
- 研究分支：`research/r1-scan-configuration-20261120`
- 最新回读 R1：`02f18c65e5ba6038da10067c6285450de6cce911`（相对 S-00 的 `fa3b18e...` 增加 28 提交，diff 主要为 UI 自动保存/手动台架/删除等；未见 SCAN 专项的生产接线变化）
- 状态：`PRODUCT_RESEARCH_V2 / DESIGN_PROPOSED / FIELD_NOT_VERIFIED / NO_CODE_CHANGE`
- 注意：此文件**补充** S-00~S-03 的第一轮源码和接口审查；对 S-01 “逐编号精确绑定为 P0 唯一默认”提出可配置生产场景改进，**尚待审查**，不自动取代既有冻结合同。

## 0. 给操作者解释需求的最短答案

**扫描枪只告诉 AutoTest「这一台 ECU 贴着什么编号」。单靠一串唯一编号，软件通常不知道它究竟是什么产品、应跑哪个测试工程/DBC/烧录程序。决定这些的是配置来源，不是扫码枪。**

正确产品边界：
1. **Scanner Device Profile（设备接入）**：COM/HID、设备身份、帧分隔、超时、字符编码、连接和 busy；解决“能否正确采集原始字符”。
2. **Identity Rules（编号识别）**：外部编号的语法、长度、校验码、取出的完整唯一 DUT Key；解决“格式是否合法、是不是唯一键”。
3. **Routing / Binding Policy（配置路由/绑定）**：利用已发布的 DUT 清单、工单、专用工位等权威外部上下文，确定**唯一**测试 Recipe；解决“这一台应采用什么方案”。
4. **Production Recipe（生产方案）**：当前项目/有序用例集合/TCF/DBC/按键/TBC/码率/Flash/报告/复检；解决“要怎样测试”。
5. **Frozen Run Plan（运行前快照）**：真实 Project/Test/资源/连接/操作者/扫描/绑定决策/清单 SHA，清晰提示并经许可后启动。
6. **TEST + RECORD（唯一真实执行链）**：按批准有序用例实际运行、每用例独立 RunId、原始 CAN/DBC/UDS/Flash 证据、正式结果/放行。

不能把以上全叫“扫码配置”：至少应分为**设备配置、编号规则、生产绑定、生产方案、执行记录**五种操作语义。

## 1. 工业产品/标准调查：借鉴原则与不应抄的部分

### NI TestStand

NI 官方说明：条码读取属 UUT 标识或工位通用流程，可放在 process model / callback，而不是混进每个 DUT 测试序列；`Test UUTs` 会重复识别、测试、报告，`Single Pass` 可直接执行一个 DUT 测试而无需同一身份提示。结果表会记录 `UUT_SERIAL_NUMBER`、Station、Execution、Batch。

- https://www.ni.com/en/support/documentation/supplemental/06/a-guide-to-developing-effective--maintainable-teststand-sequence.html
- https://www.ni.com/en/shop/electronic-test-instrumentation/application-software-for-electronic-test-and-instrumentation-category/what-is-teststand/test-management-software-developers-guide.html
- https://www.ni.com/docs/en-US/bundle/teststand/page/uut-result-table-schema.html

**AutoTest 适配**：生产扫码模式 vs 研发/单步模式分开；扫码用于选择已经批准的运行方案而不是替代 TEST 程序；单 PC 单工位一个 DUT，绝不引入 TestStand 多工位并行的额外复杂度。

### GS1 唯一身份语义

GS1 的 (01) GTIN 与 (21) Serial 在标准编码中可以组成序列化产品唯一标识；也存在厂商自定义的非 GS1 编码。**不是所有扫码数据都天然包含型号/BOM/测试方法**。若用户实际采用 GS1，按其 AI 规则解码；若客户自定义，只按客户正式规范解码，不把任意前缀解读为真实硬件型号。

- https://support.gs1.org/support/solutions/articles/43000734238-how-does-serialisation-differ-from-unique-identification-in-the-gs1-system-/
- https://support.gs1.org/support/solutions/articles/43000757407-which-data-elements-application-identifiers-ai-should-must-be-used

### ISA-95 资源、工单与执行事实

ISA-95 将生产定义/工单、设备资源、实际生产记录区分，这给“离线工位只取任务快照、按固定程序执行、把结果回写关联”提供架构参照。但 R1 不需要完整 MES，也不应因未联网而阻断开发或生产单工位。

- https://reference.opcfoundation.org/specs/OPC-10030/4.2
- https://reference.opcfoundation.org/specs/OPC-10031-4/4.1

### 扫码设备协议

不同扫描器可以配置 HID Keyboard 或 USB CDC COM Port；USB CDC 可能需要厂商驱动，不能只凭 USB 外形判断上位机连接方式。串口接入的边界应绑定真实设备/协议，不能因 “VerifiedSerial” 枚举就断言已确认设备真实性。

- https://docs.zebra.com/us/en/scanners/multi-plane/mp72-ig/c-mp72-host-interfaces-and-cable-pinouts/c-mp72-usb-connections-interface/r-mp72-usb-device-type.html
- https://docs.zebra.com/us/en/scanners/general/sm72-ig/r-param-desc-communication-protocol-functionality.html

## 2. 用户真正可能遇到的 4 种制造场景：不应强制一种匹配模型

| 模式 | 实际生产条件 | 扫码后如何决定 Recipe | 主要风险 | R1 推荐 |
| --- | --- | --- | --- | --- |
| **A. 固定工位/固定型号** | 工位当班只生产一类 ECU，工程师已明确发布并选中 Recipe | **工位当前已批准生产任务**决定 Recipe；扫码仅把外部编号绑定到新 Attempt | 偶然放入错型号 ECU，单凭扫描不能验证 | **可以作为首版最短路径**，但显示“按工位当前任务选配方”，须有产品身份交叉确认或工位防错措施 |
| **B. 混线/精确序列号绑定** | 不同 ECU 由同一工位检测，外部生产系统提前生成编号及其产品型号/版本 | 发布的**编号→Recipe**精确映射表；查找唯一条目后锁定 | 外部清单不完整/错版本/重复绑定 | **强烈建议**，S-01 的默认多型号生产模型 |
| **C. 按工单/生产批次分配** | 操作员先批准本班生产工单与产品任务，编号只在该工单合法空间内 | (已批准工单 + DUT Key) → Recipe；必须有明确工单范围和编号归属校验 | 工单切换后旧号与新工单冲突 | 可作为后续增量，不能把“所有同前缀”默认当同一工单 |
| **D. 条码自带型号/配置版本** | 客户标准明确定义条码含有型号、工厂、硬件版本等，并有校验与授权对应表 | 唯一解析 → **型号/硬件版本到 Recipe 发布绑定** → 实际实物交叉核验 | 伪造/贴错条码；前缀不可靠、改码易冲突 | 仅明确业务授权后启用；不得把正则 `model` 字段本身当作真实硬件证明 |

**重要设计修订候选**：S-01 采用“逐编号精确绑定”作为多型号环境的保守 P0。对**固定工位单一生产任务**，可以不强制导入每台 DUT 清单，而是由**操作者发布/确认的固定 Recipe**提供测试配置、扫码只绑定唯一 DUT 身份。此模式必须明确标注 `ROUTE_BY_STATION_TASK` 而不是 `ROUTE_BY_SERIAL`；若要求同编号不能在其它产品工单测试，还需权威编号合法集合/产品身份交叉检验。不要在精确绑定失败时偷偷“回退”到固定工位配方。

### 路由策略必须单选并显示来源

`RoutingPolicy.Mode` 明确是 `ExactSerialBinding`、`ApprovedStationTask`、`ApprovedWorkOrder` 或 `VerifiedLabelAttributes` 之一；某次生产批次只执行一个 policy。无匹配、多匹配、来源失效/过期、产品冲突均禁止选择第一个或自动换工程。只能由授权配置员切换模式并发布新 Revision/SHA，操作者扫码时显示已选策略名称。

## 3. 两个示例：仅有编号 vs 已带身份字段

**示例 1：外部编号只是字符串，无法由自身判断型号**

- ECU-A 的外部编号示例：`000001287654`；
- ECU-B 的外部编号示例：`000001287655`；
- 两者看上去相邻，不能从尾号判断哪个是 EPB-A 或 EPB-B。

如果是混线：离线导入受控的外部编号清单并发布，例如：

| ExternalDutKey | 可信外部声明型号/版本 | Recipe 引用 | 状态 |
| --- | --- | --- | --- |
| 000001287654 | EPB-A / HW-A（仅举例） | `EOL-EPB-A@r3/SHA` | Active |
| 000001287655 | EPB-B / HW-B（仅举例） | `EOL-EPB-B@r2/SHA` | Active |

扫 `000001287654` 后只检索这一行；**不能**拿串号大小、正则或当前打开的测试用例偷偷推断。保存的仍是外部编号原文，保留前导 0。

如果是固定工位 A：已发布`当班任务=EPB-A`，所有合法扫入的 DUT 只作为工位 A 的身份（可额外要求唯一编号清单或物理 DID 校验）；系统必须在报告写明本次 Recipe 来自 `ApprovedStationTask` 而非逐编号外部绑定。

**示例 2：编码中真实包含型号字段**

外部客户明确书面规定 `A-2026-000127` 的 A 是型号 A，`B-2026-000128` 是型号 B，且有对应校验规则。可以解析 `model=A`、`serial=000127`，但**合法性与配置正确性是两次不同验证**：先按条码规范判断有效，再根据发布的 `model=A → Recipe A` 绑定找到唯一配方。若同一型号因硬件版/客户订单需要不同 Flash/DBC，必须加入明确附加字段或权威外部表，不能全按型号默认单一配方。

以上编号和方案名均为**说明性虚构例子**，不代表真实 ECU 客户编码或 AutoTest 已有文件。

## 4. 推荐产品对象与依赖关系

### 4.1 ScannerDeviceProfile（工位级，不能混进编号规则）

字段：
- `Transport`：`UsbCdcSerial` / `KeyboardWedge` / `ManualEngineering`，建议设备按**用户实际 USB COM 枪**优先研究串口路径，HID 为兼容选项。
- `DeviceLocator`：Windows PnP 设备实例、VID/PID/厂商序列号（设备确实提供时）+ 当前 COM 号；设备重新枚举时通过稳定身份核对，**COM5 等端口号不可视为永久身份**。
- 对真实串口枪标定 `PortName, BaudRate, DataBits, Parity, StopBits, Handshake, Encoding`；USB CDC 的具体串口参数是否被设备使用必须以产品说明/实测为准，不随意猜默认值。
- `FramePolicy`：CR / LF / CRLF / STX-ETX / 固定长度 / 厂商协议分帧，只有设备实际提供哪一种才启用哪一种；`MaximumLength`，合法字符集，超时和错误恢复。
- `SuffixTrimPolicy`：只去除真实 framing 控制字节，不应对业务编号默认 `Trim`、`ToUpper`、数字化（前导零绝不能丢）。
- `OperatorDeviceVerification`：最后验证时间、实际设备来源、原始字节 HEX 诊断、不可信/已验证标记。
- `AdmissionPolicy`：单工位空闲可接受扫描，运行/等待归档/设备故障立即拒绝且**不留潜伏队列**。重新连接不能覆盖本次 DUT。
- **安全要求**：扫描事件无法直接触发 IGN/KEY/FLASH/CAN 写入；完全不安装全局键盘 Hook；键盘模拟只能在专用输入区生效。

此前的 R1 源码 `SerialBarcodeCapture` 目前只有 `IVerifiedBarcodeLineSource` 与手工提供的 verified identity 字符串，**并没有真正打开/管理 COM 口**；`MainWindowViewModel` 当前走 `KeyboardWedgeCapture` / `ProjectScanMatcher`，业务流仅匹配状态。真正 COM 设备的驱动/帧格式/断线恢复必须作为 W-FIELD 验收，不因抽象类存在宣布硬件接通。

### 4.2 IdentityRuleSet（工程/客户编号规则）

`RuleSetId/Revision/SHA/Publication + Rules[]`：
- 全串匹配、外部编号唯一键字段、固定/可变长度、字符集或 GS1 AI（仅实际业务存在时）、可选校验位、必要的客户/批次字段；
- 对大小写、全半角、空白及连接符的任何转换都由**发布规则**明示，默认无损；并验证同一规范化值不会对应两个原文；
- 对一个原始扫值：零匹配→ INVALID，多匹配→ AMBIGUOUS，且不得运行；
- 解析出 `model` 只是“从标签得到的声明”，不是用真实 ECU UDS 或装配信息证明了实际硬件型号；
- 不把原始扫描器 Device COM 速度/驱动设置混入规则资产。

### 4.3 ProductionRouteBinding（独立绑定和生产任务）

建议分开发布以下可审计资源：
- `DutConfigBindingTable`：精确 DUT→Recipe、外部清单来源、有效状态、生效期、发布者/审核；混线优先；
- `StationProductionTask`：当前单工位已批准 Recipe、型号/工单/预期 ECU 实体、允许编号格式/有效列表、批次开始结束与复核；
- `WorkOrderRoute` / `VerifiedLabelRoute`：升级候选；不可无授权启用。

所有模式最终产出同一 `ResolvedRecipeRef`（Recipe ID/Revision/SHA），以及 `RouteProof`（何种策略/哪个表或任务/发布版本/匹配行/外部来源的 SHA/为什么命中）。

**导入权威编号表**应支持模板 CSV / 表格一次导入、预览差异、批量冲突检测、行级审计、旧版保留、显式发布。生产运行不要每扫一次都依赖打开 CSV 文件；将发布快照纳入工程受控资产或受控本地索引，运行中验 SHA。若规模大，采用受索引的 SQLite/二进制快照只是**解析数据的索引**，不另建记录真相库；出厂事实仍使用正式 RECORD。

### 4.4 ProductionRecipe（工程级，独立于外部 DUT 编号）

配方包含：
- `ProjectId/Revision/ManifestSha` + 适用 ECU model/HW revision/客户；
- 从**已保存测试项目**取稳定 `TestProjectEntryId` 的有序子集，每 Entry 关联 `TestDocument AssetId/Revision/SHA` 和准备后 `PreparedTestPlanSHA`；不得运行时使用 UI 研发勾选态覆盖；
- `TCF`（被测系统/DBC/Button）、`TBC`（真实台架/接口/固件预检）、所有实际 TEST 需要的 DBC/按键/波形资源，引用 `AssetId + Revision + SHA`；
- `ECU CAN bitrate` 必选经过批准的目标值，并在运行前以主控 `0x43` 同 session epoch 真实读回；KEY CAN 固定 500k；
- `FlashPolicy`：Disabled/Required，若 Required 需要程序已有已启用 Flash 步骤、完整 `XML/HEX/安全 DLL及配置指纹、目标 ECU、速率`；**不允许扫码自动注入 Flash 步骤或授权**；
- `Report/Retest/ReleasePolicy` 的引用（是否必须所有用例 PASS、何种复测授权、是否可以独立出厂放行）；扫码 Match 本身绝不是 DUT PASS。

### 4.5 FrozenScanRunPlan（本次任务）

每次扫描真正准备运行时生成不可变 `PlanId/CanonicalSHA` 与：
`ScanEventId, RawID/ExternalDutKey, SourceType, RuleRevision, RouteProof, RecipeRev/SHA, ProjectRev/SHA, OrderedCases[], TCF/TBC/DBC/KEY pins, Bitrate readback epoch, FlashJob SHA, StationEpoch, OperatorConfirm, PreflightDiagnostics`。

任何编号/工位/项目/资源/文件/码率/Device epoch 变化 → `PLAN_INVALIDATED`，不能“继续使用刚才的那份计划”。

## 5. 真实 Windows 工位的操作流程——两种模式绝对不能串

### 生产扫码模式

1. 配置员准备受控工程，发布编号规则、路线绑定或当班工位任务、生产 Recipe；单工位当前只能选择一个**无歧义**的有效 RoutingPolicy。
2. 工位操作者打开 AutoTest，上次工程自动恢复；显式进入“生产扫码”，工位空闲，真实扫描器连接，显示设备来源/发布配置/型号/当班任务。
3. 真实扫码 → 显示原始 ID、来源、归一化结果、匹配路径（比如“逐号绑定 #123 → EPB-A EOL 方案 r3”或者“工位固定任务 #7 → EPB-A EOL r3”）。
4. 显示**明确将执行的测试**：测试项目、有序 6/8 个用例示例（具体数量由当前工程决定）；TCF/DBC/KEY/TBC；ECU CAN 码率目标与当前硬件读回；是否烧录/固件哈希摘要；是否历史复检/已放行；每项 PASS/WAIT/BLOCK 的预检状态。
5. 生产先做只读预检，若码率需更改，工位空闲且**明确按钮授权**后才向主控 0x42 应用，并以 0x43 读回；有 UDS ECU DID/版本识别能力时，可使用经授权的只读动作交叉检查实物型号与标签/绑定，失败阻断。
6. **操作员明确点击“确认开始”**后，唯一的 `RealTestWorkbenchService` 进行真实 TEST；每用例产生当前真实 RunId，危险 FLASH 仍需额外完整指纹人工授权。
7. 运行期间扫码事件不得改当前 DUT、不得排队下一只；完成后由现有 RECORD 正式核对结果/原始证据/所有 case。生成一个 batch/attempt 关联多个真实 RunId。
8. 批次结束明确显示 PASS/FAIL/INCONCLUSIVE/ERROR 与“是否已放行”**两个不同字段**。只有正式 ReleaseDecision 可增加出厂首次放行统计，重复检测是新 Attempt/Run 但原 DUT 只增加一份首次放行。
9. 操作者确认取走 ECU，清空当次会话；准备下一次扫码；未知硬件副作用/报告失败要求人工恢复，不静默清除 busy。

### 研发/调试模式

- 直接打开工程、选择用例、运行此步骤/用例/本次勾选范围；即使没有扫描器、未发布规则或编号为空也能执行原有真实 TEST 安全预检。
- 可手工填写外部 DUT 编号作为**调试标注**，保存 `ManualEngineering` 来源；没有验证的生产路由和 ReleaseDecision 不能被当作出厂 PASS。
- 单独扫描设备调试/查看原始字符属于**工具模式**，不能改变当前测试程序和工位状态。
- “运行某一条步骤”不需要先建立生产 Batch，不应因扫码新功能造成操作路径倒退。

## 6. 正式记录：不重复增加“新 ECU”统计

数据关系：
`ExternalDutKey (business identity) 1 → N ProductionAttempts`；
`Attempt 1 → 1 Batch`；
`Batch 1 → N ordered CaseRunLinks`；
`CaseRunLink 1 → 1 actual RunId / RunRecord / StepOccurrence facts / attachments`；
`ExternalDutKey 1 → N historical ReleaseDecisions (audited state changes), but FIRST_RELEASE count keyed by project/order/identity/releaseScope is idempotent`。

边界规则：
- **重复扫码但未启动**：可能新增 ScanEvent，不新增 Attempt/Run。
- **同一 DUT 测试失败后复测**：新 Attempt/RunID，原记录不改；复检次数+1，新增出厂件数+0。
- **同一 DUT 已通过后再测**：新 Attempt/Run，第一次成功放行不重复计件；复检理由/权限与返工状态记录。
- **失败/取消/Unknown/只有 ACK/记录不完整**：不可发放 `Released` 状态，不能把 `COUNT(DISTINCT dut_id)` 直接当新增出厂。
- **生产 batch 内 A 用例 PASS、B 用例 FAIL**：不能按 A 单项 PASS 给整个 DUT 放行；每个用例仍有独立历史和报告。
- **一个外部编号出现在不同项目/型号绑定**：按正式 RoutePolicy 和有效合同阻断冲突；不要静默复用历史或自动跳工程。

**产品标示**建议明确分开：
`扫描次数 / 独立 DUT 编号数 / 实际批次次数 / 实际用例 Run 次数 / 测试整体通过 DUT 数 / 首次出厂放行 DUT 数 / 复检次数 / 未决异常数`。

## 7. 具体 UI/操作产品建议：让操作者看懂而不是理解术语

### 配置管理（工程师；不要把所有东西放成一个长表单）

工程左树下可见：
- `扫码设备`：工位 USB CDC COM 或 HID 绑定/调试/接收日志；开关/数据分帧/失败恢复；它是工位设置，不属于 DUT Recipe。
- `编号规则`：预览格式匹配、字段、前导零、合法性/校验、规则版本和发布。
- `编号与生产任务绑定`：查看/批量导入/发布的 DUT Binding；或当前已批准的 Station Task/Work Order；显示策略名、冲突、有效期和未绑定列表。
- `生产测试方案`：选择一个工程 TEST Project，勾选并排序正式**保存的有序用例**，关联 TCF/DBC/按键/台架/波特率/Flash；发布版本。
- `生产历史和放行`：按 DUT 编号查询全部尝试、各用例 RunId、附件哈希、历史匹配规则和审批、重复放行拦截。

编辑器体验：列表先增加规则/绑定项 → **右侧 Inspector** 编辑；搜索可以查编号、模型、绑定方案、冲突，不开繁多临时窗口；编辑自动保存草稿，**草稿保存 != 发布生产版本**。发布动作显式确认冻结和依赖影响，旧版保持可恢复用于历史运行复现。

### 生产操作区（操作员；不覆盖主 TEST 工具工作区）

建议顶部一个小型“生产扫码”入口，常驻展示：`工位就绪 + 扫描器 USB COM 身份/连接 + 当班路由策略/Recipe 发布版本`。扫后展开**匹配预览**：`DUT 编号 / 来源 / 产品配置 / 运行 6 项用例 / 是否 FLASH / ECU CAN 码率 / 复检状态 / 阻断项`，并提供 `重新扫描、查看方案、预检、确认开始`。

**绝不**：扫码结束符 Enter 直接导致真实 IO；匹配成功显示“ECU 检测合格”；改变研发临时勾选范围；用默认最新方案替换已冻结 SHA；把纯键盘文本冒充专用 COM 枪的实体来源。

## 8. R1 当前源码对比后的额外发现（2026-11-21）

- 当前 `R1@02f18c65...` 相对 S-00 的 `fa3b18e...` 多 28 提交，改动主要聚焦 UI 自动保存、手动台架浮层、关闭窗口、项目删除；未见 `Specialists/Scan`、`ProjectScanMatcher`、`RealTestWorkbenchService` 和 `Application/Production` 的实现变动。
- `Desktop/Shell/MainWindowViewModel` 仍新建 `KeyboardWedgeCapture` 并配置 “keyboard” router，ScanStatus 仅调用 `ProjectScanMatcher.MatchAsync`，信息明确写“仅匹配，未执行测试”；真实扫码输入/生产执行不应被宣称已联动。
- `Specialists/Scan/SerialBarcodeCapture.cs` 仅有 `IVerifiedBarcodeLineSource`，没有实际 COM Port 会话/原始字节分帧或设备身份重新枚举；**与用户之前实际使用的独立 USB/COM 串口扫码枪不一致**。这是优先补的硬件入口调研，不应盲目继续按 KeyboardWedge 实施。
- `ProjectScanMatcher` 现有模型按解析出的 model/customer 过滤 recipe，且 bridge 处 recipe revision 固定 1；缺独立 binding、已发布 StationTask、冻结 Recipe SHA、完整 TEST scope/TCF/TBC/Flash/波特率。
- `RealTestWorkbenchService.ExecuteAsync` 已有真实 USB/TEST/FLASH/REPORT，DUT identity 非空时 `FreezeActualRunIdentity` 仍固定 `RecordRunMode.Development`；无号或单步为 `Diagnostic`；没有明确 Production 模式/BatchId/AttemptId 的独立接线。
- `ProductionWorkflow` 现有独立调度接口存在，但尚无指向真实 R1 `RealTestWorkbenchService` 的 production adapter；其 `ProductionSession` 预生成 RunId 与 R1 每个实际用例 RunId 不同；**不能把两个流程都当成生产真相源**。
- `ProductionStatistics.GetSummaryAsync` 的 `DistinctDutCount` 使用 `COUNT(DISTINCT dut_id)`，含已执行失败个体，不能显示为首次放行数量。
- 上述都是源码静态审查，不能宣称 Windows build/test/真实 COM 枪现场通过。

## 9. 建议 R1 优先级与最小落地顺序

**P0 两条试验路径同时保留、不可互相暗中 fallback**：

A. **单工位固定任务最短路径**：已批准工程/Recipe 与固定 StationTask → 实体 COM 扫码读出唯一 DUT ID → 预览“本次测试方案来自固定任务” → 预检、启动单用例或有序项目 → 真实 TEST/RECORD。这条路径不依赖导入海量编号清单，但必须验证当班只允许的产品类型/外部编号合法来源，且不得宣称“条码自动决定型号”。

B. **混线精确绑定路径**：已批准工程 + 规则 + 外部编号清单绑定表 + Recipe → COM 扫码 → 唯一精确查找 → 同一冻结计划 → TEST/RECORD；不命中必须阻断，不能回退到 A。

实施顺序：
1. 验证**实际扫码枪型号/Windows COM 枚举/原始 HEX/协议分帧**，补现有 `SerialBarcodeCapture` 的真实设备适配器；必须明确来源、会话/断线、超时、串帧、不堵塞 UI。
2. 工程侧完成 **ScannerDeviceProfile 与 IdentityRuleSet 分离**、`RoutingPolicy`（固定任务与精确绑定）及发布快照；先做纯匹配、差错可解释。
3. `RecipeV2 + FrozenRunPlan`，冻结有序用例 EntryIds、TCF/DBC/KEY/TBC/速率/Flash 及精确 SHA，前置工位读回/编译核验。
4. 由 R1 审查补唯一生产入口的 `RecordRunMode.Production`、`BatchId/AttemptId/actual RunId` 映射；**禁止再造 TEST runner/FLASH 写入逻辑/RECORD 数据库**。
5. 完成真实枪→真实 ECU→正式报告→复检无重复放行的现场验收。HID Keyboard 模式按实际设备需要作为兼容能力，不替换已验证的 COM 路径。

任何 Stage 成功都不得在缺乏物理测试和证据的情况下显示生产 PASS。

## 10. 需要用户最后决定的业务策略（不阻断本研究）

1. 将来是否需要混线自动识别多个 ECU 型号/版本，还是大多数时候“一个工位一个型号，人工切换工单”？
2. 真实外部条码格式是否含可经客户规范证明的型号/硬件版本/生产工单，或者只有**完全无意义的唯一编号**？
3. 如混线，谁能提供**权威、可离线导入**的 ECU 编号→型号/配置对应表？如无此表也无型号字段，软件不可能从唯一字符串中可靠推断产品配置。
4. 是否可通过 ECU UDS DID/真实只读 CAN 信号校验标签标称的 ECU 型号/软硬件版？这属于第二来源核验，硬件/协议未证明时不能做保证。
5. 首版生产数据里“出厂放行”是否需要操作者手动复核，还是满足特定工艺规则可自动放行？安全默认是**独立审批且不自动放行**。
6. 扫码设备实际型号/数据格式/端口时序，请在硬件联调时提供设备识别信息；**当前默认研究 USB COM 而非要求用户改成 HID**。

## 11. 设计验收：必须能在产品里说明每次“为什么选择了这个方案”

准备结果至少呈现一个可追溯解释：

> `接收到 DUT 000001287654 [USB CDC COM, 来源证据 e...] → 使用编号规则 R-r2 校验通过 → 根据 BindingTable B-r3 的 Row 157 精确绑定 Recipe EPB-A EOL r4 → 选择当前工程的用例 Entry 1/3/5 (已冻结修订与 SHA) → TCF/DBC/KEY/TBC/ECU CAN 波特率/FLASH 版本核对 → 主控波特率实际 0x43 读回通过 → 等待操作者明确开始。`

或：

> `接收到 DUT 000001287654 → 编号规则通过 → 使用已发布的工位生产任务 ST-EPB-A-r5 选择 Recipe，不声称从条码识别出型号 → 需核实 DUT 属于此任务 → 冻结用例与配置 → 等待开始。`

所有文字仅为虚构**演示说明**。真正生产运行的解释必须来自实际唯一发布对象的 ID、修订、SHA、真实 USB/COM 来源、工位证据和 RECORD，用于复检审计而非漂亮状态标签。

**结论**：基于源码和成熟产品调研，首版 AutoTest 不应把“编号规则、硬件串口、测试程序、工程/工位/烧录和出厂数量”塞进同一张扫码配置表。它们是关联但不同责任的产品对象。对本项目最具实际价值的是先把**实体 COM 扫码 + 明确路由（固定任务或精确绑定）+ 冻结测试方案 + R1 正式运行 + 复检/放行证据**做成一条可现场验证的纵向链。
