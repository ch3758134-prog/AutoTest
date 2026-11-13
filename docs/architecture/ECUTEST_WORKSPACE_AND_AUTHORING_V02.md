# AutoTest｜ecu.test 式工作区与测试编写交互设计 V0.2

> 设计＋候选实现说明。GitHub `local`，待 Windows Build / WPF UI / 真实台架测试。不作为已完成验收。2026-11-13。

## 1. 用户确认的前提

- 工程资源树**单击** Package / DBC / TCF / TBC / 配置 / Project，立即进入正式编辑标签，不用双击，不采用临时预览编辑。
- 新建 Package 后立即进入可编辑的测试步骤工作区，默认选中“测试”阶段。添加测试步骤时，先选操作、立即编辑对象和参数，确认以后才插入测试树。取消不产生空节点或悬空 Mapping。
- ECU 测试对象与设备驱动分离。CAN1 为 DUT，CAN0 为 KEY_SIM，20 Key 为电阻网络开关，不是 20 个离散功能按键；每种 ECU 的功能操作和目标电压由 TCF 引用的控制器配置定义。
- DBC 是按信号编辑和解码的依据，不是创建原始 CAN 的必需条件；原始 CAN 帧支持独立输入。按键允许已配置 ECU 功能按键和高级直接 KEY_SIM 组合操作。
- 测量与测试结论分离：Read、WaveAnalyze 产生数据；Expect 执行明确判定；物理 ACK 不等于 ECU 已按预期响应。

## 2. ecu.test 组织思想与 AutoTest 的单台架取舍

| 面向用户对象 | 本版本语义 | 持久文件与代码 |
|---|---|---|
| Workspace | 一份 AutoTest 工程清单与资源树，不要求为不同 ECU 强制混用单工程 | 现有 `project.autotest-project.json`（继续兼容旧项目） |
| TCF（被测系统配置） | ECU 型号/硬件版本；当前 DBC 与 ECU 按键配置的受修订约束引用；可离线选择 | `test-configuration` · `*.tcf.json` |
| TBC（固定台架） | 唯一 Windows PC / WinUSB 主控 / CAN0 KEY_SIM / CAN1 DUT 的描述，无假想多台架适配或虚构物理能力 | `bench-configuration` · `*.tbc.json` |
| Package（测试用例） | 一个测试程序和独立测试步骤树，按准备/测试/清理阶段 | 现有 `test-program` · `*.test.json` |
| Project（测试项目） | 可排序、可重复、可启停的 Package 引用列表。当前仅编辑并保存执行编排，不虚构自动批量运行 | `test-project` · `*.prj.json` |
| Mapping | 包含目标资源 ID、修订、哈希、通道、信号、功能状态的内部引用；常见步骤自动创建 | 现有 `TestDocument.Mappings` |

TCF 的选择是**离线编辑上下文**，TBC 的选择是**台架声明**。这两个动作不调用 USB、不改变 IGN 或 KEY_SIM，也不等同于授予真实测试权限。

**现有工程兼容**：未建立/未选择 TCF 时仍可使用原有单工程资源编辑模式。选中 TCF 后，步骤选择范围收敛到该 TCF 的 DBC 与功能按键；真实运行检查引用的版本和 SHA256。

## 3. 主窗口行为

1. 左侧单击资产即打开正式编辑标签。重复选择同一资产激活已打开的标签；切换资源不丢弃上一资源的未保存内容。
2. 编辑标签有关闭按钮。存在未保存修改时，仅该标签的 ConfirmClose 被调用；新建/切换工作空间或关闭程序要求确认相关未保存标签。
3. 工程导航按 Package、Project、TCF、TBC、DBC、功能按键、波形、固件、扫码等分类。右键菜单仍保留重命名、移除资源等操作；分类项支持直接新建。
4. 顶部状态显示**当前选择的 TCF 与 TBC**，独立于 USB 连接事实。无任何编辑器时显示新建 Package/导入 DBC 入口。

## 4. Package：一步添加、一处编辑

- **入口 A：** 打开/新建 Package → 选择阶段或步骤 → “＋ 添加测试步骤” → 按类别选动作 → 右侧表单即时配置 → 确认 → 插入测试序列。
- **入口 B（待后续补齐对象浏览拖放）：** 对 DBC 报文/信号、已配置 ECU 功能动作的右键菜单发起同一种步骤编辑流程。
- 支持的基本动作：CAN 发送、ECU 按键、IGN、Read、WaitUntil、WaveStart/Stop/Analyze、Expect、Calculate、SetVariable、If、Loop、Sequence、Wait。
- 选中步骤可以打开其专用编辑界面；树形摘要显示动作目标与关键值。准备 / 测试 / 清理及 IF 分支/Loop 子节点沿用唯一 `StepNode` 模型。
- 组合器确认时，用 `StepEditTransaction.InsertWithMapping` 一笔写入步骤及其本地对象 Mapping。取消不保存，撤销会将二者同时撤销。既有映射如果目标或资源修订不一致，必须明确重新绑定。
- 旧的高级 Mapping 面板折叠显示，保留查询和重绑定能力，不要求普通工程师先手工绑定。

## 5. CAN / DBC 发送与数据读取

