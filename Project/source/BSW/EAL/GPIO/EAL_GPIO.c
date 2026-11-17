/**
 * @file    EAL_GPIO.c
 * @date    2026-07-16
 * @author  TestCtr
 * @brief   EAL 数字IO驱动 - 板级信号层实现
 *
 * 调用链路：CDD/ASW → EAL_GPIO → MCAL_GPIO → fsl_gpio / fsl_port
 * 信号 → 引脚映射集中在本文件查表，上层只按信号名操作。
 *
 * 固件有效绑定来源：EAL_GPIO.h 引脚宏（旧源码版本；不是现场已验证）。
 * 原声明的 IGN PB19 与实际运行的 PTC10 冲突；此版消除重复配置，
 * 保持旧固件实际操作引脚与电平不变。板级设计网名冲突见审查文档。
 */

#include "EAL_GPIO.h"
#include "MCAL/Gpio/MCAL_GPIO.h"

/* ============================================
 * 信号 → 引脚映射（索引与 EAL_GPIO_SignalType 一一对应）
 * ============================================ */
static const MCAL_GPIO_PortType s_sigPort[EAL_GPIO_SIG_COUNT] = {
    RL_DRV_PORT,
    KEY_PWR_DRV_PORT,
    ECU_PWR_DRV_PORT,
    IGN_PORT,
    SOL_PORT,
};

static const uint8_t s_sigPin[EAL_GPIO_SIG_COUNT] = {
    RL_DRV_PIN,
    KEY_PWR_DRV_PIN,
    ECU_PWR_DRV_PIN,
    IGN_PIN,
    SOL_PIN,
};

/* 当前旧固件实际初始电平全部 HIGH；仅忠实记录，不代表电气安全。
 * 此次整理不改变任何初始电平，须在改线/上电前单独审查。 */
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
 * 初始化：配置旧固件五个输出，使用 s_sigInitLevel 实际初始电平
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
