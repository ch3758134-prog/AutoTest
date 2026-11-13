/**
 * @file    MCAL_DWT.c
 * @brief   MCAL DWT 循环计数器 — 薄封装 CoreDebug/DWT 寄存器
 *
 * 调用链路：CDD/ASW → MCAL_DWT → CoreDebug/DWT → 芯片寄存器
 *
 * DWT 为芯片级资源，无板级语义，故不设 EAL 层，上层直接调用本模块。
 */

#include "MCAL_DWT.h"
#include "fsl_common.h"   /* CoreDebug, DWT, SystemCoreClock */

/**
 * @brief  初始化 DWT 循环计数器
 */
void MCAL_DWT_Init(void)
{
    CoreDebug->DEMCR |= CoreDebug_DEMCR_TRCENA_Msk;
    DWT->CTRL        |= DWT_CTRL_CYCCNTENA_Msk;
}

/**
 * @brief  微秒延时
 * @param  us  延时微秒数
 * @note  ticks = us × (内核频率/1e6)，内部处理 32 位计数器回绕
 */
void MCAL_DWT_DelayUs(uint32_t us)
{
    uint32_t start, end, ticks;
    ticks = us * (SystemCoreClock / 1000000U);
    start = DWT->CYCCNT;
    end = start + ticks;

    if (end > start) {
        while (DWT->CYCCNT < end) { __nop(); }
    } else {
        /* 计数器溢出，跨区域等待 */
        while (DWT->CYCCNT > end) { __nop(); }
        while (DWT->CYCCNT < end) { __nop(); }
    }
}

/**
 * @brief  毫秒延时
 * @param  ms  延时毫秒数
 */
void MCAL_DWT_DelayMs(uint32_t ms)
{
    MCAL_DWT_DelayUs(ms * 1000U);
}

/**
 * @brief  读取当前 DWT 循环计数
 */
uint32_t MCAL_DWT_GetTicks(void)
{
    return DWT->CYCCNT;
}
