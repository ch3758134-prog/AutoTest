# AutoTest｜唯一现行 UI HTML 设计源

**设立时间：** 2026-11-15  
**状态：** 工作机制已建立；**UI 专项尚未上传新一版 HTML，v3 仍未通过用户验收。**

## 唯一活动文件

**工作分支：** [`ui/ide-design`](https://github.com/ch3758134-prog/AutoTest/tree/ui/ide-design)  
**固定路径：** `docs/ui/AutoTest_UI.html`  
**HTML 读取入口：** https://github.com/ch3758134-prog/AutoTest/blob/ui/ide-design/docs/ui/AutoTest_UI.html （上传后生效）

UI 专项直接在同一个文件上持续迭代，版本依靠 Git 历史和精确 commit SHA 确定；不再把 `v3.html/v4.html/final.html` 等多个独立文件放进仓库争夺“当前标准”。如使用必要静态资源，统一放在 `docs/ui/assets/`，同一 commit 保持可离线打开。

**历史文件不再争夺现行设计地位：** `docs/ui-r1/reference/AutoTest_UI_Interactive_Prototype_V0.1.html` 是历史研究档案；G3-UX-01 v3 试验反馈还在修订，不能自动晋升为已通过的 UI 规范。

## 修改权与审查权

| 环节 | 责任 | 完成条件 |
|---|---|---|
| UI 专项研究与 HTML 迭代 | UI/UX 研究对话 | 提交 `ui/ide-design` 的同一路径，提供 commit SHA、变化内容和可复现操作 |
| 用户体验反馈 | 项目负责人 | 在 Windows 浏览器操作 HTML，指出不满意处；可以继续迭代 |
| PM 兼容性审查 | 项目管理对话 | 对精确 SHA 复查是否与当前 G1/G2/G3 产品、真实硬件和后端接口冲突，区分可接受 UI 设计、需技术协调和需正式变更 |
| 设计定版 | 用户批准 + PM 兼容性审查 | 将获批精确 HTML 修订写入 `main/docs/ui/AutoTest_UI.html`，另记批准版本/SHA |
| 后续 WPF 实施 | G4 受控开发 | WPF 的布局、视觉、操作、状态反馈以**已定版 HTML**为唯一 UI 设计参照；不得在实施中凭旧截图或旧文档另造 UI |

**工作中的 HTML 是单一迭代源，不等于尚未审查的版本可以直接驱动 G4 实施**。存在新的 UI 迭代时，未重新批准前不覆盖已接受的 `main` 设计版本。

## 标准的边界

HTML 是**UI 视觉与交互标准**：工程树、测试项目/用例编辑、菜单与鼠标操作、工作区信息密度、专业窗口、状态呈现、真实可编辑用户流程。

HTML **不是**新的业务或硬件事实源：TEST DSL、DBC/Controller/TCF/TBC、真实 CAN0/CAN1、KEY_SIM 的物理能力、工位实体写许可、UDS/ISO-TP、Flash 危险操作、WAVE 采样/质量、扫码生产与证据仍以 [现行业务决定](../project-management/new-baseline-20261112/PM-BUSINESS-DECISIONS-20261114.md)、[输入模拟勘误](../project-management/new-baseline-20261112/PM-INPUT-SEMANTICS-20261114.md)、正式设计/真实源码/实测证据为约束。

HTML 可以使用演示数据说明交互，但必须清楚隔离 EPB 样本、虚拟操作和真实台架，不得将演示当作真实设备状态。WPF 不机械移植 HTML JS/DOM，而是实现被用户认可、经 PM 对齐的**等效视觉和交互行为**。

## 一次 UI 交付需要提供的信息

- GitHub URL：`ui/ide-design/docs/ui/AutoTest_UI.html` 和完整提交 SHA；
- 当前版本改了什么，修复了哪些用户反馈，哪些仍未完成；
- 需复查的产品与后端接口差异（不能把未验证行为标为已实测）；
- 是否请求 `PM_COMPATIBILITY_REVIEW` 或只是 `CONTINUE_ITERATION`；
- 用户明确同意某版本视觉/操作后，再提交给 PM 申请定版归档。

**记录与跟踪：** [Issue #51｜唯一 HTML 设计源](https://github.com/ch3758134-prog/AutoTest/issues/51)。独立 UI 专项无需每次小改动都等 PM；完整交付后 PM 才进行必要的兼容审查。A2/DEVICE/WAVE/F1 技术研发不因 HTML 尚未完成而停滞。
