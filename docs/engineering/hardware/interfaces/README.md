# 硬件接口、固件绑定及差异

此目录记录设计网络与历史源码版本的不同事实；不为某个软件分支追认实装状态。

## 双 CAN 角色层

| 名称层 | 图纸 CAN1 | 图纸 CAN2 |
|---|---|---|
| MCU物理 Pin | U7 P42 PTA12 TX / P43 PTA13 RX | U7 P91 PTC17 TX / P90 PTC16 RX |
| 收发器 | U9 ADM3054 隔离 | U10 TJA1042T/3 非隔离 |
| J1 设计触点 | B11 CAN1_H / B1 CAN1_L | A12 CAN2_H / A1 CAN2_L |
| 历史主控固件 | MCAL_CAN_CH0（称 CAN0） | MCAL_CAN_CH1（称 CAN1） |
| 对应 HOST 物理角色 | 按实际配置与线路核实；不从名字推导 | 按实际配置与线路核实；不从名字推导 |

## USB/GPIO 源码路径

静态代码链：`ASW_UsbSvc → ASW_PwrCtrl/BusProxy → EAL_GPIO / MCAL_CAN`。旧源码 `ASW_PwrCtrl.c` 的 `SET_SOL`（0x16）使用逻辑动作 0/1 并写 SOL GPIO；`GET_SOL`（0x17）读 GPIO 逻辑电平，**不构成电磁阀实际机械位置反馈**。固件源码固定 `Main_Control@c830dbcd...`，不代表实际固件。

KEY_SIM 固定源码 `Key_Sim@313ed34e...`：CAN `0x730` 8-byte 标准帧，`0x01 + 20bit mask` 控制、`0x80` 清零；DIGITAL ON=LOW、OFF=HIGH。没有证明硬件采集五路输出电压。

## 待审查

[设计/历史固件差异清单](design_source_issues.csv)。所有冲突都保留原证据，不因源码或图纸单独一方改写另一方。未经 PM 批准不调整 GPIO、外设、协议、线束或 HOST。
