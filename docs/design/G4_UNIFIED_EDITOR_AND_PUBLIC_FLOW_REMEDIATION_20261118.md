# AutoTest｜G4 自动测试编辑器与工程资源统一整改实施清单（2026-11-18）

> 代码线：`feature/g4-html-parity-20261115`。所有状态是当前分支**源码提交状态**，不等于 Windows Build、自动测试运行或台架实测通过。
> 管理依据：`Project_Management/docs/project-management/new-baseline-20261112/G3-SUPPLEMENTS-20261116/` 下已批准的 `CR-PUBLIC-FLOW.md`、`CR-KEY-DIRECT-STEP.md`、`APPROVAL-AND-STATUS.md`，及冻结自动测试 HTML。**不引用 quick 分支**。
> 主线原则：唯一 ProjectDefinition / AssetEntry、唯一 TEST StepNode DSL、唯一真实解释器和设备授权；不造第二套运行器、不用 Mock 冒充真实通过、不静默迁移历史资源、不直接改 main。

## 顺序 00｜冻结边界与源代码核查
- [x] 核实 G4 正在开发的实际源文件和 UI 入口：`MainWindowViewModel`、`TestEditorViewModel`、`TestStepInspector`、`ProjectWorkspaceService`、`DbcWorkbenchViewModel`、`RealTestWorkbenchService`。
- [x] 核实已批准 G3 公共流程/直接 KEY 的产品语义，不把 quick 草案当管理合同。
- [ ] 用本地 Windows 的**精确当前 HEAD**构建、启动和查看冻结 HTML 布局差异，不依赖浏览器临时模拟。

## 顺序 01｜工程打开和资源加载（先保证可用性）
- [x] `OpenProjectAsync` 状态栏分阶段给出“正在读取工程清单／正在恢复编辑器”，失败不错误恢复老文档；支持查看所耗毫秒数。
- [x] `ProjectWorkspaceService.OpenAsync` 原本已经把 manifest 读取移到工作线程；不能把“工程等待”误判为该函数同步阻塞。
- [x] 选中普通 IGN/KEY/WAIT/流程步骤时不再强制读取整份 DBC 信号目录；按需加载 CAN/信号/波形。
- [ ] Windows 现场计时分解：打开 manifest、工程树项目引用读取、恢复上次编辑文档、DBC 解析、Inspector 构造、WPF 主线程刷新；长工程逐项统计。
- [ ] 对大型 DBC 做索引/缓存的内容修订失效处理（仅在确有性能证据时实施），取消不必要的重建与重复磁盘访问。
- [ ] 验收：工程不依赖 USB 连接即可打开、编辑；错误资源能定位；UI 不冻结、失败不丢旧草稿。

## 顺序 02｜工程资源树（区分资产、引用和编辑器）
- [x] 工程树增加“公共流程”独立分类与创建入口；它不属于生产测试项目的测试用例列表。
- [x] DBC 资源仍位于工程树，单击选择，双击或专门工具入口打开完整编辑窗口。
- [x] 历史 Controller 资产不因新产品语义而在工程打开时被删除；避免静默破坏旧配置。
- [ ] 统一新建、复制、重命名、移除引用、另存、已打开文档激活与固定修订的交互。
- [ ] 彻底清理新工程日常操作中不再批准的独立按键配置入口；旧资源可读且明确标识历史状态。
- [ ] 新工程与旧工程均检查：未加入项目的用例、项目包含的用例和公共流程不能重复算生产包或重复提供不一致的资源身份。

