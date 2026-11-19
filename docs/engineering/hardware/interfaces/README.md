# 硬件—软件接口索引

| 分类 | 设计依据 | 接口资料 |
|---|---|---|
| 主控板 MCU→外围→J1 | TestCtr V1.5 p1～p5 | [设计模型](../boards/testctr-v1.5/design.json)、[固定版本固件绑定](../boards/testctr-v1.5/firmware_bindings.csv) |
| 按键板 20 Key 与5路电阻网络 | EPB_ODD V1.1 p1～p4 | [KEY_SIM 目录](../boards/key-sim-v1.1/README.md) |

固件是版本化实现，不能改写设计网络名；实际负载用途与软件业务名可以不同于图纸 Net 名称。

CAN 通道注意：历史 Main_Control MCAL CH0 对应图纸 CAN1（U9隔离）而 CH1 对应图纸 CAN2（U10非隔离）；主控 5V 与 CAN1 总线侧供电关联。KEY_SIM 历史源码标准 CAN `0x730`、20bit 开关控制仍以按键板固定源码审查。

**本目录不提供无独立测量依据的负载电压、气阀位置或PCB线束接通结论。**
