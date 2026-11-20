# AutoTest｜UI 分批研究设计与纯 UI 实施状态

**日期：** 2026-11-19
**用户决议：** 规划通过；所有 P/A/B/C/D/E 批次内部连续研究、设计、编码、逐批归档，最终统一 Windows 实际操作验收，不逐批等待用户许可。

## 正式事实源
- 分批规划：`docs/ui/research/UX_BATCH_EXECUTION_PLAN_20261119.md`
- 研究输入：R01～R06、DECISION_LOG、SOURCE_REGISTER。
- 实施分支：`feature/ui-product-rebuild-20261119`
- Draft PR：**#69** — https://github.com/ch3758134-prog/AutoTest/pull/69
- 全部批次与接受脚本：`docs/ui/implementation/batches/` 共 21 个独立交付档案，以及 `docs/ui/implementation/WINDOWS_ACCEPTANCE.md`。
- 实施源：`HOST/src/AutoTest.Desktop`，在实施分支中已清理旧 G4 Desktop 表现层及过时 Desktop.Tests，重建独立 WPF/Prism 项目。

## 交付边界及研究修订
旧 UX-I01 “优先接真实 TEST/PROJECT 服务”合同已被用户明确新决议替代。此次版本只允许 `UiStore` UI 会话与用户明确的手工数据，**不使用正式 TEST/PROJECT/DEVICE/FLASH 等业务 DLL，也不执行 USB/CAN/UDS/Flash**。今后集成由单独阶段审批，不允许反向改变已验收的 UI 体验。

## 当前状态（不可夸大）
- 21 批研究/设计/实现 **代码候选和独立记录已汇集**；是否达到目标体验仍等待 Windows 操作核验。
- GitHub 已确认修改位于新 UI feature 分支，`main` 未动。
- 旧实现不作为新 UI 基线；后端专业项目与固件主线不动。
- **SOURCE_STATIC_AUDIT：已有括号平衡等基本检查；未执行编译。**
- **WINDOWS_BUILD：NOT_RUN；WPF_START：NOT_RUN；USER_UX：PENDING；REAL_DEVICE：NOT_RUN。**
- 最终验收门槛：必须能启动新窗口，完整执行 `WINDOWS_ACCEPTANCE.md` 的跨专业 UI 操作任务，发现缺陷后仍由 UI 专项修复；不能把 Draft PR 直接当成可验收产品。

## 下一项工作
由本地 Windows Codex 检出 PR #69 的精确 HEAD，执行 dotnet restore/build/run 并收集编译错误；UI 专项针对事实修复，随后用户在 Windows 统一操作验收。无需逐批寻求新批准；PR 不合并 main。
