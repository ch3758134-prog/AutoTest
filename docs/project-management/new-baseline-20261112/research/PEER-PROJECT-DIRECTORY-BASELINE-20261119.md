# AutoTest｜三工程平级目录的系统基线修正（2026-11-19）

**用户纠正意见**：此前把根目录组织成 HOST 源码为主、主控/按键板放入 `firmware/`，在物理结构上让固件成为 HOST 的附属，违背“main 作为完整 AutoTest 系统固定源码基线”的定位。要求 **HOST、Main_Control、Key_Sim 三个平级工程目录**，工程内部结构清楚。

## 本轮已实际执行

- 旧 `main@f00289f3e141f265211f6cf236b55ca435c01f44` → **新 `main@ffb23f8c05ec4a64b82cd781db6357685d082c04`**。
- `HOST` 专业分支以 CAS fast-forward 同步到同一 SHA；`Main_Control`、`Key_Sim` 专业研发分支未更改。
- 根目录三个工程：`HOST/`（.NET10、WPF/Prism、AutoTest.sln、src、tests、tools、samples、scripts）、`Main_Control/`（Keil Project/ + docs/）、`Key_Sim/`（CodeWarrior EPB_EOL/）；不再存在根目录 `src/`、`tests/`、`AutoTest.sln` 或 `firmware/`。
- `docs/` 仍为整个系统的设计、技术、历史和管理资料层；`tools/project-control-v2/` 是系统级项目管理工具，保留在根目录，**没有塞入 HOST**。
- 根 `README.md`、`AGENTS.md` 更新为最新工程身份和入口，不再宣称 G4 临时执行分支是现行开发规则。
- GitHub `.github/workflows/backend-windows.yml` 改为 **手动 workflow_dispatch**，没有自动 push/PR 触发；其测试路径更新为 `HOST/`，避免旧 CI 在每次提交自动耗时运行。

## 静态迁移核对

- 对迁移前后 **完整递归 Git Tree** 校验：旧文件 828、新文件 829；全部应保留的827条旧文件路径都有预期新位置，**0 漏项**。唯一未保留的旧文档 `firmware/README.md` 已由新的分工程 README 和系统入口取代。
- 全部既有源码与工程文件内容 SHA 保留，**0 非预期内容更改**；改动仅发生于系统 README/AGENTS、分工程 README、手动 CI、HOST 边界脚本注释、迁移后的源指纹清单及新增架构说明。
- [固件逐文件指纹清单](https://github.com/ch3758134-prog/AutoTest/blob/main/docs/baselines/FIRMWARE_SOURCE_20261119.json) 的214条目标新路径与源码 Git Blob SHA 完全匹配。
- HOST 原 `AutoTest.sln` 的22条 project path 均存在于 `HOST/` 内；相应解决方案和 `Directory.*.props` 保持同一相对层级。
- 主控 Keil `Template.uvprojx`、按键板 CodeWarrior `EPB_EOL/.cproject` 的内部路径不变；前一轮 Keil 的118条文件路径在 `Project/` 均已确认存在。
- 合并是目录与基础配置迁移，**本次没有 Windows Build/Test、Keil/CodeWarrior 构建或 ECU/IO 实体测试**，不把 Git 校验称为实测 PASS。

## 新执行入口与治理

- HOST：`cd HOST; dotnet restore AutoTest.sln; dotnet build AutoTest.sln -c Debug --no-restore`。在 `HOST` 分支进行 WPF 用户实测和后续源码修改。
- 主控：`Main_Control/Project/Template.uvprojx`，后续固件改动继续由 `Main_Control` 专业分支维护。
- 按键板：`Key_Sim/EPB_EOL/.project`，后续固件改动继续由 `Key_Sim` 专业分支维护。
- 统一的 `main` 是整机系统的正式固定源码快照，后续按专项证据/审批更新，不按 HOST 为唯一主体。
- 早前 [SYSTEM-SOURCE-BASELINE-FIRMWARE-20261119.md](SYSTEM-SOURCE-BASELINE-FIRMWARE-20261119.md) 记录旧结构和刚导入时的 SHA，是历史事实，**不再作为现行目录说明**。

## 入口

- [GitHub main 仓库目录](https://github.com/ch3758134-prog/AutoTest/tree/main)
- [系统结构更正说明](https://github.com/ch3758134-prog/AutoTest/blob/main/docs/architecture/REPOSITORY_STRUCTURE_20261119.md)
- [HOST](https://github.com/ch3758134-prog/AutoTest/tree/main/HOST)
- [Main_Control](https://github.com/ch3758134-prog/AutoTest/tree/main/Main_Control)
- [Key_Sim](https://github.com/ch3758134-prog/AutoTest/tree/main/Key_Sim)

**管理状态**：`PEER_PROJECT_LAYOUT_MERGED / GIT_CONTENT_VERIFIED / BUILD_NOT_RUN / FIELD_NOT_VERIFIED`。
