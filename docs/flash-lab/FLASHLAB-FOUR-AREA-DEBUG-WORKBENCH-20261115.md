# FlashLab 四区真实烧录调试工作台整改（2026-11-15）

状态：源码已提交；Windows 编译、窗口启动及真实 ECU 烧录尚未验证。PR #55 Draft；不修改 main。

## 当前用户批准的范围
只保留 USB 主控连接、实时 CAN1 TX/RX、完整烧录配置（XML、固件、安全文件和 Seed/Key 算法文件）、完整烧录阶段及错误证据。

## 实施证据
- FlashLabWindow.xaml 重构为顶部 USB、左侧配置、右侧十阶段及 UDS 记录、下方 CAN1 实时表格。
- SafetyFilePath 与 SeedToKeyDll_Path 两个配置字段分别导入、可在配置表编辑并随 XML 保存；不再使用同一个文件选择控件混淆。
- 安全算法实际调用来源需明确选定；现阶段仅将选中的一项纳入当前烧录任务，不假定两个算法库必须串行执行。
- CAN1 实际 TX 意图、主控 USB ACK、RX 0x41 事件实时展示；安全 Seed/Key 数据脱敏。表格仅保留最近 5000 行，原始审计 ZIP 可导出。
- 右侧十阶段：Boot/APP 探测、编程会话、Seed/Key、擦除、请求下载、传输、退出传输、校验、复位、APP 复查，由现有真实烧录流程日志驱动。
- 删除独立 0x22 DID 主界面；不再要求手工输入 SHA256 字符串，但最后的实体 ECU 擦写确认与内部冻结任务门禁保留。
- WPF 初始化期 SelectionChanged 会在控件创建过程中触发，按钮状态更新已增加未初始化控件检查。
- 新增 XML 双安全文件路径保存/重载回归测试；未新增模拟 ECU。

## 本地 Codex 只执行验证，不得修改源码
1. 精确检出 PR #55 当前 HEAD。
2. Debug build FlashLab；运行 FlashLab.Tests、Devices.Tests；Debug build Desktop 并运行 Desktop.Tests。
3. 实际启动 FlashLab EXE，验证 XAML 初始化、设备选择、全部按钮与窗口关闭。
4. 离线导入用户提供的 Configuration.xml、SafetyFile.dll、sa_demo.dll；检查两个独立路径与实际算法选择。
5. 另存 XML 后重新导入，确认安全文件路径不互相覆盖；导入镜像、预检配置但不发送任何 ECU 指令。
6. 不修改源码、不启动 CI、不连接或烧录真实 ECU；发生异常原样回报所有文件、行号、测试输出。

## 现场下一步
Windows 编译及启动确认后，用户现场接入主控 USB 与 CAN1 ECU，先观察连接、CAN1 RX，再核对 Flash、算法、供电，最终由现场人员明确确认后执行烧录并保存失败阶段证据。

## 未完成事实
原 CanTool 源码完整项目本轮无法直接读取，因此本实现基于原始配置、算法文件和既有 CanTool 研究记录，不是原工程逐文件复制。0x27 算法是否适配真实 ECU、0x31 OEM 擦除与校验、0x34/36/37 实体写入、最终版本身份验证仍需实测，不能宣称成功。
