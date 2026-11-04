# G0-06 · HOST 现有代码事实（M2 历史代码研究）

> SAEF v2.0 / G0-06。**M2 内容候审；本地 G0 仍为 NOT_STARTED，B0～B6 新复验未执行。**本文件仅记录来源可核的板级/代码事实、负责人报告的历史行为和待现场验证事项，三者不能等同。

> **证据定位：历史 HOST 源码快照已可读；当前有效软件配置尚未确认。** 本文不设计或修改新 HOST，不将此 `src` 直接改称现役可发布代码，不将曾编译生成的 DLL 当成现场验证结果。

## 仓库与当前配置身份

| 项目 | 实际核查结果 | 证据层级 |
|---|---|---|
| 研究输入 | `src(20261030-022559).zip` | 原始历史压缩包 |
| SHA-256 | `067026606ca7948cad3041a8655acf2265ea833e4df73fe176c2c010e582bcf8` | ZIP 字节身份 |
| 压缩包中的工程 | 27 个 `*.csproj` | 压缩包静态清单 |
| C# 源码 | 265 个 `.cs`（排除 `bin/`、`obj/`） | 压缩包静态清单 |
| `.sln` / `.slnx` | 0 个 | 该 ZIP 的文件清单；不代表原仓库没有解决方案 |
| `.git` / HEAD | 0 个 / 未提供 | 不能证明对应哪次历史提交，更不能称为当前 HEAD |
| 构建/测试/台架 | 本轮未运行 | `BUILD/UNIT/BENCH = NOT_VERIFIED` |

本文件由用户明确指定为**历史研究参考资源**。不能把这个 ZIP 作为当前已批准的 HOST 软件配置项；等用户本地当前仓库和实际构建回执到位时，须另行核对差异。

## 工程目录与项目依赖（源码级，非部署拓扑）

源文件对应的主要工程族：

| 工程族 | 目录示例 | 从代码能确认的职责范围 |
|---|---|---|
| 抽象与契约 | `AutoTest.Capabilities.Contracts`、`AutoTest.Application.Contracts` | 能力角色、端点、应用输入与调用合同 |
| 工位与资源 | `AutoTest.Resources.Runtime`、`AutoTest.Resources.Simulation` | 能力绑定、端点可用性与资源管理 |
| 测试模型、编辑、运行 | `AutoTest.Testing.Domain`、`AutoTest.Testing.Authoring`、`AutoTest.Testing.Runtime` | 程序树、步骤准备、运行计划、执行能力获取 |
| 物理集成 | `AutoTest.Adapters.MainController`、`AutoTest.Integration.Usb`、`AutoTest.Hardware.Io` | 主控会话、能力提供者、USB 传输与 IO 访问代码 |
| 通信及信号 | `AutoTest.Communication.Can*`、`AutoTest.Communication.Dbc`、`AutoTest.Signals.Contracts` | CAN/DBC/信号相关接口及实现 |
| 工作区、记录、界面 | `AutoTest.Workspace`、`AutoTest.Records*`、`AutoTest.QuickDesktop` | 工程资源、测试记录与 WPF 工作台 |

以上只基于工程命名与相关类文件；并未从当前 `.sln` 完整还原真实项目依赖，也不代表每个功能已完整、可编译、可使用。

## 实际类与调用链证据

**现存源码可验证的通用执行设计**（这里只核实存在与代码行为，不批准作为新架构）：

1. `AutoTest.Application/RunDependencyResolver.cs`：`RunDependencyResolver.ResolveAsync` 获取程序依赖，再调用 `ICapabilityBindingResolver.Resolve` 对测试能力需求和 Station endpoints 进行绑定。错误时返回缺绑定或能力不可用结果。
2. `AutoTest.Resources.Runtime/CapabilityBindingResolver.cs`：按能力类型、版本、特性、Provider 健康状态和显式绑定检查候选端点；存在缺失/不兼容诊断。
3. `AutoTest.Testing.Runtime/Preparation/L5RunPreparation.cs`：`RunPreparationService.PrepareAsync/PrepareScopedAsync` 将程序节点与依赖编译成运行计划；源码包括 Sequence、Scope、If 等分支处理。
4. `AutoTest.Testing.Runtime/Execution/RunCapabilityScope.cs`：`RunCapabilityScope.AcquireAsync` 按绑定访问资源管理器获取 Lease、注册释放责任，并向运行步骤暴露所租用能力。
5. `AutoTest.Adapters.MainController/MainControllerCapabilityProvider.cs`、`MainControllerSession.cs`：存在主控设备侧 Provider/Session 实现；真实通信可用性仍需集成/台架证据。

