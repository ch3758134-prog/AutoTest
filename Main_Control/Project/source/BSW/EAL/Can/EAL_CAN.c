/**
 * @file    EAL_CAN.c
 * @brief   板级 CAN 适配层 — 实现
 *
 * 调用链路：EAL_CAN.c → MCAL_CAN.c → 芯片寄存器
 */

#include "EAL_CAN.h"
#include "MCAL/Can/MCAL_CAN.h"
#include <stddef.h>

/* ============================================
 * EAL → MCAL 通道映射
 * ============================================ */
static const MCAL_CAN_ChannelType s_chMap[] = {
    MCAL_CAN_CH0,   /* KEY_CAN_PORT → MCU CAN0 → J1 B11/B1 */
    MCAL_CAN_CH1,   /* ECU_CAN_PORT → MCU CAN1 → J1 A12/A1 */
};

/* ============================================
 * EAL → MCAL 波特率映射
 * ============================================ */
static const MCAL_CAN_BaudrateType s_baudMap[] = {
    MCAL_CAN_BAUD_20K, MCAL_CAN_BAUD_50K, MCAL_CAN_BAUD_100K,
    MCAL_CAN_BAUD_125K, MCAL_CAN_BAUD_250K, MCAL_CAN_BAUD_500K,
    MCAL_CAN_BAUD_800K, MCAL_CAN_BAUD_1M,
};

/* ============================================
 * 上层回调（由 MCAL 回调透传）
 * ============================================ */
static EAL_CAN_RxCallbackType s_rxCallback[2] = {NULL, NULL};

/* 初始配置与 main.c 的 KEY/ECU 500k 初始化一致；仅更新配置成功的端口。 */
static EAL_CAN_BaudType s_currentBaud[2] = {EAL_CAN_BAUD_500K, EAL_CAN_BAUD_500K};

/* ============================================
 * MCAL 回调 → EAL 回调（适配层）
 * ============================================ */
static void MCAL_RxCbAdapter(MCAL_CAN_ChannelType mcal_ch,
                              const MCAL_CAN_FrameType *mcal_frame)
{
    EAL_CAN_PortType port = (EAL_CAN_PortType)mcal_ch;

    EAL_CAN_FrameType frame;
    frame.id   = mcal_frame->id;
    frame.dlc  = mcal_frame->dlc;
    frame.ide  = mcal_frame->ide;
    frame.rtr  = mcal_frame->rtr;
    for (uint8_t i = 0; i < 8; i++)
        frame.data[i] = mcal_frame->data[i];

    if (s_rxCallback[port] != NULL)
        s_rxCallback[port](port, &frame);
}

/* ============================================
 * EAL_CAN_Open = Init + Start（一步完成）
 * ============================================ */
void EAL_CAN_Open(EAL_CAN_PortType port, EAL_CAN_BaudType baud)
{
    if ((uint32_t)port > (uint32_t)EAL_CAN1 || (uint32_t)baud > (uint32_t)EAL_CAN_BAUD_1M)
        return;
    MCAL_CAN_Init(s_chMap[port], s_baudMap[baud]);
    MCAL_CAN_EnableIRQ(s_chMap[port]);
    s_currentBaud[port] = baud;
}

/* ============================================
 * 波特率重配置
 * ============================================ */
EAL_CAN_RetType EAL_CAN_SetBaudrate(EAL_CAN_PortType port, EAL_CAN_BaudType baud)
{
    MCAL_CAN_RetType ret;

    if ((uint32_t)port > (uint32_t)EAL_CAN1 || (uint32_t)baud > (uint32_t)EAL_CAN_BAUD_1M)
        return EAL_CAN_ERR;
    if (s_currentBaud[port] == baud)
        return EAL_CAN_OK;

    ret = MCAL_CAN_SetBaudrate(s_chMap[port], s_baudMap[baud]);
    if (ret == MCAL_CAN_BUSY)
        return EAL_CAN_BUSY;
    if (ret != MCAL_CAN_OK)
        return EAL_CAN_ERR;
    s_currentBaud[port] = baud;
    return EAL_CAN_OK;
}

EAL_CAN_BaudType EAL_CAN_GetBaudrate(EAL_CAN_PortType port)
{
    return ((uint32_t)port <= (uint32_t)EAL_CAN1) ? s_currentBaud[port]
                                                  : EAL_CAN_BAUD_500K;
}

/* ============================================
 * 发送
 * ============================================ */
EAL_CAN_RetType EAL_CAN_Send(EAL_CAN_PortType port,
                               const EAL_CAN_FrameType *frame)
{
    MCAL_CAN_FrameType mcal_frame;
    mcal_frame.id   = frame->id;
    mcal_frame.dlc  = frame->dlc;
    mcal_frame.ide  = frame->ide;
    mcal_frame.rtr  = frame->rtr;
    for (uint8_t i = 0; i < 8; i++)
        mcal_frame.data[i] = frame->data[i];

    MCAL_CAN_RetType ret = MCAL_CAN_Send(s_chMap[port], &mcal_frame);

    if (ret == MCAL_CAN_OK)   return EAL_CAN_OK;
    if (ret == MCAL_CAN_BUSY) return EAL_CAN_BUSY;
    return EAL_CAN_ERR;
}

/* ============================================
 * 接收（轮询）
 * ============================================ */
void EAL_CAN_Poll(EAL_CAN_PortType port)
{
    MCAL_CAN_PollRx(s_chMap[port], MCAL_RxCbAdapter);
}

/* ============================================
 * 过滤器
 * ============================================ */
void EAL_CAN_SetFilters(EAL_CAN_PortType port,
                         const EAL_CAN_RxFilterType *filters,
                         uint8_t count)
{
    MCAL_CAN_FilterType mcal_filters[8];
    uint8_t n = (count > 8) ? 8 : count;

    for (uint8_t i = 0; i < n; i++) {
        mcal_filters[i].mb     = filters[i].mb;
        mcal_filters[i].enable = filters[i].enable;
        mcal_filters[i].id     = filters[i].id;
        mcal_filters[i].mask   = filters[i].mask;
        mcal_filters[i].ide    = filters[i].ide;
    }
    MCAL_CAN_SetFilters(s_chMap[port], mcal_filters, n);
}

/* ============================================
 * 注册回调
 * ============================================ */
void EAL_CAN_RegisterRxCallback(EAL_CAN_PortType port,
                                 EAL_CAN_RxCallbackType cb)
{
    if (port <= EAL_CAN1)
        s_rxCallback[port] = cb;
}

/* ============================================
 * 诊断（调试用）
 * ============================================ */
uint32_t EAL_CAN_Dbg_GetESR(EAL_CAN_PortType port)
{
    return MCAL_CAN_GetESR(s_chMap[port]);
}

uint32_t EAL_CAN_Dbg_GetIFLAG(EAL_CAN_PortType port)
{
    return MCAL_CAN_GetIFLAG(s_chMap[port]);
}

uint32_t EAL_CAN_Dbg_GetCTRL(EAL_CAN_PortType port)
{
    return MCAL_CAN_GetCTRL(s_chMap[port]);
}
