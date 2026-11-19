/**
 * @file    KeySim_Protocol.h
 * @brief   模拟按键板协议帧 — 类型定义与 BuildFrame 声明
 *
 * 协议来源：docs/plan/模拟按键板规格文档.md v0.2
 *
 * 帧格式（标准 CAN，数据帧，8字节，DLC=8）：
 *   Byte 0: cmd
 *   Byte 1: action
 *   Byte 2-3: t_hold（毫秒，little-endian）
 *   Byte 4-7: reserved = 0x00
 */

#ifndef KEYSIM_PROTOCOL_H
#define KEYSIM_PROTOCOL_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* ================================================================
 * 按键命令
 * ================================================================ */
typedef enum {
    KEY_CMD_EXECUTE       = 0x01,  /* 执行动作 */
    KEY_CMD_ENTER_DEFAULT = 0x02,  /* 进入默认态 */
    KEY_CMD_ABORT         = 0x04,  /* 中断 */
    KEY_CMD_QUERY         = 0x10,  /* 查询 */
} Key_Cmd_t;

/* ================================================================
 * 按键动作（仅 EXECUTE 命令有效）
 * ================================================================ */
typedef enum {
    KEY_ACTION_DEFAULT = 0x01,  /* 默认态 */
    KEY_ACTION_RELEASE  = 0x02,  /* 释放（K7+K8 LOW）*/
    KEY_ACTION_PARK     = 0x03,  /* 驻车（K12+K13 LOW）*/
} Key_Action_t;

/* ================================================================
 * 帧打包
 * ================================================================ */

/**
 * @brief  打包一帧按键命令
 * @param[in]  cmd      命令
 * @param[in]  action   动作（EXECUTE 时有效）
 * @param[in]  t_hold   保持时间（毫秒，EXECUTE 时有效）
 * @param[out] frame    输出缓冲区（8字节）
 * @note  frame[4..7] 固定填 0
 */
void KeySim_BuildFrame(Key_Cmd_t cmd, Key_Action_t action,
                       uint16_t t_hold, uint8_t frame[8]);

#ifdef __cplusplus
}
#endif

#endif /* KEYSIM_PROTOCOL_H */
