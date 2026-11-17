# AutoTest｜TEST 公共流程、直接 KEY 组合与执行合同研究设计 v0.1

> 状态：**DRAFT / 待审批**。依据当前 GitHub `feature/g4-html-parity-20261115` 的真实源码调查编写。此文件是设计，不等于新增公共流程已实现、编译已通过或真实设备已验证。
>
> 约束：不创建第二套 TEST DSL / 测试执行器 / USB 会话。不复用旧 `Pulse/Hold/Release` 冒充 KeySet。不修改 main、固件或未经核准的物理输出。

## 0. 范围与对象身份

```text
工程 ProjectDefinition
 ├─ 测试项目 test-project                    [有序引用测试用例，不是执行树]
 │   └─ 测试用例 test-program                 [Prepare / Test / Cleanup 三段 StepNode 树]
 │        └─ CallPublicFlow → 公共流程 flow   [固定 AssetId + Revision + SHA256]
 ├─ 公共流程 public-flow                      [单一连续有序 StepNode 树；可嵌套调用]
 │   └─ CallPublicFlow → 其他公共流程          [相同引用规则]
 ├─ Controller / KEY_SIM 配置                 [HardwareKeyPolicy + ECU 功能状态]
 ├─ DBC / TCF / TBC / Wave / Flash 等正式资源
 └─ 运行准备：对 TEST / flow / mapping / 配置进行闭包快照
     → 唯一 RealRunCoordinator
     → 唯一 ConfiguredStationActionPort / HardwareConnectionService
     → RealStepFact + 设备事件证据
```

- **测试用例仍然有准备/测试/清理**。公共流程没有这三个阶段，只包含一个有序根步骤树。
- **公共流程不是测试项目条目，也不是整个测试用例的快捷引用**。它可从用例的任何合法阶段/子分支被调用，亦可调用其他公共流程。
- **步骤语法唯一**：`StepNode`、`StepDescriptorCatalog`、同一套子 Slot、StepId、参数、校验语义；只是文档根容器不同。

## 1. 已存在的正式实现：可直接复用与缺口

| 当前源码 | 可直接复用 | 必须补齐 / 不可误称已实现 |
|---|---|---|
| `AutoTest.Testing/Authoring/TestDocument.cs` | `StepNode`、StepId、Slot、递归 Walk/CloneNewIds | `TestDocument` 强制三阶段，**不可**把它原样序列化为 flow |
| `StepEditTransaction.cs` | 插入、复制、移动、重命名、属性、单次 Undo/Redo、原子 Mapping 变更逻辑 | 当前事务依赖 `TestPhase` / `TestDocument`；应抽取共享根树修改内核，不复制第二版编辑算法 |
| `StepDescriptorCatalog.cs` / `StepTreeValidator.cs` | 同类型节点、Slot 与属性校验 | 目前 `Subroutine/CallSubroutine` 声明但 `ExecutableV1=false`；`KeySet` 也尚不可执行。需按文档根类型复用树校验，并新增 flow 调用检查 |
| `TestDocumentCodec.cs` | 确定性 StepNode 及 Mapping 的持久化概念 | v1 强制三个 phase；应新增 `PublicFlowCodec` 存储不同文档外壳，**复用相同 NodeData 序列化逻辑** |
| `ProjectWorkspaceService.cs` / `AssetEntry.cs` | 已有稳定 AssetId、Revision、SHA256、原子保存/回读、工程 manifest | 新增 `public-flow` kind、`flows/` 目录、创建/复制/打开/保存/版本回读与依赖检查 |
| `TestProjectDocument.cs` | 测试项目已有有序用例引用 `ItemId + PackageAssetId + Revision + SHA` | **不**把 flow 当作 test-project Package |
| `PlanCompiler.cs` | 运行前结构检查、不可执行节点阻断、Canonical JSON + SHA256 | 当前只拍一份 `TestDocument`；需解析传递性 flow 闭包、捕获每份内容、锁定所有资源并计算总运行指纹 |
| `RealRunCoordinator.cs` | 唯一实际解释器、Wait/If/Loop/Write、取消/暂停、Cleanup、StepFact | 当前 `Subroutine/CallSubroutine` 执行会抛 NotSupported；变量是一个全局 Dictionary，Fact 不带调用路径；需同一解释器增加调用帧与 Occurrence |
| `ConfiguredStationActionPort.cs` | 经现有真实 USB、CAN0、物理授权执行 KEY_SIM，校验 Controller/DBC 引用 | RawKey 现为 `Pulse/Hold/Release`；`Pulse` 自动释放；常规 Cleanup 会 ReleaseAll；不符合直接 KeySet 的运行语义 |
| `KeypadCommandCodec.cs` | CAN0 标准帧 0x730、20-bit mask 编码、全部释放帧 | 不增加新固件 opcode；mask=0 应明确选用已确认的 `EncodeReleaseAll()` |
| `ManualBenchViewModel.cs` | 已选 Controller 资产 Revision/SHA 校验，命名 ECU 动作到 KeyMask，独立勾选 20 Key，真实日志 | 手动台架**必须继续引用正式按键配置**，不能因新增 KEY 直接步骤而删掉 Controller 绑定 |
| `RealRunEvidenceRecorder.cs` | 收集真实 CAN RX、硬件事件、运行 Fact | 需要 flow 源资源、嵌套调用路径与动态 Occurrence；不能把设备 ACK 当 ECU 电压测量或 PASS |

