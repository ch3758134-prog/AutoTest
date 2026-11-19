/**
 * @file    EAL_TIME.c
 * @brief   板级时间服务 — 实现
 *
 * 调用链路：EAL_TIME.c → MCAL_DWT.c → DWT 寄存器
 */

#include "EAL_TIME.h"
#include "MCAL/Dwt/MCAL_DWT.h"

void EAL_TIME_Init(void)
{
    MCAL_DWT_Init();
}

uint32_t EAL_TIME_GetTicks(void)
{
    return MCAL_DWT_GetTicks();
}
