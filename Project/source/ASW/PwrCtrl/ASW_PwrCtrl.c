/**
 * @file    ASW_PwrCtrl.c
 * @brief   USB 板级电源、IGN 与 Air_ALVE 原始电气输出控制。
 *
 * @details
 * 电源/IGN 保持历史协议操作行为；气阀改用 U23 的 Air_ALVE1/2
 * 两个独立 GPIO 输入。ASW 只决定电气控制请求，不在此猜测
 * Air_ALVE1/2 分别代表压紧或释放。
 *
 * 原理图电源依赖：PTC0/RL_DRV → Q4 → U2 → SOL24IN；
 * U5、U22、U23 均使用这一路共用高边电源。
 *
 * @warning 返回 CDD_USB_STS_OK 仅表示本次 GPIO 操作已执行，
 *          不代表母线电压、高边输出、阀位或气缸动作得到证实。
 */
#include "ASW_PwrCtrl.h"
#include "EAL_GPIO.h"
#include "EAL_USB.h"
#include <stddef.h>

/* 记录本固件请求的电气状态（非现场测量值）。 */
static uint8_t s_airRequest = 0U;

/** @brief 将业务命令应答状态写回 USB 协议层。 */
static void ReplyStatus(uint16_t seq, uint8_t cmd, uint8_t status)
{
    CDD_USB_SendFrame(seq, cmd, status, NULL, 0U);
}

/**
 * @brief 校验旧电源/IGN 命令的单字节 0/1 参数。
 * @return 1U 表示有效；0U 表示空指针、长度或取值错误。
 */
static uint8_t ParseOnOff(const uint8_t *payload, uint8_t len, uint8_t *out)
{
    if ((payload == NULL) || (out == NULL) || (len != 1U) || (payload[0] > 1U))
        return 0U;
    *out = payload[0];
    return 1U;
}

/**
 * @brief 同时撤销两路 Air 高边的 GPIO 导通请求。
 * @note  不修改 RL_DRV，因为 SOL24IN 还为其它高边通道供电。
 */
static void AirOff(void)
{
    EAL_GPIO_WRITE(AIR_ALVE1, 0U);
    EAL_GPIO_WRITE(AIR_ALVE2, 0U);
    s_airRequest = 0U;
}

/**
 * @brief 设置 Air 原始电气通道。
 * @param selected 0=两路关闭，1=仅 Air_ALVE1，2=仅 Air_ALVE2。
 *
 * @details
 * 先关闭另一通道再开启目标通道，避免软件同时请求两路高电平。
 * 无额外气动死区或动作定时；不能将该顺序视为机械互锁。
 * 请求非零输出前必须已通过 RL_DRV 请求使能共用 SOL24IN。
 *
 * @warning Air1/Air2 的气缸压紧/释放方向、线束和负载参数未验证。
 *          本函数不自动修改 SOL24IN 共享供电，也不验证实际电流。
 */
static uint8_t SetAirElectrical(uint8_t selected)
{
    if ((selected > 2U) ||
        ((selected != 0U) && (EAL_GPIO_READ(RL_DRV) == 0U)))
        return 0U;

    if (selected == 1U)
    {
        EAL_GPIO_WRITE(AIR_ALVE2, 0U);
        EAL_GPIO_WRITE(AIR_ALVE1, 1U);
    }
    else if (selected == 2U)
    {
        EAL_GPIO_WRITE(AIR_ALVE1, 0U);
        EAL_GPIO_WRITE(AIR_ALVE2, 1U);
    }
    else
    {
        AirOff();
    }

    s_airRequest = selected;
    return 1U;
}

/**
 * @brief 处理 USB 命令帧。
 * @param seq     请求序号，按原协议回显。
 * @param cmd     CDD_USB_CMD_* 命令字。
 * @param payload 指令数据，允许 NULL（无负载的查询命令）。
 * @param len     payload 字节数。
 * @note  GET 指令读取 MCU GPIO 输入电平，不测量24V负载、机械位置。
 */
