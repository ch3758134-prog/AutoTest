# AutoTest · G4 全量实施准备与多对话并行编码合同 v0.2

> **审查状态：G4-PREP v0.2 / 待批准 / 尚未授权编码**  
> 上游：**G3 v0.2 已批准并本地 SAEF 落档**；G3 `PASS_WITH_LIMITATIONS`，8/8 `ACCEPTED`。  
> SAEF 文档 `main` HEAD（根据用户回执）：`5de99332170d9434c7e5b4a15c9d091245a7302c`。  
> G3 已批准 HTML SHA-256：`4711733cc2b7e5d125aadce9a97e574d877f4e4825d041f3eaaf8f778f35be46`。  
> 现役 HOST 新工程：**NOT_CREATED**；G4/G5 Gate：**NOT_REVIEWED**；G4 全部测试：**NOT_RUN**。

## 00 · 批准对象与更新结论

**你此次要审批的不是 G3，也不是 G4 Gate**，而是 G4 的具体实施组织方案与代码级工程合同。用户批准本 v0.2 后，可以组织首个 G4 工程初始化工作包的单独授权；这不意味着自动创建HOST、修改固件、开台架或完成SAEF G4工作产品。

**v0.2相对v0.1的补强**：完成了对旧HOST 318 份 `.cs/.xaml` 的文件级索引；对其中 41 份关键源码给出优先复用决策；把公共API原型落到 25 个待闭合类型族与23项接口检查；增加 54 个工程/测试配置或测试文件的唯一路径责任；为101个G3验证ID制定了代码类名/人工程序/证据入口；规范各开发对话的候选补丁、共享文件的唯一写入者和本地集成方式。

**不做的事**：不重新拆分G3的17模块、23CT语义、147原设计职责，不擅自改变现役两套固件，不把旧HOST当新HOST，不以Mock代替真实硬件证据，不以本档案的HTML视觉替代真实WPF。

## 01 · 项目真实起点与能力利用

现在的事实是：旧HOST源码存在，但新HOST工程/源码Git均不存在。旧 `src(20261030-022559).zip` 已在本轮直接读取，压缩包 SHA-256 `067026606ca7948cad3041a8655acf2265ea833e4df73fe176c2c010e582bcf8`。非生成源码 **265份C#、53份XAML，27个csproj**，且不含作为新工程可直接使用的受控 `.sln`/Git仓库。

复用工作采用三个层次：**纯逻辑算法优先迁移**、**保留时序/设备行为但按G3合同改造**、**只作为操作/问题参考**。不是一概重写，也不是原项目照搬。目录 `reference/OLD_HOST_SELECTED` 保存本轮列入迁移矩阵的原始源码字节，`OLD_HOST_REUSE_DECISIONS.csv` 每行都有SHA和新文件对应；这并未证明任何一行已适配新HOST。

### 01.1 关键复用方向

| 领域 | 具体旧文件与已发现能力 | G4决策及迁移验证 |
|---|---|---|
| 主控USB | `UsbProtocol.cs` (271行)、`UsbSession.cs` (511行) | 保留CRC/分片/SEQ/异步队列算法；必须增加generation隔离、晚到回包、断连恢复负测 |
| CAN | `CanBus.cs` (692行)、`CanUsbPayloadCodec.cs` | 旧收发/等待/订阅可参考，新增统一Hub、Lease、丢帧和时钟质量；不得私开多条硬件会话 |
| DBC | `DbcDatabase.cs` (423行) 及作者编辑与source-preserving导出文件 | 优先移植成熟解析/编辑算法；DBC升级必须影响SignalRef及运行快照 |
| 测试编辑 | `AuthoringTransactionEngine.cs` (502行)、UndoRedo、节点Registry | 保留事务语义；G3 23种步骤注册需重新映射，并提供树形插入/撤销/错误引用负测 |
| 执行引擎 | `RunExecutionKernel.cs` (559行) 及RunControl、Lease、Preparation | 保留If/Loop/Subtest等可靠控制流算法；RunSnapshot不可变，运行事实与裁决分离 |
| 波形 | `QuickWaveformComparator.cs` (163行)、CaptureEngine等 | 保留比较/对齐算法，增加单位/缺帧/时间质量与真实广播来源校验 |
| 记录 | `SqliteExecutionRecordStore.cs` (293行) | 数据库读写参考；正式结构须支持追加历史、失败Run和DUT去重统计 |
| UDS | `QuickUdsService.cs` (195行) | 旧实现不足以覆盖G3 UDS全部服务，不能直接复制成“完整UDS” |
| 旧WPF | QuickDesktop视图、ViewModel、Themes | 交互与视觉经验参考；新的Prism Shell/工作区结构按G3重建 |
| CanTool | `FlashConfiguration`/`FirmwareParser`/`UdsTransport`/`FlashSequence`等 | G3有静态参考索引，本轮未重读RAR原始字节；目标ECU专属协议绝不能推定为通用标准 |

