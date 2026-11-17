# AutoTest G4｜首次使用、资源导航和 TEST 初始状态整改验收

代码基线：`feature/g4-html-parity-20261115`

**状态：实施候选。Windows 实机 Build、运行截图及交互回归未在当前执行环境完成。**
禁止把下表中尚未执行的项目写成 PASS，禁止将纯 UI 原型、渲染图或模拟数据当作 WPF 截图。

## 本批代码范围

- 删除 `EnsureOverview()` 的自动调用和固定的通用工作区导航行。工程概览只有工程根节点和菜单触发时才打开；测试项目、用例、DBC、配置继续使用现有正式文档系统。
- 新的无文档开始页面只提供当前阶段主要操作：无工程→打开/新建；无测试项目→新建测试项目；已有项目但无**真实关联**用例→进入项目创建/加入用例；已有用例→打开可用的真实 TEST 编辑。
- 最近文档仅是 `%LOCALAPPDATA%\AutoTest\last-editor.json` 的本机 UI 偏好；按工程 manifest 路径隔离，恢复前核对 AssetId、类型和资源存在性。不改变正式资产版本、TEST DSL 或 Project 定义。
- 保留左侧 TCF/TBC、烧录、Controller、波形、DBC、固件和扫码等完整资源类型；空的低频分类折叠进“更多资源与配置”，仍可从原类别创建。未能完整读取项目引用时，不推测哪些 TEST 未关联。
- 空阶段显示轻量提示并保持三阶段蓝/青绿/金区分。只由 `StepRowProjection.IsEmptyPhase` 计算，没有额外 `StepNode` 或步骤集合。正常阶段 44 DIP、正式步骤行 35 DIP 继续保持。
- 复用 `TestStepInspector.PaletteActions()`、`StepInsertionTarget`、`StepEditTransaction`、`StepRows` 与现有选择 / 撤销 / 保存流程；保留宽选择器、单击插入及 Inspector 焦点。
- 没有运行事实时默认不占用底部 180 DIP；观察区和真实事件绑定未删除，可从右下角“观察”按钮恢复。

## WP-08｜Windows 实际回归矩阵

| # | 场景 | 操作与必须观察的结果 | 状态 |
|---|---|---|---|
| 01 | 首次启动，无最近工程 | 首屏仅出现打开 / 新建工程；没有自动出现概览 tab、演示用例或假设备 | 未执行 |
| 02 | 打开真实空白工程 | 中心主操作为新建测试项目；不出现概览统计卡或假项目 | 未执行 |
| 03 | 创建正式测试项目 | 打开项目编辑，资源树节点真实存在；不会额外生成 TEST | 未执行 |
| 04 | 在项目里创建首个空白用例 | 用例进入真实项目引用，编辑器显示准备/测试/清理，无自动步骤 | 未执行 |
| 05 | 分别向三阶段添加首步 | 每个“＋添加”插入正确阶段，插入窗口展示对应目标；三色及正常行密度不变 | 未执行 |
| 06 | 选中刚插入的步骤 | 右侧立即定位该 StepId 的真实参数，可编辑并看到草稿状态 | 未执行 |
| 07 | 取消 / 撤销 / 保存 / 重开 | 取消不留下可保存的假步骤；撤销恢复同一正式事务；保存重开 StepId/数据不丢 | 未执行 |
| 08 | 退出后恢复既有工程最近文档 | 仅恢复仍在工程 manifest 内、类型匹配的最近 TEST/PROJECT/DBC/配置，失效文档回到真实开始状态 | 未执行 |
| 09 | 工程有大量真实用例 | 工程树显示项目引用顺序，未关联用例分组准确；正常编辑行密度及滚动无变化 | 未执行 |
| 10 | 1366×768 与 1920×1080 | 主操作可见；左树、文档、Inspector、阶段添加和步骤选择器没有脱屏/任务栏遮挡；高 DPI 需检查实际 DIP 布局 | 未执行 |

额外核对：项目尚无用例但存在独立 TEST 资产时，应进入**项目添加用例**状态，不应当误判为项目已有用例。打开项目引用失败时，禁止猜测“未加入项目”数量。

## 必须附带的真实 Windows 截图

- `G4_FirstUse_NoProject_1366x768.png`：程序真正运行、没有项目，显示首屏主要操作
- `G4_FirstUse_EmptyProject_1920x1080.png`：真实空白工程，无测试项目
- `G4_Edit_NormalUse_1920x1080.png`：真实工程中至少一个已经编辑的用例，显示步骤密度、Inspector 和资源树
- `G4_Edit_EmptyCase_1366x768.png`：真实空白 TEST 的三阶段，每个阶段可直接添加

截图不得生成演示资产充数；至少同时核对**首次打开**和**已有用例正常使用**。如 Windows 环境尚未运行，保持上述截图 **NOT_AVAILABLE**，不得伪造路径或审查结论。

## Windows 受控验证命令

```powershell
dotnet build src/AutoTest.G4Workbench/AutoTest.G4Workbench.csproj -c Debug
dotnet test tests/AutoTest.Desktop.Tests/AutoTest.Desktop.Tests.csproj -c Debug
dotnet run --project src/AutoTest.G4Workbench/AutoTest.G4Workbench.csproj -c Debug --no-build
```

以上是**待执行命令**，不是当前执行记录。GUI 截图与真实交互必须在 Windows 图形会话中完成。

## 未关闭问题 / 准出条件

- Windows Build/Test 回读及实际运行图未提供，G4 首次使用验收**不能 PASS**。
- 需要实测最近文档恢复与项目引用异步加载时的树选中状态。
- 需要对 1366×768 高 DPI 的选择器弹窗、Inspector 和默认收起的底栏做实际焦点与可见性验证。
- 需要在大量真实用例工程中验证树引用刷新时未关联用例分组不会短暂误显示。
- 其它设备、手动台架与波形执行能力不属于本次 UI 工作包验收，不因为本次通过而获得新的物理控制授权。
