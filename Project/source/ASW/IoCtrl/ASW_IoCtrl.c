/**
 * @file ASW_IoCtrl.c
 * @brief USB 对外 IGN(HOUT1)、六路 LED(HOUT2~HOUT7) 与两个独立夹具控制。
 *
 * 夹具1：PTB1 → U23 CH4 → Air_ALVE1 → J1 B5。
 * 夹具2：PTC2 → U23 CH3 → Air_ALVE2 → J1 B4。
 * 每一路的 0=释放、1=压紧；操作一路不改变另一路。
 * RL_DRV 是板内公共功率电路控制，不对 USB 暴露。
 * @warning OK 仅表示 MCU 已执行 GPIO 命令，不代表外部阀位。
 */
#include "ASW_IoCtrl.h"
#include "EAL_GPIO.h"
#include "EAL_USB.h"
#include <stddef.h>

/* bit0/bit1：各夹具当前压紧请求，不是实际机械位置。 */
static uint8_t s_clampRequestMask = 0U;

static void ReplyStatus(uint16_t seq, uint8_t cmd, uint8_t status)
{
    CDD_USB_SendFrame(seq, cmd, status, NULL, 0U);
}

static uint8_t ParseOnOff(const uint8_t *payload, uint8_t len, uint8_t *out)
{
    if ((payload == NULL) || (out == NULL) || (len != 1U) || (payload[0] > 1U))
        return 0U;
    *out = payload[0];
    return 1U;
}


/* HIGH 请求对应 HOUT 通道导通，LOW 请求关断；只证明 GPIO 指令。
 * USB index=1..6 顺序：RED1、YELLOW1、GREEN1、RED2、YELLOW2、GREEN2。
 */
static void SetLed(uint8_t index, uint8_t level)
{
    switch (index)
    {
        case 1U: EAL_GPIO_WRITE(LED_RED1, level); break;
        case 2U: EAL_GPIO_WRITE(LED_YELLOW1, level); break;
        case 3U: EAL_GPIO_WRITE(LED_GREEN1, level); break;
        case 4U: EAL_GPIO_WRITE(LED_RED2, level); break;
        case 5U: EAL_GPIO_WRITE(LED_YELLOW2, level); break;
        case 6U: EAL_GPIO_WRITE(LED_GREEN2, level); break;
        default: break; /* 上层已校验 1..6 */
    }
}

static uint8_t ReadLedMask(void)
{
    return (uint8_t)((EAL_GPIO_READ(LED_RED1) << 0U) |
                     (EAL_GPIO_READ(LED_YELLOW1) << 1U) |
                     (EAL_GPIO_READ(LED_GREEN1) << 2U) |
                     (EAL_GPIO_READ(LED_RED2) << 3U) |
                     (EAL_GPIO_READ(LED_YELLOW2) << 4U) |
                     (EAL_GPIO_READ(LED_GREEN2) << 5U));
}

static void WriteLedMask(uint8_t mask)
{
    /* 六路依次写入：不承诺跨 GPIOB/GPIOC 的硬件同时切换。 */
    SetLed(1U, (uint8_t)((mask >> 0U) & 1U));
    SetLed(2U, (uint8_t)((mask >> 1U) & 1U));
    SetLed(3U, (uint8_t)((mask >> 2U) & 1U));
    SetLed(4U, (uint8_t)((mask >> 3U) & 1U));
    SetLed(5U, (uint8_t)((mask >> 4U) & 1U));
    SetLed(6U, (uint8_t)((mask >> 5U) & 1U));
}

/** @brief 两只夹具请求释放；不切断内部 SOL24IN 共用功率母线。 */
static void ReleaseBothClamps(void)
{
    EAL_GPIO_WRITE(AIR_ALVE1, 0U);
    EAL_GPIO_WRITE(AIR_ALVE2, 0U);
    s_clampRequestMask = 0U;
}

/**
 * @brief 独立设置指定夹具。
 * @param clamp 1=夹具1(J1 B5)；2=夹具2(J1 B4)。
 * @param state 0=释放；1=压紧。
 * @note 仅写指定通道，不做两路互锁或自动状态切换。
 */
