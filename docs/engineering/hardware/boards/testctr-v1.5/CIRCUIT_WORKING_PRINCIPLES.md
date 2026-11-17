# TestCtr V1.5｜电路工作原理与固件接口约束

本文件依据五页原理图（p1～p5）及器件数据手册，先记录物理电路如何工作，再推导固件要求；所指固件静态版本：`Main_Control@c830dbcd56b436688437b446d222473a88b85098`。

原始设计证据：[TestCtr V1.5 原理图](https://github.com/ch3758134-prog/AutoTest/blob/main/docs/project-management/new-baseline-20261112/HW-FW-BASELINE-20261115/assets/HW-MAIN-TestCtr-V1.5.pdf)，SHA256 `44633c2445da2409dfe683822f83c2003eeaa2cbbfa9b249fc5bf26495f55353`。

## 01｜电源先决定哪些功能可能工作

`BAT+→ESR1/D28/D59→SOL24+` 供应 U4 LMR14203（形成 VCC5V）和 U8 正负12V模块；U11 从 VCC5V 再生成 MCU VCC3P3。U4 的 SHDN 受到 `PCUSB5V→R14→U16 TLP291→VCC5V_EN` 影响，不能认为电池 BAT+ 存在就等于 MCU 供电稳定。

另一路是 **受控功率母线**：`PTC0/RL_DRV→R94→Q4 2N7002→R90→U2 BTS50055 IN→SOL24IN`。BTS50055 IN 是**低有效**；RL_DRV 高令 Q4 下拉 IN，驱动 U2 提供母线电源。U2 Pin5/IS 经 R74、R78、C43 进入 DIAG_SOL 采样网络。它与每个独立的高边通道的功率输出是不同对象。

固件要求：对“母线是否使能”“某路通道请求导通”“实测是否有电”“外部负载是否动作”分开表达；关闭单路 Air 不自动关断所有高边共用的 SOL24IN。

## 02｜BTS724G 是功率驱动器，不是 MCU 的普通 GPIO 端子

U5、U22、U23 总计 11 条有名称的高边路径见 [通道映射](main_high_side_11_channels.csv)。每颗的 IN1～IN4 来自 MCU 逻辑 SOLxIN，OUT1～OUT4 通过内部受保护 MOS 从 `SOL24IN` 向外供电；**IN=HIGH 使能，但前提是 Vbb 已供电且没有保护关断**。

U22 例：U7 P82/PTC10 → SOL11IN → U22 IN3/Pin7 → OUT3/Pin14 → HOUT5 → J1 C6；J1 C6 的 **IGN** 用途来自用户报告。原代码 EAL_GPIO.c 把 IGN 映射 PTC10 与电气链一致，而 EAL_GPIO.h 的 PTB19 是源码内部不一致；原理图 Net SOL11IN 与业务名 IGN 不同并不代表硬件冲突。

状态引脚：BTS724G ST1/2 与 ST3/4 是每两路共用开漏诊断，不是各输出独立电压反馈；源图 ST1～ST6 只在 p5 出现并上拉，尚未找到其到 U7 的完整采集通路，不能宣称软件已有可读故障反馈。芯片过流/过温保护不证明负载机械动作。

**控制状态表**

| SOL24IN | 该路 IN | 预期电气行为 |
|---|---|---|
| 无电 | LOW/HIGH | 没有给负载供电的高边母线 |
| 有电 | LOW | 该高边关断请求，输出由负载/外围决定 |
| 有电 | HIGH，健康 | 该高边通过内部 MOS 向 OUT 供电 |
| 有电 | HIGH，发生故障 | 保护可能切断输出，GPIO HIGH 不能证明成功 |

## 03｜必须记录：Air 控制线还驱动 HALL 晶体管

原理图 p5 中，四个控制网不是 U23 独占：
- `SOL1IN`（`PTB0`）：`U23 IN1→OUT1→HOUT10→J1 B3`，同时连接 `Q1→H_SPEED1→J1 B16`。
- `SOL3IN`（`PTC3`）：`U23 IN2→OUT2→HOUT9→J1 B2`，同时连接 `Q2→H_SPEED2→J1 C19`。
- `SOL2IN`（`PTC2`）：`U23 IN3→OUT3→Air_ALVE2→J1 B4`，同时连接 `Q7→H_SPEED3→J1 C22`。
- `SOL4IN`（`PTB1`）：`U23 IN4→OUT4→Air_ALVE1→J1 B5`，同时连接 `Q6→H_SPEED4→J1 C23`。

由此可以直接推导：`PTB1` 置 HIGH 请求 Air_ALVE1 时，`SOL4IN` 还会作用于 Q6/H_SPEED4；`PTC2` 置 HIGH 请求 Air_ALVE2 时，还会作用于 Q7/H_SPEED3。两组电路**控制信号相同**；实际 Hall 负载是否安装和受影响程度需看外接线束和 HALL_5V 状态。不能在软件里把 Air1/2 和 HALL3/4 声称为独立可同时使用的资源；PWM 同理。

## 04｜外部 SOL1..13 是输入，MCU_SOLx 是反相隔离后的读入

p1：J1 的外部 SOL1～SOL13 进入 U14/U15/U17/U20 TLP293-4 光耦 LED 侧，器件晶体管侧通过 **2kΩ 上拉到 VDD** 形成 MCU_SOL1～MCU_SOL13，p3 将其接到 U7 输入。

外部有效电压使 LED 亮→晶体管导通→MCU 侧被拉 LOW；无足够 LED 电流→上拉 HIGH。所以软件使用的逻辑极性不能直接按外部电压直觉决定。

`SOLx`/光耦输入、`MCU_SOLx`/MCU 输入、`SOLxIN`/高边输出控制是三个不同概念。它们不能因为都含 SOL 字符串就合并。

## 05｜两路 CAN 的收发器条件不同

- 图纸 CAN1：PTA12/13 → U9 ADM3054 → L4/D46 → CAN1_H/L → J1 B11/B1。U9 VDD1=VCC3P3/GND、VDD2=PCUSB5V/PCGND。**隔离侧的电源来自 PCUSB5V**，不意味着由逻辑侧自动产生，故 USB 电源条件会影响 CAN1 物理层。
- 图纸 CAN2：PTC17/16 → U10 TJA1042T/3 → L3/D66 → CAN2_H/L → J1 A12/A1。U10 STB 在图纸上接 GND（正常模式），VCC=VCC5V、VIO=VCC3P3。软件没有基于独立 GPIO 的 U10 STB 模式切换能力。

原主控固件 MCAL 通道编号 CH0/CH1 对应图纸 CAN1/CAN2；这是命名层次，不是两个互相矛盾的物理通道。

## 06｜USB 是两侧电源域的隔离电路

P1 D+/D− → U18 ADuM4160 → U7 USB0_DP/DM，PCUSB5V/PCGND 与板侧 VCC5V/GND 分成两套电源域。U18 仅支持 USB 低/全速；固件 USB 枚举、帧缓存、丢帧/断连处理必须与设备供电重启分开判断。板上电源又依赖 USB VBUS 使能，不能把 USB 拔线仅当“应用掉线”。

## 07｜灯驱动是低边，不是高边

JU1/JU2 TLE6228 的四路输出是低边开关，导通时将负载输出拉向地；负载另一端需要外部正电源。芯片还存在 VS、STBY、ENA 和逐路 ST，不能只初始化单个 IN 就宣称输出必然工作。图纸红色修改文字只记录设计批注，不能自动当成现场焊接事实。

## 08｜DAC—运放—SPEED 的数学关系

U3 MAX5715 接收数字命令产生 DAC_1～DAC_4 电压，再经 U1 TLV4172 四路运放网络与 SP_REF、反馈阻容形成 SPEED1～4。以 U1A 为例：R54=10k、R58=5.1k、R47=10k、R44=51k；假设运放未饱和且输入电流可忽略，正相端电压约为 `V_DAC1×R58/(R54+R58)`，运放输出约为 `(1+R44/R47)×V_+−(R44/R47)×V_SP_REF`。最终对外端还经过 R48/C32，实际摆幅受运放正负供电、负载及 DAC 参考制约。

固件要建立 DAC 原始码值、参考电压、运放传递函数、限幅、校准与 SPEED 输出的映射，而不是把码值直接作为模拟输出电压。

## 09｜电流检测的两条比例完全不同

R124=20mΩ 对应 SOL 电流；R128=1.3mΩ 对应 Motor 电流。O11/O10 INA190A2 的数据手册标称增益 **50 V/V**。理想近似 SOL 每安培转换约 1.0V，Motor 每安培约 0.065V，随后通过 R87/C15 或 R89/C14 滤波到 ADC；实际量程须考虑 3.3V 供电、偏置、饱和、噪声和标定。

不能给两个采样通道使用同一安培换算系数，也不能把 ADC 饱和时的结果当成真实更大电流。

## 10｜外部 KEY 数字输入的分压保护

KEY1/2/3 分别经 100k 串联 + 150k 下拉、10nF 滤波及 ESD 钳位形成 MCU_KEY1/2/3（PTC7/PTC6/PTC5）。理想静态分压比例约 0.6；是否达到芯片输入阈值仍由外部电压、源阻抗和钳位条件决定。

J1 C6 是 HOUT5/IGN（用户报告）；PTC6 是 MCU_KEY2，连接器触点的 C6 与 Port C 的 Pin6 完全不同。

## 11｜软件实现检查规则

1. 对每个业务 API，先找到 U7 Pad → 电气 Net → 驱动 IC 输入和输出 → 输出 Net → J1，再登记现场用途。
2. 找到同一控制网的全部扇出；例如 Air1/Air2 **同时影响 HALL 支路**。
3. 对每条驱动明确供电门控、输出极性、复位态、保护/诊断及实际可读反馈。
4. 固件源码由固定 SHA 绑定版本，不能覆盖图纸真实 Net，也不能作为在机固件已刷写的证明。
5. 已知旧 EAL_GPIO.c 初始化五项均为 1U，RL_DRV、KEY_PWR、ECU_PWR、IGN、SOL 均被请求 HIGH；这不意味着外部负载已被测量为导通，但应作为上电风险处理。

## 依据与状态

- [BTS724G（Infineon）](https://www.infineon.com/assets/row/public/documents/10/49/infineon-bts724g-datasheet-en.pdf)
- [BTS50055（Infineon）](https://www.infineon.com/assets/row/public/documents/10/49/infineon-bts50055-1tma-datasheet-en.pdf)
- [TLE6228（Infineon）](https://www.infineon.com/assets/row/public/documents/10/49/infineon-tle6228gp-ds-en.pdf)
- [ADM3054（ADI）](https://www.analog.com/en/products/adm3054.html)
- [TJA1042（NXP）](https://www.nxp.com/docs/en/data-sheet/TJA1042.pdf)
- [ADuM4160（ADI）](https://www.analog.com/en/products/ADuM4160.html)
- [MAX5715（ADI）](https://www.analog.com/en/products/MAX5715.html)
- [TLV4172（TI）](https://www.ti.com/product/TLV4172)
- [INA190（TI）](https://www.ti.com/product/INA190)

**证据边界：** 设计态的器件连线可追溯；外部 IGN 用途来自用户报告；实际 PCB 铜箔、线束、负载和现役固件不在本材料内得到实测认证。KEY_SIM 的独立档案本次不改。此文件不更改固件或允许现场操作。
