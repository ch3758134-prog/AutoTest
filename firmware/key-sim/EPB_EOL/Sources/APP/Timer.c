#include "sys_include.h"

#define TI1_SetCV(_Val) \
  (RTCMOD = (byte)(_Val))

u16 sys_timer_ms;
u16 timer_count_10ms;
u16 timer_count_100ms;
u16 timer_count_1000ms;
u16 timer_wait_ms_counter;

u8 timer_1ms_flag;
u8 timer_10ms_flag;
u8 timer_100ms_flag;
u8 timer_1000ms_flag;
 u16 sys_timer_return(void)
  {
  
       return sys_timer_ms ;
 }

void Timer_1ms_Init(void)
{
	setReg8(TPM1SC, 0x48); //允许计数溢出中断，总线时钟源
    TPM1MOD = 0x00;//设置计数器初值
   //setReg8(TPM1C0SC, 0x00);
    setReg8(TPM1CNTH, 0x00);//设置计数器高8位
   //TPM1C0V = (word)(0x1F40);
    TPM1MOD = (word)(0x3E80); //设置计数器初值 
	timer_1ms_flag =0; //初始化1ms定时器标志位
	timer_count_10ms =0; //初始化10ms计数器
	timer_count_100ms =0; //初始化100ms计数器
	timer_count_1000ms =0; //初始化1000ms计数器
	timer_wait_ms_counter =0; //初始化等待毫秒计数器
}


ISR(Timer_1ms_Interrupt)
{
  clrReg8Bits(TPM1SC, 0x80);
  timer_1ms_flag =1; 
  timer_count_10ms++;
  timer_count_100ms++;
  timer_count_1000ms++;
  sys_timer_ms++; 
  timer_wait_ms_counter ++;
  
  if(timer_count_10ms >= 10) 
  {
	  timer_10ms_flag =1;
	  timer_count_10ms =0;
  }
  if(timer_count_100ms >= 100) 
  {
	  timer_100ms_flag =1;
	  timer_count_100ms =0;
  }
  if(timer_count_1000ms >= 1000)
  {
	  timer_1000ms_flag =1;
	  timer_count_1000ms =0;
  }
}

void wait_ms(u16 ms)
{
	timer_wait_ms_counter = 0;
	while(timer_wait_ms_counter<ms);
}