## 02 · G4工作到底如何分工

**单一新源码工程**由INTEGRATION首次建立，管理九个生产程序集、工程引用、公共Core/Contracts、唯一ApplicationComposition和测试基础设施。**其他对话只编辑自己获准的路径**，以完整文件和测试代码打包交付给本地机械合并。

| G3模块 | 分支所有者 | 最早投入 | 目标 |
|---|---|---|---|
| G3-0 | INTEGRATION | V0 | 强类型公共核心+Contracts与Composition |
| A1 | DEVICE | V0→V2 | 主控USB协议与会话 |
| A2 | DEVICE | V3 | Station/IGN/夹具/按键 |
| A3 | DEVICE | V2 | 共享CAN Hub/租约/时间质量 |
| B1 | PROJECT | V1 | 工程资源、修订持久化 |
| B2 | PROJECT | V1 | DBC解析/编辑/信号 |
| C1 | TEST | V1 | 测试步骤编辑/Undo/Redo |
| C2 | TEST | V1→V3 | PlanCompiler/Runtime/Judge |
| D1 | UI | V1 | 视觉tokens/组件画廊 |
| D2 | UI | V1→V4 | Prism工作台及独立工具窗口 |
| E1 | SCAN | V4 | 扫码输入/离线规则 |
| E2 | UDS | V4 | ISO-TP/UDS/非破坏诊断 |
| E3 | WAVE | V4 | 参考/捕获/电机电流波形 |
| E4 | FLASH | V5 | 镜像解析/Mock编程 |
| R1 | RECORD | V3→V5 | 事实/记录/统计 |
| P1 | PRODUCTION | V5 | 生产工作流与流程阻断 |
| FW1 | DEVICE | V2→V3 | 双板固件兼容/版本回归 |

11个方向是责任单元，不等于必须打开11个网页对话。第一个构建基线完成后，最先开放`PROJECT / TEST / UI / DEVICE`；`INTEGRATION`继续掌握工程与跨模块验证；后续依据V波次开放SCAN、UDS、WAVE、FLASH、RECORD、PRODUCTION。

### 02.1 文件所有权不是口头约定

G3的147项唯一计划文件保持原路径和G3模块归属；其中`ApplicationCommandRouter.cs`的**唯一写入**改由INTEGRATION管理，业务设计仍属于D2。新的 `.sln/.csproj`、集中依赖、测试文件和Fixture都作为G4新增的独立矩阵管理；当前全量候选文件路径为 **201** 条，全部唯一，均处于NOT_CREATED状态。

E1/E2/E3/E4虽位于同一`AutoTest.Specialists`程序集，但写入权限按`Scan/Uds/Wave/Flash`目录树隔离；程序集项目文件、NuGet版本与Composition始终只有INTEGRATION一个写入者。任何跨边界更改先出影响清单，不让Codex自行调整。

## 03 · 新工程目录、项目引用与源码限制

### 03.1 先建立唯一工程，不一次写147个空文件

```text
AutoTest/                                ← 新的独立源码 Git 仓库（尚未建立）
  AutoTest.sln                           ← INTEGRATION唯一创建
  global.json / Directory.Build.props
  Directory.Packages.props / .editorconfig / .gitignore
  src/
    AutoTest.Core/                        强类型ID、结果、时间等
    AutoTest.Contracts/                   23项公共契约跨模块DTO
    AutoTest.Devices/                     主控USB/CAN/Station
    AutoTest.Projects/                    工程/DBC/资源
    AutoTest.Testing/                     编辑/PreparedPlan/Runtime
    AutoTest.Specialists/                 Scan/Uds/Wave/Flash
    AutoTest.Records/                     记录/统计
    AutoTest.Application/                 应用协调和唯一Composition
    AutoTest.Desktop/                     Prism WPF Shell/工作区
  tests/
    AutoTest.TestFixtures/                可重复的Fake/Clock/Replay/故障注入
    AutoTest.Contracts.Tests/             Core+Contracts契约
    AutoTest.Devices.Tests/               USB/CAN/Station
    AutoTest.Projects.Tests/              工程+DBC
    AutoTest.Testing.Tests/               编辑+运行
    AutoTest.Desktop.Tests/               VM/视觉人工验证准备
    AutoTest.Specialists.Tests/           扫码/UDS/波形/烧录Mock
    AutoTest.Records.Tests/               历史记录
    AutoTest.Application.Tests/           生产工作流
    AutoTest.Integration.Tests/           跨模块端到端Mock
  docs/engineering/                      源码配置/合同Revision/集成账本
  docs/verification/                     101项验证身份/现场证据
```

