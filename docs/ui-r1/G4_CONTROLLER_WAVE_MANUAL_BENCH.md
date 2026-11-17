# G4｜正式按键配置、波形和手动台架实施说明

当前阶段：**代码候选，Windows WPF Build / 台架实测未验证，不准申报功能验收完成。**

## 入口与数据来源

- **按键配置**：正式 AutoTest Desktop 工具栏 → `按键配置`。走 `ProjectWorkspaceService` + `ConfigurationWorkbenchViewModel` + `EcuControlProfile`，配置功能动作、状态、20 Key 电阻组合、冲突约束、五路电压目标/区间。配置必须保存才可参与后续物理操作。
- **波形配置**：工具栏 → `波形配置`，使用 `WaveSettingsDocument`、DBC 信号选择、参考波形提升与比较条件。不是新的 JSON 旁路。
- **波形**：工具栏 → `波形`，独立窗口从当前工程的 `wave-records/evidence` 和 `wave-records/references` 读取经侧车校验的实际证据与版本化参考，曲线与双游标仅用于查看、不会重写原始数据、不会生成 PASS。
- **手动台架**：工具栏 → `手动台架`，独立标准最大化 WPF 窗口。左 CAN1 发送；中 KEY_SIM 20 Key、已配置功能动作、IGN、夹具状态；右 CAN RX/USB 事件与 JSONL 导出。使用 `HardwareConnectionService` 的**单一真实 USB 连接**，不使用 Mock、旧 `StationControlService` 或第二 CAN 监听器。

## 硬件边界（必须遵守）

1. USB 主控 CAN1 → DUT；CAN0 → KEY_SIM。主控命令 `SET_IGN=0x12`，请求载荷 1=ON、0=OFF。
2. KEY_SIM 的 `WS/EPB_EOL/Sources/APP/KeySim.c`：标准帧 ID 0x730、DLC 8，数据 `01 maskL maskM maskH[3:0] 00 00 00 00` 表示 20 路组合；`80 00 00 00 00 00 00 00` 为释放。Key 是电阻网络开关，合成五路模拟电压，不是二十路彼此独立的 ECU 数字输入。
3. 20 Key 组合发送需要当前工程已保存的 `EcuControlProfile.KeyHardwarePolicy`。检查可用掩码、禁止组合以及配置资产 Revision / SHA / 文件完整性。选择 Key 或 ECU 功能状态仅改变 UI 草稿，**不会自动操作硬件**。
4. 手动 CAN1、KEY_SIM/CAN0 发送使用现有 `PhysicalCommandFence` 的精确单帧确认；IGN 添加同等的精确单次授权，15 秒内、限定一个 USB 会话、写入前消费授权。
5. 真实 TEST 运行得到物理授权时锁定手动发送，避免两个执行路径交叉驱动台架；正式 Run 授权还会清空尚未消费的单次 CAN 与 IGN 令牌，不能绕过传输层锁。
6. 所有发送结果只显示主控 **设备 ACK**，绝不等价于 KEY_SIM 已受控、ECU 已识别电压、夹具已动作或 DUT 已收到。
7. **夹具电磁阀锁定**：当前 USB 固件命令映射及现场控制引脚尚未核准，不能猜测控制码、复用旧 Mock 服务或绕过安全门。
8. 连接/断连不自动送 KEY 释放、不自动切换 IGN，重连后状态标记 UNKNOWN；现场仍须确认实际电气状态。

## 能力边界

- 手动台架的 CAN1 编辑可选择**真实 DBC 报文 + 逐路完整信号物理值**或原始 ID/HEX。使用正式 `DbcCanMessageEncoder`，保存前确认实际 DBC 的 Revision/SHA，未填写完整信号、数据越界或未验证的多路复用结构均拒绝编码发送。
- 手动台架本批包含 CAN1 单次发送、CAN0 20 Key 组合下发、IGN 单次命令、CAN 真实接收日志与导出；**未实现周期 CAN 发送、夹具控制、完整动作序列编辑器**。
- 波形独立窗口支持已保存实际证据、参考、曲线、双游标；**实时采集/多轴叠加仍由正式 TEST 与既有波形专业服务承载，尚未接入独立窗口**。
- 选中 Controller 配置或 waveform 配置不意味着 ECU 接线、波形参考或实际电压已被测量验证。

## 需要进行的 Windows 验证

```powershell
dotnet build src/AutoTest.G4Workbench/AutoTest.G4Workbench.csproj -c Debug
dotnet test tests/AutoTest.Devices.Tests/AutoTest.Devices.Tests.csproj -c Debug
dotnet run --project src/AutoTest.G4Workbench/AutoTest.G4Workbench.csproj -c Debug --no-build
```

须确认：四个工具栏入口、按键配置保存/重开、波形证据读取及游标、无项目/无设备时禁用输出、配置变更阻断 Key、USB 断连状态、错误确认拒绝、真实 ACK/超时记录与 JSONL 导出。纯协议测试不代替物理工位测试。

请勿把任何未执行的命令、尚未运行的测试或 UI 代码静态检查称为 Windows 验收通过。
