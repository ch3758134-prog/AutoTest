/**
 * @file ASW_IoCtrl.h
 * @brief 主控板 IGN、六路 LED 与两路夹具的 USB 业务接口。
 * @details 不提供 ECU/按键板供电、旧 SOL 或 RL_DRV 业务接口。
 */
#ifndef ASW_IOCTRL_H
#define ASW_IOCTRL_H

#include <stdint.h>
#include "CDD_USB.h"

/** @warning OK 只说明 GPIO 操作完成，不表示夹具机械到位。 */
void ASW_IoCtrl_HandleCmd(uint16_t seq, uint8_t cmd,
                          const uint8_t *payload, uint8_t len);

/** @brief USB 枚举丢失时撤销夹具压紧请求；保留旧策略，待实测确认。 */
void ASW_IoCtrl_Task(void);

#endif /* ASW_IOCTRL_H */
