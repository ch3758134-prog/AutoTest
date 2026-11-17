/**
 * @file    CDD_USB.h
 * @brief   上位机通信帧协议（0xA5 原子指令帧）— 接口
 *
 * 本层职责：字节流 ↔ 0xA5 帧（拆帧 / 组帧 / CRC16），**不含任何业务判断**。
 * 帧格式 / 指令码 / 状态码是主控板↔上位机的【协议契约】，集中在此头文件。
 *
 * 调用链路：ASW → CDD_USB → EAL_USB → source/USB/
 * 协议来源：docs/plan/原子指令集协议.md
 */
#ifndef CDD_USB_H
#define CDD_USB_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* ============================================================
 * 帧格式（与 原子指令集协议.md 一致，字节序小端）
 *   请求 : |SOF|SEQ(2)|CMD|LEN|PAYLOAD[LEN]|CRC(2)|EOF|
 *   响应 : |SOF|SEQ(2)|CMD|STATUS|LEN|PAYLOAD|CRC(2)|EOF|
 * ============================================================ */
#define CDD_USB_FRAME_SOF       (0xA5U)
#define CDD_USB_FRAME_EOF       (0x5AU)
#define CDD_USB_FRAME_REQ_MIN   (8U)      /* LEN=0 时最小请求帧 */
#define CDD_USB_FRAME_RESP_HEAD (6U)      /* SOF+SEQ(2)+CMD+STATUS+LEN */
#define CDD_USB_FRAME_RESP_TAIL (3U)      /* CRC(2)+EOF */

/* ============================================================
 * 状态码（响应帧 STATUS 字节）
 * ============================================================ */
#define CDD_USB_STS_OK         0x00U
#define CDD_USB_STS_ERR_FMT    0x02U
#define CDD_USB_STS_ERR_CMD    0x03U
#define CDD_USB_STS_ERR_BUSY   0x05U
#define CDD_USB_STS_ERR_HW     0x06U

/* ============================================================
 * 指令码
 * ============================================================ */
/* 系统 */
#define CDD_USB_CMD_PING            0xF0U
#define CDD_USB_CMD_GET_VERSION     0xF1U
/*
 * 当前对外 GPIO 命令：IGN、两个独立夹具。
 * 已废止 0x10/0x11(ECU供电)、0x14~0x17(按键板供电/旧SOL)和0x1F。
 * RL_DRV 属于内部 PCB 功率电路控制，无 USB 命令。
 */
#define CDD_USB_CMD_SET_IGN         0x12U /* 单字节0/1 */
#define CDD_USB_CMD_GET_IGN         0x13U /* 空请求，返回1字节0/1 */
/*
 * SET_CLAMP 0x18: [clamp=1/2, state=0释放/1压紧]，共2B；
 * GET_CLAMPS 0x19: 空请求，返回1字节 bit0夹具1 / bit1夹具2。
 * 两夹具独立，查询仅是 MCU GPIO 电平，非夹具到位反馈。
 */
#define CDD_USB_CMD_SET_CLAMP       0x18U
#define CDD_USB_CMD_GET_CLAMPS      0x19U
/* CAN 发送 / 收帧回传 */
#define CDD_USB_CMD_CAN_SEND        0x40U
#define CDD_USB_CMD_CAN_RX_IND      0x41U
/* 整车环境广播（0x50~0x53，经 CAN1 → ECU） */
#define CDD_USB_CMD_SET_BROADCAST     0x50U
#define CDD_USB_CMD_BROADCAST_START   0x51U
#define CDD_USB_CMD_BROADCAST_STOP    0x52U
#define CDD_USB_CMD_BROADCAST_STATUS  0x53U

/* ============================================================
 * 帧回调（拆出完整请求帧后调用，payload 指向帧内数据，仅本次回调有效）
 * ============================================================ */
typedef void (*CDD_USB_FrameHandler)(uint16_t seq, uint8_t cmd,
                                     const uint8_t *payload, uint8_t len);

/* ============================================================
 * 接口
 * ============================================================ */
void CDD_USB_Init(void);                       /* 复位累积缓冲 */
void CDD_USB_Task(void);                       /* 主循环：读字节流→拆帧→帧回调 */
void CDD_USB_RegisterFrameHandler(CDD_USB_FrameHandler h);
void CDD_USB_SendFrame(uint16_t seq, uint8_t cmd, uint8_t status,
                       const uint8_t *payload, uint8_t len);   /* 组响应帧并发送 */

#ifdef __cplusplus
}
#endif

#endif /* CDD_USB_H */
