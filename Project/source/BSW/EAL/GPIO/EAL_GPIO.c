/**
 * @file EAL_GPIO.c
 * @brief Safe schematic-based binding for TestCtr V1.5 review candidate.
 * Only PTB1/PTC2 Air high-side inputs and PTC0 master high-side supply control
 * are bound. Legacy KEY/ECU/IGN/SOL aliases are not assigned by guessing.
 */
#include "EAL_GPIO.h"
#include "MCAL/Gpio/MCAL_GPIO.h"

typedef struct {
    MCAL_GPIO_PortType port;
    uint8_t pin;
    uint8_t supported;
    uint8_t initial_level;
} EAL_GPIO_Binding;

/* All supported outputs initially LOW, before USB/CAN start.
 * RL_DRV=LOW disconnects U2's high-side SOL24IN feed in the drawing.
 * AIR1/2=LOW disables both BTS724G inputs. Actual installed wiring UNKNOWN. */
static const EAL_GPIO_Binding s_binding[EAL_GPIO_SIG_COUNT] = {
    { MCAL_GPIO_C, 0U, 1U, 0U }, /* RL_DRV: U7 P70 -> U2 enable path */
    { MCAL_GPIO_C, 0U, 0U, 0U }, /* KEY_PWR_DRV: UNBOUND */
    { MCAL_GPIO_C, 0U, 0U, 0U }, /* ECU_PWR_DRV: UNBOUND */
    { MCAL_GPIO_C, 0U, 0U, 0U }, /* IGN: UNBOUND */
    { MCAL_GPIO_C, 0U, 0U, 0U }, /* SOL (old direct action): UNBOUND */
    { MCAL_GPIO_B, 1U, 1U, 0U }, /* Air_ALVE1: U7 P54 -> U23 IN4 */
    { MCAL_GPIO_C, 2U, 1U, 0U }, /* Air_ALVE2: U7 P72 -> U23 IN3 */
};

uint8_t EAL_GPIO_IsSupported(EAL_GPIO_SignalType sig)
{
    if ((uint32_t)sig >= (uint32_t)EAL_GPIO_SIG_COUNT) return 0U;
    return s_binding[sig].supported;
}

void EAL_GPIO_Config(MCAL_GPIO_PortType port, uint8_t pin)
{
    MCAL_GPIO_Init(port, pin, MCAL_GPIO_OUT, 0U);
}

void EAL_GPIO_Init(void)
{
    uint8_t i;
    /* Air channels first, master supply last to minimize startup activation. */
    EAL_GPIO_Config(AIR_ALVE1_PORT, AIR_ALVE1_PIN);
    EAL_GPIO_Config(AIR_ALVE2_PORT, AIR_ALVE2_PIN);
    for (i = 0U; i < EAL_GPIO_SIG_COUNT; ++i) {
        if (s_binding[i].supported != 0U &&
            i != (uint8_t)EAL_GPIO_SIG_AIR_ALVE1 &&
            i != (uint8_t)EAL_GPIO_SIG_AIR_ALVE2) {
            MCAL_GPIO_Init(s_binding[i].port, s_binding[i].pin,
                           MCAL_GPIO_OUT, s_binding[i].initial_level);
        }
    }
}

void EAL_GPIO_Set(EAL_GPIO_SignalType sig, uint8_t level)
{
    if (!EAL_GPIO_IsSupported(sig)) return;
    MCAL_GPIO_Write(s_binding[sig].port, s_binding[sig].pin,
                    (level != 0U) ? 1U : 0U);
}

uint8_t EAL_GPIO_Get(EAL_GPIO_SignalType sig)
{
    if (!EAL_GPIO_IsSupported(sig)) return 0U;
    return MCAL_GPIO_Read(s_binding[sig].port, s_binding[sig].pin);
}
