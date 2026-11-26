# 11｜AutoTest 多工程生产系统、扫码路由与未来双 EXE 架构研究

- 日期：2026-11-26
- 性质：产品架构研究 / 待实现设计基线；**不是已交付或正式生产验收**
- 用户明确方向：**当前先用一个 EXE；后续生产检测与工程调试各自成为 EXE；生产端只需要导入检测工程，不负责编辑 TEST/FLASH/DBC；生产端需要管理大量工程并根据扫码内容区分。**
- 本轮源代码：`R1@256eb2374a32e781eeff7e76a5a93bc6ae4a983f`
- 分支边界：本文件仅在 `Project_Management` 记录研究，`R1` 代码本轮未变更；`main` 受控。
- 证据级别：当前 R1 源码回读 + 外部官方研究；具体包格式、容量阈值、分步施工仍为**设计建议**，未经 Windows/ECU 实测。

## 一、研究判断

之前“单工位绑定一份固定 ProjectId、整条条码是唯一编号”的默认方案只适合**单工程实测**，不能作为长期多工程生产软件的主架构。不能为了减少用户操作而删掉生产必要的工程库、版本及路由；但也不能把这些工程管理功能堆在“工位设置”首屏。

应把以下概念独立：
1. **工程源（Engineering Project）**：TEST 用例、公共流程、DBC、波形、原生 FLASH、工程特殊动作等。工程开发端可编辑，身份稳定 `ProjectId`。
2. **可导入工程包（Installable Project Package）**：由工程端导出/保存时形成完整自包含闭包，封装工程资源、必要 DLL/算法及固件，附哈希与兼容版本；生产端仅导入，不在原工作目录打开运行。
3. **生产工程库（Production Catalog）**：保留多工程、多个只读版本，按工程名称/产品识别码/版本/状态索引；仅一条对每一产品条件生效的精确绑定。
4. **扫码解析和路由（Identity + Routing）**：物理扫码原文 -> 解析唯一序号/产品键/硬件版本/批次（字段存在才解析）-> 可信归属表（若需）-> 唯一已启用的工程包版本；0 或 >1 匹配必须拒绝而非猜测。
5. **工位设备配置（Station Device Profile）**：COM 扫码枪、主控、CAN 波特率应用机制、固定夹具输出与可信 START/反馈；**不含工程库大清单**。
6. **执行会话（Run Session）**：锁定已选工程包快照、实际扫码观测、硬件会话、TEST/FLASH/波形及记录，执行完成后独立归档；同一 ECU 复检沿用身份但创建新 RunId。

外观上：**当前一个主程序，只有“生产检测”与“工程”两个工作区**；报告、专业工具及设置是上下文入口；生产工程库作为生产侧管理窗口，不是一级工作区。未来两个 EXE 共用领域合同/引擎和设备后端，各自拥有独立 UI 与访问权限。

## 二、当前 R1 代码核对所得事实

| 当前文件或入口 | 事实与问题 | 重构动作 |
|---|---|---|
| `StationDeploymentStore.cs` | 全局 `station-deployment.json` 只 pin 一份工程 | 改为项目库中每个版本独立 pin、工位按扫描选择当前有效快照 |
| `StationProductionConfigurationStore.cs` | `station-published.json` 单份全量配置；发布时循环冻结全部绑定工程 | 改为工程安装/版本验证与路由规则版本分别管理；避免每次修改都全量重新发布所有工程 |
| `ProductionIdentityContracts.cs` | `Rules.Count == 1`、`Products.Count > 256` 报错；`ProjectId` 不允许重复，阻断一工程多个产品别名 | 规则与产品绑定以独立规范化表索引；支持多条码格式、产品别名和多工程版本 |
| `ProductionPreparationWindow.xaml.cs` | 混线须先打开每个工程再手工添加产品键；编号表以 CSV 整体读入内存 | 独立“工程管理”：批量导入、自动索引、映射表编辑/导入/验证，设备设置单独维护 |
| `AutomaticTestHomeViewModel.Scanner.cs` | 生产扫码解析成功后必须与 `_plans.Current` 当前工程完全相等；不同工程直接拒绝 | 扫码先 route 唯一工程，再进行安全空闲状态切换、资源闭包校验和预运行准备 |
| `MainWindowViewModel.Production.cs` | `ActivatePublishedProjectAsync` 已能验证、切换工程并回读 CAN 波特率，但被 `IsEngineerWorkspace` 限制，并非生产扫码自动切换链路 | 提取无 UI 模式依赖的受控切换服务，正式生产/工程调用使用不同授权策略 |
| `AutomaticTestPlanService.cs` | 正式模式恢复依赖一个已部署项目 + 上次打开工程路径 | 改为接受明确的工程包版本快照，不使用“上次打开的工程”推断正式产品 |
| `ProjectDefinition.cs`、`ProjectWorkspaceService.ModelIdentity.cs` | 仍残留可选 `EcuModelCode`/工程级 `BarcodeExpectation` 及其编辑函数 | 核查引用后在同一重构中清理旧职责；工程源身份与生产条码归属分离 |
| `NativeFlashPublicationStore.cs` | 原生 FLASH 发布配置和资源路径是工程目录相对路径，且包含固件/算法依赖 | 工程包导入时必须完整包含并验证运行资源，缺依赖阻断安装 |

