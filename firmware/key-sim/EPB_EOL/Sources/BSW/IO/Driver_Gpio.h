/*
 * Driver_Gpio.h
 *
 * 20 Key GPIO Driver for KEY_SIM Project
 * 芯片: MC9S08DZ32
 *
 */

#ifndef DRIVER_GPIO_H_
#define DRIVER_GPIO_H_

/* ============================================
 * KEY 引脚定义
 * PTADD: Port A 方向寄存器, PTAD: Port A 数据寄存器
 * 其他端口同理
 * ============================================ */

/* KEY1=PTA0, KEY3=PTA1, KEY4=PTA2, KEY5=PTA3, KEY6=PTA4, KEY7=PTA5, KEY8=PTA6, KEY13=PTA7 */
/* KEY2=PTB1, KEY19=PTB4, KEY20=PTB6 */
/* KEY9=PTD3, KEY10=PTD4, KEY11=PTD2, KEY12=PTD5, KEY17=PTD0, KEY18=PTD1 */
/* KEY14=PTE0, KEY15=PTE1, KEY16=PTE2 */

/* ============================================
 * 函数声明
 * ============================================ */
void Drv_Gpio_Init(void);
void Drv_Gpio_AllKeyHigh(void); /* Legacy name: drives all keys ON (low). */

/* ============================================
 * 单个 KEY 操作（直接宏，不抽象）
 * ============================================ */
#define KEY1_ON()    (PTAD_PTAD0 = 0)
#define KEY1_OFF()   (PTAD_PTAD0 = 1)

#define KEY2_ON()    (PTBD_PTBD1 = 0)
#define KEY2_OFF()   (PTBD_PTBD1 = 1)

#define KEY3_ON()    (PTAD_PTAD1 = 0)
#define KEY3_OFF()   (PTAD_PTAD1 = 1)

#define KEY4_ON()    (PTAD_PTAD2 = 0)
#define KEY4_OFF()   (PTAD_PTAD2 = 1)

#define KEY5_ON()    (PTAD_PTAD3 = 0)
#define KEY5_OFF()   (PTAD_PTAD3 = 1)

#define KEY6_ON()    (PTAD_PTAD4 = 0)
#define KEY6_OFF()   (PTAD_PTAD4 = 1)

#define KEY7_ON()    (PTAD_PTAD5 = 0)
#define KEY7_OFF()   (PTAD_PTAD5 = 1)

#define KEY8_ON()    (PTAD_PTAD6 = 0)
#define KEY8_OFF()   (PTAD_PTAD6 = 1)

#define KEY9_ON()    (PTDD_PTDD3 = 0)
#define KEY9_OFF()   (PTDD_PTDD3 = 1)

#define KEY10_ON()   (PTDD_PTDD4 = 0)
#define KEY10_OFF()  (PTDD_PTDD4 = 1)

#define KEY11_ON()   (PTDD_PTDD2 = 0)
#define KEY11_OFF()  (PTDD_PTDD2 = 1)

#define KEY12_ON()   (PTDD_PTDD5 = 0)
#define KEY12_OFF()  (PTDD_PTDD5 = 1)

#define KEY13_ON()   (PTAD_PTAD7 = 0)
#define KEY13_OFF()  (PTAD_PTAD7 = 1)

#define KEY14_ON()   (PTED_PTED0 = 0)
#define KEY14_OFF()  (PTED_PTED0 = 1)

#define KEY15_ON()   (PTED_PTED1 = 0)
#define KEY15_OFF()  (PTED_PTED1 = 1)

#define KEY16_ON()   (PTED_PTED2 = 0)
#define KEY16_OFF()  (PTED_PTED2 = 1)

#define KEY17_ON()   (PTDD_PTDD0 = 0)
#define KEY17_OFF()  (PTDD_PTDD0 = 1)

#define KEY18_ON()   (PTDD_PTDD1 = 0)
#define KEY18_OFF()  (PTDD_PTDD1 = 1)

#define KEY19_ON()   (PTBD_PTBD4 = 0)
#define KEY19_OFF()  (PTBD_PTBD4 = 1)

#define KEY20_ON()   (PTBD_PTBD6 = 0)
#define KEY20_OFF()  (PTBD_PTBD6 = 1)

#endif /* DRIVER_GPIO_H_ */
