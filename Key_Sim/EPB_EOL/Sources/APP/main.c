#include "sys_include.h"
#include "Driver_Gpio.h"
#include "KeySim.h"

void main(void)
{
    CanMsgInf msg;
    sys_clk_Init();
    PE_low_level_init();
    Drv_Gpio_Init();
    Drv_CAN_Init();
    Drv_CAN_Enable();
    for (;;) {
        while (Drv_CAN_ReceiveFrame(&msg)) {
            (void)KeySim_ProcessFrame(&msg);
        }
    }
}
