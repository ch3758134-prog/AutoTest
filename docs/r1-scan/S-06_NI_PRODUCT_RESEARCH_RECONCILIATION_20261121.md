# AutoTest R1-SCAN S-06｜NI 与产品路由两份并行研究交叉审查及最终候审收敛

> 2026-11-21，Issue #72。分支同期出现了两份“第二轮 S-04”，本报告**同时回读**：`S-04_NI_TESTSTAND_SYSTEM_RESEARCH_20261121.md`（NI 过程模型、Single Pass、参数与产品选择）；`S-04_PRODUCT_ROUTING_AND_SCANNER_SYSTEM_RESEARCH_20261121.md`（设备接口、GS1/ISA-95、四种实际生产路由）。两份研究都不是已批准代码或生产放行政策。配套产品操作方案 `S-05_SCANNER_CONFIGURATION_AND_PRODUCT_UX_V02_20261121.md`。
>
> 本报告优先遵照项目管理 [07 WAVE/SCAN 对照](https://github.com/ch3758134-prog/AutoTest/blob/Project_Management/docs/project-management/current-20261121/07_WAVE_SCAN_RECONCILIATION.md) 与 [02 决策台账](https://github.com/ch3758134-prog/AutoTest/blob/Project_Management/docs/project-management/current-20261121/02_DECISION_SUPERSESSION_LEDGER.md) 所列“已批准/候审/未验证”分级，不能把未验证的硬件接口或逐编号默认方案冒充用户批准。

## 1. 一致结论（高置信、建议作为实施评审输入）

1. **扫码设备接入、外部编号语义、配置路由、Recipe、冻结 RunPlan、唯一真实 TEST/RECORD 是不同职责**。NI TestStand 把 PreUUT/序列号跟 MainSequence 分开，是强佐证。
2. **随机唯一编号本身不包含可推断的产品配置**。若没有当前工位任务、客户条码规范、外部工单或权威逐号清单，不能据此自动选择 ECU DBC/Flash Profile。
3. **必须允许多个明确的匹配依据，而不是强制所有 DUT 预建逐编号绑定**。固定任务适合 EPB 单产品连续检测；精确逐号适合混线/返修；产品码和工单模式按真实资料启用。
4. **固定任务也应具独立发布映射**：`ActiveTask/ApprovedStationTask → RecipePin`，而不是“默认用当前选中的测试用例”或“无匹配就自动回退到固定任务”。
5. **研发不扫码仍直接单步/用例/勾选范围真实 TEST**；生产运行有明确匹配、预检、操作员确认和同一 R1 安全门禁。无授权不 Flash。
6. **生产批次/一次检测会话/每用例 RunId/正式出厂放行是四种不同主键**，不靠统计 distinct DUT 推断新增出厂，也不能改写历史报告。
7. 硬件、规则和 Recipe 修改后的 SHA 与 station epoch 重新确认；Unknown/资源过期/多条有效候选/型号不符全部 fail closed。

## 2. 四种模式术语统一（不可无序 fallback）

| 统一枚举 | NI S-04 表述 | 产品路由 S-04 表述 | 概念 |
| --- | --- | --- | --- |
| `ApprovedStationTask` | `CurrentTask` | `ApprovedStationTask` | 工位已发布且显式选择的任务→Recipe；SN 只标识该只 |
| `VerifiedLabelAttributes` | `ProductCode` | `VerifiedLabelAttributes` | 外部正式标签规则中的产品编码→发布映射→Recipe |
| `ApprovedWorkOrder` | `WorkOrder` | `ApprovedWorkOrder` | 外部工单/批号（及必要成员关系）→Recipe |
| `ExactSerialBinding` | `ExactDut` | `ExactSerialBinding` | 外部档案中逐号精确 SN→Recipe 或 SN→Variant→Recipe |

“验证标签属性”中的 `Verified` 仅代表**条码格式和外部规范经过审批**，**并不**意味着实际 ECU 硬件 DID 被读回验证。建议真实源代码命名采用不夸大的 `PublishedLabelAttributes` 或另设独立 `PhysicalIdentityAssurance`，避免业务误用。

**选择规则**：一次工作任务明确激活一条模式和一个版本化规则、绑定集合；若某条模式失败，不偷偷退化到另一条模式；所有结果保留决策来源，未批准模式不给出 Production Ready。

## 3. 对“扫码枪究竟是 USB HID 还是 COM”的现场证据不足

### 两份报告的差异

- S-04 NI/S-05 和初始 Issue #72、`START_HERE.md` 按**USB 键盘模拟枪优先**研究，依据是当前 WPF `KeyboardWedgeCapture` 确有专用输入框。
- 另份 S-04 产品路由调研提到用户历史设备可能是 USB/COM 串口枪，建议优先做真实 COM；但同时明确**实际型号/COM 枚举/协议/实机日志未确认**，当前 `SerialBarcodeCapture` 只有抽象接口并未开 COM。
- **这不构成实测硬件事实**。项目管理决策台账也将实际“扫码设备型号/配置”记为未证实。

### 推荐的受控结论

**接口选择必须以实机枚举和原始输入为依据，不能凭 USB 插头形状、源代码里的枚举或历史口头叫法推断**：
- 若 Windows 将实际枪登记为 `HID Keyboard`：R1 先完善已有专用焦点、CR/LF/Enter、来源标记、连续扫码与退焦校验。
- 若设备登记为 `USB CDC/Virtual COM`：需实测 `VID/PID/设备实例/COM/字节分帧/编码/断连重连`，再实现 `IVerifiedBarcodeLineSource` 的**真实**设备适配；不再继续假设键盘模拟。
- 若 Windows 支持 `HID POS Scanner`：可以后续单独适配以确认真实扫描器事件来源，不以普通 TextInput 假冒。
- 若接入了多个输入方式，存储 `CaptureSource`，生产审批允许的输入来源必须显式配置；研发可手工/粘贴，但不升级生产认证。

**先支持当前源码已有 HID 路径，并不排斥实际 COM 一旦被确认优先接真机**。首版验收必须记录设备型号、Windows 枚举、原始 scan 及结束符，不能只凭一条“规则匹配成功”PASS。

依据：
- https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/pos/barcodescanner-configure
- https://learn.microsoft.com/en-us/windows/win32/inputdev/about-raw-input

## 4. NI 调研在设计中的真正用途

- **Process Model / PreUUT**：将识别 DUT、报告与数据库操作隔离在 TEST 之外；对应 SCAN/Production 协调层。
- **Test UUTs / Single Pass**：独立生产连续扫码入口与无扫码研发测试入口；不能用生产准入规则阻断单步。
- **动态序列选择**：可按已知产品码选 TestProject/用例或复用同一 TestProgram，不强制每 SN 一行；NI 2005 的“未知前缀→默认 PDA”只是演示，不能照搬为产线安全决策。
- **Property Loader**：相同测量步骤可复用并按已批准产品规格加载**参数/限值**，AutoTest 后续可用冻结的参数配置实现而不另造 TEST DSL，R1 先以已保存 TEST 方案和 DBC/TCF 配置为主。
- **UUT_RESULT / SystemLink**：序列号、产品编码、工位、批次、运行结果相互关联而不混成单一“扫码结果”，且测试 PASS ≠ 出厂 ReleaseDecision。

依据：
- https://www.ni.com/docs/en-US/bundle/teststand/page/process-models.html
- https://www.ni.com/en/support/documentation/supplemental/08/teststand-process-model-development-and-customization.html
- https://knowledge.ni.com/KnowledgeArticleDetails?id=kA03q000001DleECAS&l=en-US
- https://www.ni.com/docs/en-US/bundle/teststand/page/uut-result-table-schema.html

## 5. 对原研究/代码实施的调整和建议准入

1. **保留 S-00 源码事实**，但之后任何提交需回读最新 R1。当前纯 SCAN 匹配不等于 R1 全过程接线；`RealTestWorkbenchService` 的 Development 记录标志和各用例真实 RunId 是需要 R1+RECORD 评审的实现点。
2. **S-01 “逐号必需”设为 SUPERSEDED_PROPOSAL**；S-04/S-05 的 `TestAssignment` 通用模式是新候选，项目管理尚未批准成唯一方案。
3. **R1 最小纵向切片**：明确已批准工位任务→外部编号捕获→纯匹配且解释选择来源→冻结当前工程 TestProject 有序子集、TCF/DBC/KEY/TBC/码率/Flash→预检→显示确认→唯一 R1 TEST。首先证明无 Flash 情况，之后单独安全验证 Flash。
4. **严格模式并行候选**：外部已提供 SN→型号配置的权威清单时，精确逐号路由优先于缺少实物型号确认的固定任务模式，但绝不能匹配失败就回退另一模式。
5. **正式生产上线前**：必须得到来自生产管理和 RECORD 的 `ProductAssurance` 及 `ReleaseDecision` 判定合同；未批准时可以展示已匹配/已准备/真实 TEST 完成，但不能统计首次放行。
6. **设备接口验收**：现场确认具体枪接口，在 A/HID 或 COM 的真实设备上核对原始输入、焦点/断线/连扫，替代模拟输入样例。
7. **无实机验收不宣称 PASS**；不修改 `R1/main/固件`；本研究分支只存审查文档。

## 6. 研究状态

`SOURCE_REVIEWED / NI_BENCHMARKED / ALTERNATIVES_COMPARED / DESIGN_RECONCILED / POLICY_AND_FIELD_PENDING`。

真实编号规则样例、是否混线、外部清单/工单真实存在性、ECU DID 核验能力、出厂/复检定义及扫码枪设备类型，需要后续真实业务现场确认。本专项当前只提供能逐步验证的**备选工业产品设计**，不能把未明确的业务要求静默写死在代码里。