### 当前必须澄清的事实

- `TestTargetKind.RawKey` 当前不绑定 Controller 资源（AssetId 为空）。它与配置功能按键混用会被拒绝。这不能直接满足“按受控硬件配置进行新的 KeySet 执行”的要求。
- `RawKeyAction.Parse` 明确只接受 `Pulse:mask:ms`、`Hold:mask`、`Release`，且不接受 Hold mask=0。**不得**将其当成新的连续编排语义。
- `RealTestWorkbenchService.HasPhysicalWrites` 目前只查找 `Write` 节点；增设 `KeySet`、嵌套 flow 后，必须在**展开的有效计划**中判定实体输出和请求授权。
- `RealRunCoordinator` 的 `RealStepFact` 当前只有 StepId；同一 flow 可被多个调用点/循环反复执行，单凭 StepId 无法区分每次执行。

## 2. 公共流程持久化合同（建议）

工程资源 `AssetEntry.Kind = "public-flow"`，默认扩展名 `.flow.json`，目录 `flows/`。稳定资源身份/修订/内容指纹以正式 `AssetEntry` 为权威；正文另存独立 `FlowId` 用于复制后的文档身份检查。

```json
{
  "schemaVersion": 1,
  "id": "FLOW-GUID",
  "name": "驻车操作公共流程",
  "steps": [
    {
      "id": "STEP-GUID",
      "type": "KeySet",
      "name": "K02 + K04",
      "properties": {
        "target": "KEY_SIM",
        "mask": "0x0000A",
        "durationMs": "300"
      },
      "slots": {}
    }
  ],
  "mappings": [],
  "flowReferences": [],
  "parameters": [],
  "localVariables": [],
  "resultParameters": []
}
```

说明：`FLOW-GUID`/`STEP-GUID` 仅表示格式占位符，不是预置演示数据。正式 codec 用 Guid + StepId 的既有验证规则。真实 KeySet 列表可多条连续编辑，引用 Controller 硬件策略、或调用其他 flow。

- `StepNode` 与 Slot 结构复用原 TEST 编码逻辑；`PublicFlowDocument` 只新建连续根树和文档元数据。
- `mappings` 与 TEST `TestTargetMapping` 同型，不构建另一套对象引用。映射名只在当前文档局部生效；编译时转换为调用帧中的已解析资产绑定。
- `flowReferences` 是文档级固定引用表：`alias, assetId, revision, sha256`，每个 `CallPublicFlow` 节点只引用 alias，不手填 GUID。对已有 TEST，`FlowReferences` 作为**可选追加字段**加入 codec，旧文件不需要迁移三阶段结构。
- `parameters/localVariables/resultParameters` 第一个版本允许空数组，但必须是**明确版本化的类型化槽位**（将来有 `name, type, nullable, default` 等），不可用无结构、无版本的任意字符串字段替代。
- 新建 Copy：生成新 AssetId + FlowId，内部所有 StepId 重建；普通另存修订：沿用 AssetId、FlowId 和 StepId。

