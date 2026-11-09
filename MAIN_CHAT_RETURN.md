# AutoTest 主对话回传单｜WPB-G4-INIT V0-R2

## 接收身份

- 工作包：`WPB-G4-INIT / INTEGRATION / V0-R2`；沿用原 V0 完整源码。
- 原 V0 ZIP SHA-256：`b63d607d7ab0d58cfbb07ed059d8e1f21b5a5a0236939634e5fd64ec22a05028`。
- G3 已批准设计 SHA：`4711733cc2b7e5d125aadce9a97e574d877f4e4825d041f3eaaf8f778f35be46`；G4 已批准设计包 SHA：`76c375140f746b9422c4542d0f06307354f9229b3c04e6a7b833e9c1fb5dca2a`。
- 源公共接口候选 `G4-V0-CT-CANDIDATE-0417108b292353b8`，SHA `0417108b292353b896b88f8170b129f1d63c4ef2dfcfbc3491990d2bd8dd7a6a`，**保持候选、未正式冻结**。

## 本轮主对话裁决已落实

1. `global.json` 修改为 SDK `10.0.400/latestPatch`，禁止 Codex 自选安装/升级。
2. Prism Community License 由主对话确认，继续测试时无需重复索证。
3. 额外 11 路径已批准进入 V0，逐项标记；原 53 + 额外 11 = 64。
4. `dotnet --info` 历史异常必须记录；不得单独 STOP。以 SDK `--version` / MSBuild / Restore / Build / Test 实际门禁为准。
5. C#、XAML、工程文件、九程序集、Mock-only 规则全部不变；不得让 Codex 编写或修复。

## 请求主对话后续判定

- 核对本次**新 ZIP 真实 SHA-256**及独立中文 Codex 合同；用于本地 A/B 分段执行。
- Windows A/B 必须回传完整原始日志、退出码、18 个预期 xUnit 案例的实际发现/执行数量。
- GUI 模拟验收后才可决定独立 HOST Git 初始提交；`CONTRACT_REVISION`、G4/G5 Gate 继续 `UNSET / NOT_REVIEWED`。

**R2 当前状态：** 作者环境仅静态检查，`Windows Build/Test/WPF = NOT_RUN`，HOST 未创建、未操作真实硬件、SAEF 未改写。
