# AutoTest｜G4 阶段源码已正式接入 main：PR #61 合并记录（2026-11-19）

**最新事实**：GitHub PR #61 **已合并**（不再是 Draft/候选）。  
**source baseline**：`main@eb1b74a3d7eeb6dcbb582d9c04c72f97f5e1204e`。  
**GitHub 验证**：PR state=`closed`、merged=`true`；main HEAD 与 PR merge SHA 一致；merge 双父为 `1d6eeba8561a779640b20c5a3f384327a0018079` 与 `92c6cb46a35ae11c1ffdefc0e0ece9339906d21a`。  
**本次审批解释**：负责人要求优先把 G4 阶段成果接入 main，并明确将由自己在 main 上实测；此前五处 Git 冲突已由集成 AI 实际解决。现已执行正式 PR merge，工作事实优先于之前仍记为“未合并”的历史管理报告。

## 接收的范围和限制

- 这是 **G4 阶段性源码基线接收**，包括已经提交的 WPF/TEST/PROJECT/DEVICE/DBC/专业接线候选，不代表 R1 或 G4 全部实体功能 PASS。
- Git 三方冲突已经解决，PR #61 合并成功，不再要求用户切换到 G4 分支或重复处理 PR #61 的冲突。
- **Windows 构建、自动化测试及 WPF 实际启动在本次 GitHub 合并操作中没有执行**。状态 `LOCAL_BUILD_PENDING / LOCAL_WPF_START_PENDING`。若失败，针对新的 main SHA 定点修正，不回 G4 旧分支改代码。
- 自动 TEST、公共流程真实运行、KEY/IO、USB/双夹具/LED、FLASH/UDS 主程序集成等功能的历史未验证项目仍是 `FIELD_NOT_VERIFIED`，不许因为源码合入自动改写。
- 新现代 Fluent 浅色整体 UI 研究仍由 `ui/ide-design` 专项主导。原 G4 UI 是当前可运行候选，不是已批准的最终新版视觉。
- FLASH/UDS 独立专项保持自己代码及真实实测范围；WAVE/SCAN 完整联动实测待稳定主 TEST 真实执行基线。

## 给现场 Codex 的最简指令

```text
仅拉取最新 GitHub main，核对 HEAD = eb1b74a3d7eeb6dcbb582d9c04c72f97f5e1204e。
在 Windows 本地执行 Restore、Debug Build、相关自动化测试，并启动真实 AutoTest.Desktop WPF。
只处理机械操作和原始日志回传，不自行改代码、不重新切 G4、不模拟设备成功。
报告 HEAD、构建失败文件/行号、测试通过/失败统计、WPF 启动结果。
```

## 后续处理原则

后续改动以正式 **main 精确源码**为基线，用户在现场实测发现问题后分专业定点处置。项目管理只确认源码接收与工作安排，不擅改 Gate 为 PASS。此前 PR #61 “dirty/未合并”记录是历史快照，不再是现行状态。

- [PR #61](https://github.com/ch3758134-prog/AutoTest/pull/61)
- [原冲突处理与候选证据](G4-PR61-MERGE-RESOLVED-20261119.md)
