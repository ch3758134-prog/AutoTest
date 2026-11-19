# AutoTest｜全新纯前端重建

用户明确要求：前端全部推倒重做，不沿用旧 G4 布局，也不在 UI 专项中处理接线。

## 源码边界

- 本分支删除旧 src/AutoTest.Desktop、src/AutoTest.G4Workbench、src/AutoTest.UiPreview、tests/AutoTest.Desktop.Tests。
- 完整旧版与成熟后端源码保留在 GitHub main，随时可以回查，不在本分支复制旧窗口以作兼容。
- 新界面工程 src/AutoTest.Frontend：WPF UI 4.3.0 / Windows Fluent Light / .NET 10，无 PROJECT、TEST、DEVICE、CAN、FLASH 等项目引用。
- 面向用户的主操作区暂为：自动测试编辑、手动台架。专业工具保持待设计入口，未来逐项研究。
- 按任务定义工作区，前端可以自由删改布局；不受旧 IDE、旧 Shell、旧 HTML 约束。

## 前端独立验证

- 本轮仅验证布局、导航、直接编辑、属性编辑、CAN 列表编辑、20 路 KEY 组合编辑、UI 草稿文件保存/读取。
- 不生成假硬件反馈、不显示假 CAN 帧、不伪造运行成功。所有实体按钮不可操作。
- 保存的是 .ui-draft.json **前端交互草稿**，不声称兼容正式 PROJECT/TEST 文件，不接真实测试执行器。
- 用户可直接拉取分支编译启动 AutoTest.Frontend；旧 Desktop 构建目标在本分支已移除。
- Windows Debug 编译、启动、实际 UI 手感、DPI、缩放均需后续本地核验；不能将 GitHub 静态修改视为通过。

## 真实接线阶段（本对话不执行）

用户确认前端操作体验以后，独立接线实施将从 main 查找正式工程模型、测试 DSL、真实 DEVICE 接口；先接手动台架实体操作，再评估自动 TEST 的执行器接线。

## 风险和回退

- 删除范围仅在 feature/fluent-bench-test-ui-20261119；main 不改。
- 源码断开是用户明确授权的研发范围，不在分支内保留旧兼容 UI。
- 完整旧代码可通过 main 或旧 G4 集成提交回查。
