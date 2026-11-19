#ifndef __Timer_H
#define __Timer_H


void Timer_1ms_Init(void);
ISR(Timer_1ms_Interrupt);
void wait_ms(u16 ms);
extern u16 sys_timer_ms;                /* 1ms 递增系统计数（16 位回绕） */
extern u8 timer_1ms_flag;
extern u8 timer_10ms_flag;
extern u8 timer_100ms_flag;
extern u8 timer_1000ms_flag;
#endif
