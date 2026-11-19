/**
 * @file    MCAL_PIT.h
 * @brief   MCAL PIT 定时器驱动 — 薄封装 fsl_pit
 *
 * 调用链路：EAL/SchM → MCAL_PIT → fsl_pit → PIT0 寄存器
 *
 * 用途：周期节拍（1ms 等），供 RTE/SchM 协作式调度或超时计数使用。
 * 使用 PIT 通道 0（PIT0_IRQn），内部维护 32 位节拍计数 s_pitTick。
 *
 * 周期上限：PIT 无分频器，直接对总线时钟（48MHz）计数，
 * 32 位 LDVAL 上限 ≈ 89.5s；超过会自动截断到上限。
 */
#ifndef MCAL_PIT_H
#define MCAL_PIT_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* ============================================
 * 周期回调
 * ============================================ */
typedef void (*MCAL_PIT_CallbackType)(void);

/* ============================================
 * 接口
 * ============================================ */

/**
 * @brief  初始化 PIT0 周期定时器（时钟 + 周期 + 中断 + 启动，一步完成）
 * @param periodUs  周期，单位微秒（上限约 89.5s @48MHz）
 * @param cb        周期回调（可 NULL，仅计数）
 */
void MCAL_PIT_Init(uint32_t periodUs, MCAL_PIT_CallbackType cb);

/**
 * @brief  运行时启动 PIT0 计数
 */
void MCAL_PIT_Start(void);

/**
 * @brief  运行时停止 PIT0 计数
 */
void MCAL_PIT_Stop(void);

/**
 * @brief  读取自启动以来的节拍数
 * @return 已触发的周期中断次数（32 位回绕）
 */
uint32_t MCAL_PIT_GetTick(void);

/**
 * @brief  PIT0 ISR 主体（清标志 + 节拍计数 + 回调）
 * @note   MCAL_PIT.c 已定义 PIT0_IRQHandler 调用本函数，
 *         如需换通道可从自定义 ISR 调用。
 */
void MCAL_PIT_ISR(void);

#ifdef __cplusplus
}
#endif

#endif /* MCAL_PIT_H */
