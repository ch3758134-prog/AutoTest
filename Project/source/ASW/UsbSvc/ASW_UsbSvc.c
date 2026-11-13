/**
 * @file    ASW_UsbSvc.c
 * @brief   上位机通信路由 + 协议应答 — 实现
 *
 * 调用链：ASW/UsbSvc → CDD/USB + ASW/PwrCtrl + ASW/BusProxy + ASW/EcuEmu
 *
 * 本文件 = 协议路由器：
 *   - 协议级命令（PING/GET_VERSION）自处理
 *   - 业务级命令按 CMD 范围路由到 PwrCtrl/BusProxy/EcuEmu
 *
 * 历史（2026-08-10 拆分）:
 *   - 原 main.c HandleRequest 拆成 4 个文件
 *   - UsbSvc 仅剩路由,业务逻辑全部下沉
 */

#include "ASW_UsbSvc.h"
#include "CDD_USB.h"
#include "ASW_PwrCtrl.h"
#include "ASW_BusProxy.h"
#include "ASW_EcuEmu.h"
#include "EAL_USB.h"
#include <stddef.h>     /* NULL */

/* ============================================
 * 协议级命令(自处理)
 * ============================================ */
static void on_ping(uint16_t seq)
{
    CDD_USB_SendFrame(seq, CDD_USB_CMD_PING,
                      CDD_USB_STS_OK, (const uint8_t *)"PONG", 4);
}

static void on_get_version(uint16_t seq)
{
    static const uint8_t ver[] = "EPB_TEST_CTRL v0.3";
    CDD_USB_SendFrame(seq, CDD_USB_CMD_GET_VERSION,
                      CDD_USB_STS_OK, ver, (uint8_t)(sizeof(ver) - 1));
}

/* ============================================
 * 路由分派(CDD_USB 帧回调注册给 ASW_UsbSvc_Init)
 * ============================================ */
static void HandleCommand(uint16_t seq, uint8_t cmd,
                          const uint8_t *payload, uint8_t len)
{
    /* 协议级 ——UsbSvc 自处理 */
    switch (cmd)
    {
        case CDD_USB_CMD_PING:
            on_ping(seq);
            return;

        case CDD_USB_CMD_GET_VERSION:
            on_get_version(seq);
            return;

        default:
            /* 非协议级命令,继续向下路由 */
            break;
    }

    /* 业务级 ——按命令码范围路由到对应 Role */
    switch (cmd)
    {
        /* 电源(0x10~0x1F) */
        case CDD_USB_CMD_SET_ECU_PWR:
        case CDD_USB_CMD_GET_ECU_PWR:
        case CDD_USB_CMD_SET_IGN:
        case CDD_USB_CMD_GET_IGN:
        case CDD_USB_CMD_SET_KEYPWR:
        case CDD_USB_CMD_GET_KEYPWR:
        case CDD_USB_CMD_SET_SOL:
        case CDD_USB_CMD_GET_SOL:
        case CDD_USB_CMD_GET_ALL_PWR:
            ASW_PwrCtrl_HandleCmd(seq, cmd, payload, len);
            return;

        /* CAN 透传(0x40) */
        case CDD_USB_CMD_CAN_SEND:
            ASW_BusProxy_HandleCanSendCmd(seq, payload, len);
            return;

        /* ECU 模拟(0x50~0x53) */
        case CDD_USB_CMD_SET_BROADCAST:
        case CDD_USB_CMD_BROADCAST_START:
        case CDD_USB_CMD_BROADCAST_STOP:
        case CDD_USB_CMD_BROADCAST_STATUS:
            ASW_EcuEmu_HandleCmd(seq, cmd, payload, len);
            return;

        default:
            CDD_USB_SendFrame(seq, cmd, CDD_USB_STS_ERR_CMD, NULL, 0);
            return;
    }
}

/* ============================================
 * 接口实现
 * ============================================ */
void ASW_UsbSvc_Init(void)
{
    CDD_USB_Init();
    CDD_USB_RegisterFrameHandler(HandleCommand);

    /* CAN 收帧 → BusProxy 透传(0x41 上报) */
    EAL_CAN_RegisterRxCallback(EAL_CAN0, ASW_BusProxy_OnCanRx);
    EAL_CAN_RegisterRxCallback(EAL_CAN1, ASW_BusProxy_OnCanRx);
}

void ASW_UsbSvc_Task(void)
{
    /* 收帧 → 路由 → 业务对象回响应 */
    CDD_USB_Task();
    EAL_USB_TxTask();

    /* 各 Role 的周期任务 */
    ASW_PwrCtrl_Task();    /* 当前无任务,接口预留 */
    ASW_BusProxy_Task();   /* 当前无任务,接口预留 */
    ASW_EcuEmu_Task();     /* 广播帧表调度 + USB 断开停广播 */
}
