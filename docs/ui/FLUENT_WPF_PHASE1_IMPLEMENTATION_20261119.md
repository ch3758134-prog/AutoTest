# AutoTest｜现代 Fluent UI 第 1 轮实施与验收记录

日期：2026-11-19
实施分支：`feature/fluent-bench-test-ui-20261119`
基线：`feature/g4-html-parity-20261115` @ `ce4dbd4401dac1a98ffa87885865d0b7c174df7f`
状态：`IMPLEMENTED_IN_BRANCH / STATIC_CHECK_PASS / WINDOWS_BUILD_NOT_RUN / FIELD_NOT_RUN / UI_NOT_ACCEPTED`

## 1. 技术引入（真实源码）
- 添加 WPF-UI 4.3.0 到中央 `Directory.Packages.props`。
- `AutoTest.Desktop`、`AutoTest.G4Workbench` 引用 WPF-UI，并在各自 App.xaml 中加载 `ui:ThemesDictionary Theme=Light` 与 `ui:ControlsDictionary`。
- 保留 Prism 现有文档上下文、Region/导航和原有 TEST/PROJECT/DEVICE/FLASH 域服务。UiPreview 独立无业务引用的设计不变。
- 当前只完成基础主题和关键操作控件的迁移，旧命名样式仍存在；不等于整体界面已经完成 Fluent 视觉验收。

## 2. 手动台架：实时服务原样，增加编辑能力
- 保留左 ECU CAN 发送、中 KEY/IGN/夹具、右实时 CAN 监控三栏和现有真实 `HardwareConnectionService`。
- 连接/断开、刷新资源、新报文、发送勾选改用 WPF UI 现代按钮；连接按钮根据实际设备状态显示明确操作语义。
- CAN 报文的名称、原始 CAN ID、原始 HEX 数据直接在主列表编辑，修改发生在同一 `BenchCanRow` 草稿，不触发物理发送。
- 对 DBC 模式仍显示编码结果，由原有锚定快速编辑浮窗修改 DBC 信号/其他参数；复杂属性与完整编辑窗口继续保留。
- 在直接编辑单元格双击时不再误触发行级浮窗；空白处双击仍可打开现有快速编辑浮窗。
- 三列布局 MinWidth 降低；主窗最小宽度 1090 DIP，待 Windows 各 DPI 核验。不隐瞒较窄窗口可能存在的横向滚动。
- `CanStartCanRows`、单行单发、KEY 动作执行及 IGN/夹具界面的 CanExecute 与真实连接/工位所有权一致；仍需设备层 PhysicalCommandFence 精确授权，不把控件禁用代替安全边界。
- 保留现有 KEY 动作存盘/重开、分段真实发送及不隐式断开语义；保持 CAN/USB ACK 与物理电气状态区别。
- **真实周期 ECU CAN 发送没有放开**：G4 `ManualBenchCanRows` 仍明确阻止其使用面向 KEY CAN 的固件 0x50 调度，需 DEVICE 另外确认合法调度方案。

## 3. 自动测试：以真实编辑器为默认主任务
- 不重建 G4 已有真实 `TestDocument/StepNode/StepId/StepEditTransaction`、DBC/公共流程工程资源、保存、撤销和预检能力。
- 常用添加步骤、检查、保存改为 WPF UI 控件，保持正式命令绑定。
- 真实执行命令保留，不作 Mock；原常驻实体执行栏改为默认折叠的可展开区域，离线编辑时释放工作区面积。
- Shell 工具栏主要操作迁移至 WPF UI，减小 Shell 最小宽度并保持工程树自动收窄；保留 G4 已实现的 WindowChrome WM_GETMINMAXINFO 工作区计算，禁止再让任务栏被无边框最大化覆盖。
- 当前代码依然存在历史 `TryLeaveInspector` 参数无效时阻断切换，以及长 Palette 等交互欠账；未在无 Windows 编译/回归证据下擅改字段事务合同。此问题是接下来 TEST 编辑重点。
- 允许本轮只验真实工程的打开/编辑/保存/关闭重开/引用正确性；没有自动执行器现场证据不得称自动 TEST Run PASS。

## 4. 当前验证事实
- GitHub 上对所有修改的 XAML、props、csproj 已回读；执行了简单 XML 标签平衡检查，8/8 文件通过，**不是 WPF XAML 编译/渲染证明**。
- 目前本执行环境没有 Windows/.NET WPF 构建与实机 USB，Windows Build、G4Workbench 和 AutoTest.Desktop 启动以及所有实机验收尚未运行。
- 2026-11-19 对比新 G4 HEAD 的十个原始受改文件与起始 G4 SHA 均一致；G4 分支中其它文件仍继续变化。后续 PR 必须以当前 HEAD 重新确认状态，不直接覆盖 main。

## 5. 首轮 Windows 实测验收
### A. 启动与布局
1. 本地 Codex 拉取本分支精确 SHA，执行 `dotnet restore AutoTest.sln`、`dotnet build AutoTest.sln -c Debug`，启动 `AutoTest.Desktop`。
2. 在 1366×768、1920×1080 及 100/125/150% 缩放查看主界面/手动台架；主界面不得覆盖 Windows 任务栏，操作按钮/列表不应不可达。
3. 验证 Prism 工程树、文档页签、右侧 Inspector、专业窗口打开关闭；备选 `AutoTest.G4Workbench` 能加载同一 Fluent Light 资源。
### B. 手动台架真实操作
1. 断开 USB 时可添加报文、直接修改名称/ID/HEX、右键/浮窗编辑，但所有实体执行按钮保持不可操作。
2. 真实连接主控后确认身份/固件信息、连接与断开按钮语义变化。
3. 观察 ECU CAN RX；对现场允许的真实 ECU 帧进行单发和多条单发，分别记录 USB ACK、CAN RX 和 ECU 真实响应，检查显示没有误报 DUT PASS。
4. 编辑命名 KEY 动作的 20 bit Mask/保持时间，保存重开后执行；观察每段结果和最终状态，并由现场确认实体电压或 DUT 行为。
5. 真实执行 IGN、两夹具，分别检查主控回读与外部实际结果；断连/超时/取消显示 UNKNOWN，不做假释放。
6. 验证 ECU CAN 周期任务明确阻断，不绕过设备合同。
### C. 自动测试真实工程编辑
1. 不连接 USB，打开/新建工程、测试项目、用例及公共流程，导入 DBC。
2. 插入 CAN/KEY/IGN/等待/IF/ELSE/循环/DBC 引用步骤，修改、保存、关闭、重开。
3. 验证添加步骤、检查问题、保存和实体执行折叠不会损坏已有命令；不运行实体测试。
4. 记录未完成字段切换等旧缺陷，作为下一轮可复现实测输入，不宣称已修复。

## 6. 实施边界
- 只在新的 feature 分支写源代码，暂不修改 main、固件、设备安全协议、RunCoordinator、正式 TEST DSL 或已实测 FLASH 编程状态机。
- UI 专项负责交互与主题，Codex 负责本地机械编译、启动和收集证据，实体 ECU 测试由现场操作人员授权和确认。
- 用户验收后再继续 TEST 草稿事务与更深入的 Fluent 界面迭代。