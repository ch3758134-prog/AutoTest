# AutoTest｜旧 WPF 实测资产与测试用例事实接收记录（2026-11-17）

> **记录类型**：研究证据接收与分类；**不构成产品设计批准、实施指令、代码合并、测试程序正式发布或现场运行许可**。  
> **记录状态**：`FACT_INTAKE_RECORDED / SOURCE_REVIEW_PENDING / DESIGN_NOT_APPROVED / FIELD_NOT_REVERIFIED`。  
> **管理分支**：`Project_Management`。原 P0/G0/G1/G2/G3 批准和 2026-11-16 G3 已批增量原文不修改。

## 一、事实来源及证据层级

用户确认四份历史压缩包：`WPF_APP_Rele_v1.zip`、`WPF_APP_Rele_v3_1.zip`、`WPF_APP_V4.zip`、`WPF_APP.zip` 曾被用于真实 ECU 测试并反复调整。本次接收的是历史专项调查 AI 2026-11-17 的静态报告，**不能等同于管理 AI 已独立逐行核验四份源码或逐项现场验证**。

- 已提供的详细报告：`AutoTest_旧WPF实测资产_项目管理AI事实汇报_20261117.md`，约 50KB，含 40 项源码观察与路径/行号，以及 44 条来源用例目录。
- 已检索到步骤逐行 CSV：`测试步骤逐行_含全部配置参数.csv`，308 行步骤记录（含表头总 311 行的 Files 文本提取视图），保存“参数原样”、来源与旧条目路径。
- 调查提及的 `44条测试用例_全部原始字段与步骤.json` 与逐条原始用例 JSON 尚未在本次检索确认可用；**此时不得声称 44 条用例已具备无损导入源包或已经进入 AutoTest 工程**。
- 44 条为跨版本、备份、构建快照的**来源条目**，含重复；不是 44 条唯一的、均通过现场测试的正式生产用例。 `WPF_APP.zip` 在本次调查中未提供可审计的有效源码/用例内容。
- 当前主控板源代码协议对照：`Main_Control@16abba7fec94dc4b7fbdc05b91ee209b5c7efa12`、USB 文档 v0.2；Keil Build 与实物刷入/联机状态未验证。

**原始资产保护原则**：归档需保留原 zip SHA256、源路径、配置 JSON 原文、解码方式、来源版本、Step 的 enabled/参数以及所有未解冲突；不替用户将旧实际 ECU 数据发布为现行通用产品规范。

## 二、将 40 项源码观察按责任归档

| 责任分类 | 源码观察编号 | 管理归属 / 注意事项 |
|---|---|---|
| HW / DEVICE / KEY_SIM | A-01～09、A-24、A-39 | 完整 20-bit mask、CAN0 ID 0x730、0x80 全断与 ECU 正常无操作不同、KEY 后续显式动作、CAN 通道及旧电源假设 |
| PROJECT / DBC | A-10～20 | 原名与显示别名分离、中文搜索、V4Alias、DBC 物理值转换、参数复用、别名键值冲突 |
| DEVICE / TEST / CAN | A-20～27 | 原始帧/DBC、单次/周期启停、同一硬件广播表事务、CAN 实时视图与原始历史分离、观察≠判定 |
| UI / TEST 编辑 | A-28～33 | 常驻 Inspector、拖拽插入、多选复制、手改名称保护、单步/范围/完整运行、本次选测、波形参数同屏 |
| TEST / 运行器 / 公共流程 | A-34～38 | 历史组合/条件/公共序列、循环引用检查、执行结果引用、错误≠判定失败；不能继承旧执行器为第二套 DSL |
| ECU 历史测试与 WAVE | A-04、A-08、A-18、A-33、A-39、A-40 | 绿通 6144/192/576、旧 VCU 冲突、旧供电动作、初始阈值和版本差异；仅限具体 ECU 工程事实 |

分类允许一项交叉引用多个专业，不因分类就自动改变 G1/G3 已批准合同。

## 三、立即保护的五组具体事实

1. **KEY 三种状态**：编辑器新步骤默认掩码（旧样本曾为 0）、ECU 的正常无操作（绿通 `576/0x0240`）、已连接设备当前真实输出三者不同；`0x80` 全部断开不等同于绿通正常无操作；无 USB/CAN/ECU 真实证据时不自动认定已写入 576。旧样本绿通：驻车 `6144/0x1800`、释放 `192/0x00C0`。保持时间结束**不自动清零**。
2. **CAN 多帧周期**：V3.1 将相邻周期步骤批量提交；V4 分步骤 `StartPeriodicAsync`。主控当前 0x50 会替换整张 CAN1 周期发送表，容量最多 8 条。历史做法存在覆盖风险；正式方案须考虑跨流程/条件/循环持续活动的统一报文集合，不能只依赖“相邻步骤合并”。
3. **DBC 中文别名**：协议 `Name` 与用户 `DisplayName` 分离；十/十六进制 ID、中文/原名、注释和信号搜索；V4 `BA_ "V4Alias"` 只是历史导入方法。旧 VCU 中制动有效性别名值 1/原名值0，档位2/0，保留3为1/0，原始 dataHex 曾为零；**不允许在迁移时无提示选一个值当真实发送报文**。
4. **真实 UI 编辑经验**：Inspector 常驻、拖拽前后/子容器、精确选择、多选/复制、手工名称保护、基于 DBC 的物理信号编辑、本次勾选不改变项目持久 Enabled、CAN 以 ID/IDE 聚合当前显示并另留原始历史、单步/区间/整例调试。这些为现行 G3-01 UI 差异验收的高价值证据，不代表原 WPF 已复刻完成。
5. **旧用例安全**：用例 Enabled 与每条 Step Enabled 分别保存；人工 `prompt` 不被自动执行替换；历史电源动作不可在新主控上自动映射；`0x707/MotR` 1500ms、绝对容差0.25、超差比例0.15 属旧样本初始判定参数，不能发布成现行 ECU 出厂判据。

