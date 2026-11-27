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
 * KEY1~KEY3 是外部自复位按钮输入，仅双边沿中断记录并主循环消抖，
 * 无任何业务输出动作；输入高低电平与按压动作的对应关系须实测。
 * MCU 上电至 GPIO 初始化完成之前的电气状态仍需由实物上下拉与波形验证。
 * GPIO LOW/USB ACK 不构成板外电压、夹具机械位置或气路安全的证明。
 */
#include "EAL_GPIO.h"
#include "EAL_TIME.h"
#include "fsl_port.h"
#include <stddef.h>

#define KEY_PORTC_PINS ((1UL << KEY1_PIN) | (1UL << KEY2_PIN) | (1UL << KEY3_PIN))
#define KEY_DEBOUNCE_TICKS EAL_TIME_MS_TO_TICKS(EAL_GPIO_KEY_DEBOUNCE_MS)

/* ISR 仅记录中断发生时间和等待消抖的位图；主循环承担全部 USB 交互。
 * Cortex-M4 对对齐的32位读写是原子的；取出 ready 位时短暂屏蔽 PORTC IRQ。
 */
static volatile uint8_t s_keyPending = 0U;
static volatile uint32_t s_keyLastEdgeTick[3] = {0U, 0U, 0U};
static uint8_t s_keyStableLevels = 0U;
static uint16_t s_keySequence = 0U;

static uint8_t KeyRawLevels(void)
{
    return (uint8_t)((EAL_GPIO_READ(KEY1) << 0U) |
                     (EAL_GPIO_READ(KEY2) << 1U) |
                     (EAL_GPIO_READ(KEY3) << 2U));
}

void PORTC_IRQHandler(void)
{
    const uint32_t flags = PORT_GetPinsInterruptFlags(PORTC) & KEY_PORTC_PINS;
    const uint32_t now = EAL_TIME_GetTicks();

    if (flags == 0U)
        return;
    PORT_ClearPinsInterruptFlags(PORTC, flags);

    if ((flags & (1UL << KEY1_PIN)) != 0U)
    {
        s_keyLastEdgeTick[0] = now;
        s_keyPending |= 0x01U;
    }
    if ((flags & (1UL << KEY2_PIN)) != 0U)
    {
        s_keyLastEdgeTick[1] = now;
        s_keyPending |= 0x02U;
    }
    if ((flags & (1UL << KEY3_PIN)) != 0U)
    {
        s_keyLastEdgeTick[2] = now;
        s_keyPending |= 0x04U;
    }
}

uint8_t EAL_GPIO_KeyLevels(void)
{
    return s_keyStableLevels;
}

uint16_t EAL_GPIO_KeySequence(void)
{
    return s_keySequence;
}

uint8_t EAL_GPIO_KeyPopEvent(EAL_GPIO_KeyEventType *event)
{
    uint8_t i;
    uint8_t ready = 0U;
    uint8_t value;
    const uint32_t now = EAL_TIME_GetTicks();

    if (event == NULL || s_keyPending == 0U)
        return 0U;

    /* 每路独立消抖。边沿 ISR 还在继续记录下一次变化时，不能清掉新事件。 */
    NVIC_DisableIRQ(PORTC_IRQn);
    for (i = 0U; i < 3U; i++)
    {
        const uint8_t bit = (uint8_t)(1U << i);
        if ((s_keyPending & bit) != 0U &&
            (uint32_t)(now - s_keyLastEdgeTick[i]) >= KEY_DEBOUNCE_TICKS)
        {
            s_keyPending &= (uint8_t)~bit;
            ready |= bit;
        }
    }
    NVIC_EnableIRQ(PORTC_IRQn);

    for (i = 0U; i < 3U; i++)
    {
        const uint8_t bit = (uint8_t)(1U << i);
        if ((ready & bit) == 0U)
            continue;
        value = (uint8_t)((KeyRawLevels() >> i) & 1U);
        /* 取 ready 之后又发生新边沿，必须重新等待消抖，不能提前上报。 */
        if ((s_keyPending & bit) != 0U)
            continue;
        if (((s_keyStableLevels >> i) & 1U) == value)
            continue; /* 机械抖动后回到原状态，不产生额外按键事件。 */

        s_keyStableLevels = (uint8_t)((s_keyStableLevels & (uint8_t)~bit) |
                                      (value != 0U ? bit : 0U));
        s_keySequence++;
        event->key = (uint8_t)(i + 1U);
        event->level = value;
        event->levels = s_keyStableLevels;
        event->sequence = s_keySequence;

        /* 同一次中断窗口的其余 KEY 不可丢失，交还主循环下一次处理。 */
        if ((ready & (uint8_t)~((uint8_t)((1U << (i + 1U)) - 1U))) != 0U)
        {
            NVIC_DisableIRQ(PORTC_IRQn);
            s_keyPending |= (uint8_t)(ready & (uint8_t)~((uint8_t)((1U << (i + 1U)) - 1U)));
            NVIC_EnableIRQ(PORTC_IRQn);
        }
        return 1U;
    }
    return 0U;
}


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

    /* 三路外部 KEY 接入已有板级电阻分压与滤波电路，避免未知内部上下拉。
     * PORTC_IRQ 只对 PTC5/6/7 配置双边沿；保留其它 PORTC 引脚功能。
     */
    NVIC_DisableIRQ(PORTC_IRQn);
    MCAL_GPIO_Init(KEY1_PORT, KEY1_PIN, MCAL_GPIO_IN, 0U);
    MCAL_GPIO_Init(KEY2_PORT, KEY2_PIN, MCAL_GPIO_IN, 0U);
    MCAL_GPIO_Init(KEY3_PORT, KEY3_PIN, MCAL_GPIO_IN, 0U);
    s_keyStableLevels = KeyRawLevels();
    s_keySequence = 0U;
    s_keyPending = 0U;
    PORT_ClearPinsInterruptFlags(PORTC, KEY_PORTC_PINS);
    PORT_SetPinInterruptConfig(PORTC, KEY1_PIN, kPORT_InterruptEitherEdge);
    PORT_SetPinInterruptConfig(PORTC, KEY2_PIN, kPORT_InterruptEitherEdge);
    PORT_SetPinInterruptConfig(PORTC, KEY3_PIN, kPORT_InterruptEitherEdge);
    PORT_ClearPinsInterruptFlags(PORTC, KEY_PORTC_PINS);
    NVIC_ClearPendingIRQ(PORTC_IRQn);
    NVIC_SetPriority(PORTC_IRQn, 5U); /* 按键中断不抢占 USB 高优先级处理。 */
    NVIC_EnableIRQ(PORTC_IRQn);
}
