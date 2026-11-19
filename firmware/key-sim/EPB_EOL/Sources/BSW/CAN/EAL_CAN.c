#include "sys_include.h"
#include "Driver_CAN.h"


extern u8 data_msg[8];//���յ�������


u8  Can_Counter; //����CAN���ļ���
static EPB_Fault_Status  EPBFaultStatus={0};//EPBȫ�ֹ���״̬
static DEBUG_Can_Status  epbstatus={0};//epbȫ��״̬

//extern EPB_Fault_Status g_EPBFaultStatus;  

// ����һ���ṹ�����洢CAN֡����Ϣ
typedef struct {
    u32 CanID;
    byte *MSG;
} CanFrame;

// ����Ҫ���͵�CAN֡����
CanFrame frames[] = {
    {Can_Brake_ID, t_BrakeSta},
    {Can_Speed_ID, t_Vehicle_Speed},
    {Can_Tank_Pressure_ID, t_Tank_Pressure},
    {Can_Wheel_Speed_ID, t_Wheel_Speed},
    {Can_Torque_ID, t_Torque},
    {Can_Gear_ID, t_Gear},
    {Can_Accelerator_ID, t_Accelerator}
};

void delay(u16 count)
{
   u16 i,j;
   for(i=0;i<count;i++)
   {
		for(j=0;j<200;j++)
		{

		}
   }
}


//char motor_temp; // ����һ���ַ����� motor_temp�����ڴ洢����¶�
//char motor_ctl_temp; // ����һ���ַ����� motor_ctl_temp�����ڴ洢��������¶�
//// �жϽ��յ��� CAN ��Ϣ�� ID �Ƿ����Ԥ���CAN֡���� ID
//   if(CanMsg.CanId==CanRecievID)
//   {
//       // �����ȣ��򽫽��յ��� CAN ��Ϣ�ĵ� 6 ���ֽ����ݸ�ֵ�� motor_temp ����
//       motor_temp = CanMsg.Data[6];
//       // �����յ��� CAN ��Ϣ�ĵ� 7 ���ֽ����ݸ�ֵ�� motor_ctl_temp ����
//       motor_ctl_temp = CanMsg.Data[7];
//
//       // ��Ϊ�����ã������յ��� CAN ��Ϣ�����������ֽڸ�ֵ�� data_msg ����
//       #if 0
//       for(i=0;i<8;i++)
//       {
//           data_msg[i] = CanMsg.Data[i];
//       }
//       #endif
//
//       // ���� motor_duty_ctl ���������ݵ���¶Ⱥ͵�������¶Ƚ�����Ӧ�ĵ��ռ�ձȿ���
//       motor_duty_ctl(motor_temp,motor_ctl_temp);
//   }

