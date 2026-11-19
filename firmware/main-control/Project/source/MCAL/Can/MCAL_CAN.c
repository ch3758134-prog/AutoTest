/**
 * @file    MCAL_CAN.c
 * @brief   MCAL CAN 驱动 — 薄封装 fsl_flexcan
 *
 * 调用链路：EAL_CAN → MCAL_CAN → fsl_flexcan → 芯片寄存器
 *
 * 已验证配置（真实总线 500k 发送通过）：
 *   - 引擎时钟 = 总线时钟 48MHz（CLKSRC=1）
 *   - 引脚 ALT2：CAN0=PTA12/13, CAN1=PTC17/16
 *   - TX: CAN0=MB8, CAN1=MB8/9; RX=MB1~7
 *     （MB0 因勘误 e5641 不可用；EACEN=1 下 RXIMR=0 标准/扩展帧都收）
 */

#include "MCAL_CAN.h"
#include "fsl_flexcan.h"
#include "fsl_clock.h"
#include "fsl_port.h"

/* ============================================
 * 通道映射（与 EAL_CAN.h 引脚一致）
 * ============================================ */
#define MCAL_CAN_RX_MB_FIRST  1U    /* RX 用 MB1~7 (MB0 因勘误 e5641 不可用) */
#define MCAL_CAN_RX_MB_LAST   7U
#define MCAL_CAN_TX_MB_MAX    2U

static CAN_Type * const s_canBase[] = {
    CAN0,   /* MCAL_CAN_CH0 */
    CAN1,   /* MCAL_CAN_CH1 */
};

static const clock_ip_name_t s_canClock[] = {
    kCLOCK_Flexcan0,
    kCLOCK_Flexcan1,
};

static PORT_Type * const s_portBase[] = {
    PORTA,  /* CAN0 引脚在 PORTA */
    PORTC,  /* CAN1 引脚在 PORTC */
};

static const clock_ip_name_t s_portClock[] = {
    kCLOCK_PortA,
    kCLOCK_PortC,
};

static const uint32_t s_txPin[] = { 12U, 17U };   /* CAN0_TX=PTA12, CAN1_TX=PTC17 */
static const uint32_t s_rxPin[] = { 13U, 16U };   /* CAN0_RX=PTA13, CAN1_RX=PTC16 */

static const uint8_t s_txMbCount[] = { 1U, 2U };  /* CAN1 先启用 2 个 TX MB 降低 MB8 忙竞争 */
static const uint8_t s_txMb[MCAL_CAN_CH_COUNT][MCAL_CAN_TX_MB_MAX] = {
    { 8U, 0U },   /* CAN0: 保持原单 TX MB */
    { 8U, 9U },   /* CAN1: TX MB8 + MB9 */
};

/* ============================================
 * 波特率 timing（总线时钟 48MHz，字段值=实际-1）
 * 250k/500k/1M 为参考项目 Drv_Can.c 实测值；其余按 48M/(PRESDIV+1)/Tq 精确推导
 * 采样点约 75%（800k 为 70%）
 * ============================================ */
static const flexcan_timing_config_t s_timing[MCAL_CAN_BAUD_COUNT] = {
    [MCAL_CAN_BAUD_20K]  = { .preDivider = 149, .rJumpwidth = 1, .phaseSeg1 = 6, .phaseSeg2 = 3, .propSeg = 3 },  /* 16 Tq */
    [MCAL_CAN_BAUD_50K]  = { .preDivider = 59,  .rJumpwidth = 1, .phaseSeg1 = 6, .phaseSeg2 = 3, .propSeg = 3 },  /* 16 Tq */
    [MCAL_CAN_BAUD_100K] = { .preDivider = 59,  .rJumpwidth = 1, .phaseSeg1 = 3, .phaseSeg2 = 1, .propSeg = 0 },  /* 8 Tq  */
    [MCAL_CAN_BAUD_125K] = { .preDivider = 47,  .rJumpwidth = 1, .phaseSeg1 = 3, .phaseSeg2 = 1, .propSeg = 0 },  /* 8 Tq  */
    [MCAL_CAN_BAUD_250K] = { .preDivider = 23,  .rJumpwidth = 2, .phaseSeg1 = 3, .phaseSeg2 = 1, .propSeg = 0 },  /* 8 Tq  */
    [MCAL_CAN_BAUD_500K] = { .preDivider = 11,  .rJumpwidth = 2, .phaseSeg1 = 3, .phaseSeg2 = 1, .propSeg = 0 },  /* 8 Tq  */
    [MCAL_CAN_BAUD_800K] = { .preDivider = 5,   .rJumpwidth = 1, .phaseSeg1 = 3, .phaseSeg2 = 2, .propSeg = 1 },  /* 10 Tq */
    [MCAL_CAN_BAUD_1M]   = { .preDivider = 5,   .rJumpwidth = 2, .phaseSeg1 = 3, .phaseSeg2 = 1, .propSeg = 0 },  /* 8 Tq  */
};

