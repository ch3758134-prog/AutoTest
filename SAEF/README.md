# AutoTest SAEF｜GitHub 受控档案说明

**GitHub 单仓**：`ch3758134-prog/AutoTest/SAEF`。本目录与正式 HOST 源码属于同一个远端 Git 仓库；早期《README.md》《project_state.json》等出现的 `D:\\01_Projects\\auto\\AutoTest_HOST` 是**2026-11-05 历史源码身份**，不是最新工作区或本轮 Git 主仓库位置。

**权威链**：`engineering/control/work_products.json` 是 55 个工作产品当前登记；`engineering/control/project_state.json` 与 `control/approvals/*.json` 是 Gate 状态/批准记录。早期 `control/gates/*.md` 中的 `NOT_REVIEWED` 是旧候审模板，不得覆盖批准事实（详见各导航页的修订）。G3 `PASS_WITH_LIMITATIONS`，G4 Gate `NOT_REVIEWED`，G4 v0.2 实施**方案**获批，不能冒称实施 Gate 已过。

## 已确认的 Git 内容（2026-11-09 审计）

- 注册工作产品 **55**：P0 8、G0 9、G1 8、G2 8、G3 8、G4 7、G5 7；这 55 个 index 路径均可在远端 Git 树定位。
- 工作产品状态：**41 ACCEPTED、7 G4 DRAFT、7 G5 NOT_STARTED**；Git 目录有 **104 个审批文件**，其中 5 个历史 Gate 批准决定可定位。
- 历史 Git commit `f9f3f6d648a8391c1707c3688b9190eb06a3af57`（G3 内容批准归档）等可从当前远端查到。
- 2026-11-09 新增 ADR/CR 分别位于 `engineering/decisions` 和 `engineering/change-requests`；这些**仅登记受控设计补录**，尚未走原生 SAEF 工作产品修订审批或 `eng.py check`。

## 资产现状（2026-11-13 管理分支建立后）

本目录由 `main@e34fc57b3c9e915545c50ec02d7566b92c50a122` 导入 `Project_Management`，并已在该分支 Git 树回读确认：

- 两份原理图 PDF 已存在于 `engineering/assets/hardware/`，其文件大小和冻结 SHA-256 可在 `engineering/control/assets.json` 中查找。本次确认了远端 Git 文件的存在，但**没有重新下载 PDF 独立计算 SHA-256**；因此不把本次目录核查冒充完整恢复演练。
- 两份冻结固件源子工程 ZIP 以 `engineering/assets/firmware/chunks/` 下的原始二进制分片保留，有恢复说明与登记的原 ZIP SHA-256；可编辑源码已分别落入同仓 `Main_Control`（`Project/`）和 `Key_Sim`（`WS/`）分支。这不证明现场板卡当前烧录镜像的身份。
- `engineering/control/assets.json` 中 ZIP 的原始逻辑路径仍是冻结资产身份标识；实际远端以分片方式保存，恢复前必须按说明拼接并校验。
- `engineering/control/ASSET_AND_RECOVERY_STATUS_20261109.md` 及 `engineering/assets/RECOVERY_EVIDENCE_20261112.md` 是各自日期的审计快照，不能把其当时的“尚未上传”叙述视作当前 Git 状态。
- 历史 SAEF `eng.py`、FRAMEWORK/catalog、全部原始批准 HTML/ZIP、现场日志与真实 Windows 端到端验证仍未确认完整恢复；**不能据此宣布整个项目档案已完整备份或 Gate PASS**。

使用时，当前正式主控/按键**研发**来源是两条固件分支；此处的原件及原理图负责工程身份追溯、设计输入和治理。新管理 AI 首先阅读 `../tools/project-control-v2/START_HERE.md`，不能因为从 `main` 拆出分支就推断审批状态发生了变化。

## 源码与设计责任

详见 `../docs/architecture/00_源码权威与设计实现责任_20261109.md`，以主对话为唯一设计和审批者，各专业只负责编码实现，本地 Codex 仅运行。
