# 00｜Git 配置快照（2026-11-09）
> 只读快照，基于查询时间；此文不是自动更新看板。任何下一次操作前重新读取远端 HEAD。没有删除任何旧分支。所有 SHA 均为完整标识。

## main
`8852344f602cc2242b1fddfddef3bef08354f5ec`（审查时）。远端 main 未启用保护规则，且尚未在本次恢复中执行全量 Windows 构建、测试或 WPF 启动。不可把“源码稳定”视作本次已验证事实。

## 旧有分支及 HEAD
| 分支 | 精确 HEAD |
|---|---|
| main | 8852344f602cc2242b1fddfddef3bef08354f5ec |
| docs/r0-lab-lifecycle-v5-20261108 | 3a1bfa320a640488af7dc51a897d85863c22cc49 |
| fix/r1-workspace-keybinding-20261109 | d1fa469a18d0b3da2713bcb14e4503543d37bb9f |
| flash/physical-lab-20261109 | e66fb5ad85e7ee48d6fa05efe21996c2c1bf377d |
| integration/r0-lab-physical-bringup-20261109 | cbb5fe759bf9036e78b4400c1cbfce4412c5ead7 |
| integration/r1-real-specialist-host-20261109 | df8480d5cf5e79778e48c415e281341ffbbd5244 |
| p1/device-readonly-uds-can1-tx-20261109 | e50be650cbcc8fdb73d1de2f057c087220a37540 |
| r0-lab/uds-flash-observability-20261108 | 50c1e121f21af0d29cf34661b02d88b1ebbe5b81 |
| r0-lab/wave-replay-offline-02 | a711628828ddcec26a40d59db445500c0fed69d1 |
| r0/device-station-keyvoltage-20261109 | cc2efc3fe26eae9fb42d120d37ae1a639d7665a1 |
| r0/integration-diagnostics-01 | 1250d13f387416df5993c79601223943f74a3fa2 |
| r0/project-dbc-obs-02 | 435fe250a492df66f01134bb06207a1439220d3c |
| r0/record-diag-02 | 571be18689f8b8d785c3c0f310357012a2a1c898 |
| r0/scan-production-diag-02 | cc075ce1cac8b3b984eff5024060bbc7edfe8c45 |
| r0/test-formal-r2-r3 | 0d29c0fc9e30795bbe8dc42eed3e1cee7da8a8dc |
| r0/ui-bench-obs-01 | ad8df5e369328a3d6518a8aab465fc570d26b870 |
| r1/ui-workbench-u1 | efb2bbc7c443021c4179df334617ea0baa32f21d |
| uds/p1-readonly-issue17-pr16-20261109 | ae22a761323f1a920349475a1d595e8f37be960e |
| work/r1-real-functional-labs-20261109 | ec6b1282fc05520e21bf7c55ee96a42ebdc05587 |

## 旧 PR 退出交付队列的实际执行结果
2026-11-09 已在 GitHub 为旧 PR #2、#4、#5、#6、#7、#8、#9、#10、#11、#12、#13、#16、#18、#19、#20 登记恢复备注并 **关闭（未合并）**。重新查询开放 PR 仅 #21（本恢复候选）。PR #1 原本已合并。旧 HEAD、原始 PR 讨论与远端分支仍保留；关闭不意味着代码审计/提取已完成。

- #13 以提交祖先关系被 #16 包含；#16 又是 `work/r1-real-functional-labs` 的祖先。**祖先包含不等于每项功能均正确、测试也不等于相同**。
- `flash/physical-lab` 与 #16 分叉：存在独立 Flash 代码与 Shell/工程文件修改，不能按文件名合并。
- UDS/DEVICE 的 P1 物理链路仍是独立候选，不能把 PR 存在当真实 UDS 通过。
- 主干分支结构与真实业务模块代码分工交错，当前不宜批量合并、强推或删除。

## 必须核实的证据缺口
- 旧 main 当前精确 SHA：完整 Windows Release Build + 全量 Tests + WPF 启动证据。
- PR #16 在早期 SHA `5b8fa13306d9e1a31f43c5e09c00d13cb1f5de91` 有 Windows CI PASS / Desktop 136/136；后续 HEAD 已变且有 CI FAIL，不能沿用通过证据。
- DEVICE WinUSB/CAN1 RX 有用户现场证明，但该本地快照与具体远端精确 SHA 的对应需确认。
- 无法从 Git 远端直接恢复全部原理图 PDF、两份现役固件 ZIP、独立 SAEF 工具和原始台架日志。
- 本次恢复仅创建新文档分支，不代表完整 Git Bundle 备份或现场资料备份完成。

## 无损清理准入
先对每个旧候选建立 `BranchHead / MergeBase / UniquePatch / FileOwners / Tests / IntegratedEquivalent / Decision`。
旧 PR 已经以 SUPERSEDED 状态关闭，保留历史讨论与 Git 引用。只有经核对某独有功能的处理结果、备份并确认无继续依赖后，才能删除其远端分支。任何旧的标签、分支、PR 关闭均需可追溯记录；**不要直接删分支让 Git 图“好看”**。
