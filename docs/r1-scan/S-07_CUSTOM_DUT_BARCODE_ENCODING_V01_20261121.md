# AutoTest R1-SCAN S-07｜允许自主定义编码格式后的 DUT 条码与测试方案匹配规范 v0.1

> 2026-11-21，Issue #72，研究分支 `research/r1-scan-configuration-20261120`。用户新增明确条件：**“编码可以我们自己来设计”**。据此从“外部随机编号不可解析产品型号”的保守前提升级为“**可由项目共同制定新产品标签编码规范**”；**这不意味着用户已批准 AutoTest 自行签发或生成每台 DUT 编号**。自定义格式建议未获最后生产冻结，用户和实际条码发行方的能力须确认。
>
> 状态：`NEW_CAPABILITY_ACKNOWLEDGED / CODEC_PROPOSED / ROUTE_MODEL_REVISED / NO_IMPLEMENTATION / FIELD_UNVERIFIED`。新编码可让产品类型与 HW 版本由标签明确声明，但标签可贴错/复制，**不是实物 ECU 身份验证**。依旧遵守不扫码研发、不自动 Flash、不重复计首次出厂、资源冻结等要求。

## 0. 用户新条件改变了什么

之前认为“外部唯一编号是没有产品语义的一段字符串”，所以必须靠当前任务/外部工单/逐号档案获知产品变型。**现在可以制定新的完整扫描负载**，把已知、稳定的产品属性与外部唯一 DUT 编号放在一个条码里：

```text
完整条码文本 = 编码协议版本 + 产品代码 + 硬件版号 + DUT 独立唯一序列号 + 输入校验字段
             └─────────── 标签声明 ───────────┘          └── 必须在所有产品间唯一 ──┘
```

核心影响：
- 新产品标签最自然的默认路由从“已选当前工位任务→Recipe”变为**“发布的 ProductVariant (ProductCode, HwCode)→RecipePin”**，无需提前给每只 DUT 建精确绑定。
- 工位任务仍可成为**预期产品范围约束**：当班只允许 `EPBA/H02`，扫出 `EPBB/H01` 即阻断，而不是静默切换到别的工程或烧录另一个程序；是否允许自动切换工程需单独审批和安全复核。
- 对历史/客户既有随机编号，支持“旧条码协议→保留裸 SN + 当前任务/工单/逐号绑定”的兼容方式；不默认给旧号补造型号信息。
- 配置映射仍然独立、版本化；条码提供**查找键**，不提供测试执行许可或固件文件路径。

## 1. 设计比较：三种标签数据组织

| 设计 | 例子 | 好处 | 问题/选择 |
| --- | --- | --- | --- |
| A. 纯随机 SN | `000000123456` | 短、外部兼容性最高 | 不知道 ECU 型号/HW 版，依赖工位任务/权威外部档案 |
| **B. 自定义带版本结构化 ID（建议默认试点）** | `UT1-EPBA-H02-000000123456-4372` | 自包含产品声明；解析简单；能用 Code 128、DataMatrix、QR 等条码符号承载同一 ASCII 串；不强制建设完整 MES | 需标签发行方遵循新协议、保证 SN 全局唯一；标签声明不等于实体读回 |
| C. GS1 标准产品标识 | 例如有效 GTIN 的 AI (01) 与唯一 SN 的 AI (21)，可附 AI (10) lot | 与供应链标准/外部企业集成更有优势 | 需合法获得/使用对应 GTIN 与遵守 GS1 AI/FNC1 规范，不能把自定义 EPBA 当 GTIN，也不能把普通 DataMatrix 假称 GS1 DataMatrix |

**建议**：针对当前离线单工位新设计标签先用 B；若必须对接客户 GS1，新增 C 的独立解码器并维护 GS1 产品代码→产品变型映射，不把两种语法混为一个 Regex。“物理条码形式”(Code128/DataMatrix/QR) 和“里面的 ASCII 数据格式”(UT1) 必须分开：先确认实际扫码枪是否能读二维，别要求未支持 2D 的扫描器读 DataMatrix。

参考：
- https://support.gs1.org/support/solutions/articles/43000757407-which-data-elements-application-identifiers-ai-should-must-be-used
- https://ref.gs1.org/ai/?lang=en
- https://support.gs1.org/support/solutions/articles/43000734429-does-datamatrix-require-the-use-of-the-function-1-character-
- https://www.ni.com/en/support/documentation/supplemental/08/teststand-process-model-development-and-customization.html

