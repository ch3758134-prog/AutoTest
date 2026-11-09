# AutoTest HOST · WPB-G4-INIT · INTEGRATION / V0-R2

## 范围与来源

本包在 **原 V0 完整源码的基础上定点修订**，不重新设计、重写或擅自修复 C#、XAML、`.csproj`、`.sln`、Core/Contracts。九个生产项目与模拟闭环保留；模式严格为 `SIMULATION_ONLY`，不含真实 USB/CAN/IGN/电磁阀/按键/刷写设备接线；本地 HOST 仓库与 SAEF 文档仓库彼此独立。

- G3 冻结输入 SHA-256：`4711733cc2b7e5d125aadce9a97e574d877f4e4825d041f3eaaf8f778f35be46`。
- G4 冻结方案 ZIP SHA-256：`76c375140f746b9422c4542d0f06307354f9229b3c04e6a7b833e9c1fb5dca2a`。
- 23 项 CT 候选 API：`G4-V0-CT-CANDIDATE-0417108b292353b8`，候选 SHA-256 `0417108b292353b896b88f8170b129f1d63c4ef2dfcfbc3491990d2bd8dd7a6a`；**不是正式 CONTRACT_REVISION**。
- R2 的唯一 SDK 配置变化：`global.json` → `10.0.400 / latestPatch / allowPrerelease=false`。以用户回传 Windows SDK 10.0.400 为依据，不代表已经 Build PASS。

## 主对话对预检的正式裁决

1. Prism Community License **主对话已确认**，不再要求用户重复提供；许可证确认不等于 NuGet 已成功恢复。
2. 原 `PROPOSED_ADDITIONAL_PATHS.csv` 的 **11 条均已批准纳入 V0 文件范围**，文件名为历史沿用，并非“仍待审批”。原有 53 条 + 新批准 11 条 = 64 条（含哈希清单本身）。
3. CT-01～CT-23 **保持候选接口**，允许进入 Windows 真实构建验证；不能提前冻结。
4. `dotnet --info` 曾发生 `System.TypeInitializationException`/内部 `NullReferenceException`：**记录但不单独阻断**。是否可用以 `dotnet --version`、MSBuild、Restore、Build、Test 的实际结果判断；任何关键失败立即停止，不允许 Codex 自行修复。
5. 其余 G3/G4、文件责任、Mock-only 禁止物理输出均保持原样。

## 组成及运行

- 九个生产程序集：Core / Contracts / Devices / Projects / Testing / Specialists / Records / Application / Desktop。
- 五个测试/Fixture 工程 + 一份独立负编译样例；预期 xUnit 执行 18 个案例（未实际运行）。
- Prism 9 WPF 最小工作台、Application 模拟站点/路由和不可持久化的内存事实集；所有结论为 `INCONCLUSIVE`，不可当作物理 PASS。
- 参照随包 `CODEX_EXECUTION.md`，实际执行请使用**同批交付的独立签发版中文合同**（内含该 ZIP 的精确 SHA-256），避免 ZIP 对自身哈希的循环依赖。

## 运行及验收状态

原 ZIP 静态检查和 R2 定点差异核查完成；新包哈希清单须在目标 Windows 解包后复核。`Windows Restore/Build/Test/WPF = NOT_RUN`，新 HOST Git `BASE_COMMIT = UNSET`，`CONTRACT_REVISION = UNSET`，`G4/G5 Gate = NOT_REVIEWED`。绝不因静态检验、模拟 ACK 或 CLI 返回 0 而冒称物理检测通过。
