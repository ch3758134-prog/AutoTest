/**
 * @file    ASW_PwrCtrl.h
 * @brief   供电、IGN、Air_ALVE 原始电气输出的 USB 业务接口。
 *
 * @details
 * ASW 负责命令参数与业务边界，EAL_GPIO 负责板级 GPIO，MCAL 负责 MCU。
 * 此层不把 Air_ALVE1/2 命名成压紧或释放；该映射必须以现场气路确定。
 */
#ifndef ASW_PWRCTRL_H
#define ASW_PWRCTRL_H

#include <stdint.h>
#include "CDD_USB.h"

/**
 * @brief  处理供电、IGN、Air_ALVE 请求和查询命令。
 * @param  seq 请求序号。
 * @param  cmd CDD_USB_CMD_* 指令码。
 * @param  payload 请求载荷，查询类指令可为 NULL。
 * @param  len 载荷长度。
 * @warning 成功状态表示命令处理成功，不代表检测到阀位或输出电压。
 */
void ASW_PwrCtrl_HandleCmd(uint16_t seq, uint8_t cmd,
                          const uint8_t *payload, uint8_t len);

/**
 * @brief USB 失去枚举时，清除已请求的 Air_ALVE 电气输出。
 * @note  不替代独立机械急停、动作超时或断电状态验证。
 */
void ASW_PwrCtrl_Task(void);

#endif /* ASW_PWRCTRL_H */
