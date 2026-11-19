# R02｜来源登记与证据边界（2026-11-19）

以下均为本轮查询到的公开原始出处；URL 用于后续 AI 回查，不以搜索引擎摘要替代原文。来源类型“厂商文档”只能证明产品提供某种机制，不能证明该机制在目标用户中比其他方案更高效；研究推论由 R02 自行承担。

| ID | 一手来源 / 核实事实 | URL | 类型与限制 |
|---|---|---|---|
| E01 | Siemens TIA Portal V21 Portal View，面向任务选择工具，必要时自动进入 Project View | https://docs.tia.siemens.cloud/r/en-us/v21/introduction-to-the-tia-portal/user-interface-and-operation/layout-of-the-user-interface/portal-view | 厂商 V21 文档；不是 AutoTest 必须双入口的证据 |
| E02 | Siemens TIA Portal V21 Project View，项目树、工作区、编辑器栏、Inspector、任务卡、状态栏 | https://docs.tia.siemens.cloud/r/zh-cn/v21/tia-%E9%97%A8%E6%88%B7%E7%AE%80%E4%BB%8B/%E7%94%A8%E6%88%B7%E7%95%8C%E9%9D%A2%E5%92%8C%E6%93%8D%E4%BD%9C/%E7%94%A8%E6%88%B7%E7%95%8C%E9%9D%A2%E7%9A%84%E5%B8%83%E5%B1%80/%E9%A1%B9%E7%9B%AE%E8%A7%86%E5%9B%BE | 厂商文档；组件名称不等于布局强制要求 |
| E03 | Siemens 保存/恢复窗口布局与键盘快捷键 | https://docs.tia.siemens.cloud/r/en-us/v20/introduction-to-the-tia-portal/user-interface-and-operation/keyboard-operation-in-the-tia-portal/arranging-windows | 官方 V20 文档，与 V21 版本分开标识 |
| E04 | Microsoft Visual Studio 的文档窗口/工具窗口、布局、多显示器与 Ownership | https://learn.microsoft.com/en-us/visualstudio/ide/customizing-window-layouts-in-visual-studio?view=visualstudio | 微软官方；不能据此认定完全自由停靠必需 |
| E05 | VS 2026 18.11 Hot Exit：未保存文档、编辑、撤销和布局恢复，与正式保存区分 | https://learn.microsoft.com/en-us/visualstudio/ide/autorecover-environment-options-dialog-box?view=visualstudio | 微软新版本特性，须启用；不等于所有应用默认能力 |
| E06 | Ignition 8.3 Designer：资源类型决定工作区与面板，Design/Preview、搜索和冲突提示 | https://www.docs.inductiveautomation.com/docs/8.3/platform/designer | 厂商官方，涉及部分多用户功能不直接映射单机 AutoTest |
| E07 | Ignition 8.3 Designer UI：树与画布选择联动、属性编辑、资源操作 | https://www.docs.inductiveautomation.com/docs/8.3/platform/designer/designer-user-interface | 厂商官方；只支持“因对象而变”设计推论 |
| E08 | Autodesk Fusion：按目的划分 Workspaces，命令上下文 Tab | https://help.autodesk.com/cloudhelp/ENU/Fusion-GetStarted/files/GS-WORKSPACES.htm | 厂商官方；不是固定页面/工作区数量依据 |
| E09 | NI TestStand：Sequence Editor 与 Operator Interface 的职责区别；共享引擎/API | https://www.ni.com/en/support/documentation/supplemental/06/guide-to-effective-test-software-development-with-teststand.html | 厂商官方；不能照搬并行执行行为 |
| E10 | NI TestStand 正式调试：Trace、断点、单步、Watch、调用栈 | https://www.ni.com/docs/sw-UG/bundle/teststand/page/debugging-sequences.html | 厂商文档，更新标记 2026-09-15；不修改 AutoTest TEST 合同 |
| E11 | ETAS INCA 实验环境，增加不同仪表/选择变量，调整专业测量布局 | https://docs.etas.com/inca/docs/V7.6/EN/Subsystems/exp/Content/Topics/EXP_Adding_Windows.htm | 厂商文档，部分页面为脚本渲染；证据主要来自可检索摘录，应避免过度推断 |
| E12 | Microsoft Windows 命令组织：上下文菜单、命令一致性、快捷键 | https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/commanding | Windows/WinUI 官方设计建议，可借鉴但 WPF 需自行实现 |
| E13 | NN/g：复杂应用中工作记录、任务恢复、专家功能和逐步呈现 | https://www.nngroup.com/articles/complex-application-design/ | UX 研究/建议，非行业强制规范 |
| E14 | ISA-101 系列官方介绍：工业过程 HMI 中态势感知、错误减少与设计生命周期 | https://www.isa.org/standards-and-publications/isa-standards/isa-101-standards | 对工业监控/操作参考价值较高，不能直接强加全部工程编辑器 |
| E15 | Microsoft Fluent/Windows Command Bar：主要命令按优先级、缩小窗口时次要命令进入溢出 | https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/command-bar | WinUI 控件级指导；具体 WPF 控件存在差异 |
| E16 | VS 2026 产品团队说明 Hot Exit 是会话恢复而非显式提交 | https://devblogs.microsoft.com/visualstudio/keep-your-investigation-between-sessions/ | 厂商博客，说明功能动机和机制，非性能实验 |


