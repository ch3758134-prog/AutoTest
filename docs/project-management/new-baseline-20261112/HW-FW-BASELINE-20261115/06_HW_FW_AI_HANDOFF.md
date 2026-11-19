# HW/FW 专职 AI 交接｜单一固件负责人，PM 审查

**角色：** AutoTest `HW-FW-MAIN-01` 主控板硬件/嵌入式系统专职研究与实施负责人。你不是 PM，也不是 G4 WPF、Flash Lab 或测试运行引擎的所有者。项目负责人明确要求由一个独立 AI 研究主控板，PM 管理跨硬件/固件/HOST 的构型及接口审批。

## 必须先读的真实原理图可视化审查入口

[HW-PIN-REVIEW.html](HW-PIN-REVIEW.html) 已重建为原始图纸 Cross-Probe；请配套 `assets/rendered/` 九张图像打开。GitHub 源码页面不运行 HTML；克隆仓库或下载对应目录后在浏览器查看。用户还已取得单文件自包含的完整离线版本。

这套可视化工具不是 CSV 换皮或模拟 PCB 图。以 PDF 的真实图形和精确文字坐标定位 MCU Pin/Net、同名跨页网络、器件标号与模块；可以写审查意见 JSON。对于 `SOL6IN` 的跨页图纸对应、`K16_CTL` 的两页分布，必须用原图验证；任何物理板上铜线/器件实际引脚、线束和在机 HEX 信息依然 `UNKNOWN`，不得推断为已认证。

继续补齐真实 PCB 版图/网表、主控 J1 完整引脚及实物对应图，并将原图/PCB 的对象标识、定位坐标和证据级别配对；不能再把纯文字引脚表当作项目负责人可审查的最终交付。

## 第一轮必须执行的研究，不修改实体
先读此目录原始 [TestCtr V1.5](assets/HW-MAIN-TestCtr-V1.5.pdf)、[KEY V1.1](assets/HW-KEY-EPB_ODD-V1.1.pdf)，[主控代码](https://github.com/ch3758134-prog/AutoTest/tree/Main_Control/Project/source) `Main_Control@c830dbcd`，以及 `Key_Sim@313ed34e`。复核 CSV 每行来源、完整 **100-pin MCU 封装表**、p1 J1 全部 64 位、全部电气模块、连接器/端子及芯片引脚复用；不能只读 README 就声称完成 PDF 检查。

**研究交付：**
1. 构型登记：MCU 型号/封装及物理引脚/复用、原理图 Net→驱动元件→PCB Pad→Connector Pin→Harness Pin→DUT/模块，附证据和 UNKNOWN；
2. 主控关键源链：`ASW_UsbSvc → ASW_PwrCtrl/BusProxy → EAL_GPIO/EAL_CAN → MCAL`，上电/复位/故障/断连及双 CAN 角色；
3. 旧主控码与 V1.5 图纸不一致清单：PTC8/9/10、PTB18/19、CAN0/CAN1 设计命名、0x100 旧 KeySim helper，以及供电/IGN 初值；
4. `CHG-HW-SOL-01` Before/After 定点调查及必要固件最小修补设计；未得到现场新引脚和 PCB/线束证据前**不改 GPIO**；
5. 现场工位的准确固件 HEX/CRC/板卡/线束/测试记录和 HOST 兼容矩阵，形成待 PM 审核的候选。

**权限：** 可以调查、编制图表和提出候选；未取得独立代码授权不得改 `Main_Control` / `Key_Sim` 正式源码，未经用户许可不得操作电磁阀、IGN、供电和 ECU，不修改主 HOST/FLASH UI/正式 TEST 模型；不直接合并 main。研究/固件候选放独立 HW/FW 受控工作流，供 PM 审查。绝不推倒 G4 或 F1。

**报审格式：** `信号ID／旧源及SHA／图纸页脚/器件Net／PCB与线束证据／拟改引脚／上电及故障态／USB/CAN语义是否变化／HOST影响／需要的现场测试／UNKNOWN`。PM 仅裁决已经明确的差异，不要求用户重新重复已确认的产品业务。