**历史硬件假设的冲突**：`AutoTest.QuickTesting/QuickCapabilityRoles.cs` 明确包含 `dut.power`、`keypad.power`、`dut.ignition`、`keypad.keys`、`station.fixture` 等角色。前两项不能据此推定现役台架具备独立 DUT/按键板供电控制；G0 的物理资源事实优先。这是 G1/G2 的设计审查输入，不在 G0 静默改写源码。

## 构建、依赖与测试现状

`src` ZIP 存在 `bin/obj` 派生输出，但这只说明打包时包含某些构建产物；本轮没有可复现的完整 Solution、Git HEAD、工具链锁定与原始测试报告。因此：

- `SOURCE_PRESENT`：适用于本历史快照中被直接阅读的类/接口。
- `BUILD_VERIFIED / UNIT_VERIFIED / INTEGRATION_VERIFIED / BENCH_VERIFIED`：本轮均 **未核证**，不得以 DLL 文件存在代替。
- 最新有效 HOST 与该历史快照的**差异**未知，不能把 G0-06 描述成“现役完整架构审计完成”。

## 扫码识别与历史 HOST 的事实边界（本次补充）

当前自动测试工位使用扫码枪读取 DUT 唯一编号，事实来源为负责人确认。此次对历史 `src` 进行关键词级检索，**尚未定位到可据此确认的完整扫码枪接入、编号解析、DUT 绑定或持久化实现证据**；关键词没有命中并不等于代码中不存在相关逻辑，更不能推定现役软件未实现。

因此：`已知工位使用扫码枪` ≠ `已确认软件扫描接口` ≠ `已验证扫码内容绑定当前 DUT` ≠ `测试记录与唯一编号已实际关联`。当前有效 HOST 的此项能力应随 G0-OPEN-HOST-CURRENT 一并核实，具体软件需求由 G1 决定。

## 与 G0 硬件能力边界的关系

G0-02/03 与 G0-07 仍是板级电气能力、外部接口的来源；G0-04/05 是固件实际编译源的静态线索；历史 HOST 的 Capability/Endpoint/Binding 是可能复用的**软件研究成果**。必须分开记录：

`PCB 设计能力` ≠ `固件当前暴露` ≠ `历史 HOST 角色存在` ≠ `当前 HOST 实际可用` ≠ `DUT 功能经实测确认`。

## 开放事项与关闭证据

- `G0-OPEN-HOST-CURRENT`：实际 Windows 仓库路径、Git HEAD、当前 `src`/Solution、`dotnet` 环境、构建/测试原始报告。这一缺口不影响 G0 对现役 PCB 的静态接收，但限制对 HOST *当前实现状态* 的准出结论。
- `G0-OPEN-HOST-DIFF`：历史快照与当前版本的差异审计，特别是曾出现的电源角色、资源绑定和真实硬件支持范围；将在取得当前版本后完成。
- 本历史快照获准作为**参考资料**而非正式可发布软件基线；不可据此启动源码改写或对 G2 的实现方案先行冻结。


## G1 使用限制（v005）

本源码快照是既有软件研究和实现参考，可用于识别曾经存在的测试编排、能力解析、资源仲裁等模式；它不是现役 HOST 的构建验证。G1 不应该从 QuickDesktop 菜单和类名倒推台架的全部目标要求，也不应把历史 `dut.power` 角色变成物理能力事实。

---

## 旧 HOST 研究地位与最新负责人说明
负责人明确：**新 HOST 重新设计**，本快照仅作既有研究成果、实现线索和过去迭代问题的参考，不作为新 HOST 的目录、模块、接口兼容或验收基线。负责人曾用历史 HOST 控制 USB、IGN、夹具、按键板、两路 CAN，且存在部分自动测试；但本轮未取得重测日志，故标记为 OWNER_REPORTED_HISTORICAL。扫码、烧录、UDS 完整链路及最终测试判定没有本轮实测证据。

## M2 研究继承索引与编码验收边界

历史 `src` 中值得正式研究的 32 项实现位置列于 `data/src_research_assets_32.csv`，包含 `UsbSession`、`CapabilityBindingResolver`、`AuthoringTransactionEngine`、`L5RunPreparation`、`RunExecutionKernel`、`RunCapabilityScope`、`DbcAuthoringSession`、`ExecutionRecordBuilder`、`Palette.xaml`、`Typography.xaml`、`ShellViewModel` 等。每项分别标记**思想保留、源码候选移植、反面经验或需重做**，不把任何旧模块直接宣布为新 HOST 标准。

负责人报告旧 HOST 曾驱动 USB、IGN、夹具、按键板、两路 CAN 和部分自动测试，属于 `OWNER_REPORTED_HISTORICAL`；对扫码、烧录、完整 UDS、完整测试结果判定无新实测证据。不能用本轮源码静态审查代替这些功能的现场验证。
