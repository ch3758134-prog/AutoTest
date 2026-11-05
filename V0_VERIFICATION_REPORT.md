# WPB-G4-INIT｜V0-R2 中文验证报告

**报告性质：** 源码重签发与环境裁决记录；非 Windows Build PASS、非实际 WPF 验收、非 G4 Gate 通过证明。

| 项目 | 当前记录 | 判断 |
|---|---|---|
| 原始 V0 ZIP | `b63d607d7ab0d58cfbb07ed059d8e1f21b5a5a0236939634e5fd64ec22a05028`，64 文件 | 完整性已核对 |
| 主对话 SDK 决议 | Windows 10.0.26200.0；SDK 10.0.400 | `global.json` 改为 `10.0.400/latestPatch` |
| .NET Desktop Runtime | 10.0.11（用户只读回传） | 已识别，仍待实际 WPF 启动 |
| `dotnet --info` | `System.TypeInitializationException`，内层 `NullReferenceException` | 记录为异常；不能单独作为 Build 失败依据 |
| 关键门禁 | `dotnet --version`、MSBuild、Restore、Build、Test | 均应逐条测量退出码，失败 STOP |
| Prism Community License | 主对话已确认 | 不再重复向用户索证 |
| 新增文件 11 条 | 主对话已批准纳入 V0 | `APPROVED_EXTRA_11` |
| G3/G4 结构与源码 | 原 9 个生产项目、所有 `.cs/.xaml/.csproj/.sln` 保持原字节 | 不重设计、不修复 |
| 公共合同 CT-01～CT-23 | 候选，保持 `G4-V0-CT-CANDIDATE-0417108b292353b8` | 允许 Build 验证，未冻结 |
| ZIP 文件 SHA 列表 | R2 内 `FILE_SHA256_MANIFEST.json` | 构建前逐项验证 |
| Windows Restore / Build / Test | `NOT_RUN` | 需本地执行 |
| WPF 窗口与 Mock 操作 | `NOT_RUN` | 需截图/人工核验 |
| 真实 USB/CAN/IGN/刷写 | `NOT_AUTHORIZED` | 严格禁止 |
| HOST 首次 Git commit | `UNSET` | 验收后由主对话另行授权 |
| G4/G5 Gate | `NOT_REVIEWED` | 不自动更新 |

## 本轮唯一允许的变更类型

1. `global.json` SDK 候选功能带 100 → 400。
2. 执行合同/中文报告/构建环境记录及授权范围描述变更，含文件哈希清单和文件树头说明。
3. 不修改现有 C#、XAML、九程序集、测试源、工程引用与 CT 签名。未完成 Windows 编译时不自行修复。

## Windows 必须回传的证据

执行 `CODEX_EXECUTION.md` 的 A（只读）与 B（受控写入），记录原始 stdout/stderr、退出码、版本、Restore 错误详情、Build 错误清单、xUnit 测试数及 TRX。A 中 `dotnet --info` 异常单独存档，B 的失败必须原样返回。B 成功后由操作员在完全无真实设备连接的情况下执行 WPF 模拟验收并截图。先审查真实结果，后决定是否允许 Git 首次提交与 CT 正式冻结。
