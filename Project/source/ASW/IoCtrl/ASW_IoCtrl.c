/**
 * @file ASW_IoCtrl.c
 * @brief USB 对外 IGN(HOUT1)、六路 LED(HOUT2~HOUT7) 与两个独立夹具控制。
 *
 * 夹具1：PTB1 → U23 CH4 → Air_ALVE1 → J1 B5。
 * 夹具2：PTC2 → U23 CH3 → Air_ALVE2 → J1 B4。
 * 每一路的 0=释放、1=压紧；操作一路不改变另一路。
 * RL_DRV 是板内公共功率电路控制，不对 USB 暴露。
 * 启动时所有输出请求和 RL_DRV 均为 OFF；任何有效 ON 请求先使能
 * RL_DRV，再打开对应输出；最后一个 OFF 请求完成后关闭 RL_DRV。
 * HOUT9/HOUT10 与 U22 CH4 未分配通道始终维持上电默认 LOW。
 * @warning OK 仅表示 MCU 已执行 GPIO 命令，不代表外部阀位。
 */
#include "ASW_IoCtrl.h"
#include "EAL_GPIO.h"
#include "EAL_USB.h"
#include <stddef.h>

/* bit0/bit1：各夹具当前压紧请求，不是实际机械位置。 */
static uint8_t s_clampRequestMask = 0U;

/* 只跟踪此 ASW 模块有权管理的 9 路输出请求（不是机械/电压反馈）。
 * bit0 IGN; bit1..6 LED1..6; bit7/8 夹具1/2。
 * 未分配的 HOUT9/10、U22 CH4 始终保持 GPIO LOW。
 */
#define IO_REQ_IGN       ((uint16_t)0x001U)
#define IO_REQ_LEDS      ((uint16_t)0x07EU)
#define IO_REQ_CLAMP1    ((uint16_t)0x080U)
#define IO_REQ_CLAMP2    ((uint16_t)0x100U)
static uint16_t s_outputRequestMask = 0U;

/* 只在业务显式要求开启输出时使能共用母线，非 USB 自由控制项。 */
static void PrepareOutputRequest(uint8_t level)
{
    if (level != 0U)
        EAL_GPIO_WRITE(RL_DRV, 1U);
}

/* 先关闭指定通道，再决定是否可以关闭公共母线；不误断其它路。 */
static void FinishOutputRequest(uint16_t bit, uint8_t level)
{
    if (level != 0U)
        s_outputRequestMask |= bit;
    else
        s_outputRequestMask &= (uint16_t)~bit;

    if (s_outputRequestMask == 0U)
        EAL_GPIO_WRITE(RL_DRV, 0U);
}

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


/* HIGH 请求对应 HOUT 通道导通，LOW 请求关断；只证明 GPIO 指令。 */
static void WriteLedGpio(uint8_t index, uint8_t level)
{
    switch (index)
    {
        case 1U: EAL_GPIO_WRITE(LED1, level); break;
        case 2U: EAL_GPIO_WRITE(LED2, level); break;
        case 3U: EAL_GPIO_WRITE(LED3, level); break;
        case 4U: EAL_GPIO_WRITE(LED4, level); break;
        case 5U: EAL_GPIO_WRITE(LED5, level); break;
        case 6U: EAL_GPIO_WRITE(LED6, level); break;
        default: break; /* 上层已校验 1..6 */
    }
}

static uint8_t ReadLedMask(void)
{
    return (uint8_t)((EAL_GPIO_READ(LED1) << 0U) |
                     (EAL_GPIO_READ(LED2) << 1U) |
                     (EAL_GPIO_READ(LED3) << 2U) |
                     (EAL_GPIO_READ(LED4) << 3U) |
                     (EAL_GPIO_READ(LED5) << 4U) |
                     (EAL_GPIO_READ(LED6) << 5U));
}

static void SetLed(uint8_t index, uint8_t level)
{
    const uint16_t bit = (uint16_t)(1U << index); /* index 1..6 */
    PrepareOutputRequest(level);
    WriteLedGpio(index, level);
    FinishOutputRequest(bit, level);
}

static void WriteLedMask(uint8_t mask)
{
    /* 组命令一次计算最终状态，避免逐路写入期间公共母线反复开关。
     * GPIO 实际依次更新，不承诺跨 GPIOB/GPIOC 的硬件同时切换。
     */
    if (mask != 0U)
        PrepareOutputRequest(1U);
    WriteLedGpio(1U, (uint8_t)((mask >> 0U) & 1U));
    WriteLedGpio(2U, (uint8_t)((mask >> 1U) & 1U));
    WriteLedGpio(3U, (uint8_t)((mask >> 2U) & 1U));
    WriteLedGpio(4U, (uint8_t)((mask >> 3U) & 1U));
    WriteLedGpio(5U, (uint8_t)((mask >> 4U) & 1U));
    WriteLedGpio(6U, (uint8_t)((mask >> 5U) & 1U));
    s_outputRequestMask = (uint16_t)((s_outputRequestMask & (uint16_t)~IO_REQ_LEDS) |
                                      ((uint16_t)mask << 1U));
    if (s_outputRequestMask == 0U)
        EAL_GPIO_WRITE(RL_DRV, 0U);
}

/** @brief 两只夹具请求关闭；只有其它通道也关闭才关公共母线。 */
static void ReleaseBothClamps(void)
{
    EAL_GPIO_WRITE(AIR_ALVE1, 0U);
    EAL_GPIO_WRITE(AIR_ALVE2, 0U);
    s_clampRequestMask = 0U;
    s_outputRequestMask &= (uint16_t)~(IO_REQ_CLAMP1 | IO_REQ_CLAMP2);
    if (s_outputRequestMask == 0U)
        EAL_GPIO_WRITE(RL_DRV, 0U);
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
    const uint16_t reqBit = (clamp == 1U) ? IO_REQ_CLAMP1 : IO_REQ_CLAMP2;
    PrepareOutputRequest(state);
    if (clamp == 1U)
        EAL_GPIO_WRITE(AIR_ALVE1, state);
    else
        EAL_GPIO_WRITE(AIR_ALVE2, state);
    if (state != 0U)
        s_clampRequestMask |= bit;
    else
        s_clampRequestMask &= (uint8_t)~bit;
    FinishOutputRequest(reqBit, state);
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
            PrepareOutputRequest(value);
            EAL_GPIO_WRITE(IGN, value);
            FinishOutputRequest(IO_REQ_IGN, value);
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
            /* [mask] bit0..5=LED1..LED6；bit6/7必须为0。 */
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