**九个生产程序集是G3的目标，不是要求V0把所有业务写完。** V0仅放能够构建的真正接口、Fake、Prism启动和Smoke；其他项目可以建立空的编译程序集，但不可宣称相应业务功能已实现。测试项目在实际使用波次建立，不必一次制造百余个空测试类。

### 03.2 依赖方向（锁定）

`Core`不得引用任何业务项目；`Contracts`仅引用Core；`Devices/Projects/Testing/Specialists/Records`只引用Core/Contracts及经批准的基础库，业务跨模块调用经Contracts；`Application`负责装配模块实现和协调；`Desktop`仅通过Application/Contracts交互、遵循Prism Region/VM，不持有USB设备句柄；各`.csproj`包引用只能由INTEGRATION修改。**不得通过重复公共DTO、反射桥接、随意添加项目相互引用来绕过编译错误。**

候选运行平台为与历史Windows工程兼容的WPF Windows TFM（历史QuickDesktop使用`net10.0-windows`）。但**目标Windows实际SDK版本、Prism 9授权、依赖NuGet恢复结果和选择的停靠组件必须在V0 Preflight后正式锁定**；本设计不猜测用户机器已具备这些条件，也不伪造已编译结果。

### 03.3 九个项目的一致配置

每个项目统一：Nullable启用、显式可见性、async取消、结构化错误码、状态所有者明确、单调时钟/UTC两类时间分开、资源引用内容指纹、不可变运行快照；`Desktop`设WPF TFM，其它项目除有明确必要外不依赖WPF类型。日志包含OperationId/RunId/StationId/版本指纹。对硬件副作用的API必须通过权限入口；GUI按钮不能旁路Station。更多约束见 `PUBLIC_TYPE_CLOSURE_REGISTER.csv`。

## 04 · 公共API闭合——先让23项合同真正编译

G3里的C#示例已经定义`StationId`、`RunId`、`AssetRef`、`OperationResult<T>`等，但也引用尚未展开的`SessionInfo`、`PreparedPlan`、`RunSnapshot`等。**G3是已批准的语义，不是已编译的C#接口程序集。**

V0由INTEGRATION唯一写入Core/Contracts，给每个CT依次闭合参数、返回值、DTO成员、命名空间、程序集、空值规则、序列化规则、取消/超时/错误码、状态和证据质量；提供可执行正/负合同测试。所有其他分支只消费经过Windows构建的同一公开签名。

**必须显式修正的原型可实施性问题（不得悄悄变成新的G3语义）**：

1. `OperationResult<T>`、`Unit`、无泛型的`OperationResult`必须一致；不能各方向出现不同成功/失败约定。
2. `PreparedPlan`/`RunSnapshot`属于Testing领域语义，但它们作为`IRunController`入参应由Contracts公开契约定义（Testing可使用内部细节），不能让Contracts反向引用Testing造成环。
3. `CanFrame`原型使用`byte[] Data`，即使声明record仍可修改；决定只读payload/clone-on-boundary，覆盖双消费者不互相污染测试。
4. `CanObservation.TimeQuality`原型的类型是`EvidenceQuality`；必须分开时间质量和证据可信度，且二者都不等于物理动作真实完成。
5. `SafetyAuthorization/LeaseToken`必须含来源、scope、生命周期，断连后失效；普通UI动作不会自动获得真机许可。
6. 所有`CancellationToken`取消、超时、晚到响应、Hub丢帧、Mock真实性等级都要落到状态/错误/证据结构，不允许吞掉。

这些是**G4实施时的接口闭合任务/待确认的细化点**，不等于用户已批准原型某种修改；如影响已批准语义，先提交受控变更清单。详见`PUBLIC_TYPE_CLOSURE_REGISTER.csv`、`PUBLIC_CONTRACT_CLOSURE_23.csv`。