//�������յ���EPB��Ϣ  
extern CanMsgInf CanMsg;
void CanRcvdEPB_Handle(void)
{
//    CanMsgInf CanMsg; // ����һ�� CanMsgInf �ṹ����� CanMsg�����ڴ洢���յ��� EPB_CAN ��Ϣ��Ϣ


    // ���� Drv_CAN_ReadFrame ������ȡ CAN �����ϵ�һ֡���ݣ�������ȡ�������ݴ洢�� CanMsg �ṹ����
//    Drv_CAN_ReadFrame(&CanMsg);

    // �жϽ��յ��� CAN ��Ϣ�� ID �Ƿ����Ԥ���CAN֡���� ID
    if(CanMsg.CanId==Can_RcvID_EPB)
    {    
    	// �����ȣ��� Can_Rx_OK ������Ϊ 1����ʾ���յ��� EPB �����ı���
    	Can_Counter ++;//CAN���ļ�����1

			// ���������ֽڣ���������Ϊ4��
			if(CanMsg.Data[4] == 0x04) 
			{
				EPBFaultStatus.ECUFaultstatus = BatHigh;
				LED_WARN_ON();
			} 
			if(CanMsg.Data[4] == 0x08) 
			{ 
				EPBFaultStatus.ECUFaultstatus = BatLow;
				LED_WARN_ON();
			}
			if(CanMsg.Data[4] == 0x10)
			{
				EPBFaultStatus.ECUFaultstatus = BatOverHigh;
				LED_WARN_ON();
			}
			if(CanMsg.Data[4] == 0x20)
			{
				EPBFaultStatus.ECUFaultstatus = BatOverLow;
				LED_WARN_ON();
			}
			if(CanMsg.Data[4] == 0x40)
			{
				EPBFaultStatus.IGNFaultstatus = IgnHigh;
				LED_WARN_ON();
			}
			if(CanMsg.Data[4] == 0x80)
			{
				EPBFaultStatus.IGNFaultstatus = IgnLow;
				LED_WARN_ON();
			}
			if(CanMsg.Data[5] == 0x01) 
			{
				EPBFaultStatus.ValveFaultstatus = PressPowerHigh;
				LED_WARN_ON();
			}
			if(CanMsg.Data[5] == 0x02) 
			{
				EPBFaultStatus.ValveFaultstatus = PressPowerLow;
				LED_WARN_ON();
			}
			if(CanMsg.Data[5] == 0x04) 
			{
				EPBFaultStatus.ValveFaultstatus = PressSignalErr;
				LED_WARN_ON();
			}
			if(CanMsg.Data[5] == 0x08) 
			{
				EPBFaultStatus.ValveFaultstatus = ValveOpen;
				LED_WARN_ON();
			}
			if(CanMsg.Data[5] == 0x10) 
			{
				EPBFaultStatus.ValveFaultstatus = ParkValveOpen;
				LED_WARN_ON();
			}
			if(CanMsg.Data[5] == 0x20) 
			{
				EPBFaultStatus.ValveFaultstatus = ReleaseValveOpen;
				LED_WARN_ON();
			}
			if(CanMsg.Data[5] == 0x40) 
			{
				EPBFaultStatus.KEYFaultstatus = ReleaseKeyOpen; 
				LED_WARN_ON();
			}
			if(CanMsg.Data[5] == 0x80) 
			{
				EPBFaultStatus.KEYFaultstatus = ParkKeyOpen;
				LED_WARN_ON();
			}
			if(CanMsg.Data[6] == 0x01) 
			{
				EPBFaultStatus.KEYFaultstatus = AKeyOpen;
				LED_WARN_ON();
			}
			if(CanMsg.Data[6] == 0x02) 
			{
				EPBFaultStatus.KEYFaultstatus = ReleaseLongPress;
				LED_WARN_ON();
			}
			if(CanMsg.Data[6] == 0x04) 
			{
				EPBFaultStatus.KEYFaultstatus = ParkKeyLongPull;
				LED_WARN_ON();
			}
			if(CanMsg.Data[6] == 0x08) 
			{
				EPBFaultStatus.KEYFaultstatus = AKeyLongPress;
				LED_WARN_ON();
			}
			if(CanMsg.Data[6] == 0x10) 
			{
				EPBFaultStatus.CANFaultstatus = BusOff;
				LED_WARN_ON();
			}
			else if(CanMsg.Data[6] == 0x20) 
			{
				EPBFaultStatus.CANFaultstatus = BrakeSigFault;
				LED_WARN_ON();
			}
			if(CanMsg.Data[6] == 0x40) 
			{
				EPBFaultStatus.CANFaultstatus = SpeedSigFault;
				LED_WARN_ON();
			}
			if(CanMsg.Data[6] == 0x80) 
			{
				EPBFaultStatus.CANFaultstatus = AccelSigFault;
				LED_WARN_ON();
			}
			if(CanMsg.Data[7] == 0x01) 
			{
				EPBFaultStatus.CANFaultstatus = ReverseSigFault;
				LED_WARN_ON();
			}
			if(CanMsg.Data[7] == 0x02) 
			{
				EPBFaultStatus.CANFaultstatus = NeutralSigFault;
				LED_WARN_ON();
			}
			if(CanMsg.Data[7] == 0x04) 
			{
				EPBFaultStatus.CANFaultstatus = MotorTorqFault;
				LED_WARN_ON();
			}
			if(CanMsg.Data[7] == 0x08) 
			{
				EPBFaultStatus.CANFaultstatus = MotorSpdFault;
				LED_WARN_ON();
			}
			if(CanMsg.Data[4]==0x00 && CanMsg.Data[5]==0x00 && CanMsg.Data[6]==0x00 && CanMsg.Data[7]==0x00)
			{
				EPBFaultStatus.EPB_status = NOfault;
			}
			
	}
  else  if(CanMsg.CanId==DBUG_CAN1)
    {  
		
			Can_Counter ++;
			if(CanMsg.Data[0] == 0x00)
			{
				epbstatus.park_status = park_off;
			}
			if(CanMsg.Data[0] == 0x01)
			{
				epbstatus.park_status = park_on;
			}
			if(CanMsg.Data[0] == 0x02)
			{
				epbstatus.park_status = park_error;
			}
			if(CanMsg.Data[1] == 0x00)
			{
				epbstatus.rele_status = rele_off;
			}
			if(CanMsg.Data[1] == 0x01)
			{
				epbstatus.rele_status = rele_on;
			}
			if(CanMsg.Data[1] == 0x02)
			{
				epbstatus.rele_status = rele_off;
			}
			if(CanMsg.Data[2] == 0x01)
			{
				epbstatus.epb_status = epb_park;
			}
			if(CanMsg.Data[2] == 0x02)
			{
				epbstatus.epb_status = epb_rele;
			}
			if(CanMsg.Data[2] == 0x03)
			{
				epbstatus.epb_status = epb_error;
			}
			if(CanMsg.Data[3] == 0x05)
			{
				epbstatus.dys_status = static_park ;
			}
			if(CanMsg.Data[3] == 0x06)
			{
				epbstatus.dys_status = static_rele ;
			}
			if(CanMsg.Data[3] == 0x07)
			{
				epbstatus.dys_status = dynamic_park ;
			}
			if(CanMsg.Data[3] == 0x08)
			{
				epbstatus.dys_status = dynamic_rele ;
			}
			if(CanMsg.Data[3] == 0x09)
			{
				epbstatus.dys_status = dynamic_park_end ;
			}
			if(CanMsg.Data[3] == 0x10)
			{
				epbstatus.dys_status = driving_park ;
			}
			if(CanMsg.Data[4] == 0x00)
			{
				epbstatus.hill_status = car_uphill;
			}
			if(CanMsg.Data[4] == 0x01)
			{
				epbstatus.hill_status = car_downhill;
			}
			if(CanMsg.Data[5] == 0x00)
			{
				epbstatus.level_status = car_levelup;
			}
			if(CanMsg.Data[5] == 0x01)
			{
				epbstatus.level_status = car_leveldown;
			}
			if(CanMsg.Data[5] == 0x02)
			{
				epbstatus.level_status = car_slopeup;
			}
			if(CanMsg.Data[5] == 0x03)
			{
				epbstatus.level_status = car_slopedown;
			}
			
	}
  	else if(CanMsg.CanId==DBUG_CAN2)
	{
			Can_Counter ++;//CAN���ļ�����1
			if(CanMsg.Data[0] == 0x01)
			{
				epbstatus.neutral_status = forward_gear;
			}
			if(CanMsg.Data[0] == 0x02)
			{
				epbstatus.neutral_status = car_neutral;
			}
			if(CanMsg.Data[1] == 0x02)
			{
				epbstatus.gear_status = reverse_gear;
			}
			if(CanMsg.Data[2] == 0x01)
			{
				epbstatus.break_status = break_switch_off;
			}
			if(CanMsg.Data[2] == 0x02)
			{
				epbstatus.break_status = break_switch_on;
			}
		
	}
    Can_Counter = 0;
}




