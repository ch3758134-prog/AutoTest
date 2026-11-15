# AutoTest｜G3-05 → G4 实施计划、接口契约与验收门槛 v0.1

**状态：G3 候审交付，作为 G4 任务的实施说明；不表示本轮已修改 HOST 或拥有实体操作许可。**
**代码候选基准：** `local@a9a26ebfba1efcf032ff8ec8c435336a89469647`；
**UI 唯一严实施参照：** [冻结 HTML](../../../ui/archive/auto-test-20261115-ec75a9fe/AutoTest_UI.html) Blob `c1a55e295c0f5321736fa3a8a7db20e13415a727`。
**职责：** 项目管理给出精确设计、GitHub 受控分支和代码修改范围；本地 Codex 只拉 SHA、Build/Run/记录原始错误，不自主研究修复；用户 Windows/真实设备试用后 PM 诊断和更新。**关闭/不依赖耗时 CI，不以 Mock 冒充实机证据**。

## 1. 先做能让用户实际打开操作的 WPF（最高优先级）

| 切片 | 工作域/精确范围 | 交付与验收 | 阻断/回退 |
|---|---|---|---|
| **G4-UI-00** | 创建短期 `feature/g4-html-parity-20261115` 候选，基于精确、已审核的 `local`，固定 HTML SHA；梳理编译入口/调用者，建立无旧 UI 的重建目标 | 明确文件删除清单、引用交接、正常启动入口；禁止双 Shell | 任何对业务源码的未授权大重构回退 |
| **G4-UI-01** | **完全替换** `Shell/MainWindow*.xaml*`、旧 Shell VM、`Testing/TestEditorView*.xaml*`/旧 VM、`TestProjectWorkbenchView*.xaml*`/旧 VM、旧 `TestStepInspector*.xaml*`、旧高频参数/映射弹窗等 | Windows 启动默认最大化；对照 HTML U-01～U-14 结构、主题、树/页签/行、Inspector、菜单、焦点、右键；未保存/关闭防护 | 用户看到旧布局、第二套兼容界面、WPF 操作路径偏差均不能接受 |
| **G4-UI-02** | 接 `ProjectWorkspaceService`、`TestProjectDocument`、`StepEditTransaction`、`TestTargetMappings`、DBC/Controller 真资产；新增必要应用状态服务 | 新建工程、项目、用例；50+ 步骤编辑、层级、撤销/重做、保存/重开、DBC 资源选择与校验；本次选择不污染 Enabled | 资源失败、UI 卡顿、映射被丢失，回退候选而不恢复旧 UI 为产品路线 |
| **G4-UI-03** | 截图/键鼠逐项差异闭环、DPI/不同尺寸/真实工程工作量 | 用户 Windows 实测、每处 UI-DIFF 闭合，交付一个**严格按 HTML 可持续实测的自动测试编辑工作台** | 未经用户确认不允许把“接近 HTML”说成严格完成 |

**重要的先后关系：** UI-01/02 为**真实离线工程编辑**，不需要等全部实体步骤实现再让用户实测；但 UI-03 不能假装所有步骤真实可执行。未实现执行的步骤需要清楚“尚未支持/预检阻断”，编辑入口不能以不存在的后端直接报 PASS。

## 2. 真实自动测试能力按工作包继续接入，不倒退 UI

