/**
 * @file    ASW_BusProxy.c
 * @brief   CAN 总线透明代理（CAN0 + CAN1）— 实现
 *
 * 关键决策（grill 2026-08-10 定稿）：
 *   - 主控板对按键板/CAN0 = 100% 透传信道（不解析 ID、不等 ACK）
 *   - 主控板对 ECU/CAN1    = 同样 100% 透传
 *   - 心跳 0x1A0 由按键板发,主控板识别它没意义,只透传
 *
 * 调用链：ASW/BusProxy → EAL/CAN + CDD/USB
 */

#include "ASW_BusProxy.h"
#include <stddef.h>     /* NULL */

/* ============================================
 * CAN_SEND (0x40) 处理
 * payload 15B = {channel, ide, id(4LE), dlc, data[8]}
 * ============================================ */
void ASW_BusProxy_HandleCanSendCmd(uint16_t seq,
                                   const uint8_t *payload, uint8_t len)
{
    EAL_CAN_FrameType f;
    EAL_CAN_RetType   ret;
    uint8_t           k;

    /* 字段格式校验 */
    if (len != 15U || payload[0] > 1U || payload[6] > 8U)
    {
        CDD_USB_SendFrame(seq, CDD_USB_CMD_CAN_SEND,
                          CDD_USB_STS_ERR_FMT, NULL, 0);
        return;
    }

    f.ide = payload[1];
    f.id  = (uint32_t)payload[2] | ((uint32_t)payload[3] << 8) |
            ((uint32_t)payload[4] << 16) | ((uint32_t)payload[5] << 24);
    f.dlc = payload[6];
    f.rtr = 0;
    for (k = 0; k < 8; k++) f.data[k] = payload[7 + k];

    ret = EAL_CAN_Send((payload[0] == 0U) ? EAL_CAN0 : EAL_CAN1, &f);
    if (ret == EAL_CAN_OK)
        CDD_USB_SendFrame(seq, CDD_USB_CMD_CAN_SEND,
                          CDD_USB_STS_OK, NULL, 0);
    else if (ret == EAL_CAN_BUSY)
        CDD_USB_SendFrame(seq, CDD_USB_CMD_CAN_SEND,
                          CDD_USB_STS_ERR_BUSY, NULL, 0);
    else
        CDD_USB_SendFrame(seq, CDD_USB_CMD_CAN_SEND,
                          CDD_USB_STS_ERR_HW, NULL, 0);
}

/* ============================================
 * CAN 收帧 → 0x41 上报
 * payload 15B = {channel, ide, id(4LE), dlc, data[8]}
 * 与 CAN_SEND 完全同构,上位机按 (CMD=0x41, SEQ=0) 直接派发
 * ============================================ */
void ASW_BusProxy_OnCanRx(EAL_CAN_PortType port,
                          const EAL_CAN_FrameType *frame)
{
    uint8_t p[15];
    uint8_t i;

    p[0] = (uint8_t)port;                       /* 0=CAN0 按键板 / 1=CAN1 ECU */
    p[1] = frame->ide;
    p[2] = (uint8_t)(frame->id & 0xFFU);
    p[3] = (uint8_t)((frame->id >> 8) & 0xFFU);
    p[4] = (uint8_t)((frame->id >> 16) & 0xFFU);
    p[5] = (uint8_t)((frame->id >> 24) & 0xFFU);
    p[6] = frame->dlc;
    for (i = 0; i < 8; i++) p[7 + i] = frame->data[i];

    /* SEQ=0 上报,上位机按 §4.2 走"广播出口"不参与配对 */
    CDD_USB_SendFrame(0, CDD_USB_CMD_CAN_RX_IND, CDD_USB_STS_OK, p, 15);
}

/* ============================================
 * 周期任务（纯事件驱动,保留接口对齐）
 * ============================================ */
void ASW_BusProxy_Task(void)
{
    /* 透传不需要周期任务；CAN 收帧通过 EAL_CAN_RegisterRxCallback
     * 在主循环 EAL_CAN_Poll 上下文直接回调本模块的 OnCanRx */
}
