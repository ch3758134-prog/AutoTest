# AutoTest · Project_Management

这里是同一 GitHub 仓库中**独立的项目管理分支**，不是 AutoTest 上位机源码分支，也不是对现有产品的重新立项。

## 从哪里开始

- **项目管理 AI / 新接手 AI**：首先阅读 [Project Control 的 START_HERE](tools/project-control-v2/START_HERE.md)，再阅读 [便携上下文发现](tools/project-control-v2/portable/CONTEXT_DISCOVERY.md)、[AI 工作模式](tools/project-control-v2/portable/AI_WORK_MODES.md) 和 [发行清单](tools/project-control-v2/PORTABLE_MANIFEST.json)。按系统原有机制确认身份、职责与用户授权；不要直接假设有项目决策或源码合并权限。
- **管理工具本体**：[tools/project-control-v2/](tools/project-control-v2/)；本分支承接后续管理工具研发，避免和 Windows HOST 主线混写。
- **工程决策、资产、审批与历史工作产品**：[SAEF/](SAEF/)；这里是项目档案，不等同于管理工具本身。
- **已有设计和过程文档的保全快照**：[docs/](docs/)；其中与 Windows 产品实现相关的文档，以 `main` 对应受控版本为软件实施依据。本分支的快照不能自动覆盖 HOST 当前实现事实。

## 单仓分支职责

| 分支 | 职责 | 当前边界 |
| --- | --- | --- |
| `main` | Windows AutoTest HOST | 独立的上位机交付与集成主线 |
| `Main_Control` | 主控板固件 | `Project/Template.uvprojx`，源码已落档，变更前复核协议和现场硬件 |
| `Key_Sim` | 按键板固件 | `WS/EPB_EOL/`，源码已落档 |
| `Project_Management` | Project Control 工具及项目治理档案 | 单独维护审批、资产、研究及交付过程 |

## 交付与批准边界

1. GitHub 分支、Git commit、CI 成功或本文件被创建，均**不代表**项目的 P0/G0/G1/Gate 或产品功能已经获得新的批准。
2. Project Control 本身的开发修改属于本分支；项目需求和软件接口跨分支变更先形成可审阅差异和负责人决定，再由对应责任分支执行。
3. 原理图由 SAEF 资产管理；主控板和按键板的**可编辑源码**以各自固件分支为研发来源。原始 ZIP 归档用于身份追溯，不是用来直接编辑的工作目录。
4. 原 `main` 中的管理工具、SAEF 和相应文档暂时保留，直到验证了迁移的读取路径、启动和审批工作流，再做受控删减。这是过渡阶段的副本，不应被误认为两个独立管理权威。
5. 不把 `Project_Management` 合并到 `main`，也不把 `main` 的代码变更直接合并到管理分支。跨分支通过记录、引用精确 Git SHA 交接。

## 本次来源

管理工具、档案、文档从 `main@e34fc57b3c9e915545c50ec02d7566b92c50a122` 的 Git 树复制，不改变任何原始文件内容；此 README 是本次新补充的分支入口。本次**没有进行 Windows 本地启动、管理工具完整回归或固件编译**。