static const uint32_t s_baudHz[MCAL_CAN_BAUD_COUNT] = {
    [MCAL_CAN_BAUD_20K]  = 20000U,
    [MCAL_CAN_BAUD_50K]  = 50000U,
    [MCAL_CAN_BAUD_100K] = 100000U,
    [MCAL_CAN_BAUD_125K] = 125000U,
    [MCAL_CAN_BAUD_250K] = 250000U,
    [MCAL_CAN_BAUD_500K] = 500000U,
    [MCAL_CAN_BAUD_800K] = 800000U,
    [MCAL_CAN_BAUD_1M]   = 1000000U,
};

/* ============================================
 * 初始化
 * ============================================ */
void MCAL_CAN_Init(MCAL_CAN_ChannelType channel, MCAL_CAN_BaudrateType baud)
{
    if ((uint32_t)channel >= (uint32_t)MCAL_CAN_CH_COUNT) return;
    if ((uint32_t)baud >= (uint32_t)MCAL_CAN_BAUD_COUNT) return;

    CAN_Type *base = s_canBase[channel];

    /* 1. 时钟使能（端口 + CAN 模块） */
    CLOCK_EnableClock(s_portClock[channel]);
    CLOCK_EnableClock(s_canClock[channel]);

    /* 2. 引脚复用 ALT2（数据手册/参考项目实测） */
    PORT_SetPinMux(s_portBase[channel], s_txPin[channel], kPORT_MuxAlt2);
    PORT_SetPinMux(s_portBase[channel], s_rxPin[channel], kPORT_MuxAlt2);

    /* 3. FLEXCAN 初始化：总线时钟源 48MHz */
    flexcan_config_t config;
    FLEXCAN_GetDefaultConfig(&config);
    config.clkSrc            = kFLEXCAN_ClkSrcPeri;     /* 总线时钟 */
    config.baudRate          = s_baudHz[baud];           /* 目标波特率（内部计算用，随后覆盖） */
    config.maxMbNum          = 16;
    config.enableLoopBack    = false;
    config.enableSelfWakeup  = false;
    config.enableIndividMask = true;                     /* 使能 RXIMR 独立掩码 */
    FLEXCAN_Init(base, &config, CLOCK_GetBusClkFreq());

    /* 4. 精确 timing 覆盖（FSL 自动 timing 固定 10 Tq，算不出精确波特率） */
    FLEXCAN_SetTimingConfig(base, &s_timing[baud]);

    /* 5. MB 配置：CAN0 TX=MB8, CAN1 TX=MB8/9, RX=MB1~7
     * 注：FLEXCAN_Init 已置 EACEN=1（CTRL2），RXIMR=0 下 ID/IDE/RTR 全 don't-care，
     * 故 MB1~7 标准/扩展帧都收（本层无需分格）。
     * RXIMR 只能在 Freeze 模式下写（参考手册），故 RX 配置需进入 freeze 窗口 */
    for (uint8_t i = 0; i < s_txMbCount[channel]; i++)
    {
        FLEXCAN_SetTxMbConfig(base, s_txMb[channel][i], true);
    }

    base->MCR |= CAN_MCR_HALT_MASK;
    while (!(base->MCR & CAN_MCR_FRZACK_MASK)) {}

    for (uint8_t mb = MCAL_CAN_RX_MB_FIRST; mb <= MCAL_CAN_RX_MB_LAST; mb++)
    {
        flexcan_rx_mb_config_t rx = {
            .id     = FLEXCAN_ID_STD(0),
            .format = kFLEXCAN_FrameFormatStandard,
            .type   = kFLEXCAN_FrameTypeData,
        };
        FLEXCAN_SetRxMbConfig(base, mb, &rx, true);
        base->RXIMR[mb] = 0;   /* 默认全收（掩码全不比较），SetFilters 可收紧 */
    }

    base->MCR &= ~CAN_MCR_HALT_MASK;
    while (base->MCR & CAN_MCR_FRZACK_MASK) {}
}

