/**
 * @file    EAL_CAN_RxTest.c
 * @brief   CAN RX 独立验证模块
 *
 * 参考项目 flexcan.c 的 RX 机制，不修改 MCAL 层。
 *
 * 数据流：
 *   帧到达 -> IFLAG1[mb]=1 -> CANx_ORed_MB_IRQHandler
 *   -> ReadMB() -> CAN_MsgBox[port][mb]
 *   -> CAN_MBRxflag[port] |= 1<<mb
 *   -> 主循环轮询 CAN_MBRxflag
 *
 * Watch: g_rx_cnt[2] / CAN_MBRxflag[2] / CAN_MsgBox
 */

#include "derivative.h"
#include "EAL_GPIO.h"
#include "EAL_CAN.h"

/* ============================================
 * 全局缓冲（参考项目风格）
 * ============================================ */
#define CAN_MB_COUNT  16
#define CAN_PORT_COUNT 2

typedef struct {
    uint32_t id;
    uint8_t  data[8];
    uint8_t  len;
    uint8_t  format;  /* 0=标准帧, 1=扩展帧 */
    uint8_t  type;     /* 0=数据帧, 1=远程帧 */
} CAN_msg_t;

CAN_msg_t CAN_MsgBox[CAN_PORT_COUNT][CAN_MB_COUNT];
volatile uint32_t CAN_MBRxflag[CAN_PORT_COUNT];

/* Watch 变量 */
volatile uint32_t g_rx_cnt[2] = {0, 0};

/* ============================================
 * ReadMB -- 从 MB 读取一帧（纯寄存器操作）
 * ============================================ */
static void ReadMB(CAN_Type *can, uint8_t mb, CAN_msg_t *msg)
{
    uint32_t cs = can->MB[mb].CS;

    /* ID */
    if (cs & CAN_CS_IDE_MASK) {
        msg->format = 1;
        msg->id     = can->MB[mb].ID & 0x1FFFFFFFU;
    } else {
        msg->format = 0;
        msg->id     = (can->MB[mb].ID >> 18) & 0x7FFU;
    }

    /* DLC */
    msg->len = (uint8_t)((cs >> 16) & 0x0FU);
    if (msg->len > 8) msg->len = 8;

    /* TYPE */
    msg->type = 0;

    /* 数据 */
    uint32_t w0 = can->MB[mb].WORD0;
    uint32_t w1 = can->MB[mb].WORD1;
    for (uint8_t i = 0; i < msg->len; i++) {
        if (i < 4) {
            msg->data[i] = (uint8_t)(w0 >> ((3 - i) * 8));
        } else {
            msg->data[i] = (uint8_t)(w1 >> ((7 - i) * 8));
        }
    }

    /* 读 TIMER 解锁 MB */
    (void)can->TIMER;
}

/* ============================================
 * CAN0 组合中断处理函数
 * ============================================ */
void CAN0_ORed_Message_buffer_IRQHandler(void)
{
    uint32_t iflag1 = CAN0->IFLAG1;
    uint32_t mb;

    while (iflag1) {
        for (mb = 0; mb < CAN_MB_COUNT; mb++) {
            if (iflag1 & (1U << mb)) {

                if (mb < 8) {
                    /* RX MB: 读帧，置标志 */
                    CAN0->IFLAG1 = (1U << mb);
                    ReadMB(CAN0, (uint8_t)mb, &CAN_MsgBox[0][mb]);
                    CAN_MBRxflag[0] |= (1U << mb);
                    g_rx_cnt[0]++;
                } else {
                    /* TX MB: 仅清 IFLAG */
                    CAN0->IFLAG1 = (1U << mb);
                }

                iflag1 &= ~(1U << mb);
                if (!iflag1) return;
            }
        }
        iflag1 = CAN0->IFLAG1;
    }
}

/* ============================================
 * CAN1 组合中断处理函数
 * ============================================ */
void CAN1_ORed_Message_buffer_IRQHandler(void)
{
    uint32_t iflag1 = CAN1->IFLAG1;
    uint32_t mb;

    while (iflag1) {
        for (mb = 0; mb < CAN_MB_COUNT; mb++) {
            if (iflag1 & (1U << mb)) {

                if (mb < 8) {
                    CAN1->IFLAG1 = (1U << mb);
                    ReadMB(CAN1, (uint8_t)mb, &CAN_MsgBox[1][mb]);
                    CAN_MBRxflag[1] |= (1U << mb);
                    g_rx_cnt[1]++;
                } else {
                    CAN1->IFLAG1 = (1U << mb);
                }

                iflag1 &= ~(1U << mb);
                if (!iflag1) return;
            }
        }
        iflag1 = CAN1->IFLAG1;
    }
}

/* ============================================
 * 主循环轮询任务
 * ============================================ */
void EAL_CAN_RxTest_Task(void)
{
    uint32_t port, mb;

    for (port = 0; port < CAN_PORT_COUNT; port++) {
        uint32_t flag = CAN_MBRxflag[port];

        for (mb = 0; mb < 8; mb++) {
            if (flag & (1U << mb)) {
                /* 清标志 */
                CAN_MBRxflag[port] &= ~(1U << mb);

                /* 从 MsgBox 读取（供调试 Watch）*/
                (void)port;
                (void)mb;
                /* 这里可以加业务逻辑，例如：
                   if (CAN_MsgBox[port][mb].id == 0x200) { ... }
                */
            }
        }
    }
}
