/**
 * @file    KeySim_Protocol.c
 * @brief   模拟按键板协议帧 — 帧打包实现
 *
 * KeySim_BuildFrame 为内部函数，仅供 KeySim_SendFrame 调用
 */

#include "KeySim_Protocol.h"

/* ================================================================
 * 帧打包
 * ================================================================ */
void KeySim_BuildFrame(Key_Cmd_t cmd, Key_Action_t action,
                       uint16_t t_hold, uint8_t frame[8])
{
    frame[0] = (uint8_t)cmd;
    frame[1] = (uint8_t)action;
    frame[2] = (uint8_t)(t_hold & 0xFF);        /* t_hold 低字节 */
    frame[3] = (uint8_t)((t_hold >> 8) & 0xFF);  /* t_hold 高字节 */
    frame[4] = 0x00;
    frame[5] = 0x00;
    frame[6] = 0x00;
    frame[7] = 0x00;
}
