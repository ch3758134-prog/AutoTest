/**
 * @file EAL_GPIO.c
 * @brief TestCtr V1.5 高边控制 GPIO 上电默认全关闭。
 *
 * 初始化所有已识别的 BTS724G 控制输入为 LOW：
 * IGN(HOUT1)、LED1~LED6(HOUT2~7)、HOUT9/HOUT10、Air_ALVE1/2、
 * 以及 U22 CH4 未分配的 SOL12IN 控制输入。
 * RL_DRV(PTC0) 必须先初始化 LOW，避免初始化期间给高边驱动供电；
 * 后续只由 ASW_IoCtrl 根据实际输出请求管理公共 SOL24IN 供电。
 *
 * KEY1~KEY3 是未启用的外部输入宏，绝不作为输出初始化。
 * MCU 上电至 GPIO 初始化完成之前的电气状态仍需由实物上下拉与波形验证。
 * GPIO LOW/USB ACK 不构成板外电压、夹具机械位置或气路安全的证明。
 */
#include "EAL_GPIO.h"

void EAL_GPIO_Init(void)
{
    /* 公共高边门控先关闭，所有独立通道配置期间保持失能。 */
    MCAL_GPIO_Init(RL_DRV_PORT, RL_DRV_PIN, MCAL_GPIO_OUT, 0U);

    /* 两只夹具关闭驱动请求；LOW 不是实际夹具位置反馈。 */
    MCAL_GPIO_Init(AIR_ALVE1_PORT, AIR_ALVE1_PIN, MCAL_GPIO_OUT, 0U);
    MCAL_GPIO_Init(AIR_ALVE2_PORT, AIR_ALVE2_PIN, MCAL_GPIO_OUT, 0U);

    /* IGN/HOUT1 和六路 LED/HOUT2~HOUT7 均默认关闭。 */
    MCAL_GPIO_Init(IGN_PORT, IGN_PIN, MCAL_GPIO_OUT, 0U);
    MCAL_GPIO_Init(LED1_PORT, LED1_PIN, MCAL_GPIO_OUT, 0U);
    MCAL_GPIO_Init(LED2_PORT, LED2_PIN, MCAL_GPIO_OUT, 0U);
    MCAL_GPIO_Init(LED3_PORT, LED3_PIN, MCAL_GPIO_OUT, 0U);
    MCAL_GPIO_Init(LED4_PORT, LED4_PIN, MCAL_GPIO_OUT, 0U);
    MCAL_GPIO_Init(LED5_PORT, LED5_PIN, MCAL_GPIO_OUT, 0U);
    MCAL_GPIO_Init(LED6_PORT, LED6_PIN, MCAL_GPIO_OUT, 0U);

    /* 尚未分配 USB 业务的高边输入也必须明确关闭。 */
    MCAL_GPIO_Init(HOUT9_PORT, HOUT9_PIN, MCAL_GPIO_OUT, 0U);
    MCAL_GPIO_Init(HOUT10_PORT, HOUT10_PIN, MCAL_GPIO_OUT, 0U);
    MCAL_GPIO_Init(U22_CH4_SPARE_PORT, U22_CH4_SPARE_PIN, MCAL_GPIO_OUT, 0U);

    /* 不在启动结尾自动使能 RL_DRV；首次有效 ON 请求才打开公共母线。 */
}
