# AutoTest｜GitHub 统一交付管理中心

**本页是所有开发对话、主对话审查、本地 Codex 验证的唯一交付入口。** 2026-11-09 起执行。GitHub `ch3758134-prog/AutoTest` 同时保存 `SAEF/`、`src/`、`tests/`、`docs/`。不再以聊天附件 ZIP、手工覆盖源码目录或本地另一个长期开发 Git 仓库作为正式交付渠道。

> **权威区分**：Git commit/PR 是可复核源码事实；`main` 是经批准合入的基线；PR 是未批准候选；SAEF 保存工程决策/变更及批准历史；本地 Codex 仅取得主对话指定的精确 commit、编译、启动并提供原始错误与日志；**不负责任何 Git/源码审查、研发决策、审批或人工实测**。设计审查通过、编译测试通过、用户实测通过、系统集成通过是四种独立事实。

## 1. 唯一流水线

```text
专业对话（在 GitHub 专业分支编码）
  -> 推送分支 -> 创建/更新 Draft PR 到 main
  -> 主对话核查 PR 的源码、范围、设计、风险与依赖
  -> 主对话给出本地 Codex 精确 commit / PR 运行任务
  -> Codex 拉取指定 SHA -> Release Build / 指定测试 / 真正启动专业软件 -> 用户自行实测（本地不管理 PR）
  -> 主对话审定：拒绝/退回专业修订/可独立用户实测/批准合并
  -> 用户按自己时间集中实测（适用的专项）；问题回原专业对话
  -> 有依据地批准进入 main / 后续 INTEGRATION -> 系统回归
```

**重要**：具有独立物理操作的 DEVICE/BENCH 等专业需要用户完成与所需功能相符的受控实测后才能宣布物理验收；R1 UI、TEST、RECORD/报告则持续开发和自动测试，正式用户操作验收安排在统一 HOST 集成后。某候选处于 `USER_LAB_PENDING` 不能导致其他分支停工。

## 2. 现有 GitHub 清单（2026-11-09 初始审计快照）

下表只记录本次实际核对到的远端情况；**以 GitHub 实时 PR HEAD 和状态为准**，不可将文档快照当自动更新的看板。

| 模块 / 候选 | GitHub 交付 | 已证实的状态 | 下一关 |
| --- | --- | --- | --- |
| 主线 | `main@d9040c461f56617cfaba3821250c6efef0c7e20d`（登记时） | 既有 HOST + SAEF + 设计文档；本次未重跑 Windows 全量验证 | 保持受控，任何候选不得直接覆盖 |
| DEVICE | 已合入历史 `46aaa6754c62028e936cc538713e88e4dca93100`，Issue [#3](https://github.com/ch3758134-prog/AutoTest/issues/3) | 只读设备诊断部分已在 main；用户正在进行设备实测/排障，其他能力待核证 | 后续 Device 改动统一走新 PR |
| BENCH / UI 业务台架 | [PR #2](https://github.com/ch3758134-prog/AutoTest/pull/2) · `ad8df5e369328a3d6518a8aab465fc570d26b870` | Draft/未合并；Windows Build/Test NOT_RUN | 主对话审查→Codex 验证→适用现场实测 |
| R1 总体 UI | [PR #4](https://github.com/ch3758134-prog/AutoTest/pull/4) · `b15638026b1f7db9501254ccc6a48dcf07c3bcf3` | Draft/未合并；Windows Build/Test NOT_RUN | 继续组件研发与自动验证，用户整体 UI 验收后置 |
| UDS / Flash | [PR #5](https://github.com/ch3758134-prog/AutoTest/pull/5) · `88acd016cbe19cc108c633e1020e984b116d24dc` | Draft/未合并；Windows Build/Test NOT_RUN | 主对话审查→专项验证；Physical 仍受独立门禁 |
| WAVE | 此前有 ZIP 候选，**未核到正式 PR** | 尚不能视为正式 Git 交付 | 将源码提交专业分支并创建 Draft PR |
| SCAN / Production | 此前有 ZIP 候选，**未核到正式 PR** | 尚不能视为正式 Git 交付 | 将源码提交专业分支并创建 Draft PR |
| PROJECT/DBC | 现役 main 有基础源码；**本轮未核到新候选 PR** | 待专业申报 Git 候选 | 继续自己范围开发，交 PR |
| TEST | 现役 main 有基础源码；**本轮未核到新候选 PR** | 用户正式操作验收在 HOST 集成后 | 持续开发，交 PR+软件验证 |
| RECORD/报告 | 现役 main 有基础源码；**本轮未核到新候选 PR** | 用户正式操作验收在 HOST 集成后 | 持续开发，交 PR+软件验证 |
| INTEGRATION | 主线/共享集成方向 | 负责后置统一接线、构建与跨专业测试 | 在主对话审批后接入，不抢改专业私有代码 |

模块/交付项数量不等于聊天数量。按交流角色可使用九个对话标识：`DEVICE`、`PROJECT`、`UI`、`WAVE`、`UDS-FLASH`、`SCAN`、`TEST`、`RECORD`、`INTEGRATION`；其中 **BENCH 与 R1 Shell 是独立所有权的 UI 子交付**，即使由不同对话承接，也必须分别报候选。具体对话名称不影响 Git 审核规则。

## 3. 固定导航

- [01｜PR 交付协议、主对话审批和合并门禁](01_交付协议与审批门禁.md)
- [02｜本地 Codex 只拉取指定 SHA、编译、启动及提供日志](02_本地Codex唯一验证工作区.md)
- [03｜九个对话统一沟通与回报格式](03_各分支统一沟通指令.md)
- [R0-LAB 05｜并行研发与集中实测分类规则](../r0-lab/05_专项独立实测优先于统一集成_20261109.md)
- [R1 UI 已归档设计与实施任务](../ui-r1/00_R1总体UI开发与接入边界_20261109.md)

## 4. 本主对话的审批责任

本主对话是唯一的设计/交付审批入口：检查源码事实与 PR diff、代码所有权、与最新 `main` 的差异、功能/权限风险、接口影响和可复核验证证据。必要时在 PR 评论记录决定，**没有证据不批准合并**。若 GitHub 不能直接提交某个 Review/合并动作，报告阻塞并给 Codex 精确机械 Git 指令；不得假称已审核/合并。

九个专业对话不必互相手工转发报告。它们各自在自己 PR 上记录候选、依赖和进度；用户在主对话只需说“审查当前 GitHub 的待交付 PR”即可发起集中审查，或贴一个 PR 链接。**ChatGPT 不会在你没有发起新任务时自动持续监控 PR**，因此不能声称后台自动审批。