## 2. 建议第一版 UT1 编码（候选，不是假定已经有真实标签）

```text
UT1-EPBA-H02-000000123456-4372
│   │    │   │            └── Check = CRC-16/CCITT-FALSE（四位大写 HEX）
│   │    │   └────────────── Serial = 外部系统分配的全球唯一 DUT SN（保留前导零）
│   │    └────────────────── HardwareCode = 硬件版编码，稳定、受控
│   └─────────────────────── ProductCode = 产品变型族/客户编号注册表键
└─────────────────────────── ProtocolVersion = 条码结构版本（不是测试方案或 ECU 硬件版本）
```

### 2.1 语法建议

`UT1-{ProductCode}-{HardwareCode}-{Serial}-{CRC16}`

| 字段 | 约束（建议） | 语义 |
| --- | --- | --- |
| `ProtocolVersion` | 固定 ASCII `UT1` | 定义解析和校验法，不应因更换 DBC/配方而变为 UT2 |
| `ProductCode` | `[A-Z0-9]{4,8}`，独立注册表控制，例如 `EPBA` | 稳定产品代码。不能塞完整客户名称/测试程序路径 |
| `HardwareCode` | `H[0-9]{2}`，例如 `H02` | **产品的受控硬件版号**；不仅仅是上位机工程版本；若客户定义更复杂采用未来 UT2 |
| `Serial` | 候选 `[A-Z0-9]{8,20}`，受唯一签发方保证**跨产品全局唯一** | DUT 身份；绝不能将它数值化、去前导零、改变大小写 |
| `CRC16` | `[0-9A-F]{4}` | 防止录入/传输偶发错误，不是防伪/身份认证 |

全串格式示例（只核查语法，CRC 需再执行）：
```regex
^UT1-(?<product>[A-Z0-9]{4,8})-(?<hardware>H[0-9]{2})-(?<serial>[A-Z0-9]{8,20})-(?<crc>[0-9A-F]{4})$
```

**额外约束**：任何发布 ProductCode 必须能在 `ProductVariantRegistry` 中明确找到，硬件版本对该产品必须真实有效；扫描成功只证明文本符合规范，**不证明产品变型实际存在或实体硬件是此版本**。

### 2.2 CRC 算法细节与固定测试向量

- 算法：CRC-16/CCITT-FALSE（多项式 `0x1021`；初始 `0xFFFF`；`RefIn=false`、`RefOut=false`；`XorOut=0x0000`）。
- 输入：**不含末尾连字符和 CRC 的整个 ASCII 文本**，包括字段之间的连字符，即 `UT1-EPBA-H02-000000123456`。
- 输出：恰好 4 位大写十六进制文本；条码末尾格式 `-XXXX`。
- 示例的计算结果：
  - `UT1-EPBA-H02-000000123456-4372`
  - `UT1-EPBB-H01-000000123457-DE82`
  - `UT1-EPBA-H03-000000123458-D9DD`
- CRC 不替代打印质量检测、条码符号级纠错，也不能阻止有意复制标签。

**身份主键不可改变**：当前用户业务前提是外部 Serial 本身在所有 DUT 中唯一。所以 `CanonicalDutId = Serial`；`ProductCode`/`HardwareCode`/`UT1`/CRC 均不是 DUT 主键的一部分。否则同一 SN 更换产品声明会被错误统计为“新 ECU”。当一个 CanonicalDutId 已有关联的产品属性与新标签冲突，返回 `DUT_IDENTITY_CONFLICT`，要求人工调查，保留历史；绝不能生成一个新的复合 DUT ID 来绕过冲突。若将来不同发行方的 SN 只在发行方局部唯一，需单独确认 `Issuer + Serial` 的业务身份合同，不能偷偷改变旧键。

### 2.3 为什么不编码工位、DBC、固件、CAN 波特率和测试方案

- **不可变标签只放能在产品生命周期内稳定的事实**：产品类型、硬件版号、外部单件序列号；产品标签一旦打印、粘贴不能因软件升级重印。
- 生产任务/工单可以换，测试程序/DBC/判定阈值/Flash HEX 可以修订；它们应该通过`ProductVariant→已发布 Recipe` 或显式工单/版本约束引用。
- 不建议把“需要烧录”写进条码布尔值。不同返修/首次出厂、软件版本或工作任务可能改变实际 Flash 流程，扫描永远不自动授权危险操作。
- **制造日期/客户/批次**：对路由无刚性作用时不要放入 UT1 核心格式；必要时通过外部系统查，或评估 UT2 的可选字段，避免让所有字段成为新的匹配歧义源。

