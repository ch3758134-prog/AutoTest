/**
 * @file    CDD_KeySim.h
 * @brief   模拟按键板业务封装 — 上层 API
 *
 * 封装：
 *   1. 按键板电源控制（KEY_PWR_DRV）
 *   2. CAN0 协议帧发送（BuildFrame → EAL_CAN_Send）
 *
 * 协议来源：docs/plan/模拟按键板规格文档.md v0.2
 *
 * CAN 参数：
 *   - 通道：EAL_CAN0
 *   - 波特率：500kbps
 *   - CAN ID：0x100（主控板 → 按键板）
 *
 * 应答：不依赖应答（规格 §4.3 已确认）
 */

#ifndef CDD_KEYSIM_H
#define CDD_KEYSIM_H

#include <stdbool.h>
#include "KeySim_Protocol.h"

#ifdef __cplusplus
extern "C" {
#endif

/* ================================================================
 * 电源控制
 * ================================================================ */

/**
 * @brief  按键板电源开关
 * @param  on  true=上电，false=断电
 * @note   上电后按键板自动进入默认态（K5 LOW + K11 LOW）
 */
void KeySim_Power(bool on);

/* ================================================================
 * 按键动作
 * ================================================================ */

/**
 * @brief  执行一次完整按键动作
 *
 * 动作时序（规格 §3.3）：
 *   ① 所有 key 置 HIGH
 *   ② 动作对应 GPIO 置 LOW（K7+K8=释放，K12+K13=驻车）
 *   ③ 保持 t_hold_ms 毫秒
 *   ④ 所有 key 置 HIGH
 *   ⑤ 默认 GPIO 置 LOW（K5+K11）
 *
 * @param[in] action     按键动作（KEY_ACTION_DEFAULT / RELEASE / PARK）
 * @param[in] t_hold_ms  保持时间（毫秒）
 *
 * @note  本函数只发 EXECUTE 命令，具体时序由按键板固件执行
 */
void KeySim_Execute(Key_Action_t action, uint16_t t_hold_ms);

/**
 * @brief  立即进入默认态
 */
void KeySim_EnterDefault(void);

/**
 * @brief  立即中断所有按键动作
 */
void KeySim_Abort(void);

#ifdef __cplusplus
}
#endif

#endif /* CDD_KEYSIM_H */
