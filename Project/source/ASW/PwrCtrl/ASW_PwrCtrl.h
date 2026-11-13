/**
 * @file    ASW_PwrCtrl.h
 * @brief   主控板电源与电磁阀控制业务（3 路 12V + 点火 + 电磁阀）— 接口
 *
 * 职责（firmware-requirements.md §5）：
 *   - 上位机 SET_* (1/0) → 翻对应 GPIO
 *   - 上位机 GET_*       → 读 GPIO 真实电平（不是期望态）
 *   - SET_ECU_PWR (on=1) 同时拉 RL_DRV + ECU_PWR_DRV（主继电器 + ECU 供电同开）
 *   - SET_SOL (action=0/1) 控制电磁阀：0=升起（PB18 HIGH），1=下落/压紧（PB18 LOW）
 *
 * 调用链：ASW/PwrCtrl → EAL/GPIO
 */
#ifndef ASW_PWRCTRL_H
#define ASW_PWRCTRL_H

#include <stdint.h>
#include "CDD_USB.h"   /* 需要 CDD_USB_SendFrame + CMD/STS */

/**
 * @brief  处理电源类命令（路由自 UsbSvc_HandleCommand）
 * @note   覆盖 CMD_SET_ECU_PWR / GET_ECU_PWR / SET_IGN / GET_IGN /
 *              SET_KEYPWR / GET_KEYPWR / SET_SOL / GET_SOL / GET_ALL_PWR
 *         不匹配的 cmd 直接回 ERR_CMD
 */
void ASW_PwrCtrl_HandleCmd(uint16_t seq, uint8_t cmd,
                           const uint8_t *payload, uint8_t len);

/**
 * @brief  周期任务占位（目前无周期任务,保留接口对齐其他 Role）
 */
void ASW_PwrCtrl_Task(void);

#endif /* ASW_PWRCTRL_H */
