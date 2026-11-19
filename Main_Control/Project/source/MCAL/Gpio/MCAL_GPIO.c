/**
 * @file    MCAL_GPIO.c
 * @brief   MCAL GPIO 驱动 — 薄封装 fsl_gpio / fsl_port
 *
 * 调用链路：EAL_GPIO → MCAL_GPIO → fsl_gpio/fsl_port → 芯片寄存器
 *
 * 端口映射：
 *   MCAL_GPIO_A~E ↔ PORTA~PORTE / GPIOA~GPIOE（MK20DN512VLL10）
 *
 * 设计原则：与 MCAL_CAN 一致，端口/引脚参数在入口做范围校验，
 * 非法参数直接静默返回，避免越界访问 HardFault。
 */

#include "MCAL_GPIO.h"
#include "fsl_clock.h"
#include "fsl_port.h"
#include "fsl_gpio.h"

/* ============================================
 * 端口映射（索引与 MCAL_GPIO_PortType 一一对应）
 * ============================================ */
static PORT_Type * const s_portBase[] = {
    PORTA, PORTB, PORTC, PORTD, PORTE,
};

static GPIO_Type * const s_gpioBase[] = {
    GPIOA, GPIOB, GPIOC, GPIOD, GPIOE,
};

static const clock_ip_name_t s_portClock[] = {
    kCLOCK_PortA, kCLOCK_PortB, kCLOCK_PortC, kCLOCK_PortD, kCLOCK_PortE,
};

/* ============================================
 * 初始化：时钟使能 → 引脚复用为 GPIO → 方向/初始电平
 * ============================================ */
void MCAL_GPIO_Init(MCAL_GPIO_PortType port, uint8_t pin,
                    MCAL_GPIO_DirType dir, uint8_t initLevel)
{
    if ((uint32_t)port >= (uint32_t)MCAL_GPIO_PORT_COUNT) return;
    if (pin > 31U) return;

    /* 1. 使能对应 PORT 时钟门控（必须最先执行，否则写 PCR 触发 HardFault） */
    CLOCK_EnableClock(s_portClock[port]);

    /* 2. 引脚复用为 GPIO 功能 */
    PORT_SetPinMux(s_portBase[port], pin, kPORT_MuxAsGpio);

    /* 3. 方向 + 初始电平 */
    gpio_pin_config_t cfg;
    cfg.pinDirection = (dir == MCAL_GPIO_OUT) ? kGPIO_DigitalOutput : kGPIO_DigitalInput;
    cfg.outputLogic  = (initLevel != 0U) ? 1U : 0U;
    GPIO_PinInit(s_gpioBase[port], pin, &cfg);
}

/* ============================================
 * 写电平（运行时调用，前置必须有 MCAL_GPIO_Init）
 * ============================================ */
void MCAL_GPIO_Write(MCAL_GPIO_PortType port, uint8_t pin, uint8_t level)
{
    if ((uint32_t)port >= (uint32_t)MCAL_GPIO_PORT_COUNT) return;
    if (pin > 31U) return;
    GPIO_WritePinOutput(s_gpioBase[port], pin, level);
}

/* ============================================
 * 读电平
 * ============================================ */
uint8_t MCAL_GPIO_Read(MCAL_GPIO_PortType port, uint8_t pin)
{
    if ((uint32_t)port >= (uint32_t)MCAL_GPIO_PORT_COUNT) return 0U;
    if (pin > 31U) return 0U;
    return GPIO_ReadPinInput(s_gpioBase[port], pin) ? 1U : 0U;
}

/* ============================================
 * 翻转输出电平
 * ============================================ */
void MCAL_GPIO_Toggle(MCAL_GPIO_PortType port, uint8_t pin)
{
    if ((uint32_t)port >= (uint32_t)MCAL_GPIO_PORT_COUNT) return;
    if (pin > 31U) return;
    GPIO_TogglePinsOutput(s_gpioBase[port], 1UL << pin);
}
