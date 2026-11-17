# Air_ALVE1 / Air_ALVE2｜主控板电气控制链与固件接口

## 已由图纸和器件手册支持的事实

TestCtr V1.5 原理图 p3（U7）、p5（U23）、p1（J1）：

| 输出 | U7 | 内部控制网络 | U23/BTS724G 输入 | U23 功率输出 | J1 |
|---|---|---|---|---|---|
| Air_ALVE1 | P54 PTB1 | SOL4IN | IN4 pin9 | OUT4 pin13 | B5 |
| Air_ALVE2 | P72 PTC2 | SOL2IN | IN3 pin7 | OUT3 pin14 | B4 |

U23 Vbb = SOL24IN；SOL24IN 的主供电链由 P70/PTC0 RL_DRV → Q4 2N7002 → U2 BTS50055 控制。BTS724G 输入高电平激活对应高边功率输出。U23 ST3/4 引脚8（图纸网名 ST2）为通道3/4共用故障输出，不能证明 Air1 与 Air2 的独立阀位、压力或动作结果。

器件依据：[Infineon 官方 BTS724G datasheet 第3页](https://www.infineon.com/assets/row/public/documents/10/49/infineon-bts724g-datasheet-en.pdf)；[原始主控原理图](../../../project-management/new-baseline-20261112/HW-FW-BASELINE-20261115/assets/HW-MAIN-TestCtr-V1.5.pdf)。

[机器可读的两路引脚/驱动/连接器链](air_valve_trace.csv)。

## 控制器代码约束

- 设计态：PTB1/ PTC2 为 U23 对应通道的数字输入，不可把旧 SOL GPIO PTB18 直接保留。
- 不能仅配置两个 GPIO 即宣称具备动作；应审查 RL_DRV/SOL24IN 供电、输出负载、短路/过温、动作时序、命令超时/USB断连、上电安全状态。
- 原主控源码 `Main_Control@c830dbcd` 把 `SOL` 绑定 PTB18（设计 SOL6IN），不属于 Air 两路。原 `EAL_GPIO.c` 五路 initLevel 全为1。
- 待审固件候选：[Main_Control Air 修改 Draft PR #57](https://github.com/ch3758134-prog/AutoTest/pull/57)，用显式 Air1/2 电气输出接口并默认禁用真实通电。其 CMD 0x18/0x19 与旧 CMD 0x16/0x17 的兼容性尚未批准。
- Air1/Air2 分别对应哪个机械动作（压紧/释放/保持）必须基于现场线束与气路实物核查，**UNKNOWN**。没有现场动作证据时不允许从电路网名推断。

## 证据分级

`SCHEMATIC_ONLY`: 源图设计关系；`DATASHEET_VERIFIED`: 器件电气功能；`FIELD_UNKNOWN`: 没有PCB实装/线束/在机固件与负载验证。本文件没有任何现场动作许可。
