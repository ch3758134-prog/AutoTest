/**
 * @file EAL_GPIO.c
 * @brief TestCtr V1.5 外部高边输出默认关闭，内部 RL_DRV 固定初始化。
 *
 * 初始化所有已识别的 BTS724G 控制输入为 LOW：
 * IGN(HOUT1)、LED_RED1/YELLOW1/GREEN1/RED2/YELLOW2/GREEN2(HOUT2~7)、
 * HOUT9/HOUT10、Air_ALVE1/2、
 * 以及 U22 CH4 未分配的 SOL12IN 控制输入。
 * RL_DRV(PTC0) 属板级共用电源链，不是 USB 业务控制信号。
 * 所有外部负载控制 GPIO 均已设为 LOW 后，才在 EAL 中将 RL_DRV 初始化 HIGH。
 * ASW_IoCtrl 不读写 RL_DRV；MCU 复位至软件初始化前的瞬态仍待实测。
 *
 * KEY1~KEY3 是未启用的外部输入宏，绝不作为输出初始化。
 * MCU 上电至 GPIO 初始化完成之前的电气状态仍需由实物上下拉与波形验证。
 * GPIO LOW/USB ACK 不构成板外电压、夹具机械位置或气路安全的证明。
 */
#include "EAL_GPIO.h"

void EAL_GPIO_Init(void)
{
    /* 两只夹具关闭驱动请求；LOW 不是实际夹具位置反馈。 */
    MCAL_GPIO_Init(AIR_ALVE1_PORT, AIR_ALVE1_PIN, MCAL_GPIO_OUT, 0U);
    MCAL_GPIO_Init(AIR_ALVE2_PORT, AIR_ALVE2_PIN, MCAL_GPIO_OUT, 0U);

    /* IGN/HOUT1 和红黄绿各两路 LED/HOUT2~HOUT7 均默认关闭。 */
    MCAL_GPIO_Init(IGN_PORT, IGN_PIN, MCAL_GPIO_OUT, 0U);
    MCAL_GPIO_Init(LED_RED1_PORT, LED_RED1_PIN, MCAL_GPIO_OUT, 0U);
    MCAL_GPIO_Init(LED_YELLOW1_PORT, LED_YELLOW1_PIN, MCAL_GPIO_OUT, 0U);
    MCAL_GPIO_Init(LED_GREEN1_PORT, LED_GREEN1_PIN, MCAL_GPIO_OUT, 0U);
    MCAL_GPIO_Init(LED_RED2_PORT, LED_RED2_PIN, MCAL_GPIO_OUT, 0U);
    MCAL_GPIO_Init(LED_YELLOW2_PORT, LED_YELLOW2_PIN, MCAL_GPIO_OUT, 0U);
    MCAL_GPIO_Init(LED_GREEN2_PORT, LED_GREEN2_PIN, MCAL_GPIO_OUT, 0U);

    /* 尚未分配 USB 业务的高边输入也必须明确关闭。 */
    MCAL_GPIO_Init(HOUT9_PORT, HOUT9_PIN, MCAL_GPIO_OUT, 0U);
    MCAL_GPIO_Init(HOUT10_PORT, HOUT10_PIN, MCAL_GPIO_OUT, 0U);
    MCAL_GPIO_Init(U22_CH4_SPARE_PORT, U22_CH4_SPARE_PIN, MCAL_GPIO_OUT, 0U);

    /* 内部硬件供电链在所有受控负载 GPIO=LOW 后独立初始化。
     * 不是 USB 业务命令；不由 ASW_IoCtrl 按请求启停。
     */
    MCAL_GPIO_Init(RL_DRV_PORT, RL_DRV_PIN, MCAL_GPIO_OUT, 1U);
}
