# AutoTest｜ecu.test 式操作语义与用户工作区设计 V0.3

> 当前为 GitHub local 候选详细设计及对应源码差异说明，2026-11-13。不是 Windows WPF 或实机通过证明。设计继承 `ECUTEST_WORKSPACE_AND_AUTHORING_V02.md`；不替换正式 TEST 文件或硬件接口。

## 1. 用户确认的产品规则

1. 左侧资源树**单击资源即正式打开编辑标签**；无临时预览标签。再次单击已打开资源，只切回该标签。工作区切换或关闭时保护未保存内容。
2. 新建 Package（测试用例）后立即打开真实步骤编辑器，默认定位测试阶段。先选择用户动作，再编辑对象和参数；点击“插入”才创建步骤。取消无空节点、无新绑定、无真实设备输出。
3. 只有一套固定 AutoTest 台架：本地 Windows PC、WinUSB 主控、CAN0 KEY_SIM、CAN1 DUT。TBC 描述台架，不设计假想多台架驱动。TCF 描述 ECU 型号/硬件版本及其 DBC 和功能输入配置。
4. Package 是独立可编辑的测试程序；Project 是带顺序、启停、固定资源修订的 Package 引用组合；不可用“项目列表已能保存”冒充“已经可以批量执行”。
5. 用户不必先手动新建 Mapping。插入对象时由唯一 TEST 编辑事务自动绑定，Mapping 高级管理只用于重绑定、诊断和复用。
6. 设备操作事实、ECU 响应、测量数据和测试结论分别呈现。连接成功或 USB ACK 不是测试通过。

## 2. 与 ecu.test 的对应及 AutoTest 的边界

| 用户对象 | 语义 | 用户操作 |
|---|---|---|
| Workspace | 工程资源与导航 | 打开工程、维护资源、选择配置、正式编辑标签 |
| TCF | 被测系统描述与资源选择 | ECU 型号/硬件版本、DBC、模拟功能按键，离线选取；修改需检查修订 |
| TBC | 固定物理台架描述 | 查看固定通道与设备能力；选择配置不自动连接/写入硬件 |
| Package | 一个测试用例 | 依次编辑准备、测试、清理中的动作及分支 |
| Project | 测试用例执行编排 | 加入 Package、排序、启停、保存固定版本；真实编排执行尚未实现 |
| Mapping | 测试对象技术身份 | 自动绑定，必要时显示修订/哈希与重绑定 |
| CAN/DBC | 物理报文和信号定义 | 按 CAN ID 或名称找报文，逐信号填写物理值，或原始 HEX |
| KEY_SIM | 20 路电阻网络开关 | 可直接选择开关，也可使用在 TCF 中定义的 ECU 功能按键 |
| Wave | 信号采集、参考、分析条件 | 选择 DBC 信号与命名参考；Analyze 输出数据，Expect 单独判定 |

此模式学习 ecu.test 对 Package、Project、TCF/TBC、Bus access 和 Mapping 的职责分离，不引入 ecu.test 专有文件格式或庞大厂商接口体系。

## 3. 鼠标、键盘与窗口统一行为

- 工程树：单击正式打开、右键按资源类型提供操作、重选已打开资源激活标签。关闭标签不是删除资源。
- Package 主编辑：默认选中“测试”阶段。主工具栏提供“所有操作…”以及 CAN、模拟按键、读取信号、期望判定的直接入口；右侧选中已有步骤后“编辑此步骤”是主操作，双击步骤或 F2 打开相同编辑器。
- Ctrl+Space：从当前选中阶段/分支/步骤位置搜索并插入动作。Ctrl+S 保存 TEST，Ctrl+Z/Ctrl+Y 撤销和重做。
- 选择对象：先按动作类型过滤；在该类别内搜索工程名、DBC 报文名、CAN ID、信号名、功能按键。搜索不修改正式 TEST；确认插入后才记录绑定。
- 主编辑与运行职责分离：参数/步骤上下文先显示；运行授权、设备执行与记录归入明确的可展开运行区。永远不将设备物理写入与编辑器选择关联。
- 页面缩小后，常用动作工具栏可以换行，不得被横向裁剪导致主要动作不可见。

## 4. 逐步操作：真实 CAN

1. 用户点击“发送 CAN”。直接打开 CAN 专用步骤编辑状态，而不是先要求去 Mapping 管理器登记。
2. 已有 DBC 时，在对象搜索中选择报文，显示 ID、标准/扩展帧、DLC 和信号表。逐个输入物理值；编码器负责检查范围与数据打包。编码不支持的复用报文拒绝误编码，可采用完整 HEX。
3. 无 DBC 时，仍能选“CAN1 原始报文”，编辑 ID、帧格式及最多 8 字节数据；界面提示建议导入 DBC 以获得按信号编辑。
4. 点击插入才建立正式 Write/Mapping。保存、关闭、重开、F2 再编辑时，原始帧及所有已选信号值应恢复，原绑定资源修订不会被同名新版本暗中替换。
5. 当前仅确认后端的单帧经典 CAN 发送语义；“周期发送/停止周期发送”的完整任务生命周期和实机验收不在本轮完成项中。

