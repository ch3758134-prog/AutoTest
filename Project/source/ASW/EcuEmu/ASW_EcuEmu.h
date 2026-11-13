/**
 * @file    ASW_EcuEmu.h
 * @brief   ECU 整车环境模拟（广播帧表 + 调度）— 接口
 *
 * 职责（firmware-requirements.md §4.3）：
 *   - SET_BROADCAST: 装填帧表（不自动启动）
 *   - BROADCAST_START: 启动帧表（幂等）
 *   - BROADCAST_STOP:  停止帧表（幂等,保留帧表）
 *   - BROADCAST_STATUS: 查询 (running, count)
 *   - 所有帧统一从 CAN1 发出（通向 ECU）
 *   - USB 断开（enum=0）→ 静默 STOP（避免无人监控的持续输出）
 *
 * 调度（无 RTOS,无合作式调度器）：主循环按各帧 period_ms 检查 now-last_tick
 *
 * 调用链：ASW/EcuEmu → EAL/CAN + EAL/USB(查 enum) + EAL/Time
 */
#ifndef ASW_ECUEMU_H
#define ASW_ECUEMU_H

#include <stdint.h>
#include "CDD_USB.h"

/**
 * @brief  处理广播类命令（路由自 UsbSvc_HandleCommand）
 *         覆盖 SET_BROADCAST / START / STOP / STATUS
 */
void ASW_EcuEmu_HandleCmd(uint16_t seq, uint8_t cmd,
                          const uint8_t *payload, uint8_t len);

/**
 * @brief  周期任务:帧表调度 + USB 断开检测
 *         由 UsbSvc_Task 在主循环调用
 */
void ASW_EcuEmu_Task(void);

#endif /* ASW_ECUEMU_H */