## 三、条码匹配原则（不可回退为强制填写型号）

**条码有产品信息：** 例如虚构示例 `EPB-A|SN0001`，识别产品键 EPB-A、序列号 SN0001，产品键通过已配置关系绑定当前生效工程；工程名称可以随意命名，内部 `ProjectId` 不进入条码。

**条码只有独立唯一号：** 例如 `000012345678`，该数字本身不包含能被可靠解释的工程信息。若工位只有一个已启用工程，可以直接绑定该工程；若有多个工程，必须通过可信的离线编号归属表（或未来 MES）求得产品键。**不能根据编号长相或之前扫描的工程猜选。**

**混合条码格式：** 多规则解析应在启用时验证歧义；运行中任何解析不通过、产品键不存在、规则冲突、路由到多个生效版本、文件损坏或设备会话变化，都不可自动烧录/运行。工程管理界面提供验证样例、冲突清单和定位操作，而不是强迫每次扫码手动选工程。

**重复与复检：** 同一真实 ECU 识别号重复扫描不新增产品；如果实际重新启动一次检测，则生成新的 RunId、使用实际工程包版本并留下复检关系。如果同一个编号历史归属和本次路由互相冲突，停止并报告身份冲突。

## 四、工程导入与版本管理的建议产品流程

工程开发侧：编辑、保存、调试、检查 -> “导出供生产使用的工程包”；生成完整独立可导入文件（例如建议扩展名 `.atpkg`，不是沿用单个 JSON 作为可运行文件）。包含必需运行依赖、规范化路径清单、内容哈希、工程 ID/版本、必要的固件/Seed-Key 安全算法与 TEST/DBC/波形引用；不提供额外导入 CanTool XML 的生产步骤。

生产侧：**添加工程** -> 选择一个或多个工程包 -> 在后台校验完整性及兼容性 -> 复制到应用管理的只读工程库 -> 显示导入结果与待处理的路由冲突 -> 在无冲突时启用；若产品已有生效版本，则按安全策略安排更新/回退，不在测试中途热替换。

目录结构可参考：
`ProgramData/AutoTest/ProjectLibrary/<ProjectId>/<PackageHash>/...`
生产运行不得依赖工程师原目录、U 盘或网络共享在线状态。SHA-256 适用于一致性检验；如需证实来源，应另外采用签名/可信发行策略，不把哈希误当身份签名。

生产工程库建议使用本地 SQLite 索引，而不是一份容纳全部工程及十万编号的超大 JSON。索引实体：`Packages`、`ProductAliases`、`BarcodeFormats`、`SerialRegistry`、`RoutingRevisions`、`InstalledVersions`、`RunRefs`；硬件设备绑定另存，历史已使用版本不得因为更新而丢失报告可追溯性。

启动扫描后：
`物理原始扫码 -> 身份解析 -> 唯一工程包版本 -> 当前硬件无占用 -> 验证可执行快照 -> 应用/回读 ECU CAN -> 等待实体 START -> 夹具/前置 FLASH -> 完整 TEST -> RECORD -> 安全释放`。
**任何一步失败都保留当前可执行版本，不切换到半成品；工位不具备实体 START 和 RECORD 实际链路之前不得称正式生产通过。**

## 五、当前与未来双 EXE 的分割线

