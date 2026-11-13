#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "sys_include.h"

#include "hidef.h"          /* for EnableInterrupts macro */
#include "derivative.h"     /* include peripheral declarations */
#include "types.h"          /* Contains User Defined Data Types */
#include "usb_bulk.h"        /* USB BULK Class Header File */
#include "USB_Transmit_App.h"    /* Virtual COM Application Header File */
#include "CAN_Transmit_App.h" 
#include "Adc_app.h"
#include <stdio.h>
/* skip the inclusion in dependency state */
#include "KLINE_Common.H"
//#include "MAX5715.h"

#include "PwmCtr.h"
void USB_Transmit_AppInit(void);
uint_8 last_byte = 0;
uint_8 this_byte = 0;
/*****************************************************************************
 * Local Functions Prototypes
 *****************************************************************************/
static void USB_Transmit_App_Callback(uint_8 controller_ID,uint_8 event_type, void* val);
//static void USB_Notify_Callback(uint_8 controller_ID,uint_8 event_type, void* val);
//static void USB_Transmit_App(void);
/*****************************************************************************
 * Local Variables
 *****************************************************************************/
/* Virtual COM Application start Init Flag */
volatile boolean start_app = FALSE;
/* Virtual COM Application Carrier Activate Flag */
//static volatile boolean start_transactions = FALSE;
volatile boolean start_transactions = TRUE;
/* Receive Buffer */
//uint_8 g_curr_recv_buf[DATA_BUFF_SIZE];
/* Send Buffer */
//static uint_8 g_curr_send_buf[DATA_BUFF_SIZE];
/* Receive Data Size */
//static uint_8 g_recv_size;
/* Send Data Size */
//static uint_8 g_send_size;

static USB_RX_MSG *usb_rx;

static  uint8_t UCTool_UpdateFlag = 0;
canTxmsgQueue_t CanTxmsgQueue[2];


uint_32 UsbStime = 0;
uint_32 UsbEtime = 0;
uint_32 UsbRxcnt = 0;
u32 CanTxcnt0 = 0;

static uint_8 rspBuf[32];


//static uint_16 KL_USBTxBufLen = 0;
//static uint_8 KL_USBTxBuf[512];

static uint_8 ValueStatebuf[16];

extern volatile  CanmsgQueue_t CanRxmsgQueue[2];

u8 SpeedSensorType = 0;

uint_8 g_usb_recv_buf[8][DATA_BUFF_SIZE];
uint8_t     gusbRxBuffNum;			//������������
uint8_t     gusbRxBuffGet;   //out
uint8_t     gusbRxBuffPut;   //in
/*********************************************************************************************
*������    :
*����      :
*����      :
*����      :
*��ע      :
*����      :
**********************************************************************************************/

uint8_t Query_UCTool_UpdateFlag(void)
{
	return UCTool_UpdateFlag;
}
/*********************************************************************************************
*������    :
*����      :
*����      :
*����      :
*��ע      :
*����      :
**********************************************************************************************/

void Clear_UCTool_UpdateFlag(void)
{
  UCTool_UpdateFlag = 0;
}
/*********************************************************************************************
*������    :
*����      :
*����      :
*����      :
*��ע      :
*����      :
**********************************************************************************************/
boolean  	UCTool_CmdRsp(uint_8 err,uint_8 cmd,uint_8 *buf ,uint_8 len)
{
	memset(rspBuf,0,32);
	if(err == 0)
	{
		rspBuf[0] = len + 1;
		rspBuf[1] = cmd;
		memcpy(&rspBuf[2],buf,len);
	}
	else
	{
		rspBuf[0] = 0x03;
		rspBuf[1] = 0x7f;
		rspBuf[2] = cmd;
		rspBuf[3] = err;
	}
	if(USB_Class_BULK_EndpInIsFree(CONTROLLER_ID,DIC_BULK_IN_ENDPOINT1)== TRUE)
	{
		if(USB_Class_BULK_Interface_DIC_Send_Data(CONTROLLER_ID,DIC_BULK_IN_ENDPOINT1,rspBuf,32) == 0)
		{
			return true;
		}
		else
		{
		 return false;
		}
  }
	else
	{
		return true;
	}
}

/*********************************************************************************************
*������    :
*����      :
*����      :
*����      :
*��ע      :
*����      :
**********************************************************************************************/

