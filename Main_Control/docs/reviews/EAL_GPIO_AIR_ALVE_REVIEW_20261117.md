# Main_Control | GPIO 与 Air_ALVE 源码候选审查

## 文件与版本

- 依据用户最新上传的 EAL_GPIO.h / EAL_GPIO.c：原有端口已经改用统一宏，IGN 在头文件为 PTC10，但 C 文件仍有过时的 PB19 注释。
- 源码工程与其它应用模块的比较基线：Main_Control@c830dbcd56b436688437b446d222473a88b85098。
- 原理图：TestCtr V1.5 p1、p3、p5。BTS724G 的数据手册说明 IN1～4 高有效，OUT 为受保护的高边功率输出。
- 本 PR 为**候选源码**；未声称 Keil 编译、在机固件或台架运行已验证。

## 结构调整

原有 EAL_GPIO_SignalType、三张并行查找表、EAL_GPIO_Set/Get、EAL_GPIO_Config、SOL_UP/DOWN 业务宏全部删除。

EAL_GPIO.h 只提供板级 Port/Pin 宏与统一 EAL_GPIO_WRITE(signal,level)、EAL_GPIO_READ(signal) 两个宏；EAL_GPIO.c 只初始化 GPIO。ASW_PwrCtrl.c 按硬件信号名称调用宏，仍保留旧电源与 IGN 协议业务。

## 原理图 Air 连接

- Air_ALVE1：U7 P54/PTB1 → SOL4IN → U23 BTS724G IN4/Pin9 → OUT4/Pin13 → Air_ALVE1 → J1 B5。**相同 SOL4IN 同时控制 Q6/H_SPEED4。**
- Air_ALVE2：U7 P72/PTC2 → SOL2IN → U23 BTS724G IN3/Pin7 → OUT3/Pin14 → Air_ALVE2 → J1 B4。**相同 SOL2IN 同时控制 Q7/H_SPEED3。**
- U23 与其它 BTS724G 共用 SOL24IN，受 RL_DRV→Q4→U2 BTS50055 供电链影响。
- MCU 输入高电平请求 U23 对应通道导通；实际高边保护、供电和负载行为由硬件决定。
- 气动的压紧、释放、保持状态不能从这两条设计网名推断。

## USB 命令边界

| CMD | 请求 | 响应与含义 |
|---|---|---|
| 0x18 SET_AIR_ALVE | 1字节 0=双路关 / 1=仅Air1 / 2=仅Air2 | 成功仅说明执行了GPIO写操作；共用RL_DRV为LOW则非零请求ERR_HW；非法3返回ERR_FMT |
| 0x19 GET_AIR_ALVE | 0字节 | 1字节 bit0=Air1 MCU输入电平；bit1=Air2 MCU输入电平。不等于高边输出或机械动作 |
| 0x16 SET_SOL | 历史机械命令 | ERR_HW（旧0/1动作尚未与Air1/2作语义匹配） |
| 0x17 GET_SOL | 历史机械动作查询 | ERR_HW（避免把GPIO电平伪装为阀位） |

0x10～0x15、0x1F 电源与 IGN 指令码保持。ASW 在 ECU 总供电关闭命令时先撤销 Air，再关闭 RL_DRV，防止日后供电恢复时已有高电平立即带动气阀。USB 枚举失效时也清除 Air 请求，但**USB 枚举存在而 HOST 进程卡死并不能被检测**。

## GPIO 初始状态

AIR_ALVE1/2 = LOW，原旧 SOL=PTB18 不再由板级初始化/动作接口配置。
RL_DRV、KEY_PWR_DRV、ECU_PWR_DRV、IGN 的初始化仍为历史 HIGH，**这不是安全性证明**。上电执行和外接负载必须先进行安全审查；没有动过相应四路的开关极性。

## 车规注释参考

- Doxygen: @file、@brief、@details、@param、@return、@warning，用于声明预置条件、参数范围、输出副作用。
- AUTOSAR Classic BSW General: 模块资源、配置项、接口和集成条件应可追踪。本候选仅**借鉴文档原则**，不宣称满足完整 AUTOSAR/ISO 26262 合规要求。
- MISRA C:2025 / CERT C 的代码质量思想：使用有意义的符号、清晰标出接口约束；未运行 MISRA 静态分析，不能声称合规。

## 待正式合并/烧录前确认

1. Air1/2 各自接什么线圈、阀动作方向、允许同时通电与否、需要的切换死区/限时。
2. 气阀工作电压、电流、母线供电和线束、机械停位/安全状态。
3. 旧上位机 0x16/0x17 的调用方处理 ERR_HW 的方式；确认是否需要迁移到 0x18/0x19。
4. USB 断连、固件复位、主机进程失联与外部急停等工况的验证。
5. 当前工程的 Keil 编译和目标台架受控测试。

此 PR 不触碰 Host、KEY_SIM、FLASH 或物理设备。