//return &EPBFaultStatus;      
//}

  
//DEBUG_Can_Status* CanRcvDBUG_Handle(void)
//{
//    CanMsgInf DbugMsg;  // ����һ�� CanMsgInf �ṹ�����DbugMsg�����ڴ洢���յ��� DEBUG_CAN1 ��Ϣ��Ϣ
//    CanMsgInf Dbugmsg;	// ����һ�� CanMsgInf �ṹ�����Dbugmsg�����ڴ洢���յ��� DEBUG_CAN2 ��Ϣ��Ϣ
//    // ���� Drv_CAN_ReadFrame ������ȡ CAN �����ϵ�һ֡���ݣ�������ȡ�������ݴ洢�� CanMsg �ṹ����
//    Drv_CAN_ReadFrame(&DbugMsg);
//
//    // �жϽ��յ��� CAN ��Ϣ�� ID �Ƿ����Ԥ���CAN֡���� ID
//    if(DbugMsg.CanId==DBUG_CAN1)
//    {
//    	Can_Rx_OK = 1;
//    	//Can_Counter ++;//CAN���ļ�����1
//		// ��Ϊ�����ã������յ��� CAN ��Ϣ�����������ֽڸ�ֵ�� data_msg ����
//		 #if 0
//		 for(i=0;i<8;i++)
//		 {
//			 data_msg[i] = DbugMsg.Data[i];
//		 }
//		 #endif
//		if(DbugMsg.Data[0] == 0x00)
//		{
//			epbstatus.park_status = park_off;
//		}
//		if(DbugMsg.Data[0] == 0x01)
//		{
//			epbstatus.park_status = park_on;
//		}
//		if(DbugMsg.Data[0] == 0x02)
//		{
//			epbstatus.park_status = park_error;
//		}
//		if(DbugMsg.Data[1] == 0x00)
//		{
//			epbstatus.rele_status = rele_off;
//		}
//		if(DbugMsg.Data[1] == 0x01)
//		{
//			epbstatus.rele_status = rele_on;
//		}
//		if(DbugMsg.Data[1] == 0x02)
//		{
//			epbstatus.rele_status = rele_off;
//		}
//		if(DbugMsg.Data[2] == 0x01)
//		{
//			epbstatus.epb_status = epb_park;
//		}
//		if(DbugMsg.Data[2] == 0x02)
//		{
//			epbstatus.epb_status = epb_rele;
//		}
//		if(DbugMsg.Data[2] == 0x03)
//		{
//			epbstatus.epb_status = epb_error;
//		}
//		if(DbugMsg.Data[3] == 0x05)
//		{
//			epbstatus.dys_status = static_park ;
//		}
//		if(DbugMsg.Data[3] == 0x06)
//		{
//			epbstatus.dys_status = static_rele ;
//		}
//		if(DbugMsg.Data[3] == 0x07)
//		{
//			epbstatus.dys_status = dynamic_park ;
//		}
//		if(DbugMsg.Data[3] == 0x08)
//		{
//			epbstatus.dys_status = dynamic_rele ;
//		}
//		if(DbugMsg.Data[3] == 0x09)
//		{
//			epbstatus.dys_status = dynamic_park_end ;
//		}
//		if(DbugMsg.Data[3] == 0x10)
//		{
//			epbstatus.dys_status = driving_park ;
//		}
//		if(DbugMsg.Data[4] == 0x00)
//		{
//			epbstatus.hill_status = car_uphill;
//		}
//		if(DbugMsg.Data[4] == 0x01)
//		{
//			epbstatus.hill_status = car_downhill;
//		}
//		if(DbugMsg.Data[5] == 0x00)
//		{
//			epbstatus.level_status = car_levelup;
//		}
//		if(DbugMsg.Data[5] == 0x01)
//		{
//			epbstatus.level_status = car_leveldown;
//		}
//		if(DbugMsg.Data[5] == 0x02)
//		{
//			epbstatus.level_status = car_slopeup;
//		}
//		if(DbugMsg.Data[5] == 0x03)
//		{
//			epbstatus.level_status = car_slopedown;
//		}	
//    }
//    Drv_CAN_ReadFrame(&Dbugmsg);
//    if(Dbugmsg.CanId==DBUG_CAN2)
//    {
//    	Can_Rx_OK = 1;
//    	Can_Counter ++;//CAN���ļ�����1
//    	if(Dbugmsg.Data[0] == 0x01)
//    	{
//    		epbstatus.neutral_status = forward_gear;
//    	}
//    	if(Dbugmsg.Data[0] == 0x02)
//    	{
//    		epbstatus.neutral_status = car_neutral;
//    	}
//    	if(Dbugmsg.Data[1] == 0x02)
//    	{
//    		epbstatus.gear_status = reverse_gear;
//    	}
//    	if(Dbugmsg.Data[2] == 0x01)
//    	{
//    		epbstatus.break_status = break_switch_off;
//    	}
//    	if(Dbugmsg.Data[2] == 0x02)
//    	{
//    		epbstatus.break_status = break_switch_on;
//    	}
//    }
//    return &epbstatus;
//}




   



