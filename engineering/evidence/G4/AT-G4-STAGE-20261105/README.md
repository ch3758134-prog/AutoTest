# AutoTest · G4 软件阶段性候选归档（2026-11-05）

**性质：实施证据登记 + 下一轮简化台架范围划分。**

- HOST 分支 `g4/v1-stage-4` 的 DEVICE 软件检查点为 `dd65cbdfbd849da98a4618bec8cb31923e695c95`；阶段文档提交后的 HEAD 为 `0098b9fcdc83db3331e661f76d1fc4375c4269c6`。
- 本机 Windows 已重新执行 Restore（退出码 0）、Release Build（退出码 0，0 warning / 0 error）和全量 Tests（退出码 0，145/145 通过）。详细测试项目计数见 `SOFTWARE_STAGE_REPORT.md`。
- 真实 B0/B1/B2、ECU、五路按键电压、UDS/Flash 尚未实测，不具备硬件 PASS。
- 下一轮是**在既有架构上的验证配置基线**，不是把 EPB 专属控制状态硬编码进 AutoTest，不宣布原 G3/G4 重审批。

文件：
- `SOURCE_SNAPSHOT.json`：上传 HOST/SAEF 来源身份、历史工作树快照及新增的本机归档确认信息。
- `LAB_SCOPE.md`：简化台架验证阶段、实测证据和安全放行。
- `SOFTWARE_STAGE_REPORT.md`：软件交付成熟度和测试结果、待关闭问题。
