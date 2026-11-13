/**
 * @file    EAL_CAN.h
 * @brief   板级 CAN 适配层 — 接口
 *
 * 封装内容：
 *   - 板级 CAN 通道名称（CAN0/CAN1 → 芯片 CAN0/CAN1）
 *   - 帧结构、波特率、过滤器配置
 *
 * 协议来源：docs/plan/模拟按键板规格文档.md v0.2
 *
 * CAN 参数：
 *   - CAN0: PTA12(TX) / PTA13(RX)  ← 板级接口 0
 *   - CAN1: PTC17(TX) / PTC16(RX)  ← 板级接口 1
 *   - 波特率：20K~1M（BusClock = 48MHz）
 *
 * MB 分配（芯片级，用户无感知）：
 *   MB 0-7  : RX 接收
 *   MB 8-15 : TX 发送
 */
#ifndef EAL_CAN_H
#define EAL_CAN_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* ============================================
 * 板级 CAN 通道（直白对应板子上物理接口编号）
 * ============================================ */
typedef enum {
    EAL_CAN0 = 0,   /* 板级 CAN0 → 芯片 CAN0 (PTA12/PTA13) */
    EAL_CAN1 = 1    /* 板级 CAN1 → 芯片 CAN1 (PTC17/PTC16) */
} EAL_CAN_PortType;

/* ============================================
 * 波特率 (BusClock = 48MHz)
 * ============================================
 * BitRate = 48MHz / ((PRESDIV+1) * (1+PROPSEG+PSEG1+PSEG2))
 */
typedef enum {
    EAL_CAN_BAUD_20K  = 0,
    EAL_CAN_BAUD_50K  = 1,
    EAL_CAN_BAUD_100K = 2,
    EAL_CAN_BAUD_125K = 3,
    EAL_CAN_BAUD_250K = 4,
    EAL_CAN_BAUD_500K = 5,
    EAL_CAN_BAUD_800K = 6,
    EAL_CAN_BAUD_1M   = 7,
} EAL_CAN_BaudType;

/* ============================================
 * CAN 帧
 * ============================================ */
typedef struct {
    uint32_t id;      /* 标准帧: bit10~0；扩展帧: 全部29位 */
    uint8_t  data[8];/* 数据负载 */
    uint8_t  dlc;     /* 长度: 0~8 */
    uint8_t  ide;      /* 0=标准帧, 1=扩展帧 */
    uint8_t  rtr;     /* 0=数据帧, 1=远程帧 */
} EAL_CAN_FrameType;

/* ============================================
 * RX 过滤器
 * ============================================ */
typedef struct {
    uint8_t  mb;         /* MB 编号 (0~7) */
    uint8_t  enable;    /* 0=禁用, 1=启用 */
    uint32_t id;         /* 接收 ID */
    uint32_t mask;       /* 掩码: 1=比较, 0=忽略 */
    uint8_t  ide;        /* 0=标准帧, 1=扩展帧, 2=任意 */
} EAL_CAN_RxFilterType;

/* ============================================
 * 发送结果
 * ============================================ */
typedef enum {
    EAL_CAN_OK   = 0,
    EAL_CAN_BUSY = 1,
    EAL_CAN_ERR  = 2,
} EAL_CAN_RetType;

/* ============================================
 * 接收回调
 * ============================================ */
typedef void (*EAL_CAN_RxCallbackType)(EAL_CAN_PortType port, const EAL_CAN_FrameType *frame);

/* ============================================
 * 接口
 * ============================================ */

/**
 * @brief  打开 CAN 通道（初始化 + 启动，一步完成）
 * @param port  板级 CAN 端口
 * @param baud  波特率
 */
void EAL_CAN_Open(EAL_CAN_PortType port, EAL_CAN_BaudType baud);

/**
 * @brief  运行时重配置波特率（不停机）
 */
void EAL_CAN_SetBaudrate(EAL_CAN_PortType port, EAL_CAN_BaudType baud);

/**
 * @brief  配置 RX 过滤器
 * @param port     板级 CAN 端口
 * @param filters  过滤器数组
 * @param count    数量 (≤8)
 */
void EAL_CAN_SetFilters(EAL_CAN_PortType port,
                         const EAL_CAN_RxFilterType *filters,
                         uint8_t count);

/**
 * @brief  发送一帧（同步，立即返回）
 * @return EAL_CAN_OK / EAL_CAN_BUSY / EAL_CAN_ERR
 */
EAL_CAN_RetType EAL_CAN_Send(EAL_CAN_PortType port,
                               const EAL_CAN_FrameType *frame);

/**
 * @brief  注册接收回调（NULL=不回调，由上层轮询）
 */
void EAL_CAN_RegisterRxCallback(EAL_CAN_PortType port,
                                 EAL_CAN_RxCallbackType cb);

/**
 * @brief  处理接收（主循环调用）
 */
void EAL_CAN_Poll(EAL_CAN_PortType port);

/* ============================================
 * 诊断接口（调试用）
 * ============================================ */
uint32_t EAL_CAN_Dbg_GetESR(EAL_CAN_PortType port);
uint32_t EAL_CAN_Dbg_GetIFLAG(EAL_CAN_PortType port);
uint32_t EAL_CAN_Dbg_GetCTRL(EAL_CAN_PortType port);

/* ============================================
 * 便捷宏
 * ============================================ */
#define EAL_CAN_STD_ID(id)    ((uint32_t)(id) & 0x7FFU)
#define EAL_CAN_EXT_ID(id)    ((uint32_t)(id) & 0x1FFFFFFFU)
#define EAL_CAN_MASK_STD_ALL  0x7FFU
#define EAL_CAN_MASK_EXT_ALL  0x1FFFFFFFU

#ifdef __cplusplus
}
#endif

#endif /* EAL_CAN_H */