void USB_Transmit_AppInit(void)
{
    uint_8   error;
    uint_8 i;
    gusbRxBuffNum = 0;			//������������
    gusbRxBuffGet = 0;   //out
    gusbRxBuffPut = 0;   //in
//    g_send_size= 0;
	  UCTool_UpdateFlag = 0;
	  for(i = 0;i < 2;i++)
		{
      CanTxmsgQueue[i].giRxBuffNum = 0;
			CanTxmsgQueue[i].giRxBuffGet = 0;
			CanTxmsgQueue[i].giRxBuffPut = 0;
		}
	  
    DisableInterrupts;
    /* Initialize the USB interface */
    error = USB_Class_BULK_Init(CONTROLLER_ID,USB_Transmit_App_Callback,
                                NULL,NULL, TRUE);
    if(error != USB_OK)
    {
        /* Error initializing USB-BULK Class */
        return;
    }
    EnableInterrupts;
}


static void CanClearBuf(void)
{
	  uint_8 i;
	  for(i = 0;i < 2;i++)
		{
      CanTxmsgQueue[i].giRxBuffNum = 0;
			CanTxmsgQueue[i].giRxBuffGet = 0;
			CanTxmsgQueue[i].giRxBuffPut = 0;
		}
		for(i = 0;i < 2;i++)
		{
      CanRxmsgQueue[i].giRxBuffNum = 0;
			CanRxmsgQueue[i].giRxBuffGet = 0;
			CanRxmsgQueue[i].giRxBuffPut = 0;
		}
}
/*********************************************************************************************
*������    :
*����      :
*����      :
*����      :
*��ע      :
*����      :
**********************************************************************************************/
boolean  	IS_USB_APP_ENUM_COMPLETE(void)
{
	 return start_app;
}
/*********************************************************************************************
*������    :
*����      :
*����      :
*����      :
*��ע      :
*����      :
**********************************************************************************************/
void USB_Transmit_AppTask(void)
{
    uint_8 i;
    /* call the periodic task function */
    //USB_Class_BULK_Periodic_Task();

    /*check whether enumeration is complete or not */
    if((start_app==TRUE) && (start_transactions==TRUE))
    {
      USB_CtrCmd_Handle();
			for(i = 0;i < 2;i++)
			{
				if(CanTxmsgQueue[i].giRxBuffNum > 0)
				{
					 DisableInterrupts;
					 this_byte = CanTxmsgQueue[i].msg[CanTxmsgQueue[i].giRxBuffGet].Data[0];
					 if(CAN_WriteData((CAN_Index)i, CanTxmsgQueue[i].msg[CanTxmsgQueue[i].giRxBuffGet].ID, CanTxmsgQueue[i].msg[CanTxmsgQueue[i].giRxBuffGet].Data, CanTxmsgQueue[i].msg[CanTxmsgQueue[i].giRxBuffGet].DataLen) == 0)
					 {
							 CanTxmsgQueue[i].giRxBuffGet++;
							 if(CanTxmsgQueue[i].giRxBuffGet>= CAN_SEND_QUEUE_SIZE)
							 {
								 CanTxmsgQueue[i].giRxBuffGet =0 ;
							 }
							 CanTxmsgQueue[i].giRxBuffNum--;
							 if(this_byte == last_byte)
							 {
							   CanTxcnt0++;
							 }
					 }
					 else
					 {
						 last_byte = this_byte;
					 }
					 
					 EnableInterrupts;
				}
		  }
    }
}



