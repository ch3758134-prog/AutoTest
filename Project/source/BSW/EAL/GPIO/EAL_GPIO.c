/**
 * @file EAL_GPIO.c
 * @brief TestCtr V1.5 当前在用 GPIO 上电初始化。
 *
 * @details
 * Air_ALVE1/2 默认 LOW(释放请求)；IGN 沿用旧固件 HIGH 上电态。
 * RL_DRV 初始化 HIGH，使能 PCB 内部 U2→SOL24IN 共享高边功率通路；
 * 这不是上位机 ECU/按键板供电命令。
 *
 * 历史供电控制引脚 PTC8/PTC9 不再初始化或驱动；其引脚复位态、
 * 外部负载电平及上电瞬态必须在现场核查，不能据此声明安全。
 */
#include "EAL_GPIO.h"

void EAL_GPIO_Init(void)
{
    /* 两路独立夹具在上电初始化时均请求释放。 */
    MCAL_GPIO_Init(AIR_ALVE1_PORT, AIR_ALVE1_PIN, MCAL_GPIO_OUT, 0U);
    MCAL_GPIO_Init(AIR_ALVE2_PORT, AIR_ALVE2_PIN, MCAL_GPIO_OUT, 0U);

    /* PCB 内部共用高边功率使能，上位机无此开关。 */
    MCAL_GPIO_Init(RL_DRV_PORT, RL_DRV_PIN, MCAL_GPIO_OUT, 1U);

    /* 历史上电初态：HIGH。并非已验证的系统安全状态。 */
    MCAL_GPIO_Init(IGN_PORT, IGN_PIN, MCAL_GPIO_OUT, 1U);
}