static void SetClamp(uint8_t clamp, uint8_t state)
{
    const uint8_t bit = (clamp == 1U) ? 0x01U : 0x02U;
    if (clamp == 1U)
        EAL_GPIO_WRITE(AIR_ALVE1, state);
    else
        EAL_GPIO_WRITE(AIR_ALVE2, state);
    if (state != 0U)
        s_clampRequestMask |= bit;
    else
        s_clampRequestMask &= (uint8_t)~bit;
}

void ASW_IoCtrl_HandleCmd(uint16_t seq, uint8_t cmd,
                          const uint8_t *payload, uint8_t len)
{
    uint8_t value;
    switch (cmd)
    {
        case CDD_USB_CMD_SET_IGN:
            if (ParseOnOff(payload, len, &value) == 0U)
            {
                ReplyStatus(seq, cmd, CDD_USB_STS_ERR_FMT);
                break;
            }
            EAL_GPIO_WRITE(IGN, value);
            ReplyStatus(seq, cmd, CDD_USB_STS_OK);
            break;

        case CDD_USB_CMD_GET_IGN:
            if (len != 0U)
            {
                ReplyStatus(seq, cmd, CDD_USB_STS_ERR_FMT);
                break;
            }
            value = EAL_GPIO_READ(IGN);
            CDD_USB_SendFrame(seq, cmd, CDD_USB_STS_OK, &value, 1U);
            break;

        case CDD_USB_CMD_SET_CLAMP:
            /* 两字节：[clamp(1/2), state(0释放/1压紧)]。 */
            if ((payload == NULL) || (len != 2U) ||
                (payload[0] < 1U) || (payload[0] > 2U) ||
                (payload[1] > 1U))
            {
                ReplyStatus(seq, cmd, CDD_USB_STS_ERR_FMT);
                break;
            }
            SetClamp(payload[0], payload[1]);
            ReplyStatus(seq, cmd, CDD_USB_STS_OK);
            break;

        case CDD_USB_CMD_GET_CLAMPS:
            if (len != 0U)
            {
                ReplyStatus(seq, cmd, CDD_USB_STS_ERR_FMT);
                break;
            }
            /* 仅 MCU 电平；bit0=夹具1、bit1=夹具2。 */
            value = (uint8_t)(EAL_GPIO_READ(AIR_ALVE1) |
                              (uint8_t)(EAL_GPIO_READ(AIR_ALVE2) << 1U));
            CDD_USB_SendFrame(seq, cmd, CDD_USB_STS_OK, &value, 1U);
            break;

        case CDD_USB_CMD_SET_LED:
            /* [index=1..6, level=0/1]；只改变指定 LED。 */
            if ((payload == NULL) || (len != 2U) ||
                (payload[0] < 1U) || (payload[0] > 6U) || (payload[1] > 1U))
            {
                ReplyStatus(seq, cmd, CDD_USB_STS_ERR_FMT);
                break;
            }
            SetLed(payload[0], payload[1]);
            ReplyStatus(seq, cmd, CDD_USB_STS_OK);
            break;

        case CDD_USB_CMD_GET_LEDS:
            if (len != 0U)
            {
                ReplyStatus(seq, cmd, CDD_USB_STS_ERR_FMT);
                break;
            }
            value = ReadLedMask();
            CDD_USB_SendFrame(seq, cmd, CDD_USB_STS_OK, &value, 1U);
            break;

        case CDD_USB_CMD_SET_LEDS:
            /* [mask] bit0..5=RED1、YELLOW1、GREEN1、RED2、YELLOW2、GREEN2；bit6/7必须为0。 */
            if ((payload == NULL) || (len != 1U) || ((payload[0] & 0xC0U) != 0U))
            {
                ReplyStatus(seq, cmd, CDD_USB_STS_ERR_FMT);
                break;
            }
            WriteLedMask(payload[0]);
            ReplyStatus(seq, cmd, CDD_USB_STS_OK);
            break;

        default:
            ReplyStatus(seq, cmd, CDD_USB_STS_ERR_CMD);
            break;
    }
}

/**
 * @brief USB 枚举丢失后撤销已有压紧请求。
 * @note 保留上一版本已存在的断连释放策略，尚待现场验证。
 *       不等于主机应用层心跳、气缸位置反馈或机械急停。
 */
void ASW_IoCtrl_Task(void)
{
    if ((s_clampRequestMask != 0U) && !EAL_USB_IsEnumReady())
        ReleaseBothClamps();
}