## 3. 编辑器复用

建议抽出共享 `StepTreeEditCore`，其操作目标为“根步骤集合 + 子 Slot 路径”，而不是 `TestPhase` 本身。现有 `StepEditTransaction` 作为 TEST 三阶段适配，公共流程使用同一内核的单根适配：

- 同一个 `StepNode`、属性编辑器、插入动作目录、StepId、复制、剪切、移动、Undo/Redo 和“未保存”状态。
- 用例视图照旧展示三个阶段；flow 视图只展示一个**连续步骤表**，不存在隐藏 Prepare/Test/Cleanup。
- 正式的 `CallPublicFlow` 选择器列出同工程的已保存 `public-flow`，明确当前版本与 SHA；编辑时只能“保持固定版本”或“显式更新引用”，**不自动跟随最新**。
- 循环 / IF / 子 Slot、断点、右键单步、运行到、失败定位沿用通用 UI 组件。尚未可运行的节点必须清楚显示“编辑可用、执行未实现”，不能生成假执行成功。
- 保存必须使用现有 ProjectWorkspaceService 的并发冲突、文件哈希、修订 + manifest 双重回读规则，不让 UI 直接写 JSON。

## 4. KEY 直接设置步骤：语义冻结建议

新增唯一 `StepNode.Type = "KeySet"`（不将 `Write + RawKeyAction(Pulse/Hold/Release)` 伪装为此步骤）。

字段：
- `target`：从同工程已保存 Controller 资源中选择一个**硬件级 Key 策略映射**，其 AssetId/Revision/SHA 已固定。建议新增 `TestTargetKind.KeyHardware`，指向 `EcuControlProfile.KeyHardwarePolicy`，不改变既有 `RawKey` 含义。核对与当前 TCF/Controller 一致且属于一个 KEY_SIM CAN0。
- `mask`：无符号 20-bit，允许 `0x00000`，范围 `0 .. 0xFFFFF`。界面用 K01～K20 多选，展示准确位值和结果 mask；原始字符串统一规范编码。
- `durationMs`：明确填写等待时长，建议初版允许 `0..60000`，且 `0` 表示下发后立即进入下一条而**不是自动释放**。必须在 Inspector 显示此含义。更长时长交给独立 Wait。

**执行顺序：** 验证目标资源快照/可用掩码/禁止组合 → 同一个已授权的 CAN0 发送路径发送 **一个**目标帧 → 等待主控 ACK（不等于 ECU 已识别）→ 等待 `durationMs` → 标记这一步运行结束。无第二次下发，无隐式 Release。

| 步骤 | 请求 mask | 动作保持 | 下一步开始后 KEY 实际期望 |
|---|---:|---:|---|
| KeySet K02+K04 | 0x0000A | 300 ms | 继续保持 0x0000A，直到下一次显式设置 |
| KeySet K07 | 0x00040 | 100 ms | 继续保持 0x00040 |
| Wait | 不发送 | 500 ms | 保持上一步输出，不改变任何 CAN0 Key 帧 |
| KeySet K01 | 0x00001 | 200 ms | 继续保持 0x00001，除非后续明确切换 |
| KeySet 全断开 | 0x00000 | 显式时长 | 用已确认的 `EncodeReleaseAll()` 控制板卡断开 |

**物理输出事实：** 记录 requested mask、CAN0 ID/opcode、设备 ACK / 超时 / 错误、请求与回复时间、运行 StepId + Occurrence。切忌将 mask 当实测五路电压，更不能自动推断 ECU 已执行驻车/释放。

