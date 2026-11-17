/**
 * @file    CDD_KeySim.c
 * @brief   模拟按键板业务封装 — 实现
 *
 * 调用链路：KeySim → EAL_CAN → MCAL_CAN → 芯片寄存器
 */

#include "CDD_KeySim.h"
#include "EAL_GPIO.h"
#include "EAL_CAN.h"

/* CAN 目标 ID（按键板监听地址）*/
#define KEYSIM_CAN_ID   0x100U

void KeySim_Power(bool on)
{
    /* 未编入当前 Keil Target 的历史 KeySim 模块：
     * 仅迁移 GPIO 接口写法，不改变此模块的旧 CAN 协议或启用状态。 */
    EAL_GPIO_WRITE(KEY_PWR_DRV, (uint8_t)(on ? 1U : 0U));
}

static void KeySim_SendFrame(Key_Cmd_t cmd, Key_Action_t action, uint16_t t_hold_ms)
{
    uint8_t raw[8];
    KeySim_BuildFrame(cmd, action, t_hold_ms, raw);

    EAL_CAN_FrameType frame;
    frame.id   = KEYSIM_CAN_ID;
    frame.ide  = 0;
    frame.rtr  = 0;
    frame.dlc  = 8;
    for (uint8_t i = 0; i < 8; i++) frame.data[i] = raw[i];

    (void)EAL_CAN_Send(EAL_CAN0, &frame);
}

void KeySim_Execute(Key_Action_t action, uint16_t t_hold_ms)
{
    KeySim_SendFrame(KEY_CMD_EXECUTE, action, t_hold_ms);
}

void KeySim_EnterDefault(void)
{
    KeySim_SendFrame(KEY_CMD_ENTER_DEFAULT, KEY_ACTION_DEFAULT, 0);
}

void KeySim_Abort(void)
{
    KeySim_SendFrame(KEY_CMD_ABORT, KEY_ACTION_DEFAULT, 0);
}
