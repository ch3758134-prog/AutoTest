# PRECHECK-01｜演进式生命周期模型真实档案首轮核查

> 2026-11-09，人工静态检查；**仅候选审查，不是工具自动化测试通过，也不是 Gate 审批**。
>
> 读取基线：`ch3758134-prog/AutoTest@4530aba0079e8b2344e4ff5e706049450458a45b`。

## 已核查来源

- `SAEF/engineering/control/work_products.json`
- `SAEF/engineering/control/project_state.json`
- `SAEF/engineering/work-products/G1/G1-03/index.md`
- `SAEF/engineering/work-products/G2/G2-01/index.md`
- `SAEF/engineering/work-products/G3/G3-01/index.md`
- `docs/architecture/00_源码权威与设计实现责任_20261109.md`

## 观察 1｜批准状态与正文标签不能直接等价

- `project_state.json` 明确显示 P0 PASS、G0～G3 PASS_WITH_LIMITATIONS、G4/G5 NOT_REVIEWED。
- `G1-03/index.md` 标题仍为“候审”，包含 `CONFIRMED/CANDIDATE/TO_VALIDATE` 的需求成熟度，**不是**每条需求独立的 Gate 状态。
- 结论：工具必须分别展示「工作产品的批准快照」「该工作产品内部的需求成熟度」「当前工程适用性」。不能见到“候审”就覆盖历史批准，也不能见到 Gate PASS 就推定所有 CANDIDATE 均正式承诺。

## 观察 2｜现存文本跨时态

- `project_state.json` 仍保留历史 `source_repository` 和 `source_revision=0098b9f...`；不能据此报告现代 HOST HEAD。
- `G3-01/index.md` 正文写着“G4尚未实施、所有测试初始 NOT_RUN”，属于该详细设计发布当时的历史陈述；当前 `main` 已持续进入源码构建与测试阶段。
- 结论：设计管理工具必须将 **当时批准状态**、**当前实现状态**、**当前验证证据** 分成独立可溯源数据，禁止用静态文案替代实时状态。

## 观察 3｜KEY_SIM 实例不代表现有 L1/L2 一定错误

- `AT-SYS-011` 已明确：经验证范围内控制按键板模拟输入，不能把五路电阻网络当作任意 DAC。
- `G2-01` 已记录：按键板 20 Key、五路电阻输出、经主控板 CAN 控制。
- 用户现场进一步澄清可组合保持、不同 DUT 输入通道/识别区间不同；需核查这些语义是否在 L3/L4/具体 UI/固件协议被正确表达。
- **候选结论**：本案例首先触发下层实现与子系统设计的一致性审查；只在证明 L1/L2 确有缺口后，才修订上层。此结果反证“发现问题就从头改 G1/G2”的机械回溯不适用。

## 观察 4｜未来工具必须识别真实验收与缺失资产

- `G2-01` 明示 B0～B6 是复验计划，不可宣称现役新 HOST 已控制硬件。
- 源码权威说明区分 `main`、候选 PR、冻结固件、历史参考、物理台架验证；不能将“已编译”“已有设备命令”“真实电压通过”合并为一个完成状态。
- 工具首版应保留证据等级、确切软件/固件/硬件身份和适用范围。

## 仍未验证（不可标为 PASS）

1. `AT-SYS-011` 到确切 ICD/子系统详细设计/当前 `main` 文件/测试的 **行级关系和实际代码符合性** 尚未完整核对。
2. `G1-03` CSV 原始数据与 Git 当前内容、各批准 SHA 的一致性尚未批量校验。
3. `eng.py` 的完整工具字节及原始工程/固件资产并非全部现存远端内容，不能宣称从 Git 可完整重建原始交付。
4. 暂无实现的关系图遍历、哈希指纹校验、编码上下文生成与对应自动化测试。因此 LM-01～09 未通过。

## 对工具设计的立即影响

- 第一项实测必须先实现“版本、批准、成熟度、实现状态、验证状态各自独立”。
- 影响传播的默认状态为 `POTENTIALLY_IMPACTED`；人工或有证据的规则确认后才为 `REVISE / NO_CHANGE / INVESTIGATE`。
- 优先跑通 `AT-SYS-011 → G2 → G3 / ICD → 当前代码/测试` 的最小追踪，再扩大到其他模块。
- 当前结论：**模型需要实现验证，不能把已有 PR #28 的设计文档算作成功验证。**