/* ============================================
 * 中断使能（轮询模式预留）
 * ============================================ */
void MCAL_CAN_EnableIRQ(MCAL_CAN_ChannelType channel)
{
    if ((uint32_t)channel >= (uint32_t)MCAL_CAN_CH_COUNT) return;
    CAN_Type *base = s_canBase[channel];

    /* 置 MB 中断掩码位，供后续 ISR 使用；当前轮询模式不使能 NVIC（无 handler 避免崩溃） */
    for (uint8_t mb = MCAL_CAN_RX_MB_FIRST; mb <= MCAL_CAN_RX_MB_LAST; mb++)
        base->IMASK1 |= (1UL << mb);
    for (uint8_t i = 0; i < s_txMbCount[channel]; i++)
        base->IMASK1 |= (1UL << s_txMb[channel][i]);
}

/* ============================================
 * 波特率重配置
 * ============================================ */
void MCAL_CAN_SetBaudrate(MCAL_CAN_ChannelType channel, MCAL_CAN_BaudrateType baud)
{
    if ((uint32_t)channel >= (uint32_t)MCAL_CAN_CH_COUNT) return;
    if ((uint32_t)baud >= (uint32_t)MCAL_CAN_BAUD_COUNT) return;
    FLEXCAN_SetTimingConfig(s_canBase[channel], &s_timing[baud]);
}

/* ============================================
 * 发送（写入空闲 TX MB，立即返回）
 * ============================================ */
MCAL_CAN_RetType MCAL_CAN_Send(MCAL_CAN_ChannelType channel, const MCAL_CAN_FrameType *frame)
{
    if ((uint32_t)channel >= (uint32_t)MCAL_CAN_CH_COUNT) return MCAL_CAN_ERR;
    CAN_Type *base = s_canBase[channel];

    flexcan_frame_t f;
    f.id     = (frame->ide) ? FLEXCAN_ID_EXT(frame->id) : FLEXCAN_ID_STD(frame->id);
    f.format = (frame->ide) ? kFLEXCAN_FrameFormatExtend : kFLEXCAN_FrameFormatStandard;
    f.type   = (frame->rtr) ? kFLEXCAN_FrameTypeRemote : kFLEXCAN_FrameTypeData;
    f.length = frame->dlc;
    /* 大端打包（同参考 CAN_WriteData: data[0] 在高字节） */
    f.dataWord0 = ((uint32_t)frame->data[0] << 24) | ((uint32_t)frame->data[1] << 16) |
                  ((uint32_t)frame->data[2] << 8)  |  (uint32_t)frame->data[3];
    f.dataWord1 = ((uint32_t)frame->data[4] << 24) | ((uint32_t)frame->data[5] << 16) |
                  ((uint32_t)frame->data[6] << 8)  |  (uint32_t)frame->data[7];

    for (uint8_t i = 0; i < s_txMbCount[channel]; i++)
    {
        if (FLEXCAN_WriteTxMb(base, s_txMb[channel][i], &f) == kStatus_Success)
            return MCAL_CAN_OK;
    }

    return MCAL_CAN_BUSY;   /* 所有 TX MB 均忙（上一批帧未发完） */
}

/* ============================================
 * 轮询接收
 * ============================================ */