## 四、历史用例来源账本（仅来源数量）

| 来源 | 条目数 | 备注 |
|---|---:|---|
| V1 绿通 | 6 | 其中1条空用例，部分步骤 disabled |
| V3.1 绿通 | 8 | 包含长按、维修释放与多次操作 |
| V3 构建快照 | 9 | 含防溜坡、自动驻车及禁用项 |
| V4 绿通 | 6 | 基础功能和波形等 |
| V4 三功能 | 3 | 准备、静态驻车、静态释放 |
| 旧玉柴 | 2 | 静态驻车、静态释放 |
| 旧绿通编号备份 | 10 | AIR_EPB_0101～0109/0201，含 prompt/恢复 |
| **合计** | **44** | **308 条来源展开步骤，不去重、不认证为运行通过** |

迁移目录未来至少保留 `OriginArchive / OriginPath / OriginItem / OriginalHash / ECU+硬件版本 / OriginalEnabled / OriginalStepEnabled / OriginalParameters / ConvertedType / ConvertedResourceRefs / Conflict / MigrationStatus / FieldVerificationStatus`。禁止直接写入现行用户工程或启用真实自动运行。

## 五、待研究/审查问题（不是已批准新设计）

| 编号（本档局部标识，非正式 CR 编号） | 待完成事项 | 责任 | 当前状态 |
|---|---|---|---|
| LEG-01 | KEY 的 0、576、当前设备状态、0x80、保持/复位动作；KEY_SIM 实际固件核证 | DEVICE + TEST | RESEARCH_REQUIRED |
| LEG-02 | CAN 周期发送活动集与 0x50 整表替换、停止/断连和跨流程调用 | DEVICE + TEST | DESIGN_REQUIRED |
| LEG-03 | DBC 中文别名数据模型及两类旧导入兼容 | PROJECT/DBC + UI | DESIGN_REQUIRED |
| LEG-04 | VCU 原名/别名数值、原始 dataHex 冲突 | PROJECT/DBC + DUT 实测 | CONFLICT_UNRESOLVED |
| LEG-05 | 现行冻结 HTML 与历史高频交互逐操作对照 | UI + G4 WPF | GAP_REVIEW_REQUIRED |
| LEG-06 | 旧公共序列/条件/循环引用语义映射到唯一 TEST DSL | TEST + PROJECT | DESIGN_REQUIRED |
| LEG-07 | 旧步骤/用例 Enabled、prompt、局部调试收尾和异常/失败 | TEST + RECORD | DESIGN_REQUIRED |
| LEG-08 | 历史 WAVE 资源、阈值及旧跨版本差异 | WAVE + TEST | FIELD_CALIBRATION_REQUIRED |
| LEG-09 | 历史 44 条及 308 步无损导入、原件/原始配置校验 | PROJECT + TEST | SOURCE_ASSETS_REQUIRED |
| LEG-10 | 旧供电/SOL 命令与新双夹具/IGN、实际固件身份的受控阻断 | DEVICE + INTEGRATION | COMPATIBILITY_REQUIRED |

## 六、与现行已批准补充设计的关系

- [公共流程](../G3-SUPPLEMENTS-20261116/CR-PUBLIC-FLOW.md)：旧 `ReferencedSequence` 是调查输入，不能直接覆盖现有 `StepNode`、`CallSubroutine` 或 G3 已批的正式公共流程模型。
- [直接 KEY_SIM](../G3-SUPPLEMENTS-20261116/CR-KEY-DIRECT-STEP.md)：保持结束不自动 ReleaseAll、ECU 无操作不是硬件清零，将作为详细状态和 UI 验收补充研究；不恢复独立按键配置用户入口。
- [主控 USB 双夹具](../G3-SUPPLEMENTS-20261116/CR-MAIN-CONTROL-USB-CLAMPS.md)：不能自动转换旧电源/SOL 指令；固件源码已更新不等于现场已刷入，须核对真实版本。
- 现有冻结 WPF IDE 布局保持不变；历史 UI 细节可用于补充验收案例，不以旧界面取代冻结 HTML。
- 不重新审批 P0，不单独启动新执行器、不直接对接旧测试程序到真实 DUT。

## 七、管理后续流程

这次仅记录**调查事实及待判断事项**。下一阶段由项目管理出具 `G3-LEGACY-01 旧资源兼容及测试用例迁移合同` 和 `G3-LEGACY-02 UI/DBC/KEY/CAN 操作差异合同` 候审稿，连同精确 G4 分专业工作包交负责人批准后，才正式修改相关设计并允许开发。真实发送、波形判定与自动生产运行仍必须有实物与原始证据。