## R03 补充一手来源

| ID | 一手来源 / 核实事实 | URL | 类型与限制 |
|---|---|---|---|
| E17 | Visual Studio 2026 IDE 启动窗的“无代码继续”、打开/新建/文件夹 | https://learn.microsoft.com/en-us/visualstudio/ide/quickstart-ide-orientation?view=visualstudio | 厂商文档；不等于所有专业文档无需工程 |
| E18 | Visual Studio 启动行为可设置开始窗口/最近方案/空环境 | https://learn.microsoft.com/en-us/visualstudio/ide/customizing-the-start-page-for-visual-studio?view=visualstudio | 厂商文档，选项与版本相关 |
| E19 | TIA Portal V20 允许恢复最后工程/编辑器，选择启动视图 | https://docs.tia.siemens.cloud/r/en-us/v20/introduction-to-the-tia-portal/setting-the-tia-portal/overview-of-the-program-settings | 厂商 V20 文档，不冒充 V21 全部选项 |
| E20 | Photoshop 2026 Home 有最近文档、打开/新建、学习入口 | https://helpx.adobe.com/photoshop/desktop/get-started/learn-the-basics/homescreen-overview.html | 厂商文档；图形/云业务无需照搬 |
| E21 | ecu.test 2026.2 Workspace 创建/最近 workspace/自动跳过选择/复制样例 | https://docs.tracetronic.com/help/ecu.test/2026.2/en/Workspace/Workspace-creation.html | 厂商文档；**ecu.test 确实先选择/建立 workspace**，不可错误称其完全无 workspace |
| E22 | NN/g 专业软件中长时等待、中断恢复和外部化工作记忆 | https://www.nngroup.com/articles/designing-for-waits-and-interruptions/ | 用户研究与设计原则，不能当成性能基准 |
| E23 | Microsoft WinUI LOB 设计：导航/文档/命令分层，窄窗口自适应 | https://learn.microsoft.com/en-us/windows/apps/get-started/line-of-business/design-for-lob | WinUI 官方参考；WPF 实施由 UI 团队设计 |
| E24 | Windows 导航并无统一模式；层级不宜无限嵌套 | https://learn.microsoft.com/en-us/windows/apps/design/basics/navigation-basics | Microsoft 设计建议，不是必须采用 NavigationView |
| E25 | NI TestStand Process Model 负责 UUT 识别、日志、报告与执行周边操作 | https://www.ni.com/docs/en-US/bundle/teststand/page/process-models.html | 厂商 2026-09 更新；不直接变更 AutoTest 运行合同 |

## R04 补充一手来源