u8 t_Wheel_Speed[8] = {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00};//����
u8 t_BrakeSta[8]={0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00};//�ƶ�̤��
u8 t_Vehicle_Speed[8] = {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00};//����
u8 t_Tank_Pressure[8] = {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00};//����ѹ��
u8 t_Torque[8] = {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00};//Ť��
u8 t_Gear[8] = {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00};//��λ
u8 t_Accelerator[8] = {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00};//����̤��
u8 t_test1[8] = {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00};//���Ա���1
u8 t_test2[8] = {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00};//���Ա���2
u8 t_test3[8] = {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00};//���Ա���3

#define MAX_QUEUE_SIZE  32  // ������е���󳤶�
#define SEND_INTERVAL   100 // ���巢�ͼ��ʱ�䣨��λ�����룩


// ������нṹ
typedef struct {
    u32 CanID;
    byte MSG[8];
} CANFrame;


typedef struct {
    CANFrame buffer[MAX_QUEUE_SIZE];
    int head;
    int tail;
    int count;
} CANQueue;

// ��ʼ������
void CANQueue_Init(CANQueue *queue) {
    queue->head = 0;
    queue->tail = 0;
    queue->count = 0;
}


// ���岼�����ͺͳ���
#define true 1
#define false 0

// ����������ӱ���
u8 CANQueue_Push(CANQueue *queue, u32 CanID, byte *MSG) {
	s16 i;
    if (queue->count >= MAX_QUEUE_SIZE) {
        return false; // ��������
    }
    queue->buffer[queue->tail].CanID = CanID;
    for (i = 0; i < 8; i++) {
        queue->buffer[queue->tail].MSG[i] = MSG[i];
    }
    queue->tail = (queue->tail + 1) % MAX_QUEUE_SIZE;
    queue->count++;
    return true;
}

