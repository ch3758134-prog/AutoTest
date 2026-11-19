#include "sys_include.h"
#include "Driver_Gpio.h"
#include "KeySim.h"

#define KEYSIM_CAN_ID 0x730U

static void KeySim_WriteMask(u32 mask)
{
    if (mask & 0x00001UL) KEY1_ON();  else KEY1_OFF();
    if (mask & 0x00002UL) KEY2_ON();  else KEY2_OFF();
    if (mask & 0x00004UL) KEY3_ON();  else KEY3_OFF();
    if (mask & 0x00008UL) KEY4_ON();  else KEY4_OFF();
    if (mask & 0x00010UL) KEY5_ON();  else KEY5_OFF();
    if (mask & 0x00020UL) KEY6_ON();  else KEY6_OFF();
    if (mask & 0x00040UL) KEY7_ON();  else KEY7_OFF();
    if (mask & 0x00080UL) KEY8_ON();  else KEY8_OFF();
    if (mask & 0x00100UL) KEY9_ON();  else KEY9_OFF();
    if (mask & 0x00200UL) KEY10_ON(); else KEY10_OFF();
    if (mask & 0x00400UL) KEY11_ON(); else KEY11_OFF();
    if (mask & 0x00800UL) KEY12_ON(); else KEY12_OFF();
    if (mask & 0x01000UL) KEY13_ON(); else KEY13_OFF();
    if (mask & 0x02000UL) KEY14_ON(); else KEY14_OFF();
    if (mask & 0x04000UL) KEY15_ON(); else KEY15_OFF();
    if (mask & 0x08000UL) KEY16_ON(); else KEY16_OFF();
    if (mask & 0x10000UL) KEY17_ON(); else KEY17_OFF();
    if (mask & 0x20000UL) KEY18_ON(); else KEY18_OFF();
    if (mask & 0x40000UL) KEY19_ON(); else KEY19_OFF();
    if (mask & 0x80000UL) KEY20_ON(); else KEY20_OFF();
}

u8 KeySim_ProcessFrame(const CanMsgInf *msg)
{
    u8 i;
    u32 mask;
    if (msg->CanId != KEYSIM_CAN_ID || msg->FrameType != DATA_FRAME || msg->MsgLth != 8U) return 0U;
    if ((msg->Data[3] & 0xF0U) != 0U) return 0U;
    for (i = 4U; i < 8U; ++i) if (msg->Data[i] != 0U) return 0U;
    if (msg->Data[0] == 0x80U) {
        if (msg->Data[1] != 0U || msg->Data[2] != 0U || msg->Data[3] != 0U) return 0U;
        KeySim_WriteMask(0UL);
        return 1U;
    }
    if (msg->Data[0] != 0x01U) return 0U;
    mask = (u32)msg->Data[1] | ((u32)msg->Data[2] << 8U) | (((u32)msg->Data[3] & 0x0FU) << 16U);
    KeySim_WriteMask(mask);
    return 1U;
}