/*********************************************************************************************
*������    :USB_CtrCmd_Handle
*����      :USB ���������
*����      :
*����      :
*��ע      :
*����      :
**********************************************************************************************/
void USB_CtrCmd_Handle(void)
{
	  uint_8 i,len = 0;
    if(gusbRxBuffNum > 0)
    {
			//usb_rx = (USB_RX_MSG*)g_curr_recv_buf;
			usb_rx = (USB_RX_MSG*)g_usb_recv_buf[gusbRxBuffGet];
			gusbRxBuffGet++;
			gusbRxBuffGet = gusbRxBuffGet % 8;
			gusbRxBuffNum--;
			switch(usb_rx->Cmd)
			{
				 case Can_Init:
				 {
					 for(i = 0;i < 2;i++)
					 {
							CanTxmsgQueue[i].giRxBuffNum = 0;
							CanTxmsgQueue[i].giRxBuffGet = 0;
							CanTxmsgQueue[i].giRxBuffPut = 0;
					 }
					 VCI_InitCan((CAN_Index)usb_rx->DevType, (PVCI_INIT_CONFIG)usb_rx->buf);
					 break;
				 }
				 case Can_Start:
				 {
					 mCAN_Module_En((CAN_Index)usb_rx->DevType);
					 break;
				 } 
				 case Can_Reset:
				 {
					 mCAN_Module_Dis((CAN_Index)usb_rx->DevType);
					 break;
				 }
				 case Can_ClrBuf:
				 {
					 CanClearBuf();
					 break;
				 }
				 case Can_BusOffRecovery:
				 {
					 break;
				 }
				 case CAN_Transmit_In:
				 {
					 if(CanTxmsgQueue[(CAN_Index)usb_rx->DevType].giRxBuffNum < CAN_SEND_QUEUE_SIZE )
					 {
						 CanTxmsgQueue[(CAN_Index)usb_rx->DevType].msg[CanTxmsgQueue[(CAN_Index)usb_rx->DevType].giRxBuffPut] = *(VCI_SEND_CAN_OBJ *)usb_rx->buf;
						 CanTxmsgQueue[(CAN_Index)usb_rx->DevType].giRxBuffPut++;
						 CanTxmsgQueue[(CAN_Index)usb_rx->DevType].giRxBuffPut = CanTxmsgQueue[(CAN_Index)usb_rx->DevType].giRxBuffPut%CAN_SEND_QUEUE_SIZE;
						 CanTxmsgQueue[(CAN_Index)usb_rx->DevType].giRxBuffNum++;
						 UsbRxcnt++;
					 }
					 break;
				 }
				 
				case Adc_DataStart:
				{
					break;
				}
				case Adc_DataStop:
				{
					break;
				}
				case UCTool_UpdateReq:
				{
					UCTool_UpdateFlag = 1;
					break;
	      }
				case KLine_Transmit_In:
				{

					break;
				}
				case KLine_Transmit_Out:
				{
					break;
				}
///////////////////////////////////////////////////////////////////////////////////////////
				case CmdPower12V:
				{
					if(usb_rx->buf[1] == 1)
					{
					  Power12V_Out(1);
					}
					else
					{
						 Power12V_Out(0);
					}
					UCTool_CmdRsp(0,usb_rx->Cmd,&usb_rx->buf[0],1);
					break;
				}
				
				case CmdPower24V:
				{
					if(usb_rx->buf[1] == 1)
					{
					  Power24V_Out(1);
					}
					else
					{
						 Power24V_Out(0);
					}
					UCTool_CmdRsp(0,usb_rx->Cmd,&usb_rx->buf[0],1);
					break;
				}
				
				case CmdHout:
				{
					//SinWaveFrqInit();
					SysPower_OutputCtr(&usb_rx->buf[1]);
					UCTool_CmdRsp(0,usb_rx->Cmd,&usb_rx->buf[0],4);
					break;
				}
				case CmdLout:
				{
					Mcu_Lamp_OutputCtr(&usb_rx->buf[1]);
					UCTool_CmdRsp(0,usb_rx->Cmd,&usb_rx->buf[0],8);
					break;
				}
				
       	case CmdPlcMotoRun:
				{
					if(usb_rx->buf[1] == 1)
					{
					   PLC_MOTO_RUN(1);
					}
					else
					{
						 PLC_MOTO_RUN(0);
					}
					UCTool_CmdRsp(0,usb_rx->Cmd,&usb_rx->buf[0],1);
					break;
				}
       	case CmdPlcScanStart:
				{
					if(usb_rx->buf[1] == 1)
					{
					   PLC_SCAN_START(1);
					}
					else
					{
						 PLC_SCAN_START(0);
					}
					UCTool_CmdRsp(0,usb_rx->Cmd,&usb_rx->buf[0],1);
					break;
				}
				case CmdPlcTestRst:
				{
					if(usb_rx->buf[1] == 1)
					{
					   EOL_TEST_PASS(1);
					}
					else if(usb_rx->buf[1] == 2)
					{
					   EOL_TEST_FAIL(1);
					}
					else
					{
						 EOL_TEST_PASS(0);
						 EOL_TEST_FAIL(0);
					}
					UCTool_CmdRsp(0,usb_rx->Cmd,&usb_rx->buf[0],1);
					break;
				}
				
				
				
				case CmdReadValueState:	
				{
				  len = Read_ValueState(&ValueStatebuf[1]);
					ValueStatebuf[0] = usb_rx->buf[0];
					UCTool_CmdRsp(0,usb_rx->Cmd,ValueStatebuf,len + 1);
					break;
				}
				case CmdReadGrat2State:
				{
					if(GetGrat2State() == OFF)
					{
					  Mcu_Lamp_OutputCtr(&usb_rx->buf[1]);
						usb_rx->buf[1] = 0;
					  UCTool_CmdRsp(0,usb_rx->Cmd,&usb_rx->buf[0],2);
					}
					else
					{
						usb_rx->buf[1] = 0x02;
						usb_rx->buf[2] = 0x01;
						usb_rx->buf[3] = 0x02;
						usb_rx->buf[4] = 0x02;
						Mcu_Lamp_OutputCtr(&usb_rx->buf[1]);//���ϰ����������
						usb_rx->buf[1] = 1;
						UCTool_CmdRsp(0,usb_rx->Cmd,&usb_rx->buf[0],2);
					}
					
					break;
				}
				
        case CmdWheelSpeedAng:	
				{

					break;
				}
				
				case CmdWheelSpeedAngFinish:
				{

					break;
				}
				case CmdReadSpeedLinePwm:
				{
					//LineSpeedToPc();
					break;
				}
			}
			// g_recv_size =0;
		}
		
    return;
}
/*********************************************************************************************
*???    :USB_App_Callback
*??      :
 * This function is called from the class layer whenever reset occurs or enum
 * is complete. After the enum is complete this function sets a variable so
 * that the application can start.
 * This function also receives DATA Send and RECEIVED Events
*??      :
 *    @param       controller_ID    : Controller ID
 *    @param       event_type       : Value of the event
 *    @param       val              : gives the configuration value
*??      :
*??      :
*??      :
**********************************************************************************************/
static void USB_Transmit_App_Callback (uint_8 controller_ID,uint_8 event_type,void* val)
{
    UNUSED (controller_ID)
    UNUSED (val)
	  DisableInterrupts;
    if(event_type == USB_APP_BUS_RESET)
    {
        start_app=FALSE;
    }
    else if(event_type == USB_APP_ENUM_COMPLETE)
    {    	
        start_app=TRUE;
    }
    else if((event_type == USB_APP_DATA_RECEIVED) && (start_transactions == TRUE))
    {
        /* Copy Received Data buffer to Application Buffer */
			  uint_8 index;
        USB_PACKET_SIZE BytesToBeCopied;
        PTR_USB_DEV_EVENT_STRUCT dp_rcv = (PTR_USB_DEV_EVENT_STRUCT)val;
        
        BytesToBeCopied = (USB_PACKET_SIZE)((dp_rcv->len > DATA_BUFF_SIZE) ? DATA_BUFF_SIZE:dp_rcv->len);
			  if(gusbRxBuffNum < 8)
				{
					for(index = 0; index<BytesToBeCopied ; index++)
					{
							g_usb_recv_buf[gusbRxBuffPut][index]= dp_rcv->buffer_ptr[index];
					}
					gusbRxBuffPut++;
					gusbRxBuffPut = gusbRxBuffPut % 8;
					gusbRxBuffNum++;
				}
			
			
			
			
//        for(index = 0; index<BytesToBeCopied ; index++)
//        {
//            g_curr_recv_buf[index]= dp_rcv->buffer_ptr[index];
//        }
//        g_recv_size = index;
				
        (void)USB_Class_BULK_Interface_DIC_Recv_Data(CONTROLLER_ID, dp_rcv->ep_num, NULL, 0);
        //(void)USB_Class_BULK_Interface_DIC_Recv_Data(CONTROLLER_ID, dp_rcv->ep_num, g_curr_recv_buf, BytesToBeCopied);
				//g_recv_size = BytesToBeCopied;
				//USB_CtrCmd_Handle();
    }
    else if((event_type == USB_APP_SEND_COMPLETE) && (start_transactions == TRUE))
    {
			 //Txcnt++;
      //(void)USB_Class_BULK_Interface_DIC_Recv_Data(CONTROLLER_ID, NULL, 0);
    }
    EnableInterrupts;
    return;
}
/*********************************************************************************************
*������    :USB_Notify_Callback
*����      :This function handles USB_APP_BULK_CARRIER_ACTIVATED and USB_APP_BULK_CARRIER_DEACTIVATED PSTN Events
*����      : *    @param       controller_ID    : Controller ID
             *    @param       event_type       : PSTN Event Type
             *    @param       val              : gives the configuration value
*����      :
*��ע      :
*����      :
**********************************************************************************************/
#if 0
static void USB_Notify_Callback (uint_8 controller_ID, uint_8 event_type, void* val)
{
    UNUSED (controller_ID)
    UNUSED (val)
    if(start_app == TRUE)
    {
        if(event_type == USB_APP_BULK_CARRIER_ACTIVATED)
        {
            start_transactions = TRUE;
        }
        else if(event_type == USB_APP_BULK_CARRIER_DEACTIVATED)
        {
            //start_transactions = FALSE;
        }
    }
    return;
}
#endif 
/* EOF */