## 05 · 与功能同步编写的测试代码

**G4负责功能代码+软件自身验证代码；G5负责正式系统级验收，G4中可以按准入开展早期真机实测。** 主对话/INTEGRATION提供共享Fake、统一数据身份和跨模块回归，各模块对话承担自己的单测与负测。绝不在G5才第一次写测试。

### 05.1 101项验证怎么交付

本版把G3-07的101个验证ID全部映射到：唯一责任方向、测试项目、候选测试类/方法、最早波次、模拟/Windows人工/实机验证等级、保存证据和前置条件。**映射不表示已经写了101个测试方法**，所有状态仍为NOT_RUN。

- 纯逻辑：如`A1-T01/T02/T03`帧CRC/拆包，使用真实固件黄金向量+错误帧负测。
- 跨模块：如`A3-T01`一条CAN同时交付DBC/UDS/波形，多消费者不重复占用设备；INTEGRATION另补端到端测试。
- UI：如`D2-T01/D2-T03`真实WPF启动、选中状态与运行高亮分离，必须有Windows截图/手工操作回执；单测只能补充，不能替代视觉验收。
- 物理：如`A2-T05/T06`按键16/默认IGN，必须有B0/B4前置和实物测量；模拟软件Pass不算台架Pass。
- 危险/专项：`E4-T07`真实烧录属于独立现场授权，不得在默认自动测试中触发。

### 05.2 每个分支最少交付内容

1. 本工作包要求的**完整源码文件**（包括.cs、.xaml与.csproj变更申请，未经INTEGRATION批准不可自行修改.csproj）。
2. 同一交付包内的**真实可执行单元/接口/Mock测试代码**，对应G3测试ID；不能只写测试说明。
3. 可重放Fixture/样帧/期待结果，或者明确列出的实机/人工验证步骤。
4. 完整文件列表、源码Git`BASE_COMMIT`、公共合同Revision、SHA-256、编译命令、测试日志（无法执行必须标明NOT_RUN）。
5. 出现API缺口需先CR，不允许为了通过测试编造第二套类型或Mock假阳性。

**验证执行**：Codex在Windows机械运行指定`dotnet restore/build/test`、WPF启动及允许的工具操作，原样回传输出；用户执行涉及电气安全、台架与UI主观验收的动作；ChatGPT负责分析、修改和补回归。G4-03/04/05是软件验证证据归档位置；G5-01/02/03才是正式整机验证工作产品。

## 06 · 多网页对话如何并行、如何合并

### 06.1 稳定公共基线先行

在`WPB-G4-INIT`真实编译并由用户确认第一个WPF/Mock可运行结果之前，**禁止分支对话各自创建一套完整解决方案**。先产生可还原的源码Git`BASE_COMMIT`、Contracts签名Revision和测试Fixture。随后主对话给PROJECT、TEST、UI、DEVICE发同一基线与对应分支包。

### 06.2 每个对话拿到的最小交接材料

`G3已批准HTML/八工作产品` + `G4 v0.2主档案` + `本方向branch_brief` + `旧源码对应选段` + `本轮允许写入的精确路径` + `BASE_COMMIT` + `CONTRACT_REV` + `测试ID/构建命令` + `真机禁令`。

### 06.3 一个合并周期的真实操作

```text
用户批准具体工作包与文件权限
         ↓
主对话发放固定源码Git基线+Contracts版本+独占路径
         ↓
各ChatGPT网页对话独立生成【源码+测试+哈希+差异说明】ZIP
         ↓
Codex只读核对Git分支/HEAD/允许文件/已存在文件SHA
         ↓
逐包机械应用（不得冲突覆盖）→ 编译→对应单测→全量回归
         ↓
失败：停止，保留原始日志给主对话分析；绝不由Codex改代码
成功：真实WPF启动/实测（适用时）→ 源码Git提交 → 新基线
         ↓
主对话发下一批修订基线给受影响的分支
```

不同网页对话**不共享内存，也不会自动看到其他对话所写代码**。不能在分支里让ChatGPT自行假设某文件已经合并成功，更不能把功能完成写成编译通过。若多个分支依赖同一个公开类型，必须待其签名由INTEGRATION真实编译后再发任务。

### 06.4 集成失败的责任