// �Ӷ�����ȡ������
u8 CANQueue_Pop(CANQueue *queue, u32 *CanID, byte *MSG) {
	s16 i;
    if (queue->count == 0) {
        return false; // ����Ϊ��
    }
    *CanID = queue->buffer[queue->head].CanID;
    for (i = 0; i < 8; i++) {
        MSG[i] = queue->buffer[queue->head].MSG[i];
    }
    queue->head = (queue->head + 1) % MAX_QUEUE_SIZE;
    queue->count--;
    return true;
}

// �������߼�����
void CAN_SendFrames(CANQueue *queue) {
    static int sent_count = 0; // ��̬��������¼�ѷ��͵ı�������

    while (queue->count > 0) {
        u32 CanID;
        byte MSG[8];

        // �Ӷ�����ȡ��һ������
        if (!CANQueue_Pop(queue, &CanID, MSG)) {
            break; // ����Ϊ�գ��˳�
        }

        // ���ͱ���
        if (Drv_CAN_SendFrame(CanID, MSG, 8) == ERR_TXFULL) {
            // ������ͻ������������ȴ����������
            while (!(CANTFLG & 0x07)); // �ȴ�����һ�����ͻ���������
            {
				delay(1);
				break;
            }
        }

        // ÿ�����������ĺ���ʱ
        sent_count++;
        if (sent_count % 3 == 0) {
        	delay(100);
        }
    }
	// �������Ϊ�գ�Ϩ��ָʾ��
	if (queue->count == 0) {
		PTDD_PTDD0 = 0;
	}
}

void Can_SendMsg_Task_10ms(void)//CAN��������ÿ10ms����һ��
{
    static CANQueue queue;
    static bool is_initialized = false;

    // ��ʼ�����У�����ʼ��һ�Σ�
    if (!is_initialized) {
        CANQueue_Init(&queue);
        is_initialized = true;
    }

    	// �ڷ���ǰ����ָʾ��
		PTDD_PTDD0 = 1;

		
//	    Drv_CAN_SendFrame(Can_Brake_ID,t_BrakeSta);
//	  
//	    Drv_CAN_SendFrame(Can_Speed_ID,t_Vehicle_Speed);
//	    
//	    Drv_CAN_SendFrame(Can_Tank_Pressure_ID,t_Tank_Pressure);
//		
//		Drv_CAN_SendFrame(Can_Wheel_Speed_ID,t_Wheel_Speed);
//		Drv_CAN_SendFrame(Can_Torque_ID,t_Torque);	
//										
//		Drv_CAN_SendFrame(Can_Gear_ID,t_Gear);							
//			
//		Drv_CAN_SendFrame(Can_Accelerator_ID,t_Accelerator);
		
		
		
	    CANQueue_Push(&queue, Can_Brake_ID, t_BrakeSta);// �����ƶ�״̬����
	    CANQueue_Push(&queue, Can_Speed_ID, t_Vehicle_Speed);  // ���ͳ��ٱ���
	    CANQueue_Push(&queue, Can_Tank_Pressure_ID, t_Tank_Pressure);// ��������ѹ������
	    CANQueue_Push(&queue, Can_Wheel_Speed_ID, t_Wheel_Speed);// �������ٱ���
	    CANQueue_Push(&queue, Can_Torque_ID, t_Torque);//����Ť�ر���
	    CANQueue_Push(&queue, Can_Gear_ID, t_Gear);// ���͵�λ����
	    CANQueue_Push(&queue, Can_Accelerator_ID, t_Accelerator);// ���ͼ���̤�屨��
	    
	    
	    // ���Ͷ����еı���
	    CAN_SendFrames(&queue);
}


