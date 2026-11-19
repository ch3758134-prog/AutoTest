#include <stddef.h>
#include "derivative.h"     /* include peripheral declarations */
#include "usb.h"

void USB_Init(void)
{
	
	    /* disable flash protect */
	FMC->PFAPR |= (FMC_PFAPR_M4AP_MASK);
    
    /* enable USB reguator */
	SIM->SOPT1 |= SIM_SOPT1_USBREGEN_MASK;
    
    /* disable memory protection */
  MPU->CESR=0;
	
#if 1	
	SIM->SOPT2 |= SIM_SOPT2_USBSRC_MASK;  /* Divided PllFll clock */
  /* SIM_CLKDIV2: USBDIV=1,USBFRAC=0 */
  SIM->CLKDIV2 = (uint32_t)((SIM->CLKDIV2 & (uint32_t)~(uint32_t)(
                 SIM_CLKDIV2_USBDIV(0x06) |
                 SIM_CLKDIV2_USBFRAC_MASK
                )) | (uint32_t)(
                 SIM_CLKDIV2_USBDIV(0x01)
                ));                    /* Div=2 - Mult=1  */
  /* Enable module clock */
  /* SIM_SCGC4: USBOTG=1 */
  SIM->SCGC4 |= SIM_SCGC4_USBOTG_MASK;
#endif
#if 0
	SIM->SOPT2   |=   SIM_SOPT2_USBSRC_MASK     | /* MCGPLLCLK used as src      */
                    SIM_SOPT2_PLLFLLSEL_MASK  ; /* Select MCGPLLCLK as clock  */
  SIM->CLKDIV2 &= ~(SIM_CLKDIV2_USBFRAC_MASK  | /* Clear CLKDIV2 FS values    */
                    SIM_CLKDIV2_USBDIV_MASK);
  SIM->CLKDIV2  =   SIM_CLKDIV2_USBDIV(0)     ; /* USB clk = (PLL*1/2)        */
                                                /*         = ( 48*1/1)=48     */
  SIM->SCGC4   |=   SIM_SCGC4_USBOTG_MASK;      /* Enable USBOTG clock        */
#endif
//  SIM->CLKDIV2|=USB_FARCTIONAL_VALUE;              //USB分频因子设置  //主频96M 分频后50M
//  SIM->SCGC4|=(SIM_SCGC4_USBOTG_MASK);             //USB模块时钟门使能
	NVIC_EnableIRQ(USB0_IRQn); //使能USB模块IRQ中断
    /* reset USB moudle */
	USB0->USBTRC0 |= USB_USBTRC0_USBRESET_MASK;
	while(USB0->USBTRC0 & USB_USBTRC0_USBRESET_MASK){};

    /* clear all IT bit */
  USB0->ISTAT |= 0xFF;
    
    /* enable USB reset IT */
  USB0->INTEN |= USB_INTEN_USBRSTEN_MASK;
	USB0->USBCTRL=0x40;                             //D-  D+ 下拉
	USB0->USBTRC0|=0x40;                            //强制设置第6位为1  真是纠结，DS上就这么写的
	USB0->CTL|=0x01;                                //USB模块使能
	USB0->CONTROL|=USB_CONTROL_DPPULLUPNONOTG_MASK; //DPP上拉使能

}


void USB_DisConnect(void)
{
	USB0->CONTROL &= ~USB_CONTROL_DPPULLUPNONOTG_MASK;
}

void USB_Connect(void)
{
	USB0->CONTROL |= USB_CONTROL_DPPULLUPNONOTG_MASK;
}
