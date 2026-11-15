# AutoTest｜G3 全量详细设计交付 v0.1（2026-11-15）

> **交付状态：待项目负责人正式批准。** 本包提出 G3 Gate 候选 `PASS_WITH_LIMITATIONS`，建议批准后优先进行**严格 HTML 对照的 WPF UI 重建 + 真实工程编辑实测**。本包的创建和提交不是 G3 已批准，不启动未许可的设备动作或刷写。

## 先看这三项结论

1. **严格 UI 实施可行：** 新版自动测试 HTML 已获用户正面反馈，已将完整、原样的文件固定归档；原生 WPF 必须逐项对照视觉、操作、鼠标/快捷键/状态，不得以历史 UI 迁就实现。
2. **清理旧 UI 而不是兼容：** `local` 的 MainWindow、TEST Editor、ProjectWorkbench、Inspector 和旧参数弹窗要在 G4 UI 候选中删除或整体重建，旧 VM 业务逻辑迁至唯一正式服务；工程/DBC/TEST/USB/CAN/记录等真实后端保留并补功能，不能整支 `local` 直接合入 `main`。
3. **前后端分步推进：** 第一版自动测试 WPF 尽快交用户在 Windows 实测编辑和资源管理；TEST 流程控制、项目真实 Run、DEVICE、扫码生产、报告逐步接线；波形/判定在用户真实测试中调整；Flash Lab 在已有独立分支开展独立 EXE 研究/实施，独立 UDS 专业 UI 在 Flash 实测研究后继续。

## 本次完整交付

| 文件 | 你应该看什么 |
|---|---|
| [G3-00｜完整准出审查](G3-00-GATE-REVIEW.md) | 所有模块覆盖关系、已完成/未完成、10 条限制、建议准出结果与批准边界 |
| [G3-01｜严格 HTML→WPF 实施合同](G3-01-STRICT-UI-PARITY.md) | 冻结 HTML 样式/关键交互、实现映射 U-01～16、视觉/交互验收 V-UI-01～08 |
| [G3-02｜HOST 旧 UI 清理与后端继承](G3-02-SOURCE-CLEANUP.md) | 精确 UI 文件删改/删除策略、必留业务源码及新增后端清单 |
| [G3-03｜TEST/流程/项目运行/证据](G3-03-TEST-RUN-CONTRACT.md) | 测试项目、IF/SWITCH/WHEN、变量、操作、项目级 Run、清理/安全/生产扫码语义 |
| [G3-04｜专业模块接口与推进顺序](G3-04-SYSTEM-MODULES.md) | PROJECT/DBC/DEVICE/WAVE/SCAN/RECORD、Flash 独立 EXE、UDS 后置和波形实测优化 |
| [G3-05｜G4 代码任务与验收](G3-05-G4-WORKPACKAGES.md) | G4-UI-00～03、后续分切代码、精确接口候选、Windows 实测和差异处理 |

## HTML 单独冻结，不随 UI 研究分支漂移

- [原样冻结 HTML](../../../ui/archive/auto-test-20261115-ec75a9fe/AutoTest_UI.html)
- [冻结说明、SHA 和可用范围](../../../ui/archive/auto-test-20261115-ec75a9fe/README.md)
- 源：`ui/ide-design@ec75a9fe69626d2a83e0c4ae6b23d187d5ccf5a2:docs/ui/AutoTest_UI.html`
- Git Blob：`c1a55e295c0f5321736fa3a8a7db20e13415a727`
- UI 研究对话继续更新其工作 HTML；**它不自动覆盖这份实施参照**。只有用户认可的新版自动测试设计通过定点审查，才会另存新快照并修订 G3/G4 对照基准。

## 独立 Flash Lab 研究分支已经启动

- 专用分支：[`feature/flash-lab-20261114`](https://github.com/ch3758134-prog/AutoTest/tree/feature/flash-lab-20261114)
- [F0 真实源码核对、独立 EXE 结构和 F1/F2 前置条件](https://github.com/ch3758134-prog/AutoTest/blob/feature/flash-lab-20261114/docs/flash-lab/F0-SOURCE-AND-INDEPENDENT-EXE-20261115.md)
- F0 研究提交：`79e4447092668df3e443f32313e500ef5cbefc6a`
- 现在是**研发调查/设计阶段**，**独立 EXE 尚未建成**，不能声称 USB/CAN1/UDS/实体刷写已验证。先烧录研究及独立工具，UDS 专业 UI 研究后置。

## 上游与批准边界

- P0/G0/G1/G2 已批准；G3-DEVICE-01、G3-WAVE-01、G3-TECH-INTEGRATION-01 **三份技术专项已批准**，本包继承其技术限制；G3 整体尚未正式准出。
- [用户产品决策](../PM-BUSINESS-DECISIONS-20261114.md) 与 [输入模拟语义纠错](../PM-INPUT-SEMANTICS-20261114.md) 在旧设计存在错误的地方优先解释。G1 REQ-32/33 独立 CAN 工具差异按 [Issue #50](https://github.com/ch3758134-prog/AutoTest/issues/50) 定点修订候审。
- 旧 `main` 与 `local` 分叉：本轮文件审查参考 `local@a9a26ebfba1efcf032ff8ec8c435336a89469647`；未来实际编写源码必须重核 Git HEAD、变更文件和引用影响。
- 研究成果/HTML 不能替代 Windows WPF/真实 ECU 实测，不因 Gate 批准自动允许危险实体输出、删除安全 Fence 或启动真实 Flash 写。

## 待负责人审查的两个决定

**A｜G3 本轮技术准出：** 是否批准本包 G3-00～05 的 v0.1 精确正文，Gate 结论 `PASS_WITH_LIMITATIONS`，并认可 G3-L01～10 的限制与 G4 授权边界？

**B｜G4 首批实施：** 是否同时授权仅 `G4-UI-00～03` 的**原生 WPF 严格重建旧 UI、真实工程编辑和 Windows 用户实测**，以及独立 Flash Lab 的 F1 **离线配置/镜像/EXE 基础**（不含 CAN1 实体发送、ECU 编程）？

两个决定可分别给出。若只批准 A，不自动视为批准 B。
