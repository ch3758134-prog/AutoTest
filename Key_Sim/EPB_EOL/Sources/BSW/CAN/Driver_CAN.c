#include "sys_include.h"

// #define Motorola  

volatile u8 canMessageReceivedFlag = 0;
static volatile CanMsgInf canRxQueue[8];
static volatile u8 canRxHead = 0U;
static volatile u8 canRxTail = 0U;
static volatile u8 canRxCount = 0U;
volatile u16 canRxOverflowCount = 0U;
/* Kept for legacy modules that still declare this symbol; reception uses the FIFO below. */
CanMsgInf CanMsg;
 u8  sDrvCan_Busoff;//CAN����״̬��־λ
 u8  DLC_LN =8;//CAN֡����
 extern u8  Can_Counter; //����CAN���ļ���
 extern u8  BUAD_status; //������״̬
 u8 ExtendedFrame;//��չ֡��־λ
 u8 StandardFrame;//��׼֡��־λ
 
byte Drv_CAN_SendFrame(u32 CanID, byte *MSG, byte Len) //����CAN֡
{
	  u8 i;
	  TMsgBuff *MsgBuff;
	  u32 tmpId = CanID;
	  while (!(CANTFLG & 0x07)) {}         /* 等待任意 TX buffer 空闲 */
	  CANTBSEL = CANTFLG;
	  MsgBuff = (TMsgBuff *)&CANTIDR0;

	  tmpId = (((tmpId & 0x1FFC0000UL) << 3) | 0x00180000UL | ((tmpId & 0x0003FFFFUL) << 1));
	  MsgBuff->IDR3 = ((DwordSwap *)&tmpId)->b.b3;
	  MsgBuff->IDR2 = ((DwordSwap *)&tmpId)->b.b2;
	  MsgBuff->IDR1 = ((DwordSwap *)&tmpId)->b.b1;
	  MsgBuff->IDR0 = ((DwordSwap *)&tmpId)->b.b0;

	  for (i=0; i<Len; i++) MsgBuff->Data[i] = MSG[i];
	  MsgBuff->IDR3 &= 0xFEU;
	  MsgBuff->DLR = Len;
	  MsgBuff->TBPR = 0x00U;
	  CANTFLG = 1;                         /* 启动发送 */
	  while (!(CANTFLG & 0x01)) {}         /* 等待 Buffer0 发送完成 */
	  return ERR_OK;
}

void Drv_CAN_ReadFrame(CanMsgInf *Msg)//��ȡCAN֡
{
  byte i,Length;
  u32  tmpId =0;
  ((DwordSwap*)&tmpId)->b.b0 = CANRIDR0;//CAN���ջ�����Ĵ���ָ��
  ((DwordSwap*)&tmpId)->b.b1 = CANRIDR1;
  ((DwordSwap*)&tmpId)->b.b2 = CANRIDR2;
  ((DwordSwap*)&tmpId)->b.b3 = CANRIDR3;
  if (tmpId & MB_ID_IDE)//�ж��Ƿ�Ϊ��չ֡ 
  {
    Msg->CanId = ((tmpId >> 1) & 0x3FFFFUL) | ((tmpId >> 3) & 0x1FFC0000UL); //��չ֡
    ExtendedFrame = 1;
  }
  else {
    Msg->CanId  = tmpId >> 21;         //��׼֡
    StandardFrame = 1;
  }
  
  if(ExtendedFrame) 
  {
	  if(CANRIDR3 & 0x01)
	  {
		  Msg->FrameType = 1;//Զ��֡  
	  }
	  else
	  {
	    Msg->FrameType = 0;//���֡
	  }
	  ExtendedFrame = 0;
  }
  
  
  if(StandardFrame)
  {
	  if(CANRIDR1 & 0x10) 	//�ж��Ƿ�ΪԶ��֡
	  {
		Msg->FrameType = 1;//Զ��֡
	  } 
	  else 
	  {
		Msg->FrameType = 0;//���֡
	  }
	  StandardFrame = 0;
  }
	
  Length = (byte)(CANRDLR & 0x0F); //��ȡ��ݳ���
  Msg->MsgLth  = (byte)(CANRDLR & 0x0F);
 
  

    for (i=0U; i<Length; i++) 
  {
    Msg->Data[i] = *((byte *)&CANRDSR0 + i); //CAN���ջ�����ָ��
  }

  
    CANRFLG = 1;//������жϱ�־λ                              
}


