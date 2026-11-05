# G4 本地机械集成合同候选（不是立即执行指令）

此合同只能在用户授权具体工作包后，由主对话填充完整源Git HEAD / 文件哈希 / 指令后发Codex；**不可直接执行当前模板**。

## 输入清单

- G3审批HTML SHA256 `4711733cc2b7e5d125aadce9a97e574d877f4e4825d041f3eaaf8f778f35be46`，SAEF 文档基线 `5de99332170d9434c7e5b4a15c9d091245a7302c`；源Git HEAD另填且必须核查。
- 单一源码仓库绝对路径：`UNSET`；`BASE_COMMIT=UNSET`；`CONTRACT_REVISION=UNSET`。
- 本轮按精确路径授权的 `NEW / REPLACE / DELETE`，旧SHA和新SHA必须逐项验证。
- 待运行的restore/build/test/publish/WPF/真实设备命令，由ChatGPT指定；非授权命令一律不运行。

## 执行顺序

1. 只读检查源码根路径、当前branch、HEAD和clean；不匹配停止。
2. 只读计算目标文件旧SHA、确认文件所有权与接口Revision；不匹配停止。
3. 先检查补丁包内部相对路径唯一、无路径穿越、无未授权删除、无跨方向重复覆盖。
4. 单独应用经批准的补丁，执行固定构建/测试命令，保留真实 stdout/stderr/exitcode。
5. 必要UI启动和台架操作依用户批准及具体安全指令执行。**Codex不得自行触发危险硬件动作**。
6. 失败就STOP，不自行更换依赖、重写csproj、改代码或修测试。保留工作区变化和原始日志等待主对话下达处置方案。
7. 全部实际验证通过后，再按批准的Git提交说明进行受控提交，并回传Git HEAD/clean/测试报告。

## 必须回执

`BASE_COMMIT`、输入交付ZIP SHA、应用文件/旧新SHA、实际命令/ExitCode、构建/测试数量、失败完整堆栈、Windows截图/实机回执（如适用）、Git提交/最终状态、未运行项目及原因。其他旧工程和SAEF文档仓库保持不变。
