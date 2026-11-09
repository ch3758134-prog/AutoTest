# V0-R2 Windows 构建环境记录（主对话预检裁决后）

| 项目 | 已知信息 / 状态 |
|---|---|
| 用户只读预检 OS | Windows NT 10.0.26200.0 |
| .NET SDK 已报告 | 10.0.400 |
| `.NET Desktop Runtime` 已报告 | 10.0.11（另有 8.0.30、9.0.20） |
| 本包 `global.json` | `10.0.400 / latestPatch / allowPrerelease=false` |
| Git 已报告 | 2.55.0.windows.2 |
| 初始 HOST 根目录 | 不存在；上级未发现 Git |
| 原包 SHA-256 | `b63d607d7ab0d58cfbb07ed059d8e1f21b5a5a0236939634e5fd64ec22a05028` |
| `dotnet --info` 历史异常 | `System.TypeInitializationException` → `NullReferenceException`（`Microsoft.DotNet.Cli.Installer.Windows.InstallerBase`）；单独记录，不单独 STOP |
| SDK/工具链实用性 | 用 `dotnet --version`、`dotnet msbuild -version`、Restore、Build、Test 逐步判定 |
| Prism 9 Community License | 主对话已确认；不重复向用户索证 |
| Prism.DryIoc `9.0.537` 恢复 | `NOT_RUN`；保留既有版本 |
| xunit/测试 SDK 恢复 | `NOT_RUN`；保留既有版本 |
| Windows Restore/Build/Test | `NOT_RUN` |
| WPF 实机/截图 | `NOT_RUN` |
| HOST Git 首次提交 | `NOT_RUN`，需单独后置授权 |

任何关键命令非零退出或不符合预期 → 立即 STOP、回传完整日志；Codex 不得自动修复。按随包 `CODEX_EXECUTION.md` 与**外置签发版中文执行合同**操作。
