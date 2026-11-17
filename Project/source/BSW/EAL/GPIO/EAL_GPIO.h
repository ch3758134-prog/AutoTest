/**
 * @file EAL_GPIO.h
 * @brief TestCtr V1.5 GPIO binding. Design schematics and firmware mappings
 *        are different evidence layers; only verified design bindings below
 *        are enabled in this candidate.
 */
#ifndef EAL_GPIO_H
#define EAL_GPIO_H
#include <stdint.h>
#include "MCAL/Gpio/MCAL_GPIO.h"
#ifdef __cplusplus
extern "C" {
#endif

/* TestCtr V1.5: U7 P70 PTC0 -> RL_DRV -> Q4 -> U2 BTS50055 -> SOL24IN. */
#define RL_DRV_PORT      MCAL_GPIO_C
#define RL_DRV_PIN       0U
/* U23 BTS724G OUT4 -> Air_ALVE1 -> J1 B5, IN4 <- SOL4IN <- U7 P54 PTB1. */
#define AIR_ALVE1_PORT   MCAL_GPIO_B
#define AIR_ALVE1_PIN    1U
/* U23 BTS724G OUT3 -> Air_ALVE2 -> J1 B4, IN3 <- SOL2IN <- U7 P72 PTC2. */
#define AIR_ALVE2_PORT   MCAL_GPIO_C
#define AIR_ALVE2_PIN    2U

typedef enum {
    EAL_GPIO_SIG_RL_DRV = 0,
    /* Legacy names remain addressable but are intentionally UNBOUND.
       Old code maps these to design nets SOL9IN, SOL10IN, SOL11IN, SOL6IN
       instead of independently verified KEY/ECU/IGN/actuator functions. */
    EAL_GPIO_SIG_KEY_PWR_DRV,
    EAL_GPIO_SIG_ECU_PWR_DRV,
    EAL_GPIO_SIG_IGN,
    EAL_GPIO_SIG_SOL,
    EAL_GPIO_SIG_AIR_ALVE1,
    EAL_GPIO_SIG_AIR_ALVE2,
    EAL_GPIO_SIG_COUNT
} EAL_GPIO_SignalType;

void EAL_GPIO_Init(void);
void EAL_GPIO_Config(MCAL_GPIO_PortType port, uint8_t pin);
uint8_t EAL_GPIO_IsSupported(EAL_GPIO_SignalType sig);
void EAL_GPIO_Set(EAL_GPIO_SignalType sig, uint8_t level);
uint8_t EAL_GPIO_Get(EAL_GPIO_SignalType sig);

/* Existing macros preserved for out-of-target legacy code. When unbound,
 * EAL_GPIO_Set is a no-op. ASW must return ERR_HW instead of claiming success. */
#define RL_DRV_HIGH()        EAL_GPIO_Set(EAL_GPIO_SIG_RL_DRV, 1U)
#define RL_DRV_LOW()         EAL_GPIO_Set(EAL_GPIO_SIG_RL_DRV, 0U)
#define KEY_PWR_DRV_HIGH()   EAL_GPIO_Set(EAL_GPIO_SIG_KEY_PWR_DRV, 1U)
#define KEY_PWR_DRV_LOW()    EAL_GPIO_Set(EAL_GPIO_SIG_KEY_PWR_DRV, 0U)
#define ECU_PWR_DRV_HIGH()   EAL_GPIO_Set(EAL_GPIO_SIG_ECU_PWR_DRV, 1U)
#define ECU_PWR_DRV_LOW()    EAL_GPIO_Set(EAL_GPIO_SIG_ECU_PWR_DRV, 0U)
#define IGN_HIGH()           EAL_GPIO_Set(EAL_GPIO_SIG_IGN, 1U)
#define IGN_LOW()            EAL_GPIO_Set(EAL_GPIO_SIG_IGN, 0U)
#define SOL_HIGH()           EAL_GPIO_Set(EAL_GPIO_SIG_SOL, 1U)
#define SOL_LOW()            EAL_GPIO_Set(EAL_GPIO_SIG_SOL, 0U)
#define SOL_UP()             SOL_HIGH()
#define SOL_DOWN()           SOL_LOW()

#ifdef __cplusplus
}
#endif
#endif
