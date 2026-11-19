/**
 * @file    ASW_BusProxy.h
 * @brief   CAN 总线透明代理（CAN0 + CAN1 双向透传）— 接口
 *
 * 职责（firmware-requirements.md §4.1 / §4.2）：
 *   - 上位机 CAN_SEND → 按 channel 字段透传到对应 CAN 控制器（不解析 ID）
 *   - CAN0/CAN1 任一收帧 → 0x41 (CAN_RX_IND) 上报上位机（不解析、不等 ACK）
 *   - **完全不解析任何 CAN ID**——心跳检测、按键动作 ACK 都是上位机职责
 *
 * 调用链：ASW/BusProxy → EAL/CAN → MCAL/CAN → 硬件
 *                  → CDD/USB (上行 0x41)
 */
#ifndef ASW_BUSPROXY_H
#define ASW_BUSPROXY_H

#include <stdint.h>
#include "EAL_CAN.h"     /* EAL_CAN_PortType / EAL_CAN_FrameType */
#include "CDD_USB.h"     /* CDD_USB_CMD_CAN_SEND + CDD_USB_SendFrame */

/**
 * @brief  处理 CAN_SEND (0x40) 命令（路由自 UsbSvc_HandleCommand）
 * @param  payload 15B = {channel, ide, id(4LE), dlc, data[8]}
 */
void ASW_BusProxy_HandleCanSendCmd(uint16_t seq,
                                   const uint8_t *payload, uint8_t len);

/**
 * @brief  CAN 收帧回调（由 EAL_CAN_RegisterRxCallback 注册,主循环上下文）
 * @note   收啥帧 → 原样打包成 0x41 上报（不解析 ID,不区分 port 业务含义）
 */
void ASW_BusProxy_OnCanRx(EAL_CAN_PortType port,
                          const EAL_CAN_FrameType *frame);

/**
 * @brief  周期任务占位（纯事件驱动,保留接口对齐其他 Role）
 */
void ASW_BusProxy_Task(void);

#endif /* ASW_BUSPROXY_H */
