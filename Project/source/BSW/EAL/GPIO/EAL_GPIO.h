/**
 * @file    EAL_GPIO.h
 * @brief   TestCtr V1.5 主控板固定 GPIO 引脚定义与便捷读写。
 *
 * @details
 * EAL 是板级硬件适配层：只记录本板固定的 Port/Pin 连接。
 * MCAL_GPIO 负责寄存器、时钟、MUX、方向及电平；ASW 负责业务动作。
 *
 * 数据依据：TestCtr V1.5 原理图第 1、3、5 页。
 * 注意：图纸网络名称、J1 触点编号、固件业务名称属于不同命名层。
 *       本文件仅改变 GPIO 的逻辑电平，不保证外部功率输出或机械动作。
 *
 * 维护规则：修改引脚时必须从 U7 Pad 经过驱动器 IN/OUT 追到 J1，
 *           并检查同一 Net 的其它负载；不要只对照 MCU Pin 名称。
 */
#ifndef EAL_GPIO_H
#define EAL_GPIO_H

#include "MCAL/Gpio/MCAL_GPIO.h"

#ifdef __cplusplus
extern "C" {
#endif

/* --------------------------------------------------------------------------
 * 1. 固定的板级 GPIO 连接
 * -------------------------------------------------------------------------- */

/**
 * @brief  共用高边输出母线使能。
 * @details U7 P70/PTC0 → RL_DRV → Q4(2N7002) → U2(BTS50055) → SOL24IN。
 *          PTC0=HIGH 使 Q4 导通，并拉低 U2 的低有效 IN；
 *          U2 正常且电源存在时，向 U5/U22/U23 提供共用 Vbb。
 * @warning 不是某一只 Air 气阀的独立电源；不要在关闭单路气阀时
 *          无条件关断整个 SOL24IN。
 */
#define RL_DRV_PORT           MCAL_GPIO_C
#define RL_DRV_PIN            0U

/* --------------------------------------------------------------------------
 * 1a. HOUT1~HOUT7：从 MCU Pad 经 BTS724G 到 J1 的固定硬件映射
 * 这些名称指实际板外端子，不代表 MCU 原生引脚。
 * 所有 HOUT 共用 SOL24IN 功率母线；GPIO 状态不是输出电压反馈。
 * -------------------------------------------------------------------------- */
#define HOUT1_PORT MCAL_GPIO_C  /* PTC1  → SOL8IN  → U5 CH1  → J1 C10 */
#define HOUT1_PIN  1U
#define HOUT2_PORT MCAL_GPIO_B  /* PTB19 → SOL7IN  → U5 CH2  → J1 C11 */
#define HOUT2_PIN  19U
#define HOUT3_PORT MCAL_GPIO_B  /* PTB18 → SOL6IN  → U5 CH3  → J1 C25 */
#define HOUT3_PIN  18U
#define HOUT4_PORT MCAL_GPIO_C  /* PTC4  → SOL5IN  → U5 CH4  → J1 C26 */
#define HOUT4_PIN  4U
#define HOUT5_PORT MCAL_GPIO_C  /* PTC10 → SOL11IN → U22 CH3 → J1 C6 */
#define HOUT5_PIN  10U
#define HOUT6_PORT MCAL_GPIO_C  /* PTC9  → SOL10IN → U22 CH2 → J1 C7 */
#define HOUT6_PIN  9U
#define HOUT7_PORT MCAL_GPIO_C  /* PTC8  → SOL9IN  → U22 CH1 → J1 C8 */
#define HOUT7_PIN  8U

/* 业务分配：IGN→HOUT1；LED1~LED6→HOUT2~HOUT7。
 * 只保留现有 IGN 命令的实际 GPIO 路由；LED 不初始化、不控制。
 * HOUT5/PTC10 从此不再被 IGN 初始化或驱动。
 */
#define IGN_PORT   HOUT1_PORT
#define IGN_PIN    HOUT1_PIN
#define LED1_PORT  HOUT2_PORT
#define LED1_PIN   HOUT2_PIN
#define LED2_PORT  HOUT3_PORT
#define LED2_PIN   HOUT3_PIN
#define LED3_PORT  HOUT4_PORT
#define LED3_PIN   HOUT4_PIN
#define LED4_PORT  HOUT5_PORT
#define LED4_PIN   HOUT5_PIN
#define LED5_PORT  HOUT6_PORT
#define LED5_PIN   HOUT6_PIN
#define LED6_PORT  HOUT7_PORT
#define LED6_PIN   HOUT7_PIN

/* 外部 KEY1~KEY3 输入，经电阻分压/RC/钳位后接 MCU。
 * 暂只定义物理绑定，不配置为 GPIO 输入，不启用 ADC/阈值判断。
 * KEY1=J1 C17; KEY2=J1 C18; KEY3=J1 C16。
 */
#define KEY1_PORT MCAL_GPIO_C /* PTC7 / MCU_KEY1 */
#define KEY1_PIN  7U
#define KEY2_PORT MCAL_GPIO_C /* PTC6 / MCU_KEY2 */
#define KEY2_PIN  6U
#define KEY3_PORT MCAL_GPIO_C /* PTC5 / MCU_KEY3 */
#define KEY3_PIN  5U

/**
 * @brief  Air_ALVE1：气阀功率输出 1。
 * @details U7 P54/PTB1 → SOL4IN → U23 BTS724G IN4(Pin9)
 *          → OUT4(Pin13) → Air_ALVE1 → J1 B5。
 *          相同 SOL4IN 网络还驱动 Q6 / H_SPEED4 的 HALL 模拟支路。
 * @note   夹具1：J1高电平请求压紧，低电平请求释放；
 *          GPIO HIGH 不能证明气缸已经到位。
 */
#define AIR_ALVE1_PORT        MCAL_GPIO_B
#define AIR_ALVE1_PIN         1U

/**
 * @brief  Air_ALVE2：气阀功率输出 2。
 * @details U7 P72/PTC2 → SOL2IN → U23 BTS724G IN3(Pin7)
 *          → OUT3(Pin14) → Air_ALVE2 → J1 B4。
 *          相同 SOL2IN 网络还驱动 Q7 / H_SPEED3 的 HALL 模拟支路。
 * @note   夹具2：J1高电平请求压紧，低电平请求释放。
 *          两路 Air 独立，共用 SOL24IN 但无业务互锁。
 */
#define AIR_ALVE2_PORT        MCAL_GPIO_C
#define AIR_ALVE2_PIN         2U

/* --------------------------------------------------------------------------
 * 2. 统一操作宏（不再增加信号枚举或 Port/Pin 查表）
 *
 * 用法：EAL_GPIO_WRITE(IGN, 1U);  EAL_GPIO_READ(AIR_ALVE1);
 * 现役输出只调用 IGN、AIR_ALVE1/2 和 RL_DRV；LED/KEY 仅预留。
 * signal 只能传上述已定义的板级名称；level 表示 MCU GPIO 的
 * 0/1 电平，不是 12V/24V 负载状态。单个 level 参数只求值一次。
 * -------------------------------------------------------------------------- */
#define EAL_GPIO_WRITE(signal, level) \
    MCAL_GPIO_Write(signal##_PORT, signal##_PIN, (uint8_t)(level))
#define EAL_GPIO_READ(signal) \
    MCAL_GPIO_Read(signal##_PORT, signal##_PIN)

/**
 * @brief  初始化本文件列出的板级控制信号。
 * @details 在 Clock_Init() 之后、USB/CAN 业务开始前调用一次。
 * @note   仅初始化 Air1/2(LOW)、内部 RL_DRV(HIGH)、IGN(HIGH)；
 *          不初始化已取消的 PTC8/PTC9 供电控制引脚。
 * @warning 初始化态不是外部电压或机械到位反馈。
 */
void EAL_GPIO_Init(void);

#ifdef __cplusplus
}
#endif
#endif /* EAL_GPIO_H */