| 共享程序集/服务 | Engineering EXE | Production EXE |
|---|---|---|
| 工程数据合同、工程包读取与校验 | 创建/修改/导出 | 只读导入与安装 |
| TEST DSL/执行器、DBC、WAVE、FLASH、硬件设备、RECORD 核心 | 调试运行 | 固定工程版本生产运行 |
| 工程编辑器/公共流程/步骤单步 | 提供 | 不提供 |
| 工程库、产品路由、工位设备设置 | 可提供调试预览 | 工位管理员负责，操作员正常不进入 |
| 扫码、硬件互锁、正式结果 | 工程实测可调用，结果是调试记录 | 实体 START、完整证据与生产记录才能生成正式结论 |

**本期仍只编译一个 Desktop EXE。** 但在逻辑上划开 `EngineeringWorkspace`、`ProductionWorkspace`、`ProjectPackage`、`ProductionCatalog`、`BarcodeRouter`、`StationActivation`。未来两个 EXE 只是不同 WPF 外壳和启动组合，不能靠两个 UI 变量决定是否允许烧录或产生正式报告。

## 六、施工顺序建议

1. P0 数据合同/路由：定义工程包和目录、产品别名、多条码格式、版本选择唯一性；删除固定单工程部署的全局权威性，建立独立工程库。
2. P0 离线导入与安装：工程导出完整闭包，生产批量导入、内容检查、原子安装、差异更新/回退，完全无需重复编辑 TEST/FLASH。
3. P0 扫码自动切换：生产侧扫码原文先显示，路由到唯一工程包，再按硬件空闲/会话权限切换；失败和冲突绝不猜测、不启动。
4. P0 生产执行与记录：一次扫码对应选定包版本、物理 START、夹具、前置 FLASH、完整 TEST、独立 RECORD；缺现场接口照常阻断并如实显示。
5. P1 界面收敛：顶层“生产检测/工程”；生产侧管理入口“工程管理”、独立低频“工位设备设置”；高级路由配置按需进入，不设第三个工作区。
6. P1 双 EXE 后续拆分：共用服务与执行合同，删除产品核心的 WPF 特定引用和通过 UI 模式决定安全权限的做法。

## 七、必要验证矩阵

- 单工程无产品字段扫码：默认绑定正确；同一编号重复扫描不新增产品。
- 多工程带产品字段扫码：A/B 切换不同 TEST、FLASH、CAN 波特率，无需手动先打开工程；无法匹配或匹配冲突拒绝。
- 多工程仅序列号：无可信登记来源时拒绝；有归属表时能唯一匹配，历史归属冲突阻断。
- 工程规模：10 / 100 / 1000 份工程包导入、检索、切换、批量更新性能压测；10 万登记编号加载与查询，具体指标待 Windows 基准测试，不声称现有代码达标。
- 安全：缺固件、缺 DLL、缺 DBC、版本指纹变更、正在检测时导入更新、烧录中断、设备断开、 CAN 波特率回读不一致皆不能误通过。
- 审计：每次正式记录都含原始扫码、归一化编号、路由依据与版本、ProjectId+PackageHash、固件哈希、TEST 版本、RunId 和每步证据；复检独立。
- 双 EXE 可拆分测试：Production 侧无编辑接口，默认不载入工程开发编辑器，源码层不能由界面隐藏替代访问控制。

## 八、外部研究依据（不替代项目现场验证）

- NI TestStand User Interface Development Best Practices: https://www.ni.com/en/support/documentation/supplemental/08/teststand-user-interface-development-best-practices.html
- NI TestStand System Deployment Best Practices: https://www.ni.com/en/support/documentation/supplemental/08/teststand-system-deployment-best-practices.html
- NI Identifying Components to Deploy: https://www.ni.com/docs/en-US/bundle/teststand/page/identifying-components-to-deploy.html
- GS1 AI (01) GTIN / (21) Serial / (10) Batch: https://ref.gs1.org/standards/genspecs/21.0.1/

## 九、状态与权属

这是基于用户明确“双 EXE 长期方向 + 当前单 EXE + 多工程扫码自动匹配”要求的**研究性方案**。工程包文件规范、产品别名的准确字段定义及路由 UI 细节仍需通过真实历史工程和扫码样例进一步验证；不得声称已实现、通过现场验收或架构设计已达到 9 分。

仅按用户另行指令在 `R1` 施工；`Project_Management` 管理研究和门禁；不自行更改 main。