| ID | 来源内容 | URL | 边界 |
|---|---|---|---|
| E26 | Visual Studio Solution Explorer preview tabs、搜索与文档分类 | https://learn.microsoft.com/en-us/visualstudio/ide/productivity-features?view=visualstudio | 厂商 UI 机制 |
| E27 | Visual Studio 文档与工具窗口布局、固定预览页签 | https://learn.microsoft.com/en-us/visualstudio/ide/customizing-window-layouts-in-visual-studio?view=visualstudio | 厂商机制，具体交互因版本不同 |
| E28 | Visual Studio Error List 双击跳转到源问题，支持过滤/查找 | https://learn.microsoft.com/en-us/visualstudio/ide/error-list-window?view=visualstudio | 厂商机制 |
| E29 | NI TestStand Step Settings 编辑所选步骤的内置与类型专用参数 | https://www.ni.com/docs/en-US/bundle/teststand-api-ref/page/tsref/step-settings-pane.html | NI 官方 |
| E30 | NI TestStand Steps Pane 结构化步骤与列表操作 | https://www.ni.com/docs/ar-EG/bundle/teststand-api-reference/page/tsref/steps-pane-sequence-file-tab.html | NI 官方，语言页面 ar-EG 但英文内容 |
| E31 | Ignition 8.3 资源类型决定编辑器，选择与属性联动 | https://docs.inductiveautomation.com/docs/8.3/platform/designer/designer-user-interface | 厂商 |
| E32 | Ignition 8.3 保存工程的版本/冲突检查 | https://www.docs.inductiveautomation.com/docs/8.3/platform/designer/saving-projects | 厂商，不是单机 AutoTest 必须实现多人协作 |
| E33 | Ignition Designer Find/Replace 支持项目范围查找与选择替换 | https://docs.inductiveautomation.com/docs/8.3/platform/designer/find-and-replace | 厂商 |
| E34 | Fusion 参数表与直接特征编辑两种入口 | https://help.autodesk.com/cloudhelp/ENU/Fusion-Model/files/SLD-MODIFY-PARAMETERS.htm | 厂商，设计类比不是电气技术事实 |
| E35 | Fusion Timeline 可编辑/排序/组织具体特征 | https://help.autodesk.com/cloudhelp/ENU/Fusion-Designs/files/ASM-USE-TIMELINE.htm | 厂商 |
| E36 | ecu.test Action Window 按当前位置插入步骤 | https://docs.tracetronic.com/help/ecu.test/2026.2/en/Main-program/Action-window/Inserting-test-steps.html | ecu.test 2026.2 |
| E37 | ecu.test Package Editor Problems 双击导航相关步骤 | https://docs.tracetronic.com/help/ecu.test/en/Main-program/Editor/Test-case-editor/Toolbar.html | ecu.test 当前文档，版本可能指向 2026.2 |
| E38 | Windows 命令设计强调操作上下文、快捷键、菜单和 undo | https://learn.microsoft.com/en-us/windows/apps/design/basics/commanding-basics | WinUI 设计参考，WPF 控件须自行实现 |
| E39 | WPF 数据绑定 ValidationRules / INotifyDataErrorInfo | https://learn.microsoft.com/en-us/dotnet/desktop/wpf/data | 框架支持事实，不能取代业务级交叉验证 |

## 源与判断分离方式

- `FACT-E##`：源明言软件具有何种机制（例如 Portal View 自动进入 Project View）。
- `INFERENCE`：跨案例判断（例如 AutoTest 工程树不应等同全部软件入口）。
- `REQUIREMENT`：仅来自用户明确决定/正式 PM 受控文件（例如单 PC 单 DUT、专业工具独立、实际硬件独占）。
- `HYPOTHESIS`：尚未实际操作验收，不能标 USER_ACCEPTED。
- `GAP`：资料不足，需重新检索或实际用户试验。

来源可能随版本更新；下轮复查关键条目时记录日期与新旧差异，不无声替换 R02 历史依据。
