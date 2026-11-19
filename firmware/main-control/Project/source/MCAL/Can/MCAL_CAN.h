/**
 * @file    MCAL_CAN.h
 * @brief   MCAL CAN 驱动 — 薄封装 fsl_flexcan
 *
 * 调用链路：EAL_CAN → MCAL_CAN → fsl_flexcan → 芯片寄存器
 *
 * 硬件配置（参考 ABS_TestCtr1.5 实测 + 本项目已验证）：
 *   - 引擎时钟：总线时钟 48MHz（CTRL1[CLKSRC]=1）
 *   - 引脚：CAN0 = PTA12/PTA13(ALT2)；CAN1 = PTC17/PTC16(ALT2)
 *   - MB 分配：CAN0 TX=MB8；CAN1 TX=MB8/9；RX=MB1~7（MB0 因勘误 e5641 不可用）
 *   - 波特率：8 种（20K~1M），timing 基于 48MHz 精确计算
 */
#ifndef MCAL_CAN_H
#define MCAL_CAN_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* ============================================
 * 通道
 * ============================================ */
typedef enum {
    MCAL_CAN_CH0 = 0,   /* 芯片 CAN0 (PTA12/PTA13) */
    MCAL_CAN_CH1 = 1,   /* 芯片 CAN1 (PTC17/PTC16) */
    MCAL_CAN_CH_COUNT
} MCAL_CAN_ChannelType;

/* ============================================
 * 波特率（顺序必须与 EAL_CAN.c 的 s_baudMap 一致）
 * ============================================ */
typedef enum {
    MCAL_CAN_BAUD_20K = 0,
    MCAL_CAN_BAUD_50K,
    MCAL_CAN_BAUD_100K,
    MCAL_CAN_BAUD_125K,
    MCAL_CAN_BAUD_250K,
    MCAL_CAN_BAUD_500K,
    MCAL_CAN_BAUD_800K,
    MCAL_CAN_BAUD_1M,
    MCAL_CAN_BAUD_COUNT
} MCAL_CAN_BaudrateType;

/* ============================================
 * 帧
 * ============================================ */
typedef struct {
    uint32_t id;        /* 标准帧: bit10~0；扩展帧: 全部29位 */
    uint8_t  data[8];   /* 数据负载 */
    uint8_t  dlc;       /* 长度 0~8 */
    uint8_t  ide;       /* 0=标准帧, 1=扩展帧 */
    uint8_t  rtr;       /* 0=数据帧, 1=远程帧 */
} MCAL_CAN_FrameType;

/* ============================================
 * 接收过滤器
 * ============================================ */
typedef struct {
    uint8_t  mb;        /* MB 编号 (1~7) */
    uint8_t  enable;    /* 0=禁用, 1=启用 */
    uint32_t id;        /* 接收 ID */
    uint32_t mask;      /* 掩码: 1=比较, 0=忽略 */
    uint8_t  ide;       /* 0=标准帧, 1=扩展帧 */
} MCAL_CAN_FilterType;

/* ============================================
 * 发送结果
 * ============================================ */
typedef enum {
    MCAL_CAN_OK   = 0,
    MCAL_CAN_BUSY = 1,
    MCAL_CAN_ERR  = 2,
} MCAL_CAN_RetType;

/* ============================================
 * 接收回调
 * ============================================ */
typedef void (*MCAL_CAN_RxCallbackType)(MCAL_CAN_ChannelType channel,
                                        const MCAL_CAN_FrameType *frame);

/* ============================================
 * 接口
 * ============================================ */

/**
 * @brief  初始化 CAN 通道（时钟 + 引脚 + 模块 + MB 配置，一步完成）
 * @param channel  MCAL_CAN_CH0 / CH1
 * @param baud     MCAL_CAN_BAUD_xxx
 */
void MCAL_CAN_Init(MCAL_CAN_ChannelType channel, MCAL_CAN_BaudrateType baud);

/**
 * @brief  使能 MB 中断掩码（为后续 ISR 预留；当前轮询模式不使能 NVIC）
 */
void MCAL_CAN_EnableIRQ(MCAL_CAN_ChannelType channel);

/**
 * @brief  运行时重配置波特率（不停机）
 */
void MCAL_CAN_SetBaudrate(MCAL_CAN_ChannelType channel, MCAL_CAN_BaudrateType baud);

/**
 * @brief  发送一帧（写入空闲 TX MB，立即返回）
 * @return MCAL_CAN_OK 已写入硬件 TX MB；MCAL_CAN_BUSY TX MB 全忙；MCAL_CAN_ERR 非法通道
 */
MCAL_CAN_RetType MCAL_CAN_Send(MCAL_CAN_ChannelType channel, const MCAL_CAN_FrameType *frame);

/**
 * @brief  轮询接收（主循环调用），读到帧后回调 cb
 */
void MCAL_CAN_PollRx(MCAL_CAN_ChannelType channel, MCAL_CAN_RxCallbackType cb);

/**
 * @brief  配置 RX 过滤器（写 RXIMR 掩码 + 激活/禁用 MB）
 */
void MCAL_CAN_SetFilters(MCAL_CAN_ChannelType channel,
                         const MCAL_CAN_FilterType *filters,
                         uint8_t count);

/* 诊断 */
uint32_t MCAL_CAN_GetESR(MCAL_CAN_ChannelType channel);
uint32_t MCAL_CAN_GetIFLAG(MCAL_CAN_ChannelType channel);
uint32_t MCAL_CAN_GetCTRL(MCAL_CAN_ChannelType channel);

#ifdef __cplusplus
}
#endif

#endif /* MCAL_CAN_H */
