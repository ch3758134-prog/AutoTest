/**
 * @file    EAL_CAN_RxTest.h
 * @brief   CAN RX 独立验证模块 — 头文件
 */

#ifndef EAL_CAN_RXTEST_H
#define EAL_CAN_RXTEST_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* 全局变量声明（外部可访问，供 Watch 观察）*/
extern volatile uint32_t g_rx_cnt[2];

void EAL_CAN_RxTest_Task(void);

#ifdef __cplusplus
}
#endif

#endif /* EAL_CAN_RXTEST_H */
