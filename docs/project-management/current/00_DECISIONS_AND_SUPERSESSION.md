# AutoTest｜现行负责人决策及旧结论取代台账
**基线日期：2026-11-21｜状态：USER_CONFIRMED / MANAGEMENT_BASELINE｜管理分支：Project_Management**

本文件记录本轮负责人连续问答中已明确接受的产品决定。它**取代相同问题上的更早 P0/G1/G3/G4/R1/SCAN 叙述**，但不改写原历史档案，不表示当前代码或真机已实施。查看 [现行入口](README.md)、[详细产品模型](01_PRODUCT_SYSTEM.md)、[运行事实](06_R1_GAP_AND_ACCEPTANCE.md)。

## 决策索引（必须按这些要求开展开发）
| ID | 已确认的决定 | 对旧认知的纠正 | 落实责任 |
|---|---|---|---|
| D01 | AutoTest 是通用 ECU 开发、自动测试、生产检测一体平台；单 PC/单工位/一次一台 DUT | 不只是 EPB 出厂测试按钮软件 | PM/UI |
| D02 | **专业工具、通用资源、自动测试、生产检测分别定位**；共享底层技术，互不天然从属 | 工具不是 TEST 的附属页，资源不是生产或 TEST 私产 | UI/PROJECT/专业工具 |
| D03 | 手动台架、DBC、UDS、独立烧录、波形工具在**无工程**时仍能独立使用；按工具实际需求使用资源 | 不要求工具先加载工程；不强制共用生产设置 | UI/DEVICE/工具 |
| D04 | **一个 ECU 型号对应一个工程**；一套当前有效 DBC、TEST 体系、生产烧录配置、固件 | 不为同型号常规建立复杂变型/配方路由 | PROJECT/SCAN |
| D05 | 工程编辑**自动保存**、恢复上次工程、未完成草稿可恢复；改动不污染执行中的冻结快照 | 保存并非退出时反复弹窗；不因自动保存放宽执行有效性 | PROJECT/UI |
| D06 | TEST 用**文件目录式树**组织功能/故障等分类；目录无步骤和执行能力，只有用例含准备/测试/清理和步骤树 | 不强制多个 TestProject/生产运行范围选择 | PROJECT/TEST |
| D07 | 正式出厂按**树顺序执行全部已启用用例**；开发仍可单步、用例、勾选范围 | 生产不由操作员逐台勾选、点击运行 | TEST/PRODUCTION |
| D08 | 用例失败后如何诊断、收尾、继续/终止由可编辑 TEST 逻辑决定；有合理**默认模板**且不强制反复编辑；模板管理低可见 | 不增加独立全局失败脚本/多套策略；也不能把非 PASS 统统代码写死停止 | TEST/PROJECT |
| D09 | 工程独立管理 DBC、参考波形、当前固件；DBC 导入后纳工程，自动保存；WAVE 工具负责采集参考，TEST 引用判定 | 资源不依赖原导入路径，波形不只是画图 | PROJECT/WAVE |
| D10 | 生产扫码编码包含**型号+唯一编号**；每次扫码核对工程；同型号不弹窗、已知异型号确认后自动切工程、未知拒绝 | 不让扫码重新选择 DBC/TEST/固件；复杂条码模式和设备类型尚待验证 | SCAN/PROJECT |
| D11 | 正式生产：放置 ECU → 扫码 → **自动夹紧** → **等待台架实体启动键** → 本次烧录 → 全部自动测试 → 保存结果/报告 → 收尾释放 → 等待下次扫码 | 扫码后不要求操作 PC 点击“开始”；不在扫码瞬间直接擦写；实体按键为目标设计待实测 | PRODUCTION/DEVICE |
| D12 | **每次完整出厂检测必须重新烧录**，包括复检；烧录失败不进入 TEST；生产烧录是前置工位阶段，非要求工程师插入 TEST Flash 步骤 | 旧 NoFlash/ConditionalFlash/在 TEST 中手工配置 Flash 为必要步骤的出厂模式被取代 | FLASH/PRODUCTION |
| D13 | 测试结束结果持续展示直到下次有效扫码，自动归档、回到待扫码；运行中不接受扫码排队/切换 | 无逐台确认保存或清屏 | UI/SCAN |
| D14 | 原始 CAN 日志为通信底本；TEST 执行中按步骤配置提取关键测量形成测试日志与简明单台报告；批量报告独立固定格式方向，细节未定 | 不做昂贵的报表编辑器；报告≠日志≠CAN 原始证据 | TEST/RECORD |
| D15 | 每次正式检测（PASS/FAIL/停止/烧录失败）应有独立记录与报告，失败不可覆盖历史；重复扫码统计不重复算新 ECU | 不能只给通过的 ECU 报告，也不能用次数当数量 | RECORD/SCAN |
| D16 | 独立生产检测工作区、工程师编辑调试工作区，仍是同一 AutoTest，复用同一真实 TEST/FLASH/RECORD 后端 | 不再将生产交互塞入 TEST 编辑页；不另造第二 TEST 引擎 | UI/R1 |
| D17 | 原 CanTool XML **所表达的烧录参数**应在 AutoTest 原生可视化编辑并保存在工程内固定位置；当前固件唯一；独立 FLASH 工具仍独立 | 用户不要每次导入外部 XML；工具的临时参数与生产配置并非强制同一份 | FLASH/PROJECT |
| D18 | 失败现场保存、台架按钮的实际输入、夹具真实到位判据、批次/复检统计、编码字段、报告格式等保留专项研究/实测缺口 | 不因研究文档写得复杂就自行宣告冻结 | SCAN/REPORT/HW |

