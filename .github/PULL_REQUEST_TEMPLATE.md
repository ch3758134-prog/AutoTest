# AutoTest 专业交付候选

> 本 PR 为候选，未经主对话审批不得直接合并 `main`。统一交付中心：`docs/delivery/README.md`。

## 交付身份
- CandidateId:
- Specialist / owner:
- BasedOnMainSha:
- HeadSha:
- 功能范围与本轮实质变化：
- ChangedPaths（可核查 Git diff）：

## 启动和接口
- 专业入口（独立 WPF/工具或未来 HOST 入口）：
- 功能来源：Mock / Replay / Physical / 混合，具体说明：
- 复用的合同、服务与资源 Revision：
- 对共享接口、DEVICE、Shell/Prism 等的依赖/变更申请：
- 操作、失败与原始 Trace/日志导出方法：

## 软件验证（未经运行必须填 NOT_RUN）
- Build 命令、环境和结果：
- xUnit 命令、PASS/FAIL/SKIP、日志：
- WPF 启动/操作验证及原始证据：
- 静态检查、ZIP CRC 不能冒充 Build/Test 通过。

## 实测及安全
- 是否需要用户专项实测：YES / NO（R1 UI、TEST、RECORD/报告正式操作验收后置）
- UserLab：NOT_RUN / USER_LAB_PENDING / FAILED / RETESTED / ACCEPTED：
- Physical 权限/准入/失联与失败处理：
- 未验证的电气/ECU/Bootloader 事实：
- 当前不能宣称完成的功能：

## 下一步
- KnownGaps / 本专业继续开发项：
- 下一轮预计提交的最小可验证增量：
- 需要主对话裁决的最小问题单：

> 版本与结果必须绑定本 PR 当前 exact HEAD SHA。PR 更新后，应核对此前软件验证是否仍适用。不得将真实客户身份/条码原文、密钥、生产原始数据或未经脱敏的物理日志上传公开仓库。