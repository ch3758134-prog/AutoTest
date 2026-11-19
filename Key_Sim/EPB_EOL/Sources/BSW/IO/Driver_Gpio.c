/*
 * Driver_Gpio.c
 *
 * 20 Key GPIO Driver for KEY_SIM Project
 * 芯片: MC9S08DZ32
 *
 */

#include "sys_include.h"
#include "Driver_Gpio.h"

void Drv_Gpio_Init(void)
{
    /* Port A 方向: KEY1,3,4,5,6,7,8,13 全部输出 */
    PTADD_PTADD0 = 1;
    PTADD_PTADD1 = 1;
    PTADD_PTADD2 = 1;
    PTADD_PTADD3 = 1;
    PTADD_PTADD4 = 1;
    PTADD_PTADD5 = 1;
    PTADD_PTADD6 = 1;
    PTADD_PTADD7 = 1;

    /* Port B 方向: KEY2,19,20 输出 */
    PTBDD_PTBDD1 = 1;
    PTBDD_PTBDD4 = 1;
    PTBDD_PTBDD6 = 1;

    /* Port D 方向: KEY9,10,11,12,17,18 输出 */
    PTDDD_PTDDD0 = 1;
    PTDDD_PTDDD1 = 1;
    PTDDD_PTDDD2 = 1;
    PTDDD_PTDDD3 = 1;
    PTDDD_PTDDD4 = 1;
    PTDDD_PTDDD5 = 1;

    /* Port E 方向: KEY14,15,16 输出 */
    PTEDD_PTEDD0 = 1;
    PTEDD_PTEDD1 = 1;
    PTEDD_PTEDD2 = 1;

    /* 所有 KEY 默认关闭 */
    KEY1_OFF();
    KEY2_OFF();
    KEY3_OFF();
    KEY4_OFF();
    KEY5_OFF();
    KEY6_OFF();
    KEY7_OFF();
    KEY8_OFF();
    KEY9_OFF();
    KEY10_OFF();
    KEY11_OFF();
    KEY12_OFF();
    KEY13_OFF();
    KEY14_OFF();
    KEY15_OFF();
    KEY16_OFF();
    KEY17_OFF();
    KEY18_OFF();
    KEY19_OFF();
    KEY20_OFF();
}

void Drv_Gpio_AllKeyHigh(void)
{
    KEY1_ON();
    KEY2_ON();
    KEY3_ON();
    KEY4_ON();
    KEY5_ON();
    KEY6_ON();
    KEY7_ON();
    KEY8_ON();
    KEY9_ON();
    KEY10_ON();
    KEY11_ON();
    KEY12_ON();
    KEY13_ON();
    KEY14_ON();
    KEY15_ON();
    KEY16_ON();
    KEY17_ON();
    KEY18_ON();
    KEY19_ON();
    KEY20_ON();
}
