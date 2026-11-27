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
 * 当前对外 GPIO 命令：IGN、两个独立夹具、六路LED。
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
/*
 * HOUT2~HOUT7 依次为 RED1、YELLOW1、GREEN1、RED2、YELLOW2、GREEN2：
 * SET_LED  0x1A: [index=1..6, level=0/1] 2B；仅控制指定一路。
 * GET_LEDS 0x1B: 无参数；响应1B位图 bit0..5 依次为以上六路 GPIO 电平。
 * SET_LEDS 0x1C: [bitmap] 1B；必须 0x00..0x3F。
 * STATUS=OK仅表示MCU GPIO已更新，不证明负载电压/LED实际亮灭。
 */
#define CDD_USB_CMD_SET_LED         0x1AU
#define CDD_USB_CMD_GET_LEDS        0x1BU
#define CDD_USB_CMD_SET_LEDS        0x1CU
/* CAN 发送 / 收帧回传 */
#define CDD_USB_CMD_CAN_SEND        0x40U
#define CDD_USB_CMD_CAN_RX_IND      0x41U
/*
 * 仅 ECU CAN（FlexCAN1 / J1 A12 H + A1 L）的运行时码率控制：
 * SET_ECU_CAN_BAUD 0x42：[baud_code] 1B（见 EAL_CAN_BaudType 0..7）;
 * GET_ECU_CAN_BAUD 0x43：空请求，成功返回 [baud_code] 1B。
 * code: 0=20k,1=50k,2=100k,3=125k,4=250k,5=500k,6=800k,7=1M。
 * 复位默认 500k；更改不影响 KEY CAN，不持久化。
 */
#define CDD_USB_CMD_SET_ECU_CAN_BAUD 0x42U
#define CDD_USB_CMD_GET_ECU_CAN_BAUD 0x43U
/*
 * 主控板外部瞬时按钮输入，独立于 KEY_SIM 20 路 CAN 模拟按键。
 * 0x44 KEY_INPUT_EVENT：SEQ=0 的异步上报，5B:
 * [key=1..3, stable_level=0/1, stable_levels_bitmap(bit0..2), sequence_lo, sequence_hi]。
 * 0x45 GET_KEY_INPUTS：无请求 Payload，响应3B:
 * [stable_levels_bitmap, sequence_lo, sequence_hi]。
 * sequence 是主控板上电以来的 16bit 稳定变化计数（回绕）；不能证明 USB 上报无丢失。
 * 按下极性尚待实体确认，故 USB 仅传 MCU 逻辑电平，不预设按下/松开。
 */
#define CDD_USB_CMD_KEY_INPUT_EVENT  0x44U
#define CDD_USB_CMD_GET_KEY_INPUTS   0x45U
/* 整车环境广播（0x50~0x53，固定由 ECU_CAN_PORT 发往 ECU） */
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