## 3. 编码自动选配方的完整关系（独立维护，产品树不要嵌入扫码器）

```text
实体扫码枪（真实 HID/COM 另经实测确认）
  → 原始字节/文本捕获，CR/LF 结束符只作为传输，不作为编号
  → SchemaRegistry 选择 UT1 解析器（或显式 legacy/GS1 解析器）
  → 全串语法与 CRC 校验；CanonicalDutId=Serial（字符原样保留）
  → ProductVariantRegistry 查 (ProductCode=EPBA, HW=H02)
  → PublishedConfigurationBinding 查 (EPBA,H02,TaskScope/OptionalWorkOrder)
     → ProductVariant + Frozen Recipe Pin + 可解释匹配原因
  → RunPlan 编译：TestProject+有序 Entry IDs，TCF/DBC/KEY/TBC，
                  目标 ECU CAN kbit/s，Flash XML/HEX/Key/Security SHA，记录政策
  → 工位/资源真实预检；如果有 ECU DID 只读能力，按已批准的产品身份验证规则核对
  → 显示 DUT/SN/产品/方案/烧录/速率/复检状态；明确确认
  → 唯一 R1 RealTestWorkbenchService + Record；不在扫码事件中执行 IO/Flash
```

### 3.1 默认路由建议的变化

此前 S-05 默认优先 `ApprovedStationTask`，因为默认 SN 是无法解释的随机号。**现在若发行方采用 UT1**，建议新增并优先验证 `PublishedLabelAttributes` 的**明确产品码+HW 码映射**。它不是按任意前缀猜配方：只有版本化语法+CRC+注册的 ProductVariant+发布绑定全部有效，才进入 PlanPrepared。

- **单型号当班**：先选当前任务 `ExpectedProductVariant=EPBA/H02`，扫标签得 EPBA/H02，两者必须相等，否则 `TASK_PRODUCT_MISMATCH`。当前任务中的 RecipePin 是“本次哪一版工艺”的授权来源；标签是交叉检查。
- **允许已审批混线**：配置员发布唯一 `(ProductCode,HwCode)→RecipePin` 的混线策略，扫不同产品可以提示不同配置，但**是否自动切换工程/码率/硬件资源与 Flash 仍须通过新的全量预检和显式开始确认**；未批准混线时禁止自动切换。
- **多种合法同型号 Recipe**：产品码+HW 不能决定固件/工单版，必须额外选择已发布工单/TaskScope，否则返回 `RECIPE_AMBIGUOUS`；不能默认取最新。
- **历史外部随机 SN**：没有 UT1 时以旧的 `IdentityRuleSet` 解析保留原 Serial，再按已发布 `ApprovedStationTask / ApprovedWorkOrder / ExactSerialBinding` 方式选配方；**不要把旧 SN 补上想象的型号字段，也不要在生产无命中时静默回退**。
- **逐号档案仍可存在**：不是每只生产 DUT 的必选项，而是返修、特许/混线或外部权威绑定的增强校验。如果明确选择了逐号模式，不命中必须失败，不转成产品码模式。

### 3.2 产品变型与配置映射的真实字段

```text
ProductVariantRegistry
  VariantId, ProductCode, HardwareCode,
  ExternalPartNumber?, Customer?, Validity,
  Source/Approval, Revision/SHA, Published

ConfigurationBinding
  BindingId, VariantId, OptionalTaskScope/WorkOrder,
  RecipeId/Revision/SHA, ValidFrom/To?, Published,
  SourceAudit, Priority? // 默认禁止重叠，不允许靠 Priority 掩盖歧义

ProductionRecipe
  RecipeId, Revision/SHA, ProductVariantId,
  TestProject AssetId/Revision/SHA, OrderedCaseEntryIds,
  TCF/DBC/KEY/TBC AssetPins, RequiredEcuCanBaud,
  FlashPolicy and existing compiled FlashJob/SHA, RecordPolicyPin

ScanResolution
  RawScan/Source, ProtocolVersion/ParserSHA,
  DutSerial, ProductCode, HardwareCode,
  CRCValid, VariantPin, BindingPin, RecipePin,
  RouteExplanation, PhysicalIdentityAssurance,
  IsRetest/ReviewNeeded
```

