# V02-04｜独立 TEST UI 交互原型

此项目**仅用作视觉设计和操作研究**。不引用现有 TEST/PROJECT/DEVICE、没有 USB/CAN/ECU 运行入口，也不把草稿当正式工程或真实证据。

启动：在 Windows 安装 .NET 10 SDK，执行 `dotnet run --project UI/AutoTest.TestEditorPreview/AutoTest.TestEditorPreview.csproj`。

研究工作区默认加载 12 个用例、40 个设计样本步骤（准备 6、测试 29、清理 5）。界面中的 `Demo_` 资源名和 HEX 都是设计研究样本，不代表任何实车 ECU 或已验证工程。

主要操作：单击步骤显示类型专用属性；“添加步骤”打开可搜索类型面板；IF/ELSE、WHEN、SWITCH、循环、TRY 等拥有真正分支槽；右键、复制、移动、删除、包裹结构、撤销与重做；Problems 定位缺少资源或非法字段；未完成输入不会因选择其他步骤而被清空；Ctrl+S 保存 **preview.json** 草稿（仅本原型可读取），Ctrl+O 打开草稿；A/B 视觉布局切换；小屏内用“步骤／详情”切换，不打开模态属性弹窗。

交付前至少需经 Windows build、启动、同一 40 步内容下 A/B/窄屏与基础编辑功能实测。本文只定义验收任务，不声称已完成本地 Windows 验证。

资料：`docs/ui/visual-research/V02-01`～`V02-03`。这不是旧 `AutoTest.UI` 的补丁；不会替换正式 GUI，用户选择后才开展整合。
