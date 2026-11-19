# AutoTest｜三工程平级目录调整（2026-11-19）

## 改动原因和最终约束

此前仓库根目录直接存放 `src/`、`tests/`、`AutoTest.sln` 等 HOST 主代码，另将主控和按键板归入 `firmware/`。这在物理目录层次上将 HOST 设为默认主体，另外两块板成为附属组件，不符合负责人对**完整项目系统开发基线**的要求。

**修正**：`HOST/`、`Main_Control/`、`Key_Sim/` 是同层级工程目录；`main` 的代码基线对三者一视同仁。仓库根目录只放跨工程 `docs/`、仓库配置及项目管理工具，不再直接放 HOST 的解决方案和 `src/`。

## 新旧路径对应

| 原位置 | 新位置 | 说明 |
|---|---|---|
| `src/`、`tests/`、`samples/`、`scripts/` | `HOST/src/` 等 | HOST 内部相对引用保留 |
| `AutoTest.sln`、`AutoTest.Backend.slnf`、`Directory.*.props`、`NuGet.Config`、`global.json` | `HOST/` | 进入 HOST 再运行 dotnet |
| `tools/AutoTest.HardwareDebug/`、`tools/AutoTest.LabLinkGui/`、`tools/verify-backend-boundaries.ps1` | `HOST/tools/` | HOST 工具与项目同根 |
| `tools/project-control-v2/` | 保持 `tools/project-control-v2/` | 属全系统独立管理工具，不塞进 HOST |
| `firmware/main-control/` | `Main_Control/` | Keil 相对源码路径不变 |
| `firmware/key-sim/` | `Key_Sim/` | CodeWarrior 工程相对引用不变 |
| `firmware/BASELINE_MANIFEST.json` | `docs/baselines/FIRMWARE_SOURCE_20261119.json` | 214 条源 Git blob SHA 不变，目标路径更新 |
| `docs/` | `docs/` | 历史与跨专业记录继续位于系统层 |
| `.github/`、`.gitignore`、`.editorconfig`、`AGENTS.md` | 根目录保留 | 系统级 Git/研发治理 |

## 编译与兼容

- HOST 根目录为 `HOST/`，恢复、构建与启动：`cd HOST; dotnet restore AutoTest.sln; dotnet build AutoTest.sln -c Debug --no-restore`。
- `AutoTest.sln` 与 `AutoTest.Backend.slnf` 内部的 `src/`、`tests/`、`tools/` 项目相对路径无需重写，因为它们与解决方案整体一起迁移。
- 主控 `Main_Control/Project/Template.uvprojx` 内部 `source/`、`startup/` 和链接脚本保持原样；按键板 `Key_Sim/EPB_EOL/.project` 与 `.cproject` 工程目录结构保持。
- GitHub `backend-windows.yml` 改为**手动执行**，所有 HOST 路径以 `HOST/` 开始；不在仓库推送时自动触发 CI。
- 历史设计报告、旧工作流和旧 Issue 中的原路径只作为当时的历史事实。新的执行任务、Codex 和自动化应使用本文件的新路径。

## 来源、校验与风险

- 前一全系统基线：`main@f00289f3e141f265211f6cf236b55ca435c01f44`。
- Main_Control 源码来源：`356896e10ff95447395b7903d352f6db201fbe6c`；KEY_SIM：`313ed34ecc8a6e01529998787c8c870cc2e4db4a`。
- HOST 源码对象和全部现有三套项目文件须按 SHA 逐项对比；唯一允许的内容改动为系统入口 README、AGENTS、手动 CI、分工程 README 和重映射的清单及本记录。
- 本次不能用 Git SHA 一致宣称 Windows Build/Keil/CodeWarrior 或实体 ECU 实测通过。需本地工程加载与真实功能专项验证。
