# AutoTest UI V3｜手动台架 CAN、按键动作与 FLASH 工作台

本次交付：`UI/prototypes/AutoTest_HydEPB_product_ui_v3_20261121.html`（离线高保真 HTML 验收版）。

## 手动台架
- CAN 报文不再以横向小卡片隐藏 DATA。列表中直接显示**每条报文完整 DLC 和 8/16/32/64 字节 HEX**，保留完整逐字节编辑。
- “单次”仅对应 **发送一次**；“周期”对应 **启动周期发送 / 停止周期发送** 并显示可编辑周期 ms。无硬件连接时不得标记发送成功，不启动虚构周期计时器。
- 按键动作以可直接触发的按钮呈现，点击不再打开 Key 编辑弹窗；Key 组合 + 保持时间的编辑由独立按钮进入。当前脱机原型会反馈“USB 未连接、未执行”，不能模拟按键响应。
- 演示按键动作沿用绿通 HydEPB：驻车 K12+K13、释放 K7+K8、无操作 K7+K10（均示例 1000ms）。

## FLASH 真实源码对照
查阅 `feature/flash-lab-20261114`：
- `src/AutoTest.FlashLab/Shell/FlashLabWindow.xaml`：左配置/中央进度阶段/右 CAN 收发日志三栏，明确分开真实 ECU UDS 响应与 USB ACK。
- `src/AutoTest.FlashLab/Presentation/FlashLabViewModel.cs`：正式十阶段：Boot/APP 探测、编程会话、Seed/Key、擦除、0x34 请求下载、0x36 传输、0x37 退出、ECU 校验、ECU 复位、APP 后验。
- 本轮 HTML 将这套流程用于界面设计（进度条从 0% 待命开始；不模拟任何阶段成功）；右侧 CAN 表含时间/方向/ID/DLC/完整 DATA/结果；可导入已有 JSONL/CSV 等日志。

## 静态与浏览器验证
- Node.js 三段脚本语法检查通过。
- Chromium 以 set_content 离线加载；主窗口 5 个测试用例加载成功，手动台架点击动作反馈未执行。
- VCU_1 和 VCU_2 真实配置报文完整可见；新建 64 字节报文、第 64 字节写入 FF 后列表同步显示；单次与周期命令标签互斥。
- Flash 显示 10 阶段及 0% 进度；导入一条测试格式的 JSONL CAN 帧后表格成功显示；未出现 JS pageerror。
- 测试用的合成 JSONL 仅作为解析器输入，不保存在原型内，不作为现场证据。

## 门禁
本版本仍是 Gate-Design 离线可交互产品设计候选，没有 USB/KEY/CAN/UDS/FLASH 实体后端接线。只有正式 WPF 与设备后端集成后，才可在连接设备时执行一次按键、周期发送或烧录；运行结果须来自真实 ACK/证据。
