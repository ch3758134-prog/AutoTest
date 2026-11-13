/**
 * @file    EAL_GPIO.h
 * @date    2026-07-16
 * @author  TestCtr
 * @brief   EAL 数字IO驱动 - 板级信号层 + 板级引脚宏（RL_DRV / KEY_PWR_DRV / ECU_PWR_DRV / IGN / SOL）
 *
 * 调用链路：CDD/ASW → EAL_GPIO → MCAL_GPIO → fsl_gpio / fsl_port
 *
 * 两种用法共存：
 *   - 信号级：EAL_GPIO_Init/Set/Get + EAL_GPIO_SIG_xxx（板级语义，映射在 EAL_GPIO.c 查表）
 *   - 引脚级：板级引脚宏 + EAL_GPIO_Config(port, pin)（直接按 MCU 引脚操作）
 *
 * 命名规范：
 *   - 信号枚举：板级语义（主继电器 / 供电 / 点火）
 *   - 业务宏：与旧版同名，语义不变
 */
#ifndef EAL_GPIO_H
#define EAL_GPIO_H

#include <stdint.h>
#include "MCAL/Gpio/MCAL_GPIO.h"

#ifdef __cplusplus
extern "C" {
#endif

/* ============================================
 * 板级引脚宏（直接按 MCU 引脚操作；KEY=PC8 / ECU=PC9 已实测确认）
 * ============================================ */
#define RL_DRV_PORT        MCAL_GPIO_C   /* 主继电器（总电源） */
#define RL_DRV_PIN         0
#define KEY_PWR_DRV_PORT   MCAL_GPIO_C   /* 按键板供电驱动 */
#define KEY_PWR_DRV_PIN    8
#define ECU_PWR_DRV_PORT   MCAL_GPIO_C   /* ECU 供电驱动 */
#define ECU_PWR_DRV_PIN    9
#define IGN_PORT           MCAL_GPIO_B   /* ECU 点火驱动 */
#define IGN_PIN            19
#define SOL_PORT           MCAL_GPIO_B   /* 电磁阀控制：HIGH=升起，LOW=下落/压紧 */
#define SOL_PIN            18

/* ============================================
 * 板级信号枚举（索引顺序与 EAL_GPIO.c 映射表一一对应）
 * ============================================ */
typedef enum {
    EAL_GPIO_SIG_RL_DRV,        /* 主继电器（总电源）  PTC0 */
    EAL_GPIO_SIG_KEY_PWR_DRV,   /* 按键板供电驱动      PC8  */
    EAL_GPIO_SIG_ECU_PWR_DRV,   /* ECU 供电驱动        PC9  */
    EAL_GPIO_SIG_IGN,           /* ECU 点火驱动        PB19 */
    EAL_GPIO_SIG_SOL,           /* 电磁阀控制          PB18 */
    EAL_GPIO_SIG_COUNT
} EAL_GPIO_SignalType;

/* ============================================
 * 接口
 * ============================================ */

/**
 * @brief  配置单个 GPIO 为推挽输出，初始低电平（引脚级，只调一次）
 * @param port  MCAL_GPIO_A~E（用板级引脚宏 RL_DRV_PORT 等）
 * @param pin   引脚号（用板级引脚宏 RL_DRV_PIN 等）
 */
void EAL_GPIO_Config(MCAL_GPIO_PortType port, uint8_t pin);

/**
 * @brief  初始化所有板载输出信号（推挽输出，按信号定义设置初始电平，只调一次）
 */
void EAL_GPIO_Init(void);

/**
 * @brief  设置板级信号电平
 * @param sig    EAL_GPIO_SIG_xxx
 * @param level  0=LOW, 1=HIGH
 */
void EAL_GPIO_Set(EAL_GPIO_SignalType sig, uint8_t level);

/**
 * @brief  读取板级信号电平
 * @return 0=LOW, 1=HIGH
 */
uint8_t EAL_GPIO_Get(EAL_GPIO_SignalType sig);

/* ============================================
 * 业务宏（保留原名，语义不变）
 * ============================================ */

/* 主继电器控制 */
#define RL_DRV_HIGH()         EAL_GPIO_Set(EAL_GPIO_SIG_RL_DRV, 1)
#define RL_DRV_LOW()          EAL_GPIO_Set(EAL_GPIO_SIG_RL_DRV, 0)

/* 12V 控制业务宏 */
#define KEY_PWR_DRV_HIGH()    EAL_GPIO_Set(EAL_GPIO_SIG_KEY_PWR_DRV, 1)
#define KEY_PWR_DRV_LOW()     EAL_GPIO_Set(EAL_GPIO_SIG_KEY_PWR_DRV, 0)

#define ECU_PWR_DRV_HIGH()    EAL_GPIO_Set(EAL_GPIO_SIG_ECU_PWR_DRV, 1)
#define ECU_PWR_DRV_LOW()     EAL_GPIO_Set(EAL_GPIO_SIG_ECU_PWR_DRV, 0)

#define IGN_HIGH()            EAL_GPIO_Set(EAL_GPIO_SIG_IGN, 1)
#define IGN_LOW()             EAL_GPIO_Set(EAL_GPIO_SIG_IGN, 0)

/* 电磁阀控制：MCU HIGH=升起，MCU LOW=下落/压紧 */
#define SOL_HIGH()            EAL_GPIO_Set(EAL_GPIO_SIG_SOL, 1)
#define SOL_LOW()             EAL_GPIO_Set(EAL_GPIO_SIG_SOL, 0)
#define SOL_UP()              SOL_HIGH()
#define SOL_DOWN()            SOL_LOW()

#ifdef __cplusplus
}
#endif

#endif /* EAL_GPIO_H */