| 切片 | 合同/实现变化 | 真实验收 |
|---|---|---|
| G4-TEST-FLOW-01 | 用现有 `StepNode` + Slots 扩展 IF/SWITCH/WHEN/TRY/CALL/变量表达式、CAN 等；受限 AST、结构和资源诊断 | 条件真/假、类型错误、超时、循环/异常/子程序真实执行路径可追溯；不再有 HTML 支持但后端默默跳过 |
| G4-A2-00/01 | 项目级 Run、选择、计划资源快照、Station Lease、包作用域与项目作用域 Cleanup | 同项目多条用例有序运行，不因上一包结束自动撤销下一包所需工位权限；停止/异常正确释放所有权 |
| G4-A2-02 | ProjectRun/PackageAttempt/StepOccurrence 事实、断点/单步/跳过/运行到、安全取消 | UI 行状态/底部日志由真实事件驱动；物理动作不可回滚边界可见 |
| G4-DEVICE-01 | 测试步骤与硬件真实 CAN0/1、IGN、夹具/KEY_SIM 的已批准通路对齐 | 分离主控 ACK 与 DUT 响应，状态未知不误报恢复；不控制外部 DUT 电源 |
| G4-DBC-01 | DBC 信号值编辑、按真实报文编码/解码、实时监视与资源指纹 | 修改物理值确实改变相应帧，Byte order/Multiplex 错误明确报出 |
| G4-SCAN-01 | 扫码生产绑定+真实预检/授权+自动 Run，**移除 ProductionWorkflow 模拟路径** | 一次扫码一台 DUT，一条可追踪 Run；重复码/复测分开、不覆盖、不假造 PASS |
| G4-RECORD-01 | 运行事实/日志/报告与真实资源、判据、步骤、设备状态关联 | 保存/导出/打开报告、统计去重、失败证据；保存异常不得标 PASS |
| G4-WAVE-01 | 保留已批准 G3-WAVE 的来源/比较/参考与三态，先接正式 TEST 步骤 | 用户在自动测试真实采集和判定时反馈调整；优化不要求等待全部专业 Wave UI 定稿 |
| G4-FLASH-F1 | **独立分支/独立 EXE**：CanTool 配置、镜像导入、真实 USB/CAN1、只读 DID 与日志 | 实际 Windows 运行和设备证据；F2 物理刷写必须另行审查 |
| G4-UDS-TOOL | Flash Lab 的底层 UDS 通信可靠后再研究并设计独立通用 UDS 专业窗口 | 不在当前 UI 优先切片中重复写第二个 UDS 协议层 |

每项允许精确的 G4 小工作包批准，未批实体动作不得随 UI 切片偷偷执行。**不为了等独立 Flash/UDS/WAVE 的完美 UI 而阻止第一版自动测试 WPF 实测。**

## 3. 编码接口：优先在现有领域模型上添加最小适配

### 正式数据保持
```csharp
// 既有类型保留，不照抄 HTML JSON：
TestProjectDocument    // Id、Name、Packages（EntryItemId、PackageRevision/SHA、Enabled）
TestDocument           // TestDocumentId、Phases、Mappings
StepNode               // StepId、Type、Name、Properties、Slots
TestTargetMapping      // 资产 ID、Revision、SHA、DUT CAN/KEY_SIM、Controller ID
PreparedTestPlan       // 计划 JSON、Fingerprint、Diagnostics
```

### 候选新接口：UI 工作区（**新设计，不宣称现存**）
```csharp
record UiDocumentKey(Guid ProjectId, Guid AssetId, string Kind);
record StepRowProjection(
  Guid StepId, string Type, string DisplayName,
  string ParameterSummary, string ResourceSummary, int Depth,
  bool Expanded, string ValidationCode, string RunState);
interface ITestEditorWorkspace {
  Task<EditorOpenResult> OpenAsync(UiDocumentKey id, CancellationToken ct);
  Task<EditReceipt> ApplyAsync(StepEditCommand command, CancellationToken ct);
  Task<SaveReceipt> SaveAsync(UiDocumentKey id, CancellationToken ct);
  Task<ValidationReport> PreflightAsync(UiDocumentKey id, CancellationToken ct);
}
```
`StepRowProjection` 由现有 StepId/资产计算，无第二个持久 TEST 树。状态投影可以放在新 UI 模块，不需要为了重构 UI 先全库迁移。