void ASW_PwrCtrl_HandleCmd(uint16_t seq, uint8_t cmd,
                          const uint8_t *payload, uint8_t len)
{
    uint8_t value;
    switch (cmd)
    {
        case CDD_USB_CMD_SET_ECU_PWR:
            if (ParseOnOff(payload, len, &value) == 0U)
            {
                ReplyStatus(seq, cmd, CDD_USB_STS_ERR_FMT);
                break;
            }
            if (value != 0U)
            {
                /* 历史行为：先请求共用母线，再请求 ECU 供电输出。 */
                EAL_GPIO_WRITE(RL_DRV, 1U);
                EAL_GPIO_WRITE(ECU_PWR_DRV, 1U);
            }
            else
            {
                /* 母线将断开时撤销 Air 请求，避免以后恢复母线时误上电。 */
                AirOff();
                EAL_GPIO_WRITE(ECU_PWR_DRV, 0U);
                EAL_GPIO_WRITE(RL_DRV, 0U);
            }
            ReplyStatus(seq, cmd, CDD_USB_STS_OK);
            break;

        case CDD_USB_CMD_GET_ECU_PWR:
            value = EAL_GPIO_READ(ECU_PWR_DRV);
            CDD_USB_SendFrame(seq, cmd, CDD_USB_STS_OK, &value, 1U);
            break;

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
            value = EAL_GPIO_READ(IGN);
            CDD_USB_SendFrame(seq, cmd, CDD_USB_STS_OK, &value, 1U);
            break;

        case CDD_USB_CMD_SET_KEYPWR:
            if (ParseOnOff(payload, len, &value) == 0U)
            {
                ReplyStatus(seq, cmd, CDD_USB_STS_ERR_FMT);
                break;
            }
            EAL_GPIO_WRITE(KEY_PWR_DRV, value);
            ReplyStatus(seq, cmd, CDD_USB_STS_OK);
            break;

        case CDD_USB_CMD_GET_KEYPWR:
            value = EAL_GPIO_READ(KEY_PWR_DRV);
            CDD_USB_SendFrame(seq, cmd, CDD_USB_STS_OK, &value, 1U);
            break;

        case CDD_USB_CMD_SET_AIR_ALVE:
            if ((len != 1U) || (payload == NULL) || (payload[0] > 2U))
            {
                ReplyStatus(seq, cmd, CDD_USB_STS_ERR_FMT);
                break;
            }
            if (SetAirElectrical(payload[0]) == 0U)
            {
                /* 共享母线被关断时拒绝非零输出，不能承诺动作成功。 */
                ReplyStatus(seq, cmd, CDD_USB_STS_ERR_HW);
                break;
            }
            ReplyStatus(seq, cmd, CDD_USB_STS_OK);
            break;

        case CDD_USB_CMD_GET_AIR_ALVE:
            if (len != 0U)
            {
                ReplyStatus(seq, cmd, CDD_USB_STS_ERR_FMT);
                break;
            }
            /* bit0=Air1 MCU Pad电平；bit1=Air2 MCU Pad电平。 */
            value = (uint8_t)(EAL_GPIO_READ(AIR_ALVE1) |
                      (uint8_t)(EAL_GPIO_READ(AIR_ALVE2) << 1U));
            CDD_USB_SendFrame(seq, cmd, CDD_USB_STS_OK, &value, 1U);
            break;

        case CDD_USB_CMD_SET_SOL:
        case CDD_USB_CMD_GET_SOL:
            /* 旧0x16/0x17表示机械升起/压紧；未验证它与Air1/2的映射。
             * 明确拒绝，避免旧HOST无声切换到另一组物理输出。 */
            ReplyStatus(seq, cmd, CDD_USB_STS_ERR_HW);
            break;

        case CDD_USB_CMD_GET_ALL_PWR:
            value = 0U;
            if (EAL_GPIO_READ(ECU_PWR_DRV) != 0U) value |= 0x01U;
            if (EAL_GPIO_READ(IGN) != 0U)         value |= 0x02U;
            if (EAL_GPIO_READ(KEY_PWR_DRV) != 0U) value |= 0x04U;
            CDD_USB_SendFrame(seq, cmd, CDD_USB_STS_OK, &value, 1U);
            break;

        default:
            ReplyStatus(seq, cmd, CDD_USB_STS_ERR_CMD);
            break;
    }
}

/**
 * @brief  USB 离线时撤销 Air 导通请求。
 * @note   仅能检测 EAL_USB 枚举状态；不能检测主机程序卡死但 USB
 *         枚举仍有效的情况，亦不能保证掉电瞬间的物理气阀状态。
 */
void ASW_PwrCtrl_Task(void)
{
    if ((s_airRequest != 0U) && !EAL_USB_IsEnumReady())
        AirOff();
}