## 对旧管理台账的具体裁决
| 旧条目及来源 | 新状态 | 新的执行要求 |
|---|---|---|
| [旧02 TEST-01](../current-20261121/02_DECISION_SUPERSESSION_LEDGER.md) 多 TestProject 执行语义 | **部分取代** | 底层可复用 TestProjectDocument，但产品上只有一个 ECU 工程中的有序 TEST 树；目录不执行 |
| [旧02 FLASH-01/03](../current-20261121/02_DECISION_SUPERSESSION_LEDGER.md) Flash 必须是 TEST 可插入步骤、按 Recipe 决定 NoFlash | **生产用途已取代** | 生产每次前置真实烧录；原 TEST Flash 源码保留作迁移/独立技术资产，研发端是否保留待专项审查 |
| [旧02 SCAN-02](../current-20261121/02_DECISION_SUPERSESSION_LEDGER.md) 扫码不得触发夹具动作 | **业务序列取代，安全条件保留** | 合法扫码可驱动已设计的压紧阶段；身份校验不代表物理就绪；烧录需待实体启动键和预检 |
| [旧02 SCAN-03/04](../current-20261121/02_DECISION_SUPERSESSION_LEDGER.md) 逐编号绑定表/多配方选测试子集 | **候审复杂方案不采纳为默认** | 型号→唯一 ECU 工程，工程自含当前 TEST/DBC/Flash；底层修订/SHA 核对保留 |
| [Issue #70](https://github.com/ch3758134-prog/AutoTest/issues/70) 与 R1 的 TEST 内 Flash + 人工运行范围 | **目标业务已更新** | 仍复用当前源代码；生产前置流程需迁移，原技术交付不等于现场通过 |
| [Issue #72](https://github.com/ch3758134-prog/AutoTest/issues/72) 复杂 Recipe + 扫码后人工确认/启动 | **目标业务已更新** | 扫码核对工程、夹紧、实体启动、生产全自动；条码格式/设备真机仍待验证 |
| [SCAN S-09](https://github.com/ch3758134-prog/AutoTest/blob/research/r1-scan-configuration-20261120/docs/r1-scan/S-09_SCAN_FLASH_PRODUCTION_RECIPE_INTEGRATION_20261121.md)、[S-10](https://github.com/ch3758134-prog/AutoTest/blob/research/r1-scan-configuration-20261120/docs/r1-scan/S-10_NATIVE_FLASH_CONFIGURATION_FIXED_PROJECT_STORAGE_20261121.md) | **研究证据保留，业务模型重审** | 保留真实烧录技术和原生配置诉求；多层 ProductVariant/Recipe/TEST Flash、工具配置强绑定不再预设为产品合同 |
| R1 “任何非 PASS 停止后续用例” | **策略被取代** | TEST 配置决定是否继续，但已观察的正式 FAIL 不可后续覆盖 |
| 旧硬件 USB/CAN/IGN/KEY 物理映射 | **继续有效、不得通过业务讨论改动** | 见 [硬件事实](05_HARDWARE_AND_RESOURCES.md) 与原 FW 文档 |

## 不可混淆的状态
**业务批准 ≠ 设计细节已定 ≠ R1 源码已改 ≠ Windows Build PASS ≠ 真实 ECU FIELD PASS。** 本轮是业务重整及管理落档授权，没有实施真实硬件控制、删除源码或合并 main 的许可。历史原始批准仍保留用于追溯；本文件只裁定**现行适用性**。

## 仍未确定
重复扫码按上次 PASS/FAIL 是否分流；批次边界/良率定义；实体按钮及机械传感器真实接口；失败后是否保持硬件现场；当前 R1 必须以完整生产闭环作为准出的版本范围。不得替负责人做上述决定。