**状态不确定：** 一旦物理下发尝试后超时、断连或取消，电路可能已经改变，状态转 UNKNOWN；下一条物理操作必须服从既有 DEVICE 安全策略，不能只根据上一次缓存掩码继续执行。

## 5. 与功能按键、手动台架的关系

保持三种用途清楚分离：

| 用途 | 输入 | 资源依赖 | 输出行为 |
|---|---|---|---|
| ECU 功能动作（TEST） | Controller 功能名/状态，已配置动作时序 | Controller 配置固定版本 | 经既有 ButtonActionPlanner 驱动；沿用其语义 |
| 直接 KeySet（TEST/flow） | K01～K20 + durationMs | **相同正式 Controller 的 KeyHardwarePolicy** | 精确 mask + 等待，不自动释放 |
| 手动台架 | 选择已配置 Controller 功能动作，或手动勾选开关 | 必须选择并校验已保存 Controller 的 Revision/SHA | 现有单次真实授权发 CAN0；不创建独立测试运行器 |

关键：**手动台架不能撤掉按键配置引用**。改版按键配置中的功能状态依然供手动台架快速选择；硬件可用 Key/禁用组合也继续约束手动直接组合。主控 ACK/五路电压实际测量/ECU 识别结果仍分开显示。

现有正式 TEST 禁止在同一运行中混用 RawKey 与 Button，目的是避免两路状态机互相覆盖。建议首版 **KeySet 与功能动作混用时运行前报错**；后续如需混用，必须先把状态机统一为一个 KeyOutputOwner 并设计显式交接规则，**不得绕过现有冲突检测**。

## 6. 固定公共流程调用、依赖闭包与循环检查

新增 `CallPublicFlow`，不要修改既有的局部 `CallSubroutine.target` 语义：它目前属于 TEST 文档内部子程序名且不可执行。新节点字段是 `flowRef`（引用文档级固定 `flowReferences.alias`），日后可增加 typed arguments / outputs。

编译前的 `PublicFlowResolver` 从当前工程 manifest 解析 `assetId`：
1. Kind 必须是 `public-flow`，同时匹配固定 Revision/SHA，读取正文并再次比对字节级 SHA；缺失/漂移必须明确中断并提示“更新引用”。
2. 递归解析所有被引用的 flow，校验每份文档 StepId、节点结构、变量绑定和 mapping。调用图按 AssetId 做灰色递归栈检测：`A → B → A` 必须在**执行前**拒绝，给出完整路径。
3. 限制最大调用深度（建议 16）、最大依赖数（建议 256）、展开最大步骤访问预算（例如 10 万，包含 Loop 倍数，实际门槛在实施前明确测定），杜绝失控深嵌套/循环。
4. 形成 `PreparedRunBundle`：root TEST canonical JSON + 每份 flow canonical JSON、固定 AssetRef/Revision/SHA、Controller/DBC 配置依赖和统一 bundle SHA256。**运行中不再读取最新工程文件**。
5. 用户显式更新 flow 时：只变更当前引用和依赖锁定版本，通过一次原子编辑事务保存；不静默更新其他调用者。

`ProjectDefinition.Dependencies` 可作为工程层资产关系索引，但它现有的 `AssetDependency` 只存 RequiredRevision；**SHA 仍必须由文档引用与准备快照强制核对**，不能只依赖工程关系表。

## 7. 单执行器与调用帧

沿用单一 `RealTestWorkbenchService` 和 `RealRunCoordinator`：
- 入口仍由 TEST 项目/用例启动。解析依赖闭包后一次性构造真实 station 授权范围，包括**所有被调用 flow 的实体步骤**；不得只扫描根 TEST 文档中的 `Write`。
- `Execute(nodes, context)` 新增 `CallPublicFlow` 分支：压入 `CallFrame`，同一个解释器调用目标 `steps`，结束后返回调用点下一条。没有 flow 专属运行入口、专属授权或独立 USB 会话。
- `CallFrame` 推荐字段：`flowAssetId / flowRevision / flowSha / callerStepId / invocationIndex / locals / inputBindings / outputBindings`。调用栈运行时不能突变冻结资源。
- 每次调用独立局部变量字典。根级变量只按显式输入或输出绑定传递，不允许公共流程通过全局同名变量意外覆写调用者。类型化参数第一阶段可限制为空列表，但结构必须可扩展。
- 取消、单步、暂停、失败、超时在调用栈内传播。普通 flow 返回不执行 TEST Cleanup，也不自动修改 Key 状态；异常流的 finally 行为只能按正式语义触发。