void Drv_CAN_Init(void)
{                 
  u32 AcFilterId=0;
  //MSCANģ��ʹ��
  CANCTL1 = 0x80U;                    
  //MSCAN���ڳ�ʼ��ģʽ
  CANCTL0 = 0x01U;

  /* 滤波器全开 - 接收所有扩展帧和标准帧 */
  CANIDAC_IDAM = 0x00U;
  CANIDAR0 = 0xE6U;
  CANIDAR1 = 0x00U;
  CANIDAR2 = 0x00U;
  CANIDAR3 = 0x00U;
  CANIDMR0 = 0x00U;
  CANIDMR1 = 0x00U;
  CANIDMR2 = 0x00U;
  CANIDMR3 = 0x00U;
  CANIDAR4 = 0x00U;
  CANIDAR5 = 0x00U;
  CANIDAR6 = 0x00U;
  CANIDAR7 = 0x00U;
  CANIDMR4 = 0xFFU;
  CANIDMR5 = 0xFFU;
  CANIDMR6 = 0xFFU;
  CANIDMR7 = 0xFFU;

	//ϵͳʱ��8M������2��Ƶ��
	CANBTR0 = 0x41U;                     
	//ʱ���1��7Tq ʱ������  ʱ���2��8Tq ʱ������
	//CAN��Bit Time=4M/1/(7+8+1)
	CANBTR1 = 0x3AU;

  if(BUAD_status == 1)
  {
	//ϵͳʱ��8M������2��Ƶ��
	CANBTR0 = 0x43U;                     
	//ʱ���1��4Tq ʱ������  ʱ���2��3Tq ʱ������
	//CAN��Bit Time=4M/1/(4 + 3 + 1)
	CANBTR1 = 0x23U;
  }
  CANCTL1_CLKSRC = 0x01U;              
  CANCTL0_INITRQ = 0x00U;//CAN������

  while(CANCTL1_INITAK) {} 
            
  CANRFLG_RXF = 1;//������жϱ�־                    
 CANRIER_RXFIE = 0x01U; // RX int disabled for polling mode
  CANRFLG_TSTAT0 =1;
  CANRFLG_TSTAT1 =1;                     
  CANRFLG_RSTAT0 =1;
  CANRFLG_RSTAT1 =1;
  
   /* Busoff�ж�ʹ�� */
 	CANRIER_TSTATE0 = 1; //01:Generate CSCIF interrupt only if the transmitter enters or leaves ��bus-off�� state. 
 	CANRIER_TSTATE1 = 0; //Discard other transmitter state changes for generating CSCIF interrupt.
 	CANRFLG_CSCIF = 1;
 	CANRIER_CSCIE = 1;  //1:A CAN Status Change event causes an error interrupt request.
	sDrvCan_Busoff =  0;                     
}

void Drv_CAN_SetBaudrate(u8 baudSel)  /* baudSel: 0=250K, 1=500K */
  {
      /* 进入初始化模式 */
      CANCTL0_INITRQ = 1;
      while (CANCTL1_INITAK == 0) {}

      /* 设置波特率 */
      if (baudSel == 0) {
          /* 250K */
          CANBTR0 = 0x43U;
      } else {
          /* 500K */
          CANBTR0 = 0x41U;
      }

      /* 退出初始化模式 */
      CANCTL0_INITRQ = 0;
      while (CANCTL1_INITAK == 1) {}
  }

/*********************************************************************************************
*������    :
*����      :����CAN�����ж�
*����      :
*����      :
*��ע      :
*����      :
**********************************************************************************************/
ISR(Drv_CAN_Err_ISR) //CAN�����ж�
{
  // ���CAN״̬�仯�жϱ�־λ
  CANRFLG_CSCIF  =  1; 
  
  // ������з��ͻ����������
  CANTARQ_ABTRQ0=1;
  CANTARQ_ABTRQ1=1;
  CANTARQ_ABTRQ2=1;  
  
  // �������߹رձ�־λ
  sDrvCan_Busoff =  1;
}

u8 BusOff_stata(void)//CAN �����Ƿ�ر�
  {
      return sDrvCan_Busoff;// ����CAN���߹رձ�־λ��ֵ
  }

  /*********************************************************************************************
*������    :
*����      :CANʹ��
*����      :
*����      :
*��ע      :
*����      :
**********************************************************************************************/
void Drv_CAN_Enable(void)
{ 
    // ��INITRQλ����Ϊ0������CANģ�����������ģʽ
    CANCTL0_INITRQ = 0x00;             
    // �ȴ�INITAKλ��Ϊ0����ʾCANģ���Ѿ�����������ģʽ
    while(CANCTL1_INITAK) {}  
}

/*********************************************************************************************
*������    :
*����      :CAN��ֹ
*����      :
*����      :
*��ע      :
*����      :
**********************************************************************************************/
void Drv_CAN_Disable(void)
{ 
    // ��INITRQλ����Ϊ1������CANģ������ʼ��ģʽ
    CANCTL0_INITRQ = 0x01;             
    // �ȴ�INITAKλ��Ϊ1����ʾCANģ���Ѿ������ʼ��ģʽ
    while(!CANCTL1_INITAK) {}   
}

/*********************************************************************************************
*������    :
*����      :CAN RX �ж�
*����      :
*����      :
*��ע      :
*����      :
**********************************************************************************************/
ISR(Drv_CAN_RX_ISR) //CAN RX�ж�
{
    while(CANRFLG_RXF == 1) {
        CanMsgInf frame;
        Drv_CAN_ReadFrame(&frame);
        if (canRxCount < 8U) {
            canRxQueue[canRxTail] = frame;
            canRxTail = (u8)((canRxTail + 1U) % 8U);
            canRxCount++;
            canMessageReceivedFlag = 1U;
        } else {
            canRxOverflowCount++;
        }
    }
}

u8 Drv_CAN_ReceiveFrame(CanMsgInf *Msg)
{
    if (canRxCount == 0U) return 0U;
    *Msg = canRxQueue[canRxHead];
    canRxHead = (u8)((canRxHead + 1U) % 8U);
    canRxCount--;
    if (canRxCount == 0U) canMessageReceivedFlag = 0U;
    return 1U;
}