- 源码编译失败：INTEGRATION定位报错/依赖；责任分支输出完整替换文件；Codex再次机械应用、编译并回报。
- CT不一致：停止合并、生成CR及受影响分支清单，公共接口由INTEGRATION唯一修订，经所需审批再分发。
- 跨模块/运行状态错误：INTEGRATION补跨模块重现测试，协调具体模块修复；不得把责任推给用户。
- Windows UI不符合：UI方向修改Visual/VM文件，必要时由TEST/PROJECT修复业务接线；不因Mock通过就宣布UI通过。
- 物理动作异常：保留原始USB/CAN/电气测量；避免只凭ACK推断夹具或IGN有效。

## 07 · V0～V5分批可运行目标（与已批G3一致）

| 波次 | 最小完整交付 | 第一验收证据 | 边界 |
|---|---|---|---|
| V0 | 唯一起点：WIN HOST工程 + 可编译Contract + 真WPF启动+Mock | INTEGRATION | 核心CT; Mock Run smoke; Windows截图 | 不能接真实IGN/夹具/键或自作硬件动作 |
| V1 | 工程/DBC + 编辑器 + Mock测试执行，真实WPF操作 | PROJECT / TEST / UI (+Record mock) | 保存重开、DBC样帧、节点编辑、控制流、预期/实际 | 先Mock，不宣称实机响应 |
| V2 | 真实USB PING与双CAN/DBC/Trace | DEVICE + INTEGRATION | B0→B1→B2及故障/映射实测 | 不因USB连上就执行IGN或输出 |
| V3 | 受控Station + 真实ECU测试闭环 + 记录 | DEVICE/TEST/RECORD/UI | B3/B4和H3闭环，物理测量与日志分开 | 无到位传感器不能输出已到位结论 |
| V4 | 扫码、UDS、波形专业工具和测试步骤 | SCAN/UDS/WAVE/TEST/UI | 扫码实物，UDS非破坏，回放/真实电流 | 不得运行擦写与未批准的安全访问 |
| V5 | Flash Mock + 生产流程 + 统计；实刷专项批准 | FLASH/PRODUCTION/RECORD | 错镜像负测、模拟断电/NRC、生产H4 | 真实刷写/擦除必须另行现场专项授权 |

每波次至少保存：源码Git提交、工作包/CT/测试ID、Windows环境、Build/Test实际结果、运行截图或原始USB/CAN/测量、预期/实际/未解决缺陷及可回退版本。不允许只有“147文件已创建”或“画了界面”就通过。V0/V1不应因真机不可用而停滞；真实台架从B0开始逐项准入，V5实际擦写仍独立审批。

## 08 · SAEF证据归档（不造新Gate）

| 已有SAEF工作产品 | G4具体写入证据 |
|---|---|
| G4-01 源码实施与配置身份 | 九程序集/新Git/版本+文件责任/迁移决策/真实代码提交 |
| G4-02 构建环境与可复现构建 | dotnet --info、NuGet/Prism版本、Windows恢复/构建/启动原始日志 |
| G4-03 单元测试与静态检查 | 各分支单测代码/日志、负测及静态检查、关联测试ID |
| G4-04 接口契约与仿真集成 | 23CT可编译签名、FakeStation/MockBootloader、模拟协议负测 |
| G4-05 系统模块集成 | UI→Project/Testing→FakeStation→Records，逐波次端到端运行证据 |
| G4-06 缺陷与受控变更实施 | 失败日志、责任归属、已批准CR、修复提交、回归差异 |
| G4-07 实现基线审查 | 已实现/未实现范围、构建/测试证据、待关闭限制及G4 Gate输入 |

**G5-01~03**承接正式端到端、实物台架、性能可靠性验证；G4已经做过的预研/集成实测可以成为G5计划输入，但未经正式环境/配置复核不能自动替代G5证明。

## 09 · 首个工作包 WPB-G4-INIT（待单独授权）

**实施主责：INTEGRATION**。只在用户授权后，基于G3/本v0.2交付真实`AutoTest.sln`、九个候选项目的必要.csproj、Core/Contracts、ApplicationComposition、Prism Shell最小启动、FakeStation与至少一条有事实输出的Mock操作、可复现测试工程和Windows机械执行指令。

### 准入检查（先收集，不替Codex选型）

