# AutoTest｜G4 阶段成果接收、PR #61 集成与现代 Fluent UI 专项接管（2026-11-19）

> 最新负责人决定：认可 G4 阶段源码成果，优先受控接入正式 HOST main；现代 Windows Fluent 浅色 UI 的研究与用户操作验收由 ui/ide-design 专项统一负责，之后再由独立实施任务接线真实后端。
> 状态：G4_SOURCE_ACCEPTANCE_APPROVED / PR61_MERGE_CONFLICT / INTEGRATION_ASSIGNED / FIELD_G4_PARTIAL / FLUENT_DESIGN_NOT_ACCEPTED。
> 重要：同意接收源码候选≠允许不经冲突/构建审查立即合并≠G4 所有真实功能通过。管理写入仅 Project_Management，不写软件 main。

## 1. 最新决定与原管理记录的关系

- 2026-11-19 较早的 G4-STOP-UI-FIRST-REBUILD-20261119.md 中，“停止在 G4 继续反复重画 UI”“保留部分实测与后端资产”“以 UI 体验验收作为 WPF 实施前提”仍有效。
- **本次较新的决策覆盖早前的“不推进 G4 源码接收入 main”理解**：现在明确优先组织 G4 受控合入正式 HOST 主线。
- 活动 UI 设计唯一责任方：ui/ide-design（已提交 MODERN_WINDOWS_UI_TAKEOVER_20261119.md）；之前临时创建的 feature/ui-rebuild-20261119 为 PAUSED / NOT_DESIGN_SOURCE / STALE_BASE。未来若启用为正式 WPF 实施线，须重新基于已接收 main 的精确源码基线，并待具体 Fluent UI 方案用户批准；不抢先开发。
- 历史 G3 自动 TEST HTML 与旧 WPF 的操作细节是研究与兼容依据，不自动成为本次全 UI 的不可修改视觉规范；已批准业务/硬件边界继续有效。

## 2. GitHub 实际核查

| 对象 | 精确状态 |
|---|---|
| main | 1d6eeba8561a779640b20c5a3f384327a0018079 |
| G4 | feature/g4-html-parity-20261115@ce4dbd4401dac1a98ffa87885865d0b7c174df7f |
| 集成 PR | https://github.com/ch3758134-prog/AutoTest/pull/61；open / draft / mergeable=false / not merged |
| 三方祖先 | 4831ea2a01cce4112de063602534707aa55b727a |
| 差异规模 | G4领先670提交、落后main106提交，PR涉及252文件；32,287行增加，1,676行删除 |
| 现代 UI 专项 | ui/ide-design@56db0335a514c68548a9eb61ae88d4955e98bf20 |

GitHub compare 结果显示同一历史 G3 Gate 设计文件在两侧分别新增；但 main 一侧 compare 文件列表达到 300 条 API 上限，因此**不能从当前只列出的少量重合路径推断全部冲突数量**。须由受控本地三方 merge 实际获得冲突清单。

## 3. 正式 INTEGRATION 派发

- 已建立：https://github.com/ch3758134-prog/AutoTest/issues/62（P0，角色 INTEGRATION，未指定未经证实的个人 GitHub assignee）。
- 已在 PR #61 添加管理交接评论链接 #62。当前 PR 保持 Draft，**不批准现在直接 merge**。
- INT-00：锁定准确 SHA，取得完整 Git 合并冲突、文件所有权、模块差异、已完成/未验证功能与硬件安全清单；不得整仓 ours/theirs 覆盖。
- INT-01：受控临时工作区或短期候选分支逐一解决冲突、保护 main 的管理文档和现有后端；必须保留唯一 TEST DSL、DEVICE Session/Fence/UNKNOWN，旧工程兼容及版本锁定。
- INT-02：Windows 由 Codex 机械 Restore、Build、Test、真实 WPF 启动与原始日志；源码、UI、真实 CAN/USB/ECU 分层验收。
- INT-03：交付精确候选 HEAD、冲突处置表、回退方案、未验证事项与最终合并审查申请；用户同意后才合并。
- INT-04：真实合并获批后读回 main SHA，只标注“源码基线接收”，不默认为 G4 实机 PASS。

## 4. 专业边界与未完成事项

- G4 docs/design/G4_UNIFIED_EDITOR_AND_PUBLIC_FLOW_REMEDIATION_20261118.md 标记 CallPublicFlow 真执行未实现、直接 KEY 新语义未闭环，不能在 main 接收时放开 TEST 实体执行。
- G4 docs/integration/G4_ECU_CAN_LED_FLASH_20261118.md 明确主控 LED 与专业 FLASH 的新 HOST 接线缺少本轮 Windows Build/真实设备验证；不得只凭源码声称通过。
- 最新主控事实 IGN=HOUT1/PTC1/J1 C10；LED4=HOUT5/PTC10/J1 C6；RL_DRV 板级内部，不能用旧 GPIO 映射或固定 v0.4-dev 字符串断定现场固件。
- 新 UI 设计先研究 Inline Edit、Popover、持续 Inspector、右键菜单、Draft/Undo/保存、高 DPI；UDS 用于交互先行验证，但真实诊断业务仍由独立 UDS Lab 专业负责。
- 手动台架保留左 CAN 发送、中 KEY/IGN/夹具、右 CAN 监控；自动 TEST 保留资源树、准备/测试/清理、步骤和常驻 Inspector。Fluent Light 控件依赖先审查兼容/许可证/离线依赖，不自行叠加 UI 框架。
- WAVE/SCAN 完整集成待稳定的新版 UI 与正式 TEST 真实运行基线获得现场认可；FLASH/UDS 独立专项可继续实测。

## 5. 结论

项目管理目前完成的是负责人决定确认、PR 冲突调查与正式 INTEGRATION 派发；**GitHub main 未合并 G4，真正冲突处理与 Windows 验证正在等待 INTEGRATION 候选交付**。经负责人审阅后方可完成主线接收。