## 顺序 03｜DBC 专业编辑器
- [x] 完整 `DbcWorkbenchView` 改为拥有主窗口 Owner 的独立 WPF Window（不是主 IDE 中央页），保存仍经过原 DBC 资源修订/SHA/冲突检查。
- [x] DBC 导入后直接打开该独立编辑器；单击工程树不会弹出阻断性窗口。
- [ ] Windows 校验打开/关闭、未保存询问、文件回读、重复打开、跨工程切换、编辑器下拉弹窗的 Owner。
- [ ] 保留实测过的中文别名、枚举值、物理值、ID 标准/扩展、波形/实时解码一致性；如 DBC 存在不支持安全回写的原语法，明确只读或受限修改。
- [ ] DBC 保存更新后已引用步骤不得自动改为最新修订；步骤中选择 DBC 报文/信号时不强制打开完整 DBC 编辑器。

## 顺序 04｜统一添加步骤与右侧 Inspector
- [x] 原有新增步骤继续在中央步骤表插入真实 StepNode，右侧常驻编辑，不另造弹窗/草稿对象。
- [x] 新增 IGN 无需再搜索“操作对象”：由固定工位生成正式 Ignition Mapping；直接编辑开启/关闭。KEY 直接操作生成 RawKey 现行兼容路径，不重复要求下拉选择。
- [x] CAN/DBC 信号/波形需明确资源时仍有精准选择器，并保留原来的版本绑定；不能因为取消通用“操作对象”区而取消真实资源引用。
- [x] Step Inspector 显示固定版本公共流程选择器而不是要求输入 GUID/SHA。
- [ ] 每种业务类型逐项审查：IGN、独立夹具、KEY、CAN 单次/周期/停止、Read/WaitUntil/AssertSignal、Wait、IF/ELSE、SWITCH/CASE、WHEN、Loop、Try/Catch、变量、UDS、Flash、波形、公共流程调用。
- [ ] 消除多重“应用属性/保存用例/保存工程”造成的重复提交体验；所有变更保持统一 Undo/Redo、字段局部提示、非法值不丢草稿。
- [ ] 彻底复核控制流树/子 Slot 的鼠标左右键、插入前后、复制/移动/拖拽、断点、搜索与执行状态呈现，不允许只修表面 XAML。

## 顺序 05｜直接 KEY_SIM（已批准，但新的正式语义尚未完成）
- [ ] 新工程正常用户直接在 TEST/公共流程中设置 K01~K20 目标 mask、holdMs、after=Keep|ReleaseAll；不得用旧 RawKey Pulse/Hold/Release 假冒新语义。
- [ ] 标准 mask 覆盖 20 位、mask=0 表示全部断开、更新完整组合不是 OR；保持时间不隐式释放；after=ReleaseAll 发送明确清空。
- [ ] 手动台架独立维护可保存的连续 KEY/Wait/KEY/清空操作，不能要求用户先创建 Controller。
- [ ] DEVICE 验证 KEY_SIM 实际固件、合法掩码/禁用组合、CAN ACK 与五路真实电压之间区别、UNKNOWN、超时、取消与断连；未确认的物理输出必须 BLOCKED。
- [ ] 旧 RawKey/Button/Controller 和 TCF 引用只按原语义读；人工明确审查后才能迁移，保留原件备份。

## 顺序 06｜公共流程正式资源、编辑与调用
- [x] 新增 `PublicFlowDocument`（FlowId、单一 Body、类型化参数、固定引用、映射），`PublicFlowCodec` 复用 TEST 的 StepNode/Mapping 编解码。
- [x] 新增 `public-flow` 工程资产创建/打开/保存和 manifest Revision/SHA 校验及冲突拒绝。
- [x] 工程树可创建/打开公共流程，中央以连续步骤树编辑，复用既有 `StepEditTransaction` / Inspector/Undo/Redo，不创建第二套脚本引擎。
- [x] `CallPublicFlow` 作为明确步骤类型，右侧选择已保存的同工程公共流程、写精确 ID/Revision/SHA。
- [x] 提交 `PublicFlowGraphValidator` 递归依赖文件 SHA、固定修订、循环、深度/引用预算预检；保存 flow 前执行。
- [ ] 完成用例调用的固定引用表、显式版本更新、输入参数/局部变量/输出绑定、复制提取旧 Subroutine 时的兼容审查。
- [ ] 统一步骤表格视觉与冻结 HTML：公共流程只显示连续步骤，无假 Prepare/Test/Cleanup，也无独立生产运行入口。
- [ ] 公共流程内 Copy/Move/撤销/跨节点操作以及引入 DBC/波形等资源的完整 Windows 回归。

