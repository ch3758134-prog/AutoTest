/**
 * @file    EAL_GPIO.c
 * @date    2026-07-16
 * @author  TestCtr
 * @brief   EAL 数字IO驱动 - 板级信号层实现
 *
 * 调用链路：CDD/ASW → EAL_GPIO → MCAL_GPIO → fsl_gpio / fsl_port
 * 信号 → 引脚映射集中在本文件查表，上层只按信号名操作。
 *
 * 引脚分配（已实测确认，2026-07-13 / 2026-08 实测）：
 *   RL_DRV        PTC0  HIGH=总电开
 *   KEY_PWR_DRV   PC8   HIGH=12V开
 *   ECU_PWR_DRV   PC9   HIGH=12V开
 *   IGN           PB19  HIGH=12V开
 *   SOL           PB18  HIGH=升起，LOW=下落/压紧；上电默认 HIGH=升起
 */

#include "EAL_GPIO.h"
#include "MCAL/Gpio/MCAL_GPIO.h"

/* ============================================
 * 信号 → 引脚映射（索引与 EAL_GPIO_SignalType 一一对应）
 * ============================================ */
static const MCAL_GPIO_PortType s_sigPort[EAL_GPIO_SIG_COUNT] = {
    MCAL_GPIO_C,   /* EAL_GPIO_SIG_RL_DRV      */
    MCAL_GPIO_C,   /* EAL_GPIO_SIG_KEY_PWR_DRV */
    MCAL_GPIO_C,   /* EAL_GPIO_SIG_ECU_PWR_DRV */
    MCAL_GPIO_C,   /* EAL_GPIO_SIG_IGN         */
    MCAL_GPIO_B,   /* EAL_GPIO_SIG_SOL         */
};

static const uint8_t s_sigPin[EAL_GPIO_SIG_COUNT] = {
    0,     /* EAL_GPIO_SIG_RL_DRV      */
    8,     /* EAL_GPIO_SIG_KEY_PWR_DRV */
    9,     /* EAL_GPIO_SIG_ECU_PWR_DRV */
    10,    /* EAL_GPIO_SIG_IGN         */
    18,    /* EAL_GPIO_SIG_SOL         */
};

/* 上电初始电平：四路电源/点火保持 LOW；电磁阀 HIGH=升起 */
static const uint8_t s_sigInitLevel[EAL_GPIO_SIG_COUNT] = {
    1U,    /* EAL_GPIO_SIG_RL_DRV      */
    1U,    /* EAL_GPIO_SIG_KEY_PWR_DRV */
    1U,    /* EAL_GPIO_SIG_ECU_PWR_DRV */
    1U,    /* EAL_GPIO_SIG_IGN         */
    1U,    /* EAL_GPIO_SIG_SOL         */
};

/* ============================================
 * 配置单个 GPIO 为推挽输出，初始低电平（引脚级）
 * ============================================ */
void EAL_GPIO_Config(MCAL_GPIO_PortType port, uint8_t pin)
{
    MCAL_GPIO_Init(port, pin, MCAL_GPIO_OUT, 0U);
}

/* ============================================
 * 初始化：配置所有板载输出为推挽输出，使用各信号定义的安全初始电平
 * ============================================ */
void EAL_GPIO_Init(void)
{
    for (uint8_t i = 0; i < EAL_GPIO_SIG_COUNT; i++)
        MCAL_GPIO_Init(s_sigPort[i], s_sigPin[i], MCAL_GPIO_OUT, s_sigInitLevel[i]);
}

/* ============================================
 * 设置板级信号电平
 * ============================================ */
void EAL_GPIO_Set(EAL_GPIO_SignalType sig, uint8_t level)
{
    if ((uint32_t)sig >= (uint32_t)EAL_GPIO_SIG_COUNT) return;
    MCAL_GPIO_Write(s_sigPort[sig], s_sigPin[sig], level);
}

/* ============================================
 * 读取板级信号电平
 * ============================================ */
uint8_t EAL_GPIO_Get(EAL_GPIO_SignalType sig)
{
    if ((uint32_t)sig >= (uint32_t)EAL_GPIO_SIG_COUNT) return 0U;
    return MCAL_GPIO_Read(s_sigPort[sig], s_sigPin[sig]);
}