void MCAL_CAN_PollRx(MCAL_CAN_ChannelType channel, MCAL_CAN_RxCallbackType cb)
{
    if ((uint32_t)channel >= (uint32_t)MCAL_CAN_CH_COUNT) return;
    CAN_Type *base = s_canBase[channel];

    for (uint8_t mb = MCAL_CAN_RX_MB_FIRST; mb <= MCAL_CAN_RX_MB_LAST; mb++)
    {
        if (FLEXCAN_GetMbStatusFlags(base, (1UL << mb)))
        {
            flexcan_frame_t f;
            if (FLEXCAN_ReadRxMb(base, mb, &f) == kStatus_Success)
            {
                FLEXCAN_ClearMbStatusFlags(base, (1UL << mb));

                if (cb != NULL)
                {
                    MCAL_CAN_FrameType mf;
                    mf.id  = (f.format == kFLEXCAN_FrameFormatExtend) ? (f.id & 0x1FFFFFFFUL)
                                                                      : ((f.id >> 18) & 0x7FFUL);
                    mf.ide = (f.format == kFLEXCAN_FrameFormatExtend) ? 1U : 0U;
                    mf.rtr = (f.type == kFLEXCAN_FrameTypeRemote) ? 1U : 0U;
                    mf.dlc = f.length;
                    for (uint8_t i = 0; i < 8; i++)
                    {
                        mf.data[i] = (i < 4) ? (uint8_t)(f.dataWord0 >> ((3U - i) * 8U))
                                             : (uint8_t)(f.dataWord1 >> ((7U - i) * 8U));
                    }
                    cb(channel, &mf);
                }
            }
        }
    }
}

/* ============================================
 * 接收过滤器
 * ============================================ */
void MCAL_CAN_SetFilters(MCAL_CAN_ChannelType channel,
                         const MCAL_CAN_FilterType *filters,
                         uint8_t count)
{
    if ((uint32_t)channel >= (uint32_t)MCAL_CAN_CH_COUNT) return;
    CAN_Type *base = s_canBase[channel];

    /* RXIMR 只能在 Freeze 模式下写 */
    base->MCR |= CAN_MCR_HALT_MASK;
    while (!(base->MCR & CAN_MCR_FRZACK_MASK)) {}

    for (uint8_t i = 0; i < count; i++)
    {
        const MCAL_CAN_FilterType *flt = &filters[i];
        if ((flt->mb < MCAL_CAN_RX_MB_FIRST) || (flt->mb > MCAL_CAN_RX_MB_LAST)) continue;

        if (flt->enable)
        {
            flexcan_rx_mb_config_t rx;
            rx.id     = (flt->ide == 1) ? FLEXCAN_ID_EXT(flt->id) : FLEXCAN_ID_STD(flt->id);
            rx.format = (flt->ide == 1) ? kFLEXCAN_FrameFormatExtend : kFLEXCAN_FrameFormatStandard;
            rx.type   = kFLEXCAN_FrameTypeData;
            FLEXCAN_SetRxMbConfig(base, flt->mb, &rx, true);
            /* RXIMR: 1=比较, 0=忽略；标准帧 ID 在 bit28:18，需移位 */
            base->RXIMR[flt->mb] = (flt->ide == 1) ? (flt->mask & 0x1FFFFFFFUL)
                                                    : ((flt->mask & 0x7FFUL) << 18);
        }
        else
        {
            FLEXCAN_SetRxMbConfig(base, flt->mb, NULL, false);   /* 停用 */
        }
    }

    base->MCR &= ~CAN_MCR_HALT_MASK;
    while (base->MCR & CAN_MCR_FRZACK_MASK) {}
}

/* ============================================
 * 诊断
 * ============================================ */
uint32_t MCAL_CAN_GetESR(MCAL_CAN_ChannelType channel)
{
    if ((uint32_t)channel >= (uint32_t)MCAL_CAN_CH_COUNT) return 0;
    return s_canBase[channel]->ESR1;
}

uint32_t MCAL_CAN_GetIFLAG(MCAL_CAN_ChannelType channel)
{
    if ((uint32_t)channel >= (uint32_t)MCAL_CAN_CH_COUNT) return 0;
    return s_canBase[channel]->IFLAG1;
}

uint32_t MCAL_CAN_GetCTRL(MCAL_CAN_ChannelType channel)
{
    if ((uint32_t)channel >= (uint32_t)MCAL_CAN_CH_COUNT) return 0;
    return s_canBase[channel]->CTRL1;
}