**Occurrence 必须加入结果合同：** 当前 `RealStepFact.Step` 在同一 flow 被多次调用/循环时会重复。建议增加 `OriginAssetId`、`CallPath`、`LoopIterationPath` / `OccurrenceId` 的可选版本化字段，用 `RunId + call frames + step + iteration` 稳定识别每次实例。真实证据事件也挂这一键；旧版无路径的报告仍能读取，不能编造后补路径。

测试结论：`Completed` 表示该步骤执行完成，不等于 ECU PASS；仍只有明确断言/判定能给出合格结果。

## 8. 收尾策略：流程返回、TEST 结束、异常停机严格分离

| 边界 | KEY 输出规则 | 责任 |
|---|---|---|
| KeySet 步骤完成 | 不额外发送 Release，不恢复上一组 | TEST 节点语义 |
| 公共流程正常返回 | 保持当前已授权 KEY 输出，继续主流程 | 解释器 |
| TEST 准备/测试/清理阶段之间 | 没有未声明的 KEY 复位 | 测试程序 |
| 整个 TEST 正常结束 | **当前代码** `ConfiguredStationActionPort.CleanupAsync` 会 `ReleaseAll`；是否允许保持，需另行审查受控工位安全政策 | RUN/DEVICE 合同，不是 flow |
| 取消、设备异常、ACK 丢失 | 以安全策略处理 UNKNOWN、物理授权撤销与最后防护；不得声称电气状态已恢复 | RUN/DEVICE 合同 |

这解决用户要求的“公共流程结束不自动恢复所有 KEY”，但不能擅自将**整个测试运行后的物理安全收尾**删掉。若产品最终要求运行完成后仍保持 KEY，则需单独审核、授权并变更安全合同，不能靠隐藏的流程返回行为实现。

## 9. 接口 / 文件影响（建议实施位置）

| 路径 | 设计变更 |
|---|---|
| `src/AutoTest.Testing/Authoring/PublicFlowDocument.cs`（新） | 单根 `StepNode` 树、局部映射、版本化参数与固定 flow 引用 |
| `.../PublicFlowCodec.cs`（新） + `TestDocumentCodec.cs` | 共享 NodeData 编码，追加 TEST `FlowReferences` 可选字段 |
| `.../StepEditTransaction.cs` | 提取公共编辑内核，不复制 Undo/Move/Copy；用例仍保持三阶段 |
| `.../StepDescriptorCatalog.cs` / `StepTreeValidator.cs` | 新增 `KeySet`、`CallPublicFlow` 描述、步骤目标/时长/引用校验；兼容旧 TEST |
| `src/AutoTest.Desktop/Projects/ProjectWorkspaceService.cs` | `public-flow` 资源 CRUD、复制、修订/SHA、快照回读，目录 `flows/` |
| `src/AutoTest.Testing/Preparation/PlanCompiler.cs` | 递归解析和冻结完整依赖图，预检循环/深度、全量实体授权集 |
| `src/AutoTest.Testing/Runtime/RealRunCoordinator.cs` | 同一解释器调用帧、KeySet 执行、局部变量、Occurrence、异常传播 |
| `src/AutoTest.Testing/Runtime/TestPorts.cs` | 在原 ITestActuationPort 上设计强类型 `SetKeyMaskAsync`，而非新设备端口/拼装 Pulse/Hold |
| `src/AutoTest.Desktop/Testing/ConfiguredStationActionPort.cs` | 复用真实 CAN0/物理授权，验证 KeyHardwarePolicy 并执行精确 mask；保持无隐式 Release |
| `src/AutoTest.Desktop/Testing/RealTestWorkbenchService.cs` | 调用图整体预检、授权范围全闭包及固定运行指纹 |
| `src/AutoTest.Desktop/Testing/RealRunEvidenceRecorder.cs` | flow/callPath/occurrence 与实际 CAN0 命令证据关联 |
| `src/AutoTest.Desktop/Testing/TestStepInspector.xaml.cs` 等 | KeySet 20 开关 + 时间、公共流程选择器（有版本），复用正式 Inspector |
| `src/AutoTest.Desktop/Shell/MainWindowViewModel.cs`、工程树 | “公共流程”正式资源分类、单一连续编辑器入口 |
| `src/AutoTest.Desktop/Shell/ManualBenchViewModel.cs` | **继续**从 Controller 提供命名操作/可用掩码；不移除已有 Revision/SHA 安全门 |

