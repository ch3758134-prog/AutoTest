> **更新（2026-11-19，后续版本）**：main 完整系统基线已补入主控和 KEY_SIM 固件，新的 SHA 为 `f00289f3e141f265211f6cf236b55ca435c01f44`；无任何 HOST 独立提交被覆盖的前提下，`HOST` 已通过 fast-forward 同步到相同 SHA。原文中 `eb1b74a3...` 为建立 HOST 分支时的历史基线。详见 [整机源码基线记录](SYSTEM-SOURCE-BASELINE-FIRMWARE-20261119.md)。

# AutoTest｜HOST 上位机独立分支建立与职责调整（2026-11-19）

> **负责人最新指令**：不要把上位机的后续开发与实测全部放在 `main`；上位机应有自己的正式专业分支。  
> **本次操作**：已建立 `HOST` 分支，从 `main@eb1b74a3d7eeb6dcbb582d9c04c72f97f5e1204e` 创建。新分支包含刚通过 PR #61 接收的 G4 阶段源码。  
> **状态**：`HOST_BRANCH_CREATED / DEVELOPMENT_BASELINE_READY / WINDOWS_BUILD_PENDING / FIELD_TEST_PENDING`。  
> **管理分支**：`Project_Management`，本档只是记录用户治理决定，不回滚 main，不变更其它专业代码。

## 一、分支职责

| 分支 | 当前职责 |
|---|---|
| `HOST` | Windows WPF 上位机的正式日常开发、跨专业已审定功能的集成候选与现场实测。后续主机相关问题优先在此修正、复测 |
| `main` | 整体项目的稳定集成交付基线；仅在审核/验收通过之后受控接收 HOST 等专业成果，不作为日常 HOST 修改工作区 |
| `ui/ide-design` | 现代 Fluent Light UI/UX 研究、设计源与交互方案审查；设计获批后再实施到 HOST 的具体 WPF |
| `Main_Control` | 主控板固件和板级 IO / USB 业务接口 |
| `Key_Sim` | 按键板 MCU 固件 |
| `Project_Management` | 项目管理、接口约束、门禁及审批记录 |
| `feature/uds-lab-20261118`、`feature/flash-lab-20261114` | UDS、FLASH 独立研究、上位机实测与专业候选；经合同审核再接入 HOST |

## 二、对前一决定的修正

- 2026-11-19 的 PR #61 已真实合并 `main`，合并 SHA `eb1b74a3d7eeb6dcbb582d9c04c72f97f5e1204e`；这一历史事实保留，不改写、不擅自删除/回滚已经接收的源码。
- **最新研发/实测工作分支改用 `HOST`**。先前“直接在 main 上继续 WPF 修改或实测反馈开发”的解释不再适用。
- 创建分支是工作流分离，不等于物理删除 main 中的 HOST 文件；如未来需要改变仓库结构、拆分独立仓库或清理 main 上的代码，必须单独讨论、评估风险并明确批准。
- `HOST` 当前与 `main` 是相同源码快照；后续 HOST 分支提交后才发生分化。
- 尚未取得此准确 HOST 版本在 Windows 的 Debug Build、WPF 启动和真实 ECU 自动测试验收；不因新分支建立就写为通过。
- UI 设计、G4 已接收资产、FLASH/UDS 与波形/扫码联调的原验收边界保持不变。

## 三、给本地 Codex 的工作范围

```powershell
git fetch origin
git switch HOST
git pull --ff-only origin HOST
git rev-parse HEAD
dotnet restore AutoTest.sln
dotnet build AutoTest.sln -c Debug --no-restore
```

继续执行相关测试并启动正式 WPF；Windows Build/Test/启动和真实硬件运行分别记录。禁止未经授权合并 main、擅改固件或用 Mock 代替真机实测。

## 四、交接

- [已合并的 G4 阶段源码记录](G4-PR61-MAIN-MERGED-20261119.md)
- [现有 UI 专项职责和源](G4-PR61-BASELINE-ACCEPTANCE-20261119.md)

**本次分支建设不改变 G4 整体验收状态，也不改变 main 的已有正式源码**。
