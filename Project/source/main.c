/**
 * @file    main.c
 * @brief   EPB 测试控制器固件 — 主循环入口
 *
 * 已封装到模块（2026-08-10 USB 业务封装）：
 *   - EAL_USB       USB 传输封装（Project/source/BSW/EAL/USB/）
 *   - CDD_USB       0xA5 帧协议（Project/source/BSW/CDD/USB/）
 *   - ASW_UsbSvc    上位机通信业务（Project/source/ASW/UsbSvc/）
 *
 * 主循环：CAN 轮询 + ASW_UsbSvc_Task（内含 CDD_USB_Task 收帧 + 广播调度）
 * 协议契约：docs/plan/原子指令集协议.md
 * 设计文档：docs/plan/USB业务封装方案.md
 */

#include "EAL_GPIO.h"
#include "EAL_CAN.h"
#include "EAL_TIME.h"
#include "EAL_USB.h"
#include "ASW_UsbSvc.h"

extern void Clock_Init(void);

int main(void)
{
    /* 时钟 + 时间基 */
    Clock_Init();
    EAL_TIME_Init();                     /* DWT 循环计数器（供 ASW 广播调度用）*/

    /* GPIO：外部高边输出控制脚全部默认 LOW/OFF；
     * EAL 最后独立初始化内部 RL_DRV=HIGH，供电电路不属于 USB 业务。
     * 已取消的 ECU/按键板电源输出不再初始化。见 EAL_GPIO.c。 */
    EAL_GPIO_Init();

    /* 固定业务端口：KEY=J1 B11/B1，ECU=J1 A12/A1；均为 500kbps 轮询。 */
    EAL_CAN_Open(KEY_CAN_PORT, EAL_CAN_BAUD_500K);
    EAL_CAN_Open(ECU_CAN_PORT, EAL_CAN_BAUD_500K);

    /* USB：传输 + 枚举就绪（WCID WinUSB 免驱在 EAL_USB 内部）*/
    EAL_USB_Init();

    /* ASW 业务：注册帧回调 + CAN 收帧回调（内含 CDD_USB_Init）*/
    ASW_UsbSvc_Init();

    for (;;)
    {
        EAL_CAN_Poll(KEY_CAN_PORT);
        EAL_CAN_Poll(ECU_CAN_PORT);
        ASW_UsbSvc_Task();               /* CDD_USB_Task（收帧）+ 广播调度 */
    }
    return 0;
}
