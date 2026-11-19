/**
 * @file    EAL_CAN_Test.h
 * @brief   EAL CAN 测试 — 变量声明
 */

#ifndef EAL_CAN_TEST_H
#define EAL_CAN_TEST_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

extern volatile uint32_t g_tx_ok;
extern volatile uint32_t g_tx_fail;
extern volatile uint32_t g_esr;
extern volatile uint32_t g_iflag;
extern volatile uint32_t g_tests_passed;
extern volatile uint32_t g_tests_failed;

void EAL_CAN_RunProtocolTests(void);
void EAL_CAN_Test_Task(void);

#ifdef __cplusplus
}
#endif

#endif /* EAL_CAN_TEST_H */
