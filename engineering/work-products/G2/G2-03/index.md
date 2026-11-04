# G2-03｜HOST 分层、依赖、复用与 UI 子系统（架构候审 v0.7）

## 总体分区（逻辑组件≠一个DLL）

1. **展示和工作台**：`D-VISUAL`（Token/样式/组件/画廊）与 `D-SHELL`（Prism Shell、导航、ViewModel、工具窗口）。
2. **应用协同**：应用命令、操作前置、运行准备、资源租约、事件投影；不直接包含物理 USB 编解码。
3. **业务领域**：`B-PROJECT/B-DBC`、`C-AUTHOR/C-RUNTIME`、`P-PRODUCTION`、`R-RECORD`。
4. **专业服务**：`E-SCAN/E-UDS/E-WAVE/E-FLASH`；共享通信与工程资源，不各建一套测试引擎。
5. **设备/通信**：`A-SESSION/A-STATION/A-CAN`，包括模拟设备和真板适配。

依赖约束：所有高层模块依赖已批准领域接口，不直接引用 WPF 控件或设备字节；`A-SESSION` 不依赖 DBC、生产规则或 UI。工程可编辑模型不依赖运行时状态；运行态订阅不可变快照。

## 自动测试四种对象必须分离

| 对象 | 写入者 | 何时生成 | 为什么独立 |
|---|---|---|---|
| `TestDocument` | `C-AUTHOR` | 用户新建/编辑 | 可以撤销、重命名、保存、修改引用 |
| `PreparedPlan` | `C-RUNTIME` 中编译器 | 校验工程/资源后 | 固化可执行步骤、依赖与能力需求 |
| `RunSnapshot` | 运行协调者 | 真正启动前 | 冻结 DUT/工程/DBC/步骤/固件/规则修订 |
| `RunFacts/ResultProjection` | Runtime/Records；UI只读映射 | 执行中与完成后 | 保存预期、实际、来源、Unknown及故障 |

**不能**使用 `ViewModel.SelectedNode` 决定执行步骤；不能让运行引擎遍历正在被用户修改的 UI 树。

## 资源抽象与历史源码

设计沿用历史 `src` 已研究过的 `Capability/Endpoint/Binding`、编辑事务/Undo、DBC、Runtime 和记录领域的**思路**。源代码是否移植逐项审查，避免强制兼容历史错误角色（如 `dut.power`）。可比较的源码条目见 `references/research/src_asset_index.csv`，其存在不意味着新 HOST 已实现。

## UI 专门设计责任

- DesignTokens：主题色彩、字体、字号、密度、尺寸、图标与状态表达。
- SharedControls：树/表/属性编辑/步骤节点/工具条/日志/状态与虚拟化模板。
- Workbench：IDE 式文档与 Pane、窗口布局保存、独立专业工具。
- ViewModel/Application：Selection、ActiveDocument、ActiveRun、Station、ToolContext 分开；命令失效理由可见。
- 验证：真正 WPF 画廊 → Shell/编辑交互 → Mock Runtime → 现场任务，不把 HTML 原型当实现证据。

## G3 接收

`G3-0` 先签公共语义对象；`G3-B1/C1/D1/A1` 可并行详细设计。具体工程数量、类名称、WPF 控件实现方式由 G3 决定，但**不能改变数据所有者与依赖方向**。

## 注册表A｜模块责任与独立验证（G3必须继承）

| 模块ID | 模块 | 唯一职责/边界 | G3工作包 | 最早证据 |
| --- | --- | --- | --- | --- |
| A-SESSION | 主控 USB 与设备会话 | USB 帧语法、设备身份、重连世代、请求/事件关联；不推断物理状态 | G3-A1 | USB 黄金帧 → B1 |
| A-STATION | 台架能力与端点 | 逻辑 CAN 角色、IGN/夹具/按键输出请求、真实能力发布和安全前置 | G3-A2 | B0→B2/B3/B4 |
| A-CAN | CAN 分发与观测 | 统一接收入口、原始帧流、时间质量、消费者背压、发送租约 | G3-A3 | 重放竞争→B2 |
| B-PROJECT | 工程与资源 | 项目/资源身份、引用、版本、持久化、Station 映射 | G3-B1 | 创建→保存→重开 |
| B-DBC | DBC 与信号 | DBC 解析、信号定义、报文解码、时间/质量传播 | G3-B2 | 已知帧→真实 CAN |
| C-AUTHOR | 测试编辑与校验 | 测试文档/步骤/事务/撤销/校验；编辑态不污染运行态 | G3-C1 | 真实 WPF 编辑+回读 |
| C-RUNTIME | 计划、执行、判定 | 不可变执行计划、资源租约、步骤执行、观察、判定、运行事实 | G3-C2 | Mock Run→H3 |
| D-VISUAL | 视觉系统与共享控件 | Token、主题、样式、图标、画廊、视觉回归 | G3-D1 | 真 WPF 组件画廊 |
| D-SHELL | 工作台与交互 | Prism Shell、导航/选择/编辑/运行投影、专业窗口、命令反馈 | G3-D2 | 真实键鼠与Mock服务 |
| E-SCAN | USB 扫码采集与身份匹配 | PC 直接采集原始码、解析规则和匹配；运行后身份不变 | G3-E1 | 真扫码→规则样例 |
| E-UDS | ISO-TP/UDS 诊断 | 复用共享 CAN、诊断会话、请求应答/负响应；实际服务按样例扩展 | G3-E2 | 仿真→非破坏实机 |
| E-WAVE | 波形与证据 | CAN 信号时间序列、采集质量、显示、参考与比较对象 | G3-E3 | 回放→真实电流 |
| E-FLASH | 刷写作业 | 烧录配置/映像/Job/状态/模拟目标；CanTool 只作参考 | G3-E4 | 模拟 Bootloader→另行实机授权 |
| R-RECORD | 证据、历史与统计 | RunFact、RunRecord、追加历史、不同 DUT 数与测试次数 | G3-R1 | 三次运行/两个 DUT |
| P-PRODUCTION | 生产任务协同 | 扫描候选、预检、冻结 Run、执行烧录和测试、记录协调 | G3-P1 | H4 前分步模拟 |
