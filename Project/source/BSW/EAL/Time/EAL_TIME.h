/**
 * @file    EAL_TIME.h
 * @brief   板级时间服务 — 接口
 *
 * 封装内容：
 *   - 系统时间基（DWT 循环计数器），供上层（ASW/CDD）计时用
 *   - ASW 层不直接触碰 MCAL_DWT，统一经本层获取时间
 *
 * 调用链路：CDD/ASW → EAL_TIME → MCAL_DWT → DWT 寄存器
 */
#ifndef EAL_TIME_H
#define EAL_TIME_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* 核心时钟（板级常量，与 Clock_Config 一致：8MHz 晶振 → 96MHz Core）*/
#define EAL_TIME_CORE_HZ         (96000000UL)
/* 毫秒 → DWT ticks（广播周期换算用）*/
#define EAL_TIME_MS_TO_TICKS(ms) ((uint32_t)((ms) * (EAL_TIME_CORE_HZ / 1000UL)))

/**
 * @brief  初始化时间基（使能 DWT 循环计数器，首次调用 GetTicks 前必须执行）
 */
void EAL_TIME_Init(void);

/**
 * @brief  读取当前 tick 计数（内核时钟周期，可在两次调用间做差）
 * @return 当前 tick 值
 */
uint32_t EAL_TIME_GetTicks(void);

#ifdef __cplusplus
}
#endif

#endif /* EAL_TIME_H */
