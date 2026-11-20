# AutoTest｜纯 UI 全产品候选交付与质量状态

**日期：** 2026-11-19
**Draft PR：** https://github.com/ch3758134-prog/AutoTest/pull/69
**Git 分支：** `feature/ui-product-rebuild-20261119`
**用户验收策略：** 完成所有批次源码后统一体验，不逐批等待审批。

## 源码范围
- `HOST/src/AutoTest.Desktop/App.xaml(.cs)` — 全新 Prism WPF 应用。
- `HOST/src/AutoTest.Desktop/MainWindow.cs` — 浅色 Shell、文档页签、资源导航、工具副窗口、退出保护。
- `HOST/src/AutoTest.Desktop/Core/UiStore.cs` — UI-only 项目/TEST/DBC/CAN/KEY/UDS/Flash/Wave/扫码编辑状态及会话序列化。
- `HOST/src/AutoTest.Desktop/Views/UiKit.cs` — 公共视觉/输入/表格/窗口组件。
- `HOST/src/AutoTest.Desktop/Views/Workspaces.cs` — 首页、测试项目、步骤、流程、运行与调试、资源/工具中心。
- `HOST/src/AutoTest.Desktop/Views/Specialists.cs` — DBC、CAN、Bench、Input、UDS、Flash、Wave、SCAN、Record、Device 专业工作区。
- `HOST/tools/run-full-ui.ps1` — Windows 本地直接构建与启动入口。

## 已做的可追溯检查
- 按最新 main 派生独立 feature；GitHub 比对：旧 Desktop 实现全部被替换，旧 UI 测试文件和引用已删除，新源文件均可回读。
- WPF/C# 文件进行了括号与字符串闭合静态检查，未发现结构不匹配。
- 未使用任何真实后端 ProjectReference；只保留纯 UI 所需的 Prism.DryIoc 包。
- `main` 保持未修改，Draft PR 未合并。
- 设计/实施按 21 批分别记录于 `docs/ui/implementation/batches/`；统一验收脚本见 `WINDOWS_ACCEPTANCE.md`。

## 未有验证证据（严禁宣称 PASS）
| 维度 | 状态 |
|---|---|
| WPF MSBuild / NuGet restore | NOT_RUN |
| Windows 原生启动 | NOT_RUN |
| 跨工作区鼠标与键盘实测 | NOT_RUN |
| UI 会话保存/恢复数据回归 | NOT_RUN |
| 1366×768/1920×1080 及多 DPI | NOT_RUN |
| 用户最终体验验收 | PENDING |
| 真实 PROJECT/TEST/DEVICE/UDS/Flash 等接线 | OUT_OF_SCOPE |

**交付结论：** `FULL_UI_SOURCE_CANDIDATE / STATIC_REVIEW_ONLY / WINDOWS_VERIFICATION_PENDING`。本候选已覆盖全产品 UI 工作区源代码，但未经 Windows 构建与实际体验，不得称为最终 PASS。

## 统一 Windows 验证
Windows PowerShell 中：

```powershell
cd HOST
.\tools\run-full-ui.ps1
```

如果构建或启动失败，保留 PowerShell 输出与脚本生成的 `%TEMP%\AutoTest-UI-Build-*.log`。根据准确错误在本 UI 分支修复，不回退使用旧 G4 界面。