`KeypadCommandCodec`、WinUSB 命令码、CAN0/CAN1 接线、固件 **不计划修改**。

## 10. 实施门禁与验收矩阵（全部为待执行）

分四个可审查工作包，禁止一口气宣称已经运行：
1. **PF-01 模型/存储/编辑**：公共流程新建、复制、移动、撤销、保存/重开；旧 TEST 三阶段原样回读；无 USB 写操作。
2. **PF-02 引用/依赖/编译**：跨 flow 固定引用、显式升级、循环检测、SHA 漂移与运行快照，完整诊断。
3. **PF-03 KeySet / 正式执行**：现有 RealRunCoordinator + 强类型 CAN0 KeySet，run 前确认硬件配置/授权，真实 ACK/UNKNOWN、明确持续保持，mask=0。
4. **PF-04 证据/可视化/Windows 实测**：按 CallPath+Occurrence 分层时间线、错误回溯，取消/断连/异常处理，实测 screenshots。

最少测试场景：
- A：新 flow 无三个 phase、编辑/复制移动/撤销/重做/保存重开后 StepId 规则正确。
- B：TEST → A → B 嵌套连续顺序正确；B 被两处调用产生不同 Occurrence。
- C：A → B → A、A → A、引用缺失/版本过期/SHA 改变、深度超限，**编译前拒绝**。
- D：K02+K04 300ms；K07 100ms；Wait 500ms；K01 200ms：只下发 **3 次** CAN0 KEY 目标（无中途额外 Release），等待期间零 Key 帧。
- E：mask=0 显式发送全部断开；非法位、禁止组合、未确认硬件、错误 TCF、超时、断连都拒绝/标 UNKNOWN。
- F：正常 flow 返回不发送 Release；**整个 Run 结束**按已经审批的 RUN/DEVICE 安全合同收尾（不得用 flow 规则代替）。
- G：手动台架仍列出真实 Controller 已配置的 ECU 功能操作，版本过期会阻止发送；手动与正式 TEST 互斥。
- H：真实 CAN0 请求、主控 ACK、ECU 观测/波形、电压未知和最终判定分别保留证据；缺省不伪造 PASS。
- I：1366×768/1920×1080 编辑窗口、取消/单步/断点/滚动及重进编辑都正常；需来自**实际 Windows** 截图。

## 11. 研究结论 / 等待批准的关键点

1. 建议批准使用新的 **public-flow 资产容器 + 原 StepNode DSL**，避免把 TEST 的三阶段藏起来当公共流程。
2. 建议批准新的 **KeySet 强类型步骤 + pinned KeyHardware mapping**，不改变旧 RawKey(Pulse/Hold/Release) 语义。
3. 建议首版禁止混用配置功能按键状态机与直接 KeySet，直到统一占有权和切换规则审查完毕。
4. 需要单独冻结**整个 TEST 结束时**的 KEY 安全收尾策略；当前代码会 ReleaseAll。公共流程返回绝不自动 Release。
5. 在得到审批前，不修改正式 TEST 领域合同、工位接口或真实执行器；本文仅是研究设计，后续编码按 PF 工作包执行，真实 Windows/硬件验证单列证据。
