/**
 * @file    MCAL_PIT.c
 * @brief   MCAL PIT 定时器驱动 — 薄封装 fsl_pit
 *
 * 调用链路：EAL/SchM → MCAL_PIT → fsl_pit → PIT0 寄存器
 *
 * 时钟：PIT 使用总线时钟 48MHz（Clock_Config 配置）。
 * 周期 = periodUs × (busClk / 1e6) 个计数；48MHz 下 1ms = 48000。
 *
 * ISR 向量：PIT0_IRQHandler 在 startup 中为 WEAK，本文件强符号覆盖，
 * 内部转调 MCAL_PIT_ISR（清标志 + 节拍计数 + 回调）。
 */

#include "MCAL_PIT.h"
#include "fsl_pit.h"
#include "fsl_clock.h"

/* ============================================
 * PIT0 通道定义
 * ============================================ */
#define MCAL_PIT_CHANNEL   kPIT_Chnl_0
#define MCAL_PIT_BASE      PIT
#define MCAL_PIT_IRQ       PIT0_IRQn

/* 周期上限（32 位计数 @48MHz ≈ 89.5s），超出截断 */
#define MCAL_PIT_MAX_TICKS 0xFFFFFFFFUL

static volatile uint32_t s_pitTick = 0U;          /* 节拍计数 */
static MCAL_PIT_CallbackType s_pitCallback = NULL; /* 周期回调 */

/* ============================================
 * 初始化：门控时钟 → 模块初始化 → 周期 → 中断使能 → 启动
 * ============================================ */
void MCAL_PIT_Init(uint32_t periodUs, MCAL_PIT_CallbackType cb)
{
    uint32_t busClk     = CLOCK_GetBusClkFreq();
    uint32_t ticksPerUs = busClk / 1000000U;
    uint32_t ticks;

    /* 先判上限再乘，避免中间乘法溢出 32 位 */
    if (periodUs > (MCAL_PIT_MAX_TICKS / ticksPerUs))
        ticks = MCAL_PIT_MAX_TICKS;
    else
        ticks = periodUs * ticksPerUs;

    s_pitCallback = cb;
    s_pitTick     = 0U;

    pit_config_t config;
    PIT_GetDefaultConfig(&config);
    PIT_Init(MCAL_PIT_BASE, &config);

    PIT_SetTimerPeriod(MCAL_PIT_BASE, MCAL_PIT_CHANNEL, ticks);
    PIT_EnableInterrupts(MCAL_PIT_BASE, MCAL_PIT_CHANNEL, kPIT_TimerInterruptEnable);

    NVIC_EnableIRQ(MCAL_PIT_IRQ);
    PIT_StartTimer(MCAL_PIT_BASE, MCAL_PIT_CHANNEL);
}

/* ============================================
 * 运行时启动 / 停止
 * ============================================ */
void MCAL_PIT_Start(void)
{
    PIT_StartTimer(MCAL_PIT_BASE, MCAL_PIT_CHANNEL);
}

void MCAL_PIT_Stop(void)
{
    PIT_StopTimer(MCAL_PIT_BASE, MCAL_PIT_CHANNEL);
}

/* ============================================
 * 节拍计数
 * ============================================ */
uint32_t MCAL_PIT_GetTick(void)
{
    return s_pitTick;
}

/* ============================================
 * PIT0 ISR 主体
 * ============================================ */
void MCAL_PIT_ISR(void)
{
    PIT_ClearStatusFlags(MCAL_PIT_BASE, MCAL_PIT_CHANNEL, kPIT_TimerFlag);
    s_pitTick++;

    if (s_pitCallback != NULL)
        s_pitCallback();
}

/* ============================================
 * PIT0 中断向量（强符号覆盖 startup 的 WEAK 默认）
 * ============================================ */
void PIT0_IRQHandler(void)
{
    MCAL_PIT_ISR();
}
