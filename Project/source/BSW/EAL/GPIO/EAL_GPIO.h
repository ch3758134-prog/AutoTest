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

/**
 * @brief  历史 KEY_PWR_DRV 控制网络。
 * @details U7 P80/PTC8 → SOL9IN → U22 IN1/OUT1 → HOUT7 → J1 C8。
 * @note   KEY_PWR_DRV 是历史固件业务名；现场外接负载未独立验证。
 */
#define KEY_PWR_DRV_PORT      MCAL_GPIO_C
#define KEY_PWR_DRV_PIN       8U

/**
 * @brief  历史 ECU_PWR_DRV 控制网络。
 * @details U7 P81/PTC9 → SOL10IN → U22 IN2/OUT2 → HOUT6 → J1 C7。
 * @note   ECU_PWR_DRV 是历史固件业务名；现场外接负载未独立验证。
 */
#define ECU_PWR_DRV_PORT      MCAL_GPIO_C
#define ECU_PWR_DRV_PIN       9U

/**
 * @brief  IGN 输出控制。
 * @details U7 P82/PTC10 → SOL11IN → U22 IN3(Pin7) / OUT3(Pin14)
 *          → HOUT5 → J1 C6。用户确认 J1 C6 作为现场 IGN 接线。
 * @note   C6 是 J1 连接器触点，不能误认为 MCU 的 PTC6。
 *          HIGH 请求高边导通，LOW 请求关断；需要 SOL24IN 供电。
 */
#define IGN_PORT              MCAL_GPIO_C
#define IGN_PIN               10U

/**
 * @brief  Air_ALVE1：气阀功率输出 1。
 * @details U7 P54/PTB1 → SOL4IN → U23 BTS724G IN4(Pin9)
 *          → OUT4(Pin13) → Air_ALVE1 → J1 B5。
 *          相同 SOL4IN 网络还驱动 Q6 / H_SPEED4 的 HALL 模拟支路。
 * @note   BTS724G 的 IN 为高电平使能；HIGH 不表示气缸已动作。
 * @warning Air_ALVE1 对应何种机械动作尚未按现场气路确认。
 */
#define AIR_ALVE1_PORT        MCAL_GPIO_B
#define AIR_ALVE1_PIN         1U

/**
 * @brief  Air_ALVE2：气阀功率输出 2。
 * @details U7 P72/PTC2 → SOL2IN → U23 BTS724G IN3(Pin7)
 *          → OUT3(Pin14) → Air_ALVE2 → J1 B4。
 *          相同 SOL2IN 网络还驱动 Q7 / H_SPEED3 的 HALL 模拟支路。
 * @note   两路 Air 共用 SOL24IN；本层不定义压紧/释放方向。
 */
#define AIR_ALVE2_PORT        MCAL_GPIO_C
#define AIR_ALVE2_PIN         2U

/* --------------------------------------------------------------------------
 * 2. 统一操作宏（不再增加信号枚举或 Port/Pin 查表）
 *
 * 用法：EAL_GPIO_WRITE(IGN, 1U);  EAL_GPIO_READ(AIR_ALVE1);
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
 * @warning 此函数包含旧固件四路输出的初始高电平配置；调用前必须
 *          审查外接负载。它不负责机械动作、反馈诊断或故障恢复。
 */
void EAL_GPIO_Init(void);

#ifdef __cplusplus
}
#endif
#endif /* EAL_GPIO_H */
