# HW-KEY｜按键板 20 路开关、五路模拟输入条件

**来源** [EPB_ODD V1.1 原图](assets/HW-KEY-EPB_ODD-V1.1.pdf)：p1 JP1/JP2 与 CAN；p2 U3 MCU；p3 U6-U10 及 20 路电阻网络；p4 5V 电源。

## 真实硬件结构
- MCU 图纸为 **MC9S08DZ32（U3）**。p3 **U6/U7/U8/U9/U10 五颗 DG411DY** 组成 5 组、共 20 个模拟开关，输出网络包括 `RELE_NC`、`RELE_NO`、`PARK_NC`、`PARK_NO`、`MODE_NO`。
- 这不是 20 路独立模拟电压；是 **20 个开关闭合组合，经电阻网络影响约五组模拟输出**。电平/阻值组合和 DUT 有效判定区间必须按具体 ECU 配置与现场测量，不由图纸名字固定成通用驻车/释放业务键。
- JP1 p1：Pin3 `RELE_NO`、Pin4 `RELE_NC`、Pin9 `PARK_NC`、Pin10 `PARK_NO`；JP2 p1：Pin1 `MODE_NO`、Pin2 BaudRate、3 BAT_EPB、4 BAT_KEY、5 CANH、6 CANL、9 GND、10 BATIN。**这是原理图连接器针位，不是现场线束验收结论。**
- p1 CAN TJA1042 类收发器 U2；p4 MP2451 电源 U1。需要核对现场终端、供电与 ESD 接线。

## 当前按键板固件（`Key_Sim@313ed34e`）与图纸对照
`WS/EPB_EOL/Sources/APP/KeySim.c`：标准 CAN ID **0x730**，8 字节 `01 mask_low mask_mid mask_high_nibble 00 00 00 00` 设置 20-bit 组合；`80 00...00` 全释放；其他命令和高 4 位非零拒绝。没有证明回执即 DUT 的五路电压测量。
`WS/EPB_EOL/Sources/BSW/IO/Driver_Gpio.h`：`KEYx_ON()` 将相应 GPIO **拉低**、`KEYx_OFF()` 拉高；`Drv_Gpio_Init()` 将全部 KEY 关闭。需核对电路开关极性、复位上电未初始化窗口和实装 PCB。
- **明确不同**：图纸 K16 的 `PTB3/物理 Pin9`，代码 KEY16 使用 `PTE2`；应核对装板 Rev 与实际固件。
- **需要别名复核**：K1 和 K3-K8 图纸 p2 使用 `ADP0..ADP6` 及物理脚号，代码使用 `PTA0..PTA6`。不允许把 `ADP` 直接当作 `PTA` 相同功能，也不允许未经 MCU datasheet/PCB 验证直接判定全部错误。
- 精确 20 行 MCU pad/物理 Pin/KEY GPIO 对照见 [CSV](data/key_mcu_20key_map.csv)。匹配的其余 pin 只说明**图纸标签与源码一致**，不是现场已实测闭合电压。

## 重要协议分叉
- `Main_Control/BSW/CDD/KeySim/CDD_KeySim.c` 的旧业务 helper 发 **ID 0x100** 和动作枚举/保持时间。
- `Key_Sim` 当前接收源码及 HOST `AutoTest.Devices/Station/KeypadCommandCodec.cs` 使用 **ID 0x730** 与 20-bit Mask。
- 主控 `ASW_BusProxy` 对 CAN0/CAN1 采用 `0x40/0x41` 透明代理，业务工具应走经过审查的实际 CAN0 Mask 路径；**历史 0x100 helper 的存在不等于正在运行**。

这项冲突登记为 `D-KEY-PROTO-01`，由 HW/FW AI 核对真实烧录镜像、接收日志和调用点，PM 审查 HOST/按键板契约；不自行删除旧源码或改 ECU 键义。
