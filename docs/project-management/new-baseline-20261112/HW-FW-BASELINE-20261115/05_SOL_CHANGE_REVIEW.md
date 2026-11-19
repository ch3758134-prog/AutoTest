# CHG-HW-SOL-01｜现场电磁阀引脚变更登记（候审）

**当前：`FIELD_CHANGE_REPORTED / NEW_PIN_UNKNOWN / NO_FW_PATCH_APPROVED / NO_PHYSICAL_ACTUATION`**。

用户已报告“真实台架组装后电磁阀控制引脚改了”；并明确新引脚不应直接影响 HOST 协议。**变更的具体前后物理脚、连接器、PCB 走线、线束及固件映像尚未提供**。不得将 `PTB18`（现有固件代码）或 `SOL6IN`（历史图纸）先验地定义为当前实装旧引脚。

## 必填调查（未知须保留 UNKNOWN）
1. station_id、主控板丝印/PCB Rev、元件/驱动芯片/连接器、实际固件 Git SHA 与 HEX CRC、当前 HOST 提交。
2. 改动层级：**MCU 端口/物理 pad、PCB 飞线/改版、驱动级/连接器、还是外部线束**；Before/After 分别列 port-pin、物理 pin、net、驱动元件、连接器两端 pin、负载及外部电源。
3. 两端真实连通和电气关系：上拉/下拉、驱动管、电流方向、钳位/续流/过流、安全上电、复位与 USB 断开状态。
4. 固件静态引用：`EAL_GPIO.c` 的 `s_sigPort/s_sigPin/s_sigInitLevel`、`EAL_GPIO.h` 宏和枚举、`ASW_PwrCtrl.c` 的 SET/GET/动作逻辑，以及其他所有直接 PIN 使用。
5. 现役 HEX/BootLoader/SWD 接口及固件切换/恢复程序；没有这些不作现场动作。

## 不变条件与审查门槛
- USB `SET_SOL=0x16`：当前源码动作值 0=升起/1=压紧；`GET_SOL=0x17` 为读取 GPIO 推算的动作值，非独立机械传感。新 GPIO 不应擅改这些语义；确有变化另提 HOST 契约 CR。
- MCU 引脚是否复用 CAN、SWD/JTAG、时钟或其他资源须按数据手册及 **新实装 PCB** 核对；测试模式不能抢占现役 CAN/USB。
- 应用层 ACK、GPIO 回读、实际电磁阀状态/夹具状态与 DUT 结果**四层独立记录**。未授权不发实体 SET_SOL。
- 单独 HW/FW AI 负责作出原理图/PCB/源代码差异、受控补丁候选和编译验证；PM 负责固件版本发布、HOST 兼容与风险审查；现场操作者负责断电通断和获许可后的动作观测。
- 本档案登记**不是**固件修改、台架上电、物理动作许可；如尚无板卡实物证据，只完成研究，不临时硬编码一个“猜测的安全状态”。

## 准出证据
`AS_DESIGNED → AS_BUILT → AS_INSTALLED → FW_SOURCE → FW_IMAGE → POWER_OFF_CONTINUITY → POWER_ON_STATIC → AUTHORIZED_ACTION → HOST_PROTOCOL_REGRESSION → CONFIG_RELEASE`。
所有结论附照片/表格、测量时间与操作者、精确固件 SHA/CRC、工位编号、异常处理及回退方案。任何阶段未通过则保持 BLOCKED/UNKNOWN，不得在 HOST 报“已经到位”。
