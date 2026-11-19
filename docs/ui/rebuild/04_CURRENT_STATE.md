# AutoTest｜全新纯 UI 当前状态（统一入口）
日期：2026-11-19。实施分支 feature/ui-product-full-pure-20261119。
## 用户授权
连续推进研究、详细设计、纯UI源码、每批留档；不等待逐阶段用户验收；完整产品源码形成后统一由用户 Windows 操作验收。无真实后端接线。
## 已执行
P/A/B/C/D 各批次纯UI产品代码已写入：Shell、工程/资源、TEST、公共流程、DBC、CAN、综合台架、输入、UDS、Flash、Wave、生产、历史和设备。旧 Desktop 窗口/VM/主题、旧 UiPreview/G4Workbench 和旧 Desktop.Tests 作为旧UI依赖在本实施分支移除。新的 Desktop 仅包含 App + Rebuild/*，没有 TEST/PROJECT/DEVICE 项目引用。
## 测试和验收真实性
- GitHub 结构核查：已执行，旧 MainWindow.xaml、UiPreview、G4Workbench、Desktop.Tests 不在此分支文件树。
- 静态括号/项目引用检查：部分已执行。
- Windows dotnet Build：**NOT_RUN**（当前执行环境无 dotnet）。
- WPF 实际启动/键盘/鼠标/DPI：**NOT_RUN**。
- 用户统一 UX 验收：**PENDING**。
- 真实设备/烧录/TEST/报告：**NOT_CONNECTED**，严禁声明任何实体 PASS。
## 正式体验前必须做
Codex 在 Windows 拉取精确 HEAD，dotnet restore/build 单独 Desktop 工程，启动 UI；返回完整编译错误和启动截屏。编译/运行后处理全部阻断，再交用户集中操作。
## 不允许的捷径
不得把草稿控件可点击等同业务通过；不得恢复任何旧窗口来“补缺”；不得直接写main；不得在确认之前把这个版本称为UI_ACCEPTED。