**R1 接入注意**：现有 `HOST/src/AutoTest.Specialists/Scan/IdentityRuleSet.cs` 只做发布的 Regex、命名捕获与 `IdentityKeyFields` 长度拼接；不做 CRC/格式版本交叉核验。新 UT1 解码器必须独立实现 checksum/field validation，再将 `serial` 作为外部 DUT 身份；旧 `IdentityKeyFields` 若配置为 `model+serial` 就会把重贴/冲突换出新主键，是必须检查的迁移危险。现有 `ProjectScanMatcher` 主要用 model/customer 做 Recipe 匹配，还不具备 ProductVariant+HW 独立映射及真正 TEST RunPlan。

## 4. 条码物理制式与扫码枪配置建议

- **先验收现有扫码枪能力**：Windows 中确认实际设备是 Keyboard HID / HID POS / CDC COM、是否支持一维与二维，再选符号。现有源码有键盘捕获，但尚无已实测的真实物理设备来源证明。
- **一维 Code 128**：若现场枪仅支持一维，能表达本设计 ASCII 负载，但较长标签需要核对实际尺寸、打印清晰度、扫距/纸张。
- **二维普通 DataMatrix ECC200**：若现场枪支持 2D，适合在 ECU 小尺寸标签中存储这种紧凑 ASCII 负载；**“普通 DataMatrix”不能随便称 GS1 DataMatrix**。实际扫描器可能会输出前导符号标识（如 `]d1` / `]d2`），要按设备原始字节和 symbology ID 配置区分并正确解析，不应把符号前缀误当业务编号。
- **QR**：也可承载同样字符串；若产线已有读取 QR 的设备可选；是否更适合取决于标签面积、设备能力、客户标准与印刷质量。
- **GS1**：如果将来正式采用有合法 GTIN 的产品标识，使用 GS1 AI(01) GTIN + AI(21) Serial（可选 (10) batch）并正确处理 FNC1/GS 分隔；不复用 UT1 的自定义分隔/CRC 冒充 GS1。

**条码设备设置与编码格式仍独立**：枪配置决定传输模式、前后缀/CR/LF、字符集、扫描符号；AutoTest 的 `BarcodeSchemaRegistry` 决定 UT1/GS1/legacy 的解析逻辑；`ConfigurationBinding` 决定正式测试方案。研发模式不扫码仍正常运行。

## 5. 极易忽略的边界与验收矩阵

| 测试输入/场景 | 预期 |
| --- | --- |
| `UT1-EPBA-H02-000000123456-4372` | 格式和 CRC 成功；能否匹配须看 ProductVariant/发布绑定是否存在 |
| `UT1-EPBA-H02-000000123456-4373` | `BARCODE_CRC_MISMATCH`，拒绝生产匹配 |
| `UT1-EPBA-H02-000000123456-4372` 扫两次 | 不再生成新 DUT；历史查询/复检策略；未开始运行不增加实际 Run |
| 相同 SN 对应 `EPBB/H01` 标签 | `DUT_IDENTITY_CONFLICT`，需调查；绝不统计第二个新件 |
| 把前导 0 去掉或更改大小写 | 拒绝/不同原始记录；不得隐式归一化成同一 Serial |
| UT1 扫码时没有匹配 Recipe | `NO_PUBLISHED_BINDING`，不能按最近使用工程猜测 |
| UT1 解析为 EPBB/H01，但当前任务要求 EPBA/H02 | `TASK_PRODUCT_MISMATCH`，不执行生产测试 |
| 同产品/hw 同时两套未限定工单的已发布 Recipe | `RECIPE_AMBIGUOUS`，要求明确生产任务/发布修订 |
| CRC 成功但型号 DID 不符 | 不得宣称标签证明实体正确；在 DID 支持且政策要求时阻断或给出未验证状态 |
| 修改绑定/项目/DBC/HEX/波特率或改变设备 epoch | 冻结 RunPlan 失效，必须重新预检 |
| 历史非 UT1 编码且属于已有外部编号 | 用明确发布的 legacy 规则/路由，不静默转换成新格式/新 DUT |
| 同一 DUT 测试失败、复检、再次 PASS | InspectionSession/真实 RunId 各自新增；首次正式放行计数不能重复 |
| 缺扫描枪、工程师运行单步/用例 | Development / Diagnostic 正常运行，走原硬件安全门禁 |
| UT1 文本被复制/重贴到另一只 ECU | CRC 无法检测复制；需要工位、物理/UDS DID/生产流程核对，不能仅凭文本推定真实性 |

## 6. UI 应怎样让用户用得起来

配置员在工程树看到 3 个独立资产：
1. **产品编码规则**：选预置“UT1”，演示样本/导入样本验证，产品代码/硬件版号/CRC 及发布版本；未来可新建自定义 Schema/legacy 规则；自动保存草稿不等于发布生产配置。
2. **产品变型/关联表**：一行 `EPBA/H02 → Recipe A@r3`，另行 `EPBB/H01 → Recipe B@r5`；以产品码/硬件版为主键、搜索/冲突检查、修订/发布。无需逐 SN 一行。
3. **测试方案**：原有 TEST Project/用例、TCF/DBC/KEY/TBC、CAN 码率、Flash 文件 SHA 等，不在扫码规则编辑器复制第二份。

生产操作者只需：选择当前任务（可限定 `EPBA/H02`）→ 扫码 → **看到“产品 EPBA／HW H02／DUT SN…／方案来自绑定表 r…／预检结果”** → 有意确认开始 → 查看真实执行和报告。复杂配置隐藏在工程文件管理中，而不是让操作者逐条选 DBC。

对研发：依旧直接选择测试步骤/用例/勾选范围；可在工具里测试 UT1 解码器，绝不能因没发新编号规则被挡住。

## 7. 对 Issue #72、先前研究及 R1 实施策略的差异

| 项目 | 先前假设 | 本轮新建议 |
| --- | --- | --- |
| 编码能力 | 只有外部不带语义的唯一 SN | **可共拟带 `ProtocolVersion+ProductCode+HW+Serial+Check` 的新编码** |
| 默认路由 | 固定工位任务；严格逐 SN 才能混线 | 新 UT1 用发布的 `ProductVariant→Recipe` 做自动选择；固定任务作为当前生产约束；严格逐 SN 留给需要权威档案的场景 |
| 独立绑定 | 必须逐号登记或固定任务→方案 | ProductVariant 和配置方案也必须显式关联、独立发布和版本冻结，不要求每只 DUT 一行 |
| 身份主键 | 旧 Regex 可用任意 capture 拼出身份 | **唯一 SN 不带产品字段**是 canonical identity；重新贴产品标签不能改变 DUT 身份 |
| SKU/HW 与真实 ECU | 仅型号标签声明 | 仍属标签声明；需要物理防错/未来可验证的 UDS DID 交叉检查，不能靠 CRC 当硬件认证 |
| 生产入库 | 原 S-01 /S-05 与项目管理政策候审 | 依旧候审；没有生产记录合同批准不能增出厂计数、自动放行或改写 RECORD |
| 设备/符号 | HID 或 COM 取决于现有枪 | 不变；额外选一维 Code128 或二维普通 DataMatrix/QR，与 UT1 文本解析分开 |

**建议最小真实闭环**：在用户设计/打印样例标签、验证现有扫码枪读取结果后，先做 `UT1 解码/CRC → 产品代码+HW 发布关联 → 冻结单 TestProject 的一个或多个有序用例 → 设备预检→人工确认→现有 R1 TEST/RECORD`，无 Flash 场景先验收；Flash 严格独立授权再接。旧单型号固定任务和 legacy 可保持，不强迫所有真实 DUT 立即换标签。

**需要后续真实业务/用户最终冻结的最少事项**：
- 完整条码是否由生产业务在标签打印前签发，AutoTest 是否仍仅消费（按目前合同，**是**）；
- `Serial` 是否在所有产品/客户/发行方之间全局唯一，还是要追加发行方键；
- 真实产品代码注册表、硬件版号体系与历史编号兼容要求；
- 实际枪是否读二维、条码标签面积与产线是否需要 GS1/客户自有标准。

**研究结论**：用户允许自主设计编码后，AutoTest 可以实现真正有依据的“扫码→确定产品变型→查已发布方案→安全预检→现有 TEST”自动选配，不需要对每个 DUT 手写配置。但扫码格式与配置关联依然两层，CRC 不认证实物，运行版本与出厂放行仍然受正式治理。
