# AutoTest R1｜自动测试主工作区及验收工程修正

## 目标与已实施源码
- 本改动仅针对 `HOST/src/AutoTest.Desktop`，从当前 `R1` 分支建立独立整改分支。
- 正式 TEST 用例必须由 `TestEditorView` **内嵌**到 `MainWindow` 的 `ActiveEditorView` 文档区域，步骤参数使用右侧 `ActiveEditorInspector`。
- DBC / UDS / 烧录 / 波形 / 手动台架等工具保持独立受主窗口拥有的工具窗口；不得把自动测试编辑器也做成独立窗口。
- 本轮不重新实现 `TestDocument`、`StepEditTransaction`、`ProjectWorkspaceService`、`TestProjectDocument` 或真实 TEST 执行器。

## 一键创建真实验收工程
主界面工具栏增加 **验收工程**，文件菜单增加 **创建 UI 验收工程（自动生成）**。无须先手动新建工程。

点击后：
1. 在当前 Windows 用户的文档目录下 `AutoTest/UI验收工程/UI_验收_时间戳_随机后缀/` 新建一个互不覆盖的正式项目文件夹。
2. 通过 `ProjectWorkspaceService.CreateAsync` 创建正式 PROJECT 清单。
3. 通过正式 `CreateTestProjectAsync` 创建测试项目“自动测试界面验收”。
4. 使用 `CreateTestProgramAsync` + `OpenTestEditorAsync` + `SaveTestEditorAsync` 创建和保存三个真正的 TEST 文档，保留它们的正式 `TestDocumentId`。
5. 通过 `TestProjectEntry` 绑定三份用例的真实 `AssetId`、`Revision`、`Sha256`；正式 `SaveTestProjectEditorAsync` 持久化关联。
6. 主窗口打开测试项目文档页签，再将第一份 TEST 用例直接设为当前页签，不使用额外 EXE 或未归档弹窗。

### 验收用例
| 序号 | 用例 | 主要检查 |
|---|---|---|
| 01 | 准备测试清理与流程控制 | 准备/测试/清理，Wait，SetVariable，IF/ELSE，Loop，子步骤及右侧参数 |
| 02 | CAN 发送与 DBC 信号编辑 | CanSend，AssertSignal，WaitUntil，CanStopPeriodic 的步骤编辑入口 |
| 03 | UDS 波形与烧录步骤 | UdsRequest，WaveStart，WaveCompare，Flash 的步骤编辑入口 |

- 所有硬件相关步骤初始 `enabled=false`。测试项目中的三个用例初始也全部不勾选（默认不选入实体运行范围）。
- **这是可真实保存的界面验收工程，不是已经验证通过的 ECU 工程**。没有硬件测量、诊断响应、烧录结果或运行报告。
- 本轮并没有为样例虚构 DBC 引用、烧录文件、ECU 身份或波形实测值。

## 运行与验收要求
- **只启动本分支构建的** `HOST/src/AutoTest.Desktop/AutoTest.Desktop.csproj`，不要启动本地老目录里的 `src/AutoTest.Desktop` 或独立 `AutoTest.TestHiFi` 演示程序。
- 初始主界面点击 **验收工程** 后，主窗口中央应出现名为“测试用例 · 01_准备测试清理与流程控制”的文档标签。
- 用例步骤、流程和右侧步骤属性直接显示在主窗口里；允许真实编辑、保存、关闭、重新打开。
- 再打开测试项目页，检查三份用例的资源关联与勾选状态。修改、保存、关闭、重新打开验证磁盘文件。
- 运行前置检查可以验证阻断原因，不允许把未接 USB 的状态展示为 ECU PASS。
- 主窗口关闭后关闭由其拥有的工具窗口。

## 验证状态与治理
- GitHub 源码已提交并静态回读。
- 未获得本地 Windows/.NET WPF Build PASS，不应标记为完整验收通过。
- 不向 `main` 或原 `R1` 分支直接写入；最终合入按现有 Gate 审批执行。
