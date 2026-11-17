/**
 * @file ASW_PwrCtrl.c
 * @brief TestCtr V1.5 power/GPIO operations, Air_ALVE1/2 review candidate.
 *
 * Air outputs: U23 BTS724G OUT4/OUT3, from U7 PTB1/PTC2.
 * Mechanical clamp/release correspondence is NOT established.
 * Live energization is disabled by default. The source change is not
 * a license to flash an unknown physical assembly.
 */
#include "ASW_PwrCtrl.h"
#include "EAL_GPIO.h"
#include "EAL_USB.h"
#include <stddef.h>

/* MUST NOT be changed until hardware revision, wiring, output polarity,
 * mechanical valve action and field test procedure have been approved. */
#define AIR_LIVE_OUTPUT_AUTHORIZED (0U)

static uint8_t s_air_selected = 0U; /* 0=off;1=Air_ALVE1;2=Air_ALVE2 */

static void send_status(uint16_t seq, uint8_t cmd, uint8_t status)
{
    CDD_USB_SendFrame(seq, cmd, status, NULL, 0U);
}

/* Disable Air channels before cutting the SOL24IN feed. */
static void air_off(void)
{
    EAL_GPIO_Set(EAL_GPIO_SIG_AIR_ALVE1, 0U);
    EAL_GPIO_Set(EAL_GPIO_SIG_AIR_ALVE2, 0U);
    EAL_GPIO_Set(EAL_GPIO_SIG_RL_DRV, 0U);
    s_air_selected = 0U;
}

static uint8_t valid_air_select(const uint8_t *payload, uint8_t len)
{
    return (len == 1U && payload != NULL && payload[0] <= 2U) ? 1U : 0U;
}

/* NOT a mechanical action command: 1 and 2 select the raw board outputs.
 * Both-on (3) is intentionally rejected. Switching removes power first.
 * There is no implicit mapping of historical SOL up/down semantics. */
static void handle_set_air(uint16_t seq, uint8_t cmd,
                           const uint8_t *payload, uint8_t len)
{
    uint8_t select;
    if (!valid_air_select(payload, len)) {
        send_status(seq, cmd, CDD_USB_STS_ERR_FMT);
        return;
    }
    select = payload[0];
    if (select == 0U) {
        air_off();
        send_status(seq, cmd, CDD_USB_STS_OK);
        return;
    }
    if (AIR_LIVE_OUTPUT_AUTHORIZED == 0U ||
        !EAL_USB_IsEnumReady() ||
        !EAL_GPIO_IsSupported(EAL_GPIO_SIG_RL_DRV) ||
        !EAL_GPIO_IsSupported(EAL_GPIO_SIG_AIR_ALVE1) ||
        !EAL_GPIO_IsSupported(EAL_GPIO_SIG_AIR_ALVE2)) {
        send_status(seq, cmd, CDD_USB_STS_ERR_HW);
        return;
    }

    /* No overlap of Air1 and Air2. First disable outputs and power. */
    air_off();
    EAL_GPIO_Set(EAL_GPIO_SIG_RL_DRV, 1U);
    if (select == 1U)
        EAL_GPIO_Set(EAL_GPIO_SIG_AIR_ALVE1, 1U);
    else
        EAL_GPIO_Set(EAL_GPIO_SIG_AIR_ALVE2, 1U);
    s_air_selected = select;
    send_status(seq, cmd, CDD_USB_STS_OK);
}

static void handle_get_air(uint16_t seq, uint8_t cmd,
                           const uint8_t *payload, uint8_t len)
{
    uint8_t state[4];
    (void)payload;
    if (len != 0U) {
        send_status(seq, cmd, CDD_USB_STS_ERR_FMT);
        return;
    }
    /* State is commanded GPIO/readback, not proof of voltage or valve position. */
    state[0] = s_air_selected;
    state[1] = EAL_GPIO_Get(EAL_GPIO_SIG_AIR_ALVE1);
    state[2] = EAL_GPIO_Get(EAL_GPIO_SIG_AIR_ALVE2);
    state[3] = EAL_GPIO_Get(EAL_GPIO_SIG_RL_DRV);
    CDD_USB_SendFrame(seq, cmd, CDD_USB_STS_OK, state, 4U);
}

void ASW_PwrCtrl_HandleCmd(uint16_t seq, uint8_t cmd,
                           const uint8_t *payload, uint8_t len)
{
    switch (cmd) {
        case CDD_USB_CMD_SET_AIR_RAW:
            handle_set_air(seq, cmd, payload, len);
            break;
        case CDD_USB_CMD_GET_AIR_RAW:
            handle_get_air(seq, cmd, payload, len);
            break;
        /* Historical mappings are unresolved with TestCtr V1.5.
         * Reject rather than energize unrelated SOLxIN channels or
         * claim that old SET_SOL = mechanical clamp/release. */
        case CDD_USB_CMD_SET_ECU_PWR:
        case CDD_USB_CMD_GET_ECU_PWR:
        case CDD_USB_CMD_SET_IGN:
        case CDD_USB_CMD_GET_IGN:
        case CDD_USB_CMD_SET_KEYPWR:
        case CDD_USB_CMD_GET_KEYPWR:
        case CDD_USB_CMD_SET_SOL:
        case CDD_USB_CMD_GET_SOL:
        case CDD_USB_CMD_GET_ALL_PWR:
            (void)payload;
            (void)len;
            send_status(seq, cmd, CDD_USB_STS_ERR_HW);
            break;
        default:
            send_status(seq, cmd, CDD_USB_STS_ERR_CMD);
            break;
    }
}

void ASW_PwrCtrl_Task(void)
{
    /* A USB bus reset/disconnect removes output authorization. */
    if (!EAL_USB_IsEnumReady() && s_air_selected != 0U)
        air_off();
}
