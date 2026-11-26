# AutoTest R1｜SWITCH / CASE 正式执行合同与源代码交付记录

**执行日期**：2026-11-25（America/Los_Angeles）  
**实施分支**：仅 `R1`；`main` 未操作  
**性质**：源码及测试定义已提交；**未运行 Windows 编译、CI、WPF 键鼠或 ECU 实机验证**。不代表生产准出。

## 0. 为什么本轮优先处理 SWITCH

此前完整产品目标蓝图要求：工程师直接在唯一 TEST 步骤树中建立真实流程控制，编译器与解释器同源；不能让添加面板展示一个在执行时根本不能工作的步骤类型。R1 历史项目实际保留了 `Switch/Case/When/TryCatch` 类型，但当轮核对发现 `Switch/Case` 在步骤目录标记不可执行，`RealRunCoordinator` 缺对应分支，变量检查也不理解多分支。仅修改 UI 标签会造成假功能。

这次只接通能定义清楚静态语义的 SWITCH/CASE。尚未实现的 WHEN、TRY/CATCH/FINALLY、子程序、公共流程继续保持执行阻断，不声明功能完成。

## 1. 单一 TEST DSL 合同

- `StepNode(Type="Switch", Properties["variable"], Slots["cases"])`；`Case` 只能插入 `Switch.cases`，每个 `Case` 有字面量 `equals` 和独立 `body`。
- 条件来源必须是前面运行步骤实际建立、当前有效的测试变量。源码级必达来源检查防止使用后置赋值、只在一个 IF/SWITCH 分支中出现的变量。
- `CASE` 采用大小写不敏感的固定字面量**精确字符串**匹配；不隐式数字转型。`default`（大小写不敏感）保留为默认分支标签；不允许 `$变量` 动态标签。
- 全部启用 CASE 值唯一，默认 CASE 至多一个，CASE 总数 ≤64，至少一个已启用 CASE。
- 运行期只执行第一条唯一命中 CASE；未命中则执行 default，若无 default 只记录 ConditionFalse。**不 fall-through**。
- 来源缺失或状态无效：`NoData`／`incomplete`，不将 UNKNOWN 视为 ELSE/default，不继续后续正常测试实体写入；原先脚本清理和强制资源收尾合同继续适用。
- SWITCH 和选中的 CASE 分别产生真实 `StepOccurrence`，下属 CASE.body 保留 `ParentOccurrenceId` 和执行路径；动作 ACK 不自动成为功能 PASS。
- SWITCH 中全部分支（包括 default）都确立的变量才可能作为下游必达变量；**没有 default 时**必须保留未命中的路径，不能据此推出下游变量必然存在。
- CASE 本身只是控制结构，不授权任何新 CAN/UDS/FLASH 物理目标，不绕过项目资源、运行身份及硬件物理栅栏。

## 2. 落地文件

| 文件 | 本轮改动 |
|---|---|
| `AutoTest.Testing/Authoring/StepDescriptorCatalog.cs` | 将 Switch 和 Case 纳入同一正式可执行步骤目录 |
| `AutoTest.Testing/Authoring/StepTreeValidator.cs` | CASE 唯一性、空分支、最多 64、禁止动态值等编译前结构约束 |
| `AutoTest.Testing/Authoring/TestVariableFlowValidator.cs` | SWITCH 上游来源与分支出口必达变量分析 |
| `AutoTest.Testing/Runtime/RealRunCoordinator.cs` | 唯一解释器执行 SWITCH/CASE；真实 StepOccurrence；UNKNOWN 阻断 |
| `AutoTest.Desktop/Testing/StepUiCatalog.cs` | 启用流程控制选择器，标记正确行为 |
| `AutoTest.Desktop/Testing/TestEditorViewModel.cs` | SWITCH 提供实际可用的前置变量候选 |
| `AutoTest.Desktop/Testing/TestStepInspector.xaml.cs` | SWITCH 源变量、CASE/default 的任务化编辑字段与正确提示 |
| `AutoTest.Testing.Tests/SwitchFlowContractTests.cs` | 8 条纯 AST/PlanCompiler 回归测试源码，不含伪造硬件事件 |

**注意**：`StepInsertionTarget.cs` 原有 `Switch.cases` 只接受 CASE 的验证已回读；不复制实现。新增的类型与该现有入口配合。

## 3. 本轮没有做的事及关键风险

1. Windows 本地编译：未运行；八条测试定义**未执行**。用户本地 Codex 才承担 Windows 拉取/编译/启动任务。当前提交不能标记 BUILD PASS。
2. CASE 运行期端到端回归：未执行；需要分别复核有效命中、default、无 default、无数据、STOP 和嵌套 IF/LOOP 的实际 StepOccurrence 与设备许可；不能以编译器测试替代解释器实体执行。
3. `WHEN` 的新鲜数据/超时/稳定次数和 `TRY` 的可恢复错误集合尚未实现，继续运行前阻断。
4. 公共流程参数、返回值、递归阻断和完整冻结引用仍是下一专项 P0，不能用 `Switch` 合并完成替代。
5. **生产仍 NO-GO**：实体 START 的可信电信号适配、夹具安全许可及真实反馈、整台 FLASH→ProjectRun→InspectionAttempt→不可变报告及复检均无完整现场证据。现有工程验证不能升级成正式 PASS。

## 4. Windows 验证任务（Codex 只拉取编译启动）

1. 精确检出本记录对应的最新 `R1` HEAD，确认没有跑错旧 EXE；不操作 `main`。
2. 只在 Windows 编译 `HOST/AutoTest.sln` 并运行自动化测试，报告 `SwitchFlowContractTests` 原始通过/失败数、原始文件与行号；**不要改代码或启动 CI**。
3. 启动实际 `AutoTest.Desktop`，进入 TEST，新增“设置变量→SWITCH→两个 CASE→子步骤”，检查行间插入、变量候选、双击持久编辑、自动保存、关闭重开与错误提示；不得将“可以启动程序”称为逻辑真机验收。
4. 真机测试只有在硬件授权、冻结资源和现场安全条件满足后进行，分别留存 ECU CAN/步骤/周期任务/真实记录。

## 5. 后续整改优先级

- **P0，生产主链**：工装及实体 START/STOP 输入的真实硬件合同与准入调查；已发布资源→原生 FLASH/APP 后验→连续 TEST→单次不可覆盖 Inspection 的正式终端状态及报告，明确与工程调试隔离。
- **P0，TEST 语义**：WHEN、TRY/CATCH/FINALLY、公共流程的真实解释器语义、合法失败类型、证据及版本闭包；不能让不存在的执行能力在添加选择器里被误认为可用。
- **P1，工程师效率**：CASE 表格批量编辑/默认值直接操作、IF/WHEN/CASE 与 DBC 信号的结构化条件构造、复杂编辑浮窗与 CAN 监视的 Windows 实际键鼠验证。
- **P1，独立专业工具**：DBC/WAVE/UDS/FLASH/手动台架共享受控物理能力，但参数草稿独立；信号采样真值、DPI、自动保存、窗口生命周期待实体复验。

**Gate：SOURCE_COMMITTED / STATIC_REVIEW_ONLY / WINDOWS_NOT_RUN / HARDWARE_NOT_VERIFIED / PRODUCTION_NO_GO。**
