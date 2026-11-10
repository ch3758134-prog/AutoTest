# AutoTest｜恢复入口（2026-11-09，候选，不取代已批准 SAEF）
 
> **状态：RECOVERY_PROPOSAL / NOT_MERGED / NOT_RELEASED。** 本目录是无损梳理候选，不重写 G3/G4 批准历史，不直接宣布改变需求。main 与全部旧分支、PR 目前均原样保留。

## 先明确我们要造什么
AutoTest 是一套完全离线 Windows ECU 研发、集成测试与出厂检测工作环境。硬件为现役主控 PCB、按键 PCB、线束、IGN、夹具、外部电源和扫码枪；ECU 是外部 DUT。当前 EPB 是首个样本，不是 HOST 内置产品模型。当前单 PC/单 DUT，未来考虑多 DUT 的资源隔离。

**产品定义是持续探索过程。** UI、报告、工位工作流、扫码规则、烧录配置、测试操作方式中仍有大量待调查及待试用项；AI 研究的建议不自动成为已冻结需求。R1 是初始愿景和范围假设，不是完备清单，更不是逼迫所有页面提前一次设计完成的硬性承诺。任何版本只对明确选定、可验证的功能集合作出承诺。

### 两条实际工作主线
- 研发工具：工程/DBC/资源、CAN Monitor、UDS、Flash、Wave、Manual Bench、测试程序编辑/调试和原始证据。
- 生产工具：受控 DUT 身份/扫码匹配、烧录/测试、Run/StepFact/Verdict、追溯及报告。**报告样式、内容与统计应根据真实样本迭代定义。**

这些共享唯一设备 Session、Station、工程资源、Test Runtime 和 Records。不允许每个 Lab 重造另一套核心或直接拥有新的 USB 会话。

## 恢复期间唯一阅读顺序
1. 本文件：当前恢复目标、边界及状态口径。
2. `00_GIT_SNAPSHOT.md`：远端旧分支与 PR 精确身份；历史仍存在。
3. `01_PRODUCT_AND_DECISION_MODEL.md`：已确认事实 / 临时工作假设 / 待探索的区别。
4. `02_GIT_AND_MODULE_WORKFLOW.md`：未来模块责任、分支与集成规则（候选）。
5. `04_BACKEND_SOURCE_AUDIT.md`：按真实源码判断后端 KEEP / REWORK / TEST-ONLY / REPLACE，**旧 UI 彻底重做**；当前 main 尚未 Windows 验证。
6. `overview.html`：给负责人连续阅读的 **增量式** 状态总览；不把静态 HTML 当作完整 SAEF 平台。

## 三项不可越界约束
- 当前 main 软件代码未在本恢复分支修改；过去有 Build PASS 的旧 SHA 不等于当前 HEAD PASS。
- 真实烧录、IGN、夹具、按键、CAN TX 均依原有物理安全门禁。Mock/Replay/USB ACK/CAN RX 不等于 DUT 实测成功。
- SAEF 的 `control/project_state.json` 与 `work_products.json` 记录历史 Gate；其中历史文件正文“候审”字样不可充当最新状态，修订必须经受控审批。不能删除未归档的原始板级原理图、固件与工作包。
 
## 现在还没有完成
没有执行任何旧分支删除、旧 PR 关闭、main 重置、跨分支代码合并、Release Build、WPF UI 启动或物理操作。当前新治理文档为 **建议的可审核恢复基线**，非全面迁移完成。

## 批准后推进顺序
A. 锁定 Git 和现场原始资产备份，核验离线可恢复性。
B. 核验 main 当前 Windows Build/Tests/DeviceLab 与稳定 tag，明确仍不具备的实测能力。
C. 逐分支检查独有代码及测试，按责任将候选拆出，优先保留已工作的后端和现役设备通路。
D. UI 先建立 WPF DesignSystem/Gallery/真实截图及交互验证，再逐工具合入。
E. SAEF 恢复“当前状态快照 + 事实/设计决策登记 + 历史记录 + 增量 HTML”，不再为每个 AI 设想生成“已完整设计”的假象。
