# AutoTest｜UI 项目当前事实状态（2026-11-19）

## 授权与研发方式
用户批准 UX_MASTER_PLAN_V02_20261119：分层逐批研究/设计/实施/留档，全部代码完成后统一验收；不逐批要求用户审查。不再采用旧 G4 Desktop 交互或 UX-I01 真实业务优先接线路线。

## 批次计数校正
P-01~P-03（3） + A-01~A-02（2） + B-01~B-05（5） + C-01~C-07（7） + D-01~D-03（3） + E-01（1） = **21 批**。上一版草案文字写“18 批”与明细不一致，以本 21 批映射为准。

## GitHub 与源码
- 研究设计：ui/ide-design；本目录 docs/ui/batches/<批次>/REPORT_20261119.md 各批独立留档。
- 实施：feature/ui-full-rebuild-20261119，根目录 UI/AutoTest.UI，.NET 10 Windows WPF。
- 旧 HOST/src/AutoTest.Desktop、HOST/tests/AutoTest.Desktop.Tests、HOST/src/AutoTest.G4Workbench、HOST/src/AutoTest.UiPreview 在新实施分支已移除；main 保持未写。
- HOST/AutoTest.sln 已去掉旧 Desktop 和旧 Desktop.Tests 等 GUI 项，新增 UI/AutoTest.UI 图形入口。
- UI 模型 UiSession 仅保存 .autotest-ui.json 交互会话，不是正式 TEST DSL、PROJECT 资源或实测报告。
- 专业工具本地可编辑：DBC、CAN、综合台架、输入配置、UDS 请求序列、烧录配置、波形游标；主任务：自动测试/公共流程/工程资源/生产扫码/报告历史；设备状态 UNKNOWN。
- 禁止虚构真实 PASS、ACK、物理电压、报告、进度、设备已连接。

## 当前质量判断
- R/D：产品研究架构已形成，21 个批次设计映射已归档，属于 DESIGN_CANDIDATE。
- C：新 UI 源码已提交，静态目录与 Git diff 可检查；代码实现未完成 Windows 编译验证。
- V：**BUILD_NOT_RUN / WPF_LAUNCH_NOT_RUN / USER_ACCEPTANCE_NOT_RUN**。
- F：研究设计分支已按批次归档并提交。
- 下一步唯一目标：完成源码静态核查、专业交互缺口修复、提交 UI Draft PR，提供本地 Codex 拉取/编译/运行所需精确 Git ref。真实硬件接线不在本阶段。
