/**
 * @file    ASW_PwrCtrl.c
 * @brief   主控板电源控制业务 — 实现
 *
 * 调用链路：ASW/PwrCtrl → EAL/GPIO
 */

#include "ASW_PwrCtrl.h"
#include "EAL_GPIO.h"
#include <stddef.h>     /* NULL（CDD_USB_SendFrame 无 payload 时使用）*/

/* ============================================
 * 内部辅助：读 1B 并按 0/1 判定;非法值返 ERR_FMT
 * ============================================ */
static int parse_on(const uint8_t *payload, uint8_t len, uint8_t *on_out)
{
    if (len != 1U) return 0;
    if (payload[0] > 1U) return 0;
    *on_out = payload[0];
    return 1;
}

/* ============================================
 * 接口实现
 * ============================================ */
void ASW_PwrCtrl_HandleCmd(uint16_t seq, uint8_t cmd,
                           const uint8_t *payload, uint8_t len)
{
    switch (cmd)
    {
        case CDD_USB_CMD_SET_ECU_PWR:
        {
            /* 1=RL_DRV + ECU_PWR_DRV 同开；0=同关（继电器在前避免 ECU 上电但无母线）*/
            uint8_t on;
            if (!parse_on(payload, len, &on))
            {
                CDD_USB_SendFrame(seq, cmd, CDD_USB_STS_ERR_FMT, NULL, 0);
                break;
            }
            if (on) { RL_DRV_HIGH(); ECU_PWR_DRV_HIGH(); }
            else    { ECU_PWR_DRV_LOW(); RL_DRV_LOW(); }
            CDD_USB_SendFrame(seq, cmd, CDD_USB_STS_OK, NULL, 0);
            break;
        }

        case CDD_USB_CMD_GET_ECU_PWR:
        {
            uint8_t v = EAL_GPIO_Get(EAL_GPIO_SIG_ECU_PWR_DRV);
            CDD_USB_SendFrame(seq, cmd, CDD_USB_STS_OK, &v, 1);
            break;
        }

        case CDD_USB_CMD_SET_IGN:
        {
            uint8_t on;
            if (!parse_on(payload, len, &on))
            {
                CDD_USB_SendFrame(seq, cmd, CDD_USB_STS_ERR_FMT, NULL, 0);
                break;
            }
            if (on) IGN_HIGH(); else IGN_LOW();
            CDD_USB_SendFrame(seq, cmd, CDD_USB_STS_OK, NULL, 0);
            break;
        }

        case CDD_USB_CMD_GET_IGN:
        {
            uint8_t v = EAL_GPIO_Get(EAL_GPIO_SIG_IGN);
            CDD_USB_SendFrame(seq, cmd, CDD_USB_STS_OK, &v, 1);
            break;
        }

        case CDD_USB_CMD_SET_KEYPWR:
        {
            uint8_t on;
            if (!parse_on(payload, len, &on))
            {
                CDD_USB_SendFrame(seq, cmd, CDD_USB_STS_ERR_FMT, NULL, 0);
                break;
            }
            if (on) KEY_PWR_DRV_HIGH(); else KEY_PWR_DRV_LOW();
            CDD_USB_SendFrame(seq, cmd, CDD_USB_STS_OK, NULL, 0);
            break;
        }

        case CDD_USB_CMD_GET_KEYPWR:
        {
            uint8_t v = EAL_GPIO_Get(EAL_GPIO_SIG_KEY_PWR_DRV);
            CDD_USB_SendFrame(seq, cmd, CDD_USB_STS_OK, &v, 1);
            break;
        }

        case CDD_USB_CMD_SET_SOL:
        {
            uint8_t action;
            if (!parse_on(payload, len, &action))
            {
                CDD_USB_SendFrame(seq, cmd, CDD_USB_STS_ERR_FMT, NULL, 0);
                break;
            }
            /* 上位机动作值：0=升起，1=下落；固件通过 SOL_UP/SOL_DOWN 转换为 GPIO 电平 */
            if (action == 0U) SOL_UP(); else SOL_DOWN();
            CDD_USB_SendFrame(seq, cmd, CDD_USB_STS_OK, NULL, 0);
            break;
        }

        case CDD_USB_CMD_GET_SOL:
        {
            /* 返回动作值：0=升起，1=下落，而不是直接暴露 GPIO 电平 */
            uint8_t v = (EAL_GPIO_Get(EAL_GPIO_SIG_SOL) != 0U) ? 0U : 1U;
            CDD_USB_SendFrame(seq, cmd, CDD_USB_STS_OK, &v, 1);
            break;
        }

        case CDD_USB_CMD_GET_ALL_PWR:
        {
            uint8_t v = 0;
            if (EAL_GPIO_Get(EAL_GPIO_SIG_ECU_PWR_DRV)) v |= 0x01U;
            if (EAL_GPIO_Get(EAL_GPIO_SIG_IGN))         v |= 0x02U;
            if (EAL_GPIO_Get(EAL_GPIO_SIG_KEY_PWR_DRV)) v |= 0x04U;
            CDD_USB_SendFrame(seq, cmd, CDD_USB_STS_OK, &v, 1);
            break;
        }

        default:
            CDD_USB_SendFrame(seq, cmd, CDD_USB_STS_ERR_CMD, NULL, 0);
            break;
    }
}

void ASW_PwrCtrl_Task(void)
{
    /* 当前无周期任务（电源仅事件驱动）*/
}
