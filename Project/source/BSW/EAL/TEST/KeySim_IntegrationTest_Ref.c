/**
 * @file    KeySim_IntegrationTest.c
 * @brief   KeySim 集成测试 — CAN0 内部回环
 *
 * 测试流程：
 *   1. EAL_CAN_Open(CAN0, 500K)
 *   2. KeySim_Power(true)
 *   3. KeySim_Execute(RELEASE, 500) → TX
 *   4. CAN0 进入回环（LPB=1）→ 读取回环帧
 *   5. 循环
 *
 * Watch: g_integ_tx_ok / g_integ_rx_ok / g_integ_fail
 */

#include "derivative.h"
#include "EAL_GPIO.h"
#include "MCAL/Dwt/MCAL_DWT.h"
#include "EAL_CAN.h"
#include "CDD_KeySim.h"
#include "KeySim_IntegrationTest.h"

/* ================================================================
 * CAN0 回环配置（直接寄存器操作）
 * ================================================================ */
static void CAN0_EnterLoopback(void)
{
    CAN0->MCR |= (CAN_MCR_FRZ_MASK | CAN_MCR_HALT_MASK);
    while (!(CAN0->MCR & CAN_MCR_FRZACK_MASK)) { }
    CAN0->CTRL1 |= CAN_CTRL1_LPB_MASK;
    CAN0->MCR &= ~(CAN_MCR_FRZ_MASK | CAN_MCR_HALT_MASK);
    while (CAN0->MCR & CAN_MCR_FRZACK_MASK) { }
    while (CAN0->MCR & CAN_MCR_NOTRDY_MASK)  { }
}

/* ================================================================
 * 集成测试任务
 * ================================================================ */
void KeySim_IntegrationTest_Task(void)
{
    static uint32_t step = 0;
    static uint32_t last = 0;
    uint32_t now = MCAL_DWT_GetTicks();

    /* 节流：每 500ms 一步 */
    if ((now - last) < (500UL * (SystemCoreClock / 1000U))) return;
    last = now;

    switch (step) {
        case 0:
            step = 1;
            break;

        case 1:
            KeySim_Power(true);
            step = 2;
            break;

        case 2:
            KeySim_Execute(KEY_ACTION_RELEASE, 500);
            g_integ_tx_ok++;
            CAN0_EnterLoopback();
            step = 3;
            break;

        case 3:
            if (EAL_CAN_Dbg_GetIFLAG(EAL_CAN0) & 0x0FU) {
                (void)EAL_CAN_Dbg_GetESR(EAL_CAN0);
                g_integ_rx_ok++;
                step = 4;
            }
            break;

        case 4:
            KeySim_Execute(KEY_ACTION_PARK, 1000);
            g_integ_tx_ok++;
            CAN0_EnterLoopback();
            step = 5;
            break;

        case 5:
            if (EAL_CAN_Dbg_GetIFLAG(EAL_CAN0) & 0x0FU) {
                (void)EAL_CAN_Dbg_GetESR(EAL_CAN0);
                g_integ_rx_ok++;
                step = 6;
            }
            break;

        case 6:
            KeySim_EnterDefault();
            g_integ_tx_ok++;
            CAN0_EnterLoopback();
            step = 7;
            break;

        case 7:
            if (EAL_CAN_Dbg_GetIFLAG(EAL_CAN0) & 0x0FU) {
                (void)EAL_CAN_Dbg_GetESR(EAL_CAN0);
                g_integ_rx_ok++;
                step = 1;
            }
            break;

        default:
            step = 0;
            break;
    }
}
