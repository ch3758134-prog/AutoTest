/**
 * @file    EAL_GPIO.c
 * @brief   TestCtr V1.5 板级 GPIO 上电初始化。
 *
 * @details
 * 这里只进行固定 Port/Pin 的配置；引脚与电路依据集中在 EAL_GPIO.h。
 * 运行时读写直接调用 EAL_GPIO_WRITE/READ，不需要枚举或映射数组。
 *
 * 上电说明：
 *  - Air_ALVE1/2 初始化为 LOW，避免固件初始化时主动请求气阀通电。
 *  - RL_DRV、KEY_PWR_DRV、ECU_PWR_DRV、IGN 保持旧源码的 HIGH 初值，
 *    本次是结构与 Air 改脚整理，不擅自改写这四路原有上电行为。
 *  - HIGH 不是已认证的安全状态；实际负载/气路安全必须另行核实。
 *  - MCU 复位至 GPIO_Init 之前的 Pad 状态由芯片复位及外围电路决定，
 *    不能用本函数的值来保证该时间段的输出状态。
 */
#include "EAL_GPIO.h"

/**
 * @brief  配置 6 路已定义的 MCU 数字输出。
 * @note   先将 Air 两路设为 LOW，再配置旧固件的四路控制输出。
 *         MCAL_GPIO_Init 内负责引脚时钟、GPIO MUX、方向和初始电平。
 */
void EAL_GPIO_Init(void)
{
    /* U23 的两路 Air 高边输入：启动时均不请求导通。 */
    MCAL_GPIO_Init(AIR_ALVE1_PORT, AIR_ALVE1_PIN, MCAL_GPIO_OUT, 0U);
    MCAL_GPIO_Init(AIR_ALVE2_PORT, AIR_ALVE2_PIN, MCAL_GPIO_OUT, 0U);

    /* 以下四路沿用历史源码的高电平初态，不视为安全性认证。 */
    MCAL_GPIO_Init(RL_DRV_PORT,      RL_DRV_PIN,      MCAL_GPIO_OUT, 1U);
    MCAL_GPIO_Init(KEY_PWR_DRV_PORT, KEY_PWR_DRV_PIN, MCAL_GPIO_OUT, 1U);
    MCAL_GPIO_Init(ECU_PWR_DRV_PORT, ECU_PWR_DRV_PIN, MCAL_GPIO_OUT, 1U);
    MCAL_GPIO_Init(IGN_PORT,         IGN_PIN,         MCAL_GPIO_OUT, 1U);
}
