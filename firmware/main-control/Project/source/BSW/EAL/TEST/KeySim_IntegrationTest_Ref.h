/**
 * @file    KeySim_IntegrationTest.h
 * @brief   KeySim 集成测试 — 变量声明
 */

#ifndef KEYSIM_INTEGRATION_TEST_H
#define KEYSIM_INTEGRATION_TEST_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* 测试结果（Watch 观察）*/
extern volatile uint32_t g_integ_tx_ok;   /* TX 成功 */
extern volatile uint32_t g_integ_rx_ok;   /* 回环 RX 成功 */
extern volatile uint32_t g_integ_fail;    /* 失败 */

void KeySim_IntegrationTest_Task(void);

#ifdef __cplusplus
}
#endif

#endif /* KEYSIM_INTEGRATION_TEST_H */
