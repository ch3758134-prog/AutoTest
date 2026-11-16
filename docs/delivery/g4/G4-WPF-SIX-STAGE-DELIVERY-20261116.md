# AutoTest | G4 WPF 六阶段交付与实测门禁

状态：执行顺序已锁定；阶段 1 **候选待用户实测**，阶段 2～6 **尚未开始**。本文件不等于 G4 Gate PASS。

## 实施入口与约束

- 仓库：`ch3758134-prog/AutoTest`
- 当前实施分支：`feature/g4-html-parity-20261115`
- 阶段 1 的代码交付 HEAD：`b325bcb61d8920dca0337ecb329a07c87e3e7464`（**最终 HEAD 以提交本文后分支回读为准**）
- 冻结 UI：`docs/ui/archive/auto-test-20261115-ec75a9fe/AutoTest_UI.html`，不擅自修改
- 阶段 1～3：`src/AutoTest.UiPreview` 中独立的 WPF UI。每阶段单独提供 Windows 可启动候选、SHA、操作路径和 HTML 差异清单
- 阶段 4：迁入正式 Project/TEST/DBC/Controller 编辑和保存，但不在此阶段发送实体命令
- 阶段 5：通过 Application/Device 层接入真实硬件，USB 连接首先作为单独验收点，然后逐项接专业能力
- 阶段 6：全操作链/多配置/日志/异常/DPI 现场验收；回归冻结 HTML

任何阶段**不能将未执行的 Windows 构建或用户实测标记 PASS**；不通过时在该阶段修正，不直接跳到下一阶段。正式 TEST/设备协议和安全能力保持审批门禁。

## 分阶段 Gate

| 阶段 | 工程交付 | 用户实测的主要操作链 | 进入下一阶段的门槛 | 当前状态 |
|---|---|---|---|---|
| 1. 视觉原型与交互设计 | 浅色原生 WPF：布局、阶段/步骤行、分类图标、右 Inspector、搜索、选中、右键及键盘操作样式。清理 IDE/预览/无接线等冗余语言 | 打开窗体→浏览工程树→展开阶段和 IF/ELSE→插入步骤→改参数→复制/移动/撤销→调整面板和缩放，对照 HTML | 用户确认总体布局与主要交互方向；遗留 UI 差异登记 | CODE_CANDIDATE / WINDOWS_UNVERIFIED |
| 2. 正式 UI 组件与样式系统 | `ResourceDictionary`、字体/间距/色彩 token、Button、阶段行、步骤行、树节点、Inspector Field、状态组件 | 不同步骤/工作区使用同一视觉语法；修改全局样式后无局部回退 | 组件契约与样式复用审查；真实 WPF 视觉回归 | NOT_STARTED |
| 3. ViewModel 与界面状态 | 页面 ViewModel、Binding/Command、唯一选中上下文、弹层/焦点、编辑草稿/校验、撤销重做、临时勾选，仍无 ECU | 30 步流程连续编辑，切页/取消/非法参数/搜索/复制/插入位置及临时选择状态无冲突 | UI 状态连续操作验收，不依赖业务后端 | NOT_STARTED |
| 4. 真实离线工程/测试编辑接线 | 通过现有 ProjectWorkspaceService、TestDocument、StepEditTransaction、DBC、Controller 的 Application 边界；正式资源持久化与 Revision | 新建工程→项目→用例→DBC→编辑步骤→保存→关闭→重开→检查 Rev/SHA/引用一致 | 离线功能可稳定实测，版本/草稿安全，真实测试不可自动启动 | NOT_STARTED |
| 5. 真实执行与设备服务接线 | **先 USB**：WinUsbDiscovery/WinUsbByteLink/UsbSessionService，设备身份、Connect/Disconnect、只读 F0 PING、故障阶段日志。之后逐项接正式 CAN/KEY_SIM/IGN/夹具/UDS/波形和运行 | USB 发现/成功/失败/断连/重连和 Win32 错误可复现；后续物理动作逐项受控测试 | 不引入 Mock、物理控制不绕过授权，不把 ACK 当实际 ECU 成功 | NOT_STARTED |
| 6. 完整操作验收与持续优化 | G4 端到端清单、用户截图、Windows DPI/大小、错误和审计证据、重新打开及真实台架实测 | 新建→编辑→保存→预检→受控执行→证据/报告→断连恢复→复现问题 | 用户正式审查并决定是否准出；不得自动宣布 PASS | NOT_STARTED |

## 阶段 1 本轮完成的源码修订

- 移除窗口标题中的“UI 视觉预览（未接后端）”以及 AutoTest logo 后的“IDE”装饰。
- 删除顶栏“UI 独立预览/无工程/无接线”、底栏“无连接/无运行/无保存”及步骤选择层的重复技术说明。
- 工程名称收敛为“控制器 A”，去掉“样例工程”额外标签，不改冻版步骤示例内容。
- 状态消息收敛为“就绪”“已添加步骤”“已复制”“已撤销”等操作反馈；未接线功能只在用户主动点击时给出简短说明。
- 保留单一 in-memory UI 阶段 1 样例和 WPF 交互，不接真实运行和资源存储。
- 之前提前创建的 `src/AutoTest.UiPreview/Usb/UsbConnectionController.cs` 已在本轮**删除**；阶段 1 csproj 继续零业务引用。
- 现有的 USB 源码 `AutoTest.Devices` 不修改；待阶段 5 再通过正式应用服务接线。

## 阶段 1 待 Windows 实测的精确步骤

只运行本阶段 UI 项目（**不要启动旧 Desktop**）：

```powershell
dotnet build src/AutoTest.UiPreview/AutoTest.UiPreview.csproj -c Debug
dotnet run --project src/AutoTest.UiPreview/AutoTest.UiPreview.csproj -c Debug --no-build
```

验收须记录：当前 SHA、Build 原始结果、主窗口截图、HTML 同尺寸截图、阶段和步骤选中/悬停、IF/ELSE 插入、弹层、右侧属性、状态栏、窗口最大化/任务栏/DPI。现场发现的问题由 G4 AI 直接修改本分支，不让 Codex 诊断改代码。

**当前只有静态标签和 XAML 事件引用检查；Windows Build/Test/用户实际操作尚未执行，Stage1 Gate 未通过。**