## 顺序 07｜真实自动测试编译、执行及证据（当前必须 BLOCKED）
- [ ] TEST/PROJECT 定义根测试及传递性公共流程/DBC/配置的完整依赖闭包，固定运行期间资源内容与统一总 SHA。
- [ ] PlanCompiler 解析调用图、类型化参数、作用域、循环、最大总步数和调用深度；缺失或不匹配的引用必须 STOP，不得从名称猜测。
- [ ] 同一个 `RealRunCoordinator` 增加 CallPublicFlow 调用帧/返回、取消/异常传播、局部作用域与显式输出，禁止另设 flow runner。
- [ ] 物理写授权必须覆盖**完整展开后的执行步骤**和 CAN/KEY/IGN 等端口，而不是只检查根 TEST 文档；复用现有 Station Lease、USB、安全 Fence。
- [ ] `RealStepFact` 与证据新增 CallSite/FlowId/Revision/SHA/SourceStepId/OccurrenceId/CallStack，重复调用或循环的同一 StepId 必须可定位。
- [ ] KEY 保持、flow 正常返回、TEST cleanup、物理 UNKNOWN 分开按已批准产品/设备安全策略处理；主控 ACK 不等于 DUT PASS。
- [x] 当前 `CallPublicFlow` 在 StepDescriptor 中保留不可执行标志，并加入回归测试，避免未实现运行解释器时误报真实运行通过。

## 顺序 08｜保存、运行、工程生命周期交叉验证
- [ ] Ctrl+S/关闭页签/切换工程/自动保存行为在 TEST、FLOW、DBC 上一致且失败不丢编辑。
- [ ] 工程中修改 DBC/Flow 后已打开引用给出明确 stale 修订，不静默替换；清理引用时不删除真实文件。
- [ ] 运行期间编辑使用固定快照，不允许实时读入被用户修改的步骤；未保存/无法验证步骤不得进入真实执行。
- [ ] 核对扫码选择子集、公共流程不计为生产用例、运行状态/日志/报告和真实证据来源，不做 Mock PASS。

## 顺序 09｜验证与交付门禁
- [x] 已加入公共流程模型编解码、身份/复制、创建保存重开、外部文件变化冲突、循环引用拒绝、CallPublicFlow 未接执行时 BLOCKED 的代码级测试。
- [ ] **Codex 机械验证**：拉取此分支精确 HEAD，`dotnet restore AutoTest.sln`、`dotnet build AutoTest.sln -c Debug --no-restore`、相关 Testing/Projects/Desktop.Tests；修复必须由本 UI/TEST/PROJECT 设计负责方在确定原因后提交，不让 Codex 自主改源码。
- [ ] Windows 人工操作验收：新建项目→创建测试用例→准备/测试/清理步骤→KEY/IGN/CAN/DBC 信号→公共流程创建/调用→保存/重开→版本冲突提示→DBC 弹窗→真实 Run 预检与设备授权。
- [ ] 真机执行验收以设备日志/CAN 帧/USB ACK/电压或 DUT 观测证据为准，不以预览器/编译通过代替现场验证；所有关键步骤收到真实证据前不得宣布 G4 PASS。

## 归档状态

本轮为 G4 **代码改造候选**：部分编辑/资源/窗口能力已经提交，尚未有 Windows Build/Test 或用户实测证据。尤其是 `CallPublicFlow` 实际执行、直接 KEY 新合同和硬件侧限制仍是 P0 缺口，不能把现有 UI 显示视为功能完成。所有后续提交保持本分支短期集成，不得自动合入 main。
