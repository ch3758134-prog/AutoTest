/**
 * @file    MCAL_GPIO.h
 * @brief   MCAL GPIO 驱动 — 薄封装 fsl_gpio / fsl_port
 *
 * 调用链路：EAL_GPIO → MCAL_GPIO → fsl_gpio/fsl_port → 芯片寄存器
 *
 * 端口抽象为枚举（MCAL_GPIO_A ~ E），上层不再直接面对 GPIO_Type* 基址，
 * 更换芯片平台时只需改本文件映射表。
 */
#ifndef MCAL_GPIO_H
#define MCAL_GPIO_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* ============================================
 * 端口
 * ============================================ */
typedef enum {
    MCAL_GPIO_A = 0,   /* PORTA / GPIOA */
    MCAL_GPIO_B,       /* PORTB / GPIOB */
    MCAL_GPIO_C,       /* PORTC / GPIOC */
    MCAL_GPIO_D,       /* PORTD / GPIOD */
    MCAL_GPIO_E,       /* PORTE / GPIOE */
    MCAL_GPIO_PORT_COUNT
} MCAL_GPIO_PortType;

/* ============================================
 * 方向
 * ============================================ */
typedef enum {
    MCAL_GPIO_IN  = 0,   /* 数字输入（无上下拉） */
    MCAL_GPIO_OUT = 1,   /* 推挽输出 */
} MCAL_GPIO_DirType;

/* ============================================
 * 接口
 * ============================================ */

/**
 * @brief  配置 GPIO（时钟使能 + 引脚复用 + 方向 + 初始电平，一步完成）
 * @param port       MCAL_GPIO_A ~ E
 * @param pin        引脚号 0~31
 * @param dir        输入 / 推挽输出
 * @param initLevel  输出模式下的初始电平（输入模式忽略）
 */
void MCAL_GPIO_Init(MCAL_GPIO_PortType port, uint8_t pin,
                    MCAL_GPIO_DirType dir, uint8_t initLevel);

/**
 * @brief  设置 GPIO 电平
 * @param port   MCAL_GPIO_A ~ E
 * @param pin    引脚号 0~31
 * @param level  0=LOW, 1=HIGH
 */
void MCAL_GPIO_Write(MCAL_GPIO_PortType port, uint8_t pin, uint8_t level);

/**
 * @brief  读 GPIO 电平
 * @return 0=LOW, 1=HIGH
 */
uint8_t MCAL_GPIO_Read(MCAL_GPIO_PortType port, uint8_t pin);

/**
 * @brief  翻转 GPIO 输出电平
 */
void MCAL_GPIO_Toggle(MCAL_GPIO_PortType port, uint8_t pin);

#ifdef __cplusplus
}
#endif

#endif /* MCAL_GPIO_H */