| 项目 | 真实所需输出 | 不允许的猜测 |
|---|---|---|
| Windows | Windows版本、`dotnet --info`、SDK及WPF构建能力 | 不能把历史net10配置当作本机已装 |
| .NET/Prism | 目标TFM、Prism9许可身份、实际还原包与版本 | 不能未经核验升级依赖/替换Prism |
| Git根 | 新源码仓库绝对路径、工作区状态、是否有旧项目同名 | 不在SAEF文档仓库下创建src源码 |
| 外设 | V0禁连真板、无输出授权；Mock能力描述 | USB PING通过不能提前宣称 |
| 参考 | G3 SHA/23CT/迁移矩阵/旧源码SHA | 不能从文字猜固件HEX和线束 |

### 最小验收（都要真实证据）

1. `dotnet restore`、`dotnet build`、`dotnet test`有真实ExitCode=0（本阶段实际建立的项目）。
2. `AutoTest.Desktop`真正启动并展示WPF Shell和Mock状态（Windows截图），不是独立HTML外观。
3. 发起一条明确Mock命令，看到请求→执行事实→结果投影；断连/取消等至少一条负测，不能把Mock动作标成真实硬件动作。
4. Core/Contracts公开接口可真实编译，标明23 CT哪些已实现哪些只是保留原型；跨模块签名没有循环依赖。
5. 新源码Git初始提交和工作区状态、构建环境、合同Revision、G3上游文件SHA全部可追踪。
6. 任何源文件、本地编译失败、未满足许可、G3合同冲突时，停止该工作包并回报，不可由Codex自行修复。

**WPB-G4-INIT**是G4组织内的候选工作包标识，不是新的SAEF Gate，也不替代正式G4-01。用户批准此组织文档，亦不自动授权运行该包。

## 10 · 开放事实、限制和项目安全

O01～O16沿用批准G3的开放记录及最晚关闭节点，见原始`G4_OPEN_FACTS_16.csv`。现役主控/按键板固件暂冻，依严重软件无法补救的硬件缺口才走变更程序；禁止在现有项目中杜撰DUT/按键板电源控制。真实CAN0/CAN1→逻辑总线以B0/B2证据验证；IGN默认输出和高边电路要在B0核实；夹具断电动作依气路配置，且无到位传感器；KEY16问题在B4实测前保持OPEN。

USB请求/响应CRC/SEQ0异步、CAN15B载荷、0x730按键20bit掩码采用现役固件证据作为优先协议约束，源代码黄金向量尚为`DERIVED/NOT_RUN`。扫码枪PC USB直连，不经主控板。软件发布版本/板卡/固件源身份必须分开。

## 11 · 你批准v0.2之后的实际流程

1. **只批准G4实施组织**：主对话登记已批方案与限制，不自动让G4工作产品变ACCEPTED；SAEF控制依其正式规范做受控记录。
2. **单独授权WPB-G4-INIT**：写清源码仓库路径、目标Windows工具链、允许创建文件/测试和最小验收；由本主对话交付完整新工程源码而不是让Codex自主决定。
3. **Codex机械创建与验证**：只复制、恢复、编译、启动、运行测试、提交Git和回传结果；任何失败原样停止，ChatGPT修复。
4. **你试用第一个真实WPF**：至少完成启动、Mock操作和查看运行事实；通过后冻结源码`BASE_COMMIT`和`CONTRACT_REVISION`。
5. **发出第一批分支包**：PROJECT/TEST/UI/DEVICE领取同一可编译公共基线，各写功能+测试+完整交付包，主对话负责协调/裁决、Codex串行合并。
6. **V1～V5逐波次真实集成与实测**，每次由你看可运行版本，修复后受控提交；最终整理G4-01～07真实证据申请G4 Gate。

## 12 · 本次请求你批准的东西（以及不包括的）

**请批准的范围：** 17模块与147文件责任不变；工程与测试文件增补及唯一写入；旧源码按矩阵迁移审查；23CT在V0单点闭合并形成编译合同；101测试由各模块+INTEGRATION分工实现；多网页对话按同一Git基线和文件权限交付，逐波次集成与真实WPF/台架验证。

**未获本次授权的事项：** 创建源码仓库/任何新HOST代码、G4实施Gate通过、主控/按键固件修改、任何未核准物理输出、真实ECU擦写、认定历史源码已经迁移测试通过。

**审查建议：** v0.2可作为实施组织方案候审，但不能仅凭文档宣称“代码可编译”。真正能否多对话稳定编码，取决于首个V0工作包实际生成、编译并冻结公共合同基线。该阻断点已经在计划里明确。
