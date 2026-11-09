# 03｜桌面 UI 产品研发与验收流程（2026-11-09 候选）

## 纠偏：先做真实使用任务，不要继续批量造页面
旧 G4/R1 Shell/13 页导航/Mock Tab 和此前用户实际拒绝的 UI 均视为 **NOT_ACCEPTED**；在未重新验证前不作为必须沿用的布局资产。专业后端逻辑与业务行为不因此被否定。HTML 参考是设计输入，不是 WPF 可操作证据。

## UI 研发的四个责任层
| Owner | 独有责任 | 不做 |
|---|---|---|
| UX / PRODUCT DESIGN | 真实使用任务、信息结构、竞品参考、交互稿、可用性评价与设计变更 | 不声称 HTML 是正式已通过 WPF |
| UI FOUNDATION | WPF ResourceDictionary、字体、颜色、尺寸、共享控件、Theme 和 UiGallery | 不拥有业务服务和 USB |
| UI SHELL | 唯一 Prism Shell、文档/Pane、资源 Explorer、Inspector、日志与焦点导航 | 不写 UDS/Flash/TEST 算法 |
| UI SPECIALIST | 各专业工作台 View/VM、完整操作流程、专业状态与错误呈现 | 不独自再造共有控件/会话 |
| UI QUALITY | Windows 交互自动化、标准视口截图、对比与可访问性、人工任务验收 | 不凭截图冒充业务/物理 PASS |

UI QUALITY 是交付把关职责，不必初期另建独立长期分支；五种职责也不是必须五个 AI 长期并行改同一窗口。

## 每个专业工具的工作流（不可跳过）
1. **研究场景**：明确用户真的要完成什么，例如 UDS 配 Endpoint、发送已批准请求、看 Tx/Rx/NRC；Wave 加载 DBC、观察、采集参考、判定、看失配。记录参考工具截图和依据。
2. **方案**：任务流程、关键状态、信息层级、标准交互、空/错误/进行中/禁用态；原型可以迭代，不能直接转为产品验收。
3. **控件先行**：先在真实 WPF UiGallery 独立实现可复用控件和对应数据情景（Tree、Grid、PropertyGrid、WavePlot、Log、Toolbar、Tab、Modal 等）。
4. **专业页面**：使用同一后端 Port 和 ViewModel，在独立可运行 LabHost 证明实际交互，不另建业务栈。
5. **实际截图**：对 1920×1080 100%/125%、1366×768、窗口缩放、Inspector/底栏展开、长文本/空/忙/失败态出 Windows WPF 截图。不能用 AI 生成图替代。
6. **自动操作测试**：用 FlaUI UIA3 或等价工具覆盖打开工程、导航、配置、输入、保存/恢复、异常反馈，保存日志；GUI 测试需可交互 Windows Session，CI 不具备时标明未执行。
7. **专家验收**：用户执行真实任务，记录截图/错误/操作负担，按反馈调整布局与行为。
8. **接入正式 Shell**：相同专业 View/VM 进入唯一 R1 工作区，重测焦点、Panel、Session/资源、导航生命周期。

## UI 验收维度
- VISUAL：布局、字号、密度、状态表达、参考稿差异；
- INTERACTION：完成任务所需操作、焦点、快捷键、错误恢复；
- FUNCTIONAL：业务 View 与真实/模拟 Port 的数据和命令；
- INTEGRATION：统一 Shell、工程/Station/Session 与多个工具共存；
- PHYSICAL：仅适用真实设备操作，由现场独立判定。

五项状态分别记录。Build PASS 只说明编译；模拟页面/静态截图不证明真实工具可用。

## 第一个 UI 试点
先做 **完整的 UDS 控制台（Mock/Replay + 明确锁定 Physical）**：连接区、会话/端点、构造请求、结果 NRC/Raw Trace、超时和取消、配置保存。后做 Wave/DBC 典型流程，最后再决定完整主窗口布局，不先生产十三个空页面。

## 实际技术依据（读原始官方文档）
- Microsoft WPF Styles/Templates: https://learn.microsoft.com/en-us/dotnet/desktop-wpf/fundamentals/styles-templates-overview
- Microsoft Windows UX/Layout/DPI: https://learn.microsoft.com/en-us/windows/apps/get-started/best-practices
- Windows navigation: https://learn.microsoft.com/en-us/windows/apps/design/basics/navigation-basics
- FlaUI: https://github.com/FlaUI/FlaUI
- DORA small batches: https://dora.dev/capabilities/working-in-small-batches/

## 退出旧 UI
所有未获人工操作验收的旧 R1/G4 UI 候选从活动开发队列中退休。复用时只能经逐项 UI/UX 重新评估、实际窗口截图与任务验证；不允许整包将旧 R1 Shell 合并到 main 来冒充可用界面。
