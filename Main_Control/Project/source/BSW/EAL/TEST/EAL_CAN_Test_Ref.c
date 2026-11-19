/**
 * @file    EAL_CAN_Test.c
 * @brief   EAL CAN 诊断与协议帧单测
 *
 * Watch: g_tx_ok / g_tx_fail / g_esr / g_iflag / g_tests_passed / g_tests_failed
 */

#include "EAL_CAN.h"
#include "MCAL/Dwt/MCAL_DWT.h"
#include "KeySim_Protocol.h"

/* ================================================================
 * 诊断变量
 * ================================================================ */
volatile uint32_t g_tx_ok   = 0;
volatile uint32_t g_tx_fail = 0;
volatile uint32_t g_esr     = 0;
volatile uint32_t g_iflag   = 0;
volatile uint32_t g_tests_passed = 0;
volatile uint32_t g_tests_failed = 0;

#define TX_INTERVAL_MS  100U

/* ================================================================
 * 协议帧单测（Watch: g_tests_passed=5, g_tests_failed=0）
 * ================================================================ */
static void test_execute_release_500ms(void)
{
    uint8_t frame[8];
    KeySim_BuildFrame(KEY_CMD_EXECUTE, KEY_ACTION_RELEASE, 500, frame);
    if (frame[0] == 0x01 && frame[1] == 0x02 &&
        frame[2] == 0xF4 && frame[3] == 0x01 &&
        frame[4] == 0 && frame[5] == 0 && frame[6] == 0 && frame[7] == 0) {
        g_tests_passed++;
    } else {
        g_tests_failed++;
    }
}

static void test_execute_park_1000ms(void)
{
    uint8_t frame[8];
    KeySim_BuildFrame(KEY_CMD_EXECUTE, KEY_ACTION_PARK, 1000, frame);
    if (frame[0] == 0x01 && frame[1] == 0x03 &&
        frame[2] == 0xE8 && frame[3] == 0x03 &&
        frame[4] == 0 && frame[5] == 0 && frame[6] == 0 && frame[7] == 0) {
        g_tests_passed++;
    } else {
        g_tests_failed++;
    }
}

static void test_enter_default(void)
{
    uint8_t frame[8];
    KeySim_BuildFrame(KEY_CMD_ENTER_DEFAULT, KEY_ACTION_PARK, 9999, frame);
    if (frame[0] == 0x02 && frame[1] == 0x00 &&
        frame[2] == 0x00 && frame[3] == 0x00 &&
        frame[4] == 0 && frame[5] == 0 && frame[6] == 0 && frame[7] == 0) {
        g_tests_passed++;
    } else {
        g_tests_failed++;
    }
}

static void test_abort(void)
{
    uint8_t frame[8];
    KeySim_BuildFrame(KEY_CMD_ABORT, KEY_ACTION_DEFAULT, 65535, frame);
    if (frame[0] == 0x04 && frame[1] == 0x00 &&
        frame[2] == 0x00 && frame[3] == 0x00 &&
        frame[4] == 0 && frame[5] == 0 && frame[6] == 0 && frame[7] == 0) {
        g_tests_passed++;
    } else {
        g_tests_failed++;
    }
}

static void test_query(void)
{
    uint8_t frame[8];
    KeySim_BuildFrame(KEY_CMD_QUERY, KEY_ACTION_DEFAULT, 0, frame);
    if (frame[0] == 0x10 && frame[1] == 0x00 &&
        frame[2] == 0x00 && frame[3] == 0x00 &&
        frame[4] == 0 && frame[5] == 0 && frame[6] == 0 && frame[7] == 0) {
        g_tests_passed++;
    } else {
        g_tests_failed++;
    }
}

void EAL_CAN_RunProtocolTests(void)
{
    g_tests_passed = 0;
    g_tests_failed = 0;
    test_execute_release_500ms();
    test_execute_park_1000ms();
    test_enter_default();
    test_abort();
    test_query();
}

/* ================================================================
 * 测试任务（节流 TX + 诊断）
 * ================================================================ */
void EAL_CAN_Test_Task(void)
{
    static volatile uint32_t tx_last = 0;
    uint32_t now = MCAL_DWT_GetTicks();

    g_esr   = EAL_CAN_Dbg_GetESR(EAL_CAN0);
    g_iflag = EAL_CAN_Dbg_GetIFLAG(EAL_CAN0);

    if ((now - tx_last) >= (TX_INTERVAL_MS * (SystemCoreClock / 1000U)))
    {
        tx_last = now;

        EAL_CAN_FrameType frame;
        frame.id   = 0x123;
        frame.ide  = 0;
        frame.rtr  = 0;
        frame.dlc   = 8;
        for (uint8_t i = 0; i < 8; i++) frame.data[i] = (i + 1) * 0x11;

        if (EAL_CAN_Send(EAL_CAN0, &frame) == EAL_CAN_OK)
            g_tx_ok++;
        else
            g_tx_fail++;
    }
}
