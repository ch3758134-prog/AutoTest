# AutoTest SAEF｜GitHub 受控档案说明

**GitHub 单仓**：`ch3758134-prog/AutoTest/SAEF`。本目录与正式 HOST 源码属于同一个远端 Git 仓库；早期《README.md》《project_state.json》等出现的 `D:\\01_Projects\\auto\\AutoTest_HOST` 是**2026-11-05 历史源码身份**，不是最新工作区或本轮 Git 主仓库位置。

**权威链**：`engineering/control/work_products.json` 是 55 个工作产品当前登记；`engineering/control/project_state.json` 与 `control/approvals/*.json` 是 Gate 状态/批准记录。早期 `control/gates/*.md` 中的 `NOT_REVIEWED` 是旧候审模板，不得覆盖批准事实（详见各导航页的修订）。G3 `PASS_WITH_LIMITATIONS`，G4 Gate `NOT_REVIEWED`，G4 v0.2 实施**方案**获批，不能冒称实施 Gate 已过。

## 已确认的 Git 内容（2026-11-09 审计）

- 注册工作产品 **55**：P0 8、G0 9、G1 8、G2 8、G3 8、G4 7、G5 7；这 55 个 index 路径均可在远端 Git 树定位。
- 工作产品状态：**41 ACCEPTED、7 G4 DRAFT、7 G5 NOT_STARTED**；Git 目录有 **104 个审批文件**，其中 5 个历史 Gate 批准决定可定位。
- 历史 Git commit `f9f3f6d648a8391c1707c3688b9190eb06a3af57`（G3 内容批准归档）等可从当前远端查到。
- 2026-11-09 新增 ADR/CR 分别位于 `engineering/decisions` 和 `engineering/change-requests`；这些**仅登记受控设计补录**，尚未走原生 SAEF 工作产品修订审批或 `eng.py check`。

## 不能宣称 GitHub 已完全恢复的内容

`engineering/control/assets.json` 登记了四份原始资产及 SHA-256，但它们的**原始字节目前未在仓库内**：两份 PCB 原理图 PDF、两份签发固件 ZIP。公开 Git 当前的 `SAEF/.gitignore` 有意排除 `*.pdf`/`*.zip` 等二进制原件；登记哈希不等于已备份文件。

历史独立 SAEF 工具 `eng.py`、FRAMEWORK/catalog 和获批 HTML/原始 ZIP 的完整字节，**未在当前仓库树中查见**。故 GitHub 当前可恢复的是**已提交的正式文本记录 + HOST 源码和 Git 历史**，不能承诺“包含所有原始工程资产、SAEF 工具和现场日志的完整归档”。

见 `engineering/control/ASSET_AND_RECOVERY_STATUS_20261109.md`。原始资产进入安全、可恢复的存储前，不要把旧本地原始文件全部不可逆删除；现场日志也应独立保存。软件本体可重新克隆，资产缺口不阻止已准入的 DEVICE 软件验证。

## 源码与设计责任

详见 `../docs/architecture/00_源码权威与设计实现责任_20261109.md`，以主对话为唯一设计和审批者，各专业只负责编码实现，本地 Codex 仅运行。