## 5. 逐步操作：功能按键和 20 Key

1. 点击“模拟按键”，立即选取已保存的 ECU 功能动作，选择短按/保持/释放及必要时覆盖保持时间。
2. 如果尚未配置功能按键，明确提示工程师去配置 ECU 输入动作，但仍提供“高级：直接操作 KEY_SIM 20 Key”。
3. 直接方式以可视勾选设置电阻开关组合。此组合只规定闭合哪些 Key，**不声明 ECU 必然识别为驻车、释放等功能**。
4. 按键输入的固定前置延迟、保持和释放间隔来自功能状态配置或显式步骤参数；不能以 USB ACK 冒充五路模拟电压已达到目标。
5. 完整物理运行依然使用既有指纹授权、串行化控制与 Cleanup；不把直接 Key 操作改造成假想的独立 20 路电压写入。

## 6. 逐步操作：读取、流程与判定

- 点击“读取信号”：从 DBC 选择信号，配置 Latest/Next、数据新鲜度、超时和变量名称；没有 DBC 不伪造解析值。
- 点击“期望判定”：编辑表达式，保留与读取分离的 TestDatum/Expect 契约。读不到数据时不得当作数值 0。
- 选择 IF/ELSE、Loop、Sequence：直接显示子分支，允许继续插入动作；不制造第二套编排/运行模型。
- WaveStart/Stop/Analyze 沿用共享 CAN/DBC 时间与证据来源。参考波形必须明确创建；分析结果由后续 Expect 决定是否纳入 TEST Verdict。

## 7. 实施状态与未完成清单

### 已在 local 源码存在

- 资源单击正式打开、标签切换与未保存确认。
- TCF/TBC 持久化、选择、编辑；Project 有序引用及固定资源修订；Package 独立 Steps + Mapping。
- 专用步骤组合器，CAN DBC 信号表、原始 CAN1、配置按键、直接 KEY_SIM、IGN、Read、Wave、Expect、If/Loop/Wait。
- 本版新增：Package 快捷动作、Ctrl+Space、步骤上下文主操作、运行区折叠、目标搜索、重开步骤时保留精确版本、窄窗口工具栏换行。
- 正式 TEST 运行仍有连接与显式授权门禁；TCF 选择与真实资源绑定版本一致性检查保持。

### 尚未证明或尚未实现

- 当前对话**没有 Windows Build、Desktop.Tests、WPF 实际点击、截图或真实 ECU 验证**。不能将 local 标记为验收通过。
- DBC 工作台对报文/信号右键直接生成 Package 步骤、拖拽插入步骤尚未完成；目前从 Package 的动作入口完成等价操作。
- 周期 CAN 发送/停止、多层子程序调试、项目级批量真实执行、自动扫码生产 Run、底部专业实时日志、波形图形化操作仍需独立实现和实测。
- 没有完成 TCF/TBC 远程台架、多工位调度、ecu.test 专有配置激活生命周期；固定台架选择只表示配置上下文。
- 任何模型、电气目标值都不是现场电压/ECU 响应实测结论。

## 8. 本地实测顺序（不接真实设备）

1. 机械检出 GitHub local 的精确 HEAD，Windows Desktop Debug Build、五个测试项目；只收集日志不自行修复。
2. 新建临时 Workspace、TCF、固定 TBC，导入 DBC，选择 TCF/TBC，确认只是离线配置。
3. 新建 Package → 直接点击“发送 CAN”→ 选择 DBC 报文 → 按信号填写 → 插入 → Ctrl+S → 关闭 → 单击重开 → F2 验证数值；重复原始 CAN1 路径。
4. 用“模拟按键”编辑已配置功能动作和 20 Key 直接模式；取消步骤后检查不存在孤立步骤或 Mapping。
5. 用“读取信号”与“期望判定”插入步骤；尝试 IF/ELSE 及 Loop，观察层级、撤销、重做。
6. 创建 Project，加入已有 Package，排序、启停、重复引用，保存并重开；不得声称可以直接自动运行整个 Project。
7. 重点截图：单击资源切换编辑标签、缩小窗口后的快捷操作、步骤搜索、无 DBC 提示、F2 重开保留修订、TCF 选择与资源不一致时的错误。
8. 有 UI 运行异常时检查 `%LOCALAPPDATA%\AutoTest\logs\ui-errors.log`；不接台架、不改变真实 ECU 输出。

## 9. 事实参考

- ecu.test 2026.3 Work window: https://docs.tracetronic.com/help/ecu.test/2026.3/en/Main-program/Editor/Test-case-editor/Work-window.html
- ecu.test 2026.3 Action window: https://docs.tracetronic.com/help/ecu.test/2026.3/en/Main-program/Action-window.html
- ecu.test 2026.3 Project editor: https://docs.tracetronic.com/help/ecu.test/2026.3/en/Main-program/Editor/Project-editor.html
- ecu.test 2026.3 TCF/TBC tutorial: https://docs.tracetronic.com/help/ecu.test/2026.3/en/Tutorial/Fundamentals/Lesson-2-Test-configuration.html