- 选择 CAN 报文：若来自工程 DBC，则展示名称、真实 CAN ID、标准/扩展类型、DLC，并按每个信号的**物理值、单位、限值**提供编辑表。提交时调用 `DbcCanMessageEncoder.Encode` 进行完整数据校验，当前不支持复用报文自动按分支编码（可改用完整 HEX）。
- 若工程没有 DBC，或需要直接操作总线，选择“CAN1 原始报文”：输入标准/扩展帧、ID 和 0～8 字节数据；当前真实实现为**单帧发送**，不能假称已支持周期发送/停止周期任务。
- 读取/等待信号需要 DBC。没有 DBC 时提示先导入；不做伪造信号解析，不把物理原始字节伪装成 DBC 信号值。
- 发送实体仍经过 `ConfiguredStationActionPort`、`HardwareConnectionService` 和现有 `PhysicalCommandFence`。编辑器/配置选择永不触发发送。

## 6. ECU 模拟按键与直接 KEY_SIM

- 推荐先在 ECU 按键配置中定义功能操作及其 KeyMask、固定按下前延迟、保持时间、释放间隔、五路目标电压/识别窗口；这些目标电压是工程要求，**不等于实测值**。
- Package 可以直接选择这些已定义功能操作，执行 Pulse/Hold/Release，并可覆盖短按保持时间。
- 高级方式为“原始 KEY_SIM 20 Key”：可视选择开关组合，选择短按（1–60000ms）、保持或释放。原始操作只声明闭合哪些电阻开关，不推断 ECU 功能与实际模拟电压。
- 同一个测试程序的实际运行不允许原始 Key 和功能按键混用，避免开关保持状态被互相覆盖；测试结束应按现有设备清理机制释放。
- 原始 Key 与功能按键均需真实运行授权；物理 ACK 与 DUT 电气响应严格区分。

## 7. Project 测试项目编辑器

- Project 的用例列表是引用，不复制用例中的测试步骤。可添加同一 Package 多次（具有独立列表项身份）、排序、启停、移除、保存/关闭/重开。
- 每个列表项固定 Package 的工程 AssetId、修订、SHA256。引用被修改或不存在时拒绝静默保存与自动替换，用户需要显式选择新修订。
- **当前仅能编辑执行计划，不能宣称 Project 可运行整套产线流程。** 在实际批量执行前仍需实现受控 Run session、跨 Package 变量边界、失败后处理策略、日志/报告关联、停止/清理、扫码产线入口。

## 8. 保留但尚未实施的交互设计

下面这些未因为本轮代码而自动变成完成项：

- DBC 报文/信号的资源上下文菜单直接创建步骤，以及从对象树拖放插入步骤。
- 原始 CAN / DBC 的周期发送、停止发送；周期任务管理与主动释放。
- 基于专用内联 Inspector 的全步骤编辑与高质量信号选择（当前使用按对象变化的专用步骤窗口）。
- 测试项目真实批量执行及配置切换过程中的操作安全编排。
- 完整 UDS、烧录、扫码生产、手动台架的统一 IDE 工作区，断点/单步/运行到光标、底部实时日志面板，以及高级波形图形化操作体验。
- TCF/TBC 选择在重启后的持久恢复、跨 Workspace Library 复用及多个远程台架适配。当前 R1 实际固定一台。
- 真正 KEY_SIM 模拟电压的现场标定、CAN 时间同步精度与硬件层闭环。

## 9. 本轮最低验证门禁

使用独立临时工程，不对真实 ECU 发出操作。

1. Windows Desktop Debug Build：0 编译错误；五个现有测试项目能运行通过；单独列出新增测试错误与警告。
2. 新建 Workspace：单击资源即打开正式标签，连续切换 Package/DBC/TCF 不丢未保存文档，取消关闭保持当前编辑状态。
3. 新建 Package：默认选中测试阶段，添加 Wait、IF/ELSE、Loop 和原始 CAN；取消添加不能留下空节点；撤销/重做同时处理自动 Mapping；保存、关闭、重开。
4. 导入 DBC，添加“发送 CAN”：选择报文后直接修改物理信号值，显示单位/范围；不完整、超限输入阻止提交；能切回完整 HEX 模式。
5. 建立 ECU 按键配置，添加已配置功能动作；另用单独 Package 选择原始 20 Key；验证两种操作都能编辑、保存、重开。
6. 新建 TCF 与固定 TBC：选择、保存后状态栏正确；未连接设备时不发生任何物理操作；TCF 过滤 DBC/功能按键。
7. 新建 Project：依次加入两个 Package、移动、禁用其中一个、重复引用、保存重开，保持顺序和固定修订。
8. 查看 `%LOCALAPPDATA%\AutoTest\logs\ui-errors.log`，确认打开/切换新编辑器不出现未处理 WPF 绑定或构造错误。
9. 未验证台架真实操作的项目一律保留“未验证”，不可当作产品验收通过。

## 10. 事实依据与技术说明

- ecu.test 官方 TCF / TBC 分离、配置激活与离线编辑、Package / Project 工作区组织（版本 2026.3）：https://docs.tracetronic.com/help/ecu.test/2026.3/en/
- AutoTest `local` 内部后端：`ProjectWorkspaceService`、`TestDocument`、`StepEditTransaction`、`ConfiguredStationActionPort`、`RealRunCoordinator`、`WaveCaptureService`。
- 设计来自操作对象与用户动作的分离，不复制 ecu.test 的产品代码或它的测试文件格式。