//		���͵��Ա���
//		Drv_CAN_SendFrame(Can_Test1_ID,t_test1);
//		Drv_CAN_SendFrame(Can_Test2_ID,t_test2);
//		Drv_CAN_SendFrame(Can_Test3_ID,t_test3);

//	 // ���ݲ�ͬ�ĳ������ͣ����ò�ͬ���ƶ�״̬�����ٺ�����ѹ����������
//	#if Vehicle_Type == TYBYD
//		u8 t_BrakeSta[8]={0xff,0x00,0x00,0x10,0x00,0x00,0x00,0x00};		//���ٱ��ġ��ƶ�̤�屨��
//		u8 t_Vehicle_Speed[8]={0xff,0x00,0x00,0x10,0x00,0x00,0x00,0x00};
//		u8 t_Tank_Pressure[8] = {0x00,0x00,0x7D,0x7D,0x00,0x00,0x00,0x00};
//	#elif Vehicle_Type == SZJL
//		u8 t_BrakeSta[8]={0x40,0x00,0x00,0x00,0x00,0x00,0x00,0x00};//�ƶ�̤��
//		u8 t_Vehicle_Speed1[8]={0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00};//����1
//		u8 t_Vehicle_Speed2[8]={0x00,0x00,0x02,0x00,0x00,0x00,0x00,0x00};//����2
//		u8 t_Vehicle_Speed3[8]={0x00,0x00,0x3C,0x00,0x00,0x00,0x00,0x00};//����3
//		u8 t_Vehicle_Speed4[8]={0x00,0x00,0x50,0x00,0x00,0x00,0x00,0x00};//����4
//		u8 t_Tank_Pressure[8] = {0x50,0x50,0x00,0x00,0x00,0x00,0x00,0x00};//����ѹ��
//		u8 t_Wheel_Speed[8]={0x00,0x00,0x7D,0x7D,0x7D,0x7D,0x00,0x00 };//����
//		u8 t_Torque[8]={0x00,0x50,0x06,0x2E,0x00,0x00,0x00,0x00};//Ť��
//		u8 t_Gear1[8]={0x00,0x00,0x00,0x7F,0x00,0x00,0x00,0x00};//��λ�ǿյ�
//		u8 t_Gear2[8]={0x00,0x00,0x00,0x7D,0x00,0x00,0x00,0x00};//��λ�յ�
//		u8 t_Accelerator2[8]={0x00,0x7D,0x00,0x00,0x00,0x00,0x00,0x00};//����̤��50%
//		u8 t_Accelerator1[8]={0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00};//����̤��0%
//		u8 t_Accelerator3[8]={0x00,0x4B,0x00,0x00,0x00,0x00,0x00,0x00};//����̤��30%
//	#elif Vehicle_Type == ZHYL
//		u8 t_BrakeSta[8]={0x00,0x00,0x00,0x10,0x00,0x00,0x02,0x00};
//		u8 t_Vehicle_Speed[8]={0x00,0xf0,0x00,0x10,0x00,0x00,0x00,0x00};
//	#elif Vehicle_Type == TEST
//		u8 t_BrakeSta[8]={0x40,0x00,0x00,0x00,0x00,0x00,0x00,0x00};
//		u8 t_Vehicle_Speed[8]={0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00};
//		u8 t_Tank_Pressure[8] = {0x7D,0x7D,0x00,0x00,0x00,0x00,0x00,0x00};
//	#endif
