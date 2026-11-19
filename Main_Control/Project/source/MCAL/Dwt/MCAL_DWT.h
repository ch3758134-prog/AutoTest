/**
 * @file    MCAL_DWT.h
 * @brief   MCAL DWT 循环计数器 — 延时/计时接口
 *
 * 调用链路：CDD/ASW → MCAL_DWT → CoreDebug/DWT 寄存器（Cortex-M4）
 *
 * 基于 ARM CoreDebug / DWT 外设的微秒/毫秒延时。
 * 依赖 CoreDebug->DEMCR.TRCENA = 1 和 DWT->CTRL.CYCCNTENA = 1，
 * 必须在首次 Delay/GetTicks 之前调用 MCAL_DWT_Init。
 */
#ifndef MCAL_DWT_H
#define MCAL_DWT_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/**
 * @brief  初始化 DWT 循环计数器（使能 TRCENA + CYCCNTENA）
 * @note   调用延时/计时接口前必须执行一次
 */
void MCAL_DWT_Init(void);

/**
 * @brief  微秒延时（忙等）
 * @param  us  延时微秒数
 */
void MCAL_DWT_DelayUs(uint32_t us);

/**
 * @brief  毫秒延时（忙等，内部转 us）
 * @param  ms  延时毫秒数
 */
void MCAL_DWT_DelayMs(uint32_t ms);

/**
 * @brief  读取当前 DWT 循环计数（可用于计时测量）
 * @return 内核时钟周期计数，可在两次调用间做差
 */
uint32_t MCAL_DWT_GetTicks(void);

#ifdef __cplusplus
}
#endif

#endif /* MCAL_DWT_H */
