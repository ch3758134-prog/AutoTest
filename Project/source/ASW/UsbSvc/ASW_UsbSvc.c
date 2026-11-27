/**
 * @file    ASW_UsbSvc.c
 * @brief   上位机通信路由 + 协议应答 — 实现
 *
 * 调用链：ASW/UsbSvc → CDD/USB + ASW/IoCtrl + ASW/BusProxy + ASW/EcuEmu
 *
 * 本文件 = 协议路由器：
 *   - 协议级命令（PING/GET_VERSION）自处理
 *   - 业务级命令按 CMD 范围路由到 IoCtrl/BusProxy/EcuEmu
 *
 * 历史（2026-08-10 拆分）:
 *   - 原 main.c HandleRequest 拆成 4 个文件
 *   - UsbSvc 仅剩路由,业务逻辑全部下沉
 */

#include "ASW_UsbSvc.h"
#include "CDD_USB.h"
#include "ASW_IoCtrl.h"
#include "ASW_BusProxy.h"
#include "ASW_EcuEmu.h"
#include "EAL_USB.h"
#include "EAL_GPIO.h"
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
    static const uint8_t ver[] = "EPB_TEST_CTRL v0.4-dev";
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
        /* IGN、六路LED与两个独立夹具的 USB 控制。 */
        case CDD_USB_CMD_SET_IGN:
        case CDD_USB_CMD_GET_IGN:
        case CDD_USB_CMD_SET_CLAMP:
        case CDD_USB_CMD_GET_CLAMPS:
        case CDD_USB_CMD_SET_LED:
        case CDD_USB_CMD_GET_LEDS:
        case CDD_USB_CMD_SET_LEDS:
            ASW_IoCtrl_HandleCmd(seq, cmd, payload, len);
            return;

        /* CAN 透传(0x40) */
        case CDD_USB_CMD_CAN_SEND:
            ASW_BusProxy_HandleCanSendCmd(seq, payload, len);
            return;

        /* 仅 ECU CAN 运行时码率设置/查询；KEY CAN 不变。 */
        case CDD_USB_CMD_SET_ECU_CAN_BAUD:
        case CDD_USB_CMD_GET_ECU_CAN_BAUD:
            ASW_BusProxy_HandleEcuBaudCmd(seq, cmd, payload, len);
            return;

        /* 只读查询外部 KEY1~KEY3 的消抖 GPIO 电平、事件计数。 */
        case CDD_USB_CMD_GET_KEY_INPUTS:
        {
            uint8_t snapshot[3];
            uint16_t count;
            if (len != 0U)
            {
                CDD_USB_SendFrame(seq, cmd, CDD_USB_STS_ERR_FMT, NULL, 0U);
                return;
            }
            count = EAL_GPIO_KeySequence();
            snapshot[0] = EAL_GPIO_KeyLevels();
            snapshot[1] = (uint8_t)count;
            snapshot[2] = (uint8_t)(count >> 8);
            CDD_USB_SendFrame(seq, cmd, CDD_USB_STS_OK, snapshot, 3U);
            return;
        }

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
    EAL_CAN_RegisterRxCallback(KEY_CAN_PORT, ASW_BusProxy_OnCanRx);
    EAL_CAN_RegisterRxCallback(ECU_CAN_PORT, ASW_BusProxy_OnCanRx);
}

void ASW_UsbSvc_Task(void)
{
    /* 收帧 → 路由 → 业务对象回响应 */
    CDD_USB_Task();
    EAL_USB_TxTask();

    /* 各 Role 的周期任务 */
    ASW_IoCtrl_Task();    /* USB丢失枚举时撤销夹具压紧输出 */
    ASW_BusProxy_Task();   /* 当前无任务,接口预留 */
    ASW_EcuEmu_Task();     /* 广播帧表调度 + USB 断开停广播 */

    /* 外部实体按钮：在主循环读取中断标记并完成消抖；ISR 不发送 USB。
     * 即使未枚举，已确认的 GPIO 变化也推进 sequence；上位机可用 0x45 查询。
     * KEY 事件 SEQ=0 仍是异步帧，但优先于 CAN 监视队列。
     * 可能拥堵丢失，使用 sequence 检测并可用 0x45 查询实际稳定电平。
     */
    {
        EAL_GPIO_KeyEventType evt;
        while (EAL_GPIO_KeyPopEvent(&evt) != 0U)
        {
            if (EAL_USB_IsEnumReady() != 0U)
            {
                const uint8_t data[5] = {
                    evt.key, evt.level, evt.levels,
                    (uint8_t)evt.sequence, (uint8_t)(evt.sequence >> 8)
                };
                CDD_USB_SendFrame(0U, CDD_USB_CMD_KEY_INPUT_EVENT,
                                  CDD_USB_STS_OK, data, 5U);
            }
        }
    }
}