### 候选新接口：项目 Run（G3-A2）
```csharp
record ProjectRunRequest(Guid ProjectId, Guid TestProjectAssetId,
  IReadOnlyList<Guid> EntryItemIds, string Mode); // Engineer / Production
record ProjectRunSnapshot(string ProjectSha256, string ConfigSha256,
  IReadOnlyList<FrozenPackageEntry> Entries);
record ProjectRunEvent(long Sequence, Guid ProjectRunId,
  Guid? EntryItemId, Guid? AttemptId, Guid? StepId,
  Guid? OccurrenceId, string Code, string EvidenceRef);
interface IProjectRunHost {
  Task<ProjectRunPreflight> PreflightAsync(ProjectRunRequest req, CancellationToken ct);
  Task<ProjectRunResult> ExecuteAsync(ProjectRunRequest req, CancellationToken ct);
  Task RequestStopAsync(Guid runId, CancellationToken ct);
  IAsyncEnumerable<ProjectRunEvent> SubscribeAsync(Guid runId, CancellationToken ct);
}
```

**所有接口都是 G3 候选设计**：实施前确定项目归属、序列化版本、实体作用域和异常映射；不得由 Codex 在出错时自主换名字/做第二套模型。

## 4. G4 验收矩阵与否决条件

| 验收项 | 方法/证据 | 不允许的偏差 |
|---|---|---|
| UI 像素/交互 | 同 HTML SHA、相同屏幕尺寸、100/125/150% DPI，截图/操作视频、差异编号 | 自己认为“更顺手”而换设计、隐藏左侧树/右 Inspector/底部 |
| 常用编辑 | 新建工程、多个项目、不同用例、30～100 步骤、多层 IF/SWITCH/WHEN、DBC 参数、右键/快捷键/撤销/保存 | 高频参数必须另弹窗、无意义的步骤空白/滚动 |
| 真实数据 | 资产 ID/Revision/SHA、StepId/EntryItemId、资源损坏/冲突、保存/重开 | HTML demo 或名称字符串作为产品数据库 |
| 开关/资源 | 真实 DEVICE 的互斥、同一 Run 快照、断连/取消、外部 DUT 主电源限制 | ACK 当真实 DUT 到位、生产输出绕过授权 |
| 流程执行 | 每个步骤/流程从 TEST 正式编译/执行器返回真实事实 | 用 UI 动画/计时器/虚构表格显示 PASS |
| 稳定性 | Windows 实机启动、编辑焦点、工程规模响应、XAML/异常日志 | 冒烟截图通过却复杂编辑卡顿或一删文件即编译出错 |
| 交付证据 | GitHub 精确 PR SHA、Windows Build/Run 结果、用户认可和已处理 UI-DIFF | 旧 Branch 历史 Build 或 Mock 测试代替本版验证 |

**G4 首次验收范围须清楚写“UI + 真实资源编辑”，不是“整个 R1 已完成”；随后连续迭代到真实自动测试和生产闭环。**

## 5. GitHub、Codex 和用户分工

1. PM 确认 G3 技术合同、负责受控 GitHub 候选并管理代码文件责任；UI 专项仍可在同一个 HTML 文件迭代其他专业工具。
2. 受控 G4 任务使用短期分支、明确源 `local` SHA、文件名单与回退策略；**用户本地 Windows 负责 Build/运行或让 Codex 按机械指令执行**；Codex 不自行调研、改逻辑、删除其他模块或合并 main。
3. 不要求每轮 GitHub CI；在本地完成必要编译/可操作实测并提交原始错误/截图。
4. PM 处理错误或差异后给精确修改；用户决定 UI 严格一致及实体操作是否接受。只有被验证/批准的精确提交才能选择性集成 main。
5. 独立 Flash 分支不得成为未经授权刷新主 DUT 的工具；和 HOST 的共享服务接口变化先通过 PM 跨模块审查。

**本设计准出建议：** G3 允许以受控限制批准，并进入**非实体** `G4-UI-00～03` 以及**Flash F0/F1 离线研究/配置 UI** 的实施审批；真实硬件输出、F2 刷写、全面自动测试结论仍按后续专项许可和证据。
