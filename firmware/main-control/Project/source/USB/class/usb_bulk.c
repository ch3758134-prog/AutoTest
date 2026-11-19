/******************************************************************************
 *
 * Freescale Semiconductor Inc.
 * (c) Copyright 2004-2009 Freescale Semiconductor, Inc.
 * ALL RIGHTS RESERVED.
 *
 ******************************************************************************
 *
 * THIS SOFTWARE IS PROVIDED BY FREESCALE "AS IS" AND ANY EXPRESSED OR 
 * IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES 
 * OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED.  
 * IN NO EVENT SHALL FREESCALE OR ITS CONTRIBUTORS BE LIABLE FOR ANY DIRECT, 
 * INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES 
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR 
 * SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) 
 * HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, 
 * STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING 
 * IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF 
 * THE POSSIBILITY OF SUCH DAMAGE.
 *
 **************************************************************************//*!
 *
 * @file usb_BULK.c
 *
 * @author
 *
 * @version
 *
 * @date May-28-2009
 *
 * @brief The file contains USB stack BULK layer implementation.
 *
 *****************************************************************************/

/******************************************************************************
 * Includes
 *****************************************************************************/
#include "usb_bulk.h"    /* USB BULK Class Header File */
/*****************************************************************************
 * Constant and Macro's
 *****************************************************************************/

/****************************************************************************
 * Global Variables
 ****************************************************************************/
/* BULK Class Callback Function Pointer */
static USB_CLASS_CALLBACK g_BULK_class_callback = NULL;

/* BULK Class Vendor Callback Function Pointer */
static USB_REQ_FUNC g_vendor_req_callback = NULL;

/* BULK endpoint info array */
static USB_CLASS_BULK_ENDPOINT g_BULK_ep[BULK_DESC_ENDPOINT_COUNT];
/*****************************************************************************
 * Local Types - None
 *****************************************************************************/

void USB_Class_BULK_Service_Dic_Bulk_In(PTR_USB_DEV_EVENT_STRUCT event);
void USB_Class_BULK_Service_Dic_Bulk_Out(PTR_USB_DEV_EVENT_STRUCT event);

static uint_8 USB_Other_Requests(uint_8 controller_ID,USB_SETUP_STRUCT * setup_packet,uint_8_ptr *data,USB_PACKET_SIZE *size);
void USB_Class_BULK_Event(uint_8 controller_ID,uint_8 event,void* val);


/**************************************************************************//*!
 *
 * @name  USB_Class_BULK_Service_Dic_Bulk_In
 *
 * @brief The function is callback function of DIC Bulk In Endpoint
 *
 * @param event : Pointer to USB Event Structure
 *
 * @return None
 *
 ******************************************************************************
 * Called by Lower Layer when Data on DIC SEND Interface is sent
 *****************************************************************************/
void USB_Class_BULK_Service_Dic_Bulk_In (
    PTR_USB_DEV_EVENT_STRUCT event /* [IN] Pointer to USB Event Structure */
)
{
    APP_DATA_STRUCT bulk_in_recv;

#if IMPLEMENT_QUEUING
    uint_8 index;
    uint_8 producer, consumer;
    USB_ENDPOINTS *usb_ep_data = (USB_ENDPOINTS *)
        USB_Desc_Get_Endpoints(event->controller_ID);

    USB_CLASS_BULK_QUEUE queue;

    bulk_in_recv.data_ptr = event->buffer_ptr;
    bulk_in_recv.data_size = event->len;

    /* map the endpoint num to the index of the endpoint structure */
    for(index = 0; index < usb_ep_data->count; index++)
    {
        if(usb_ep_data->ep[index].ep_num == event->ep_num)
        break;
    }

    producer = g_BULK_ep[index].bin_producer;

    /* if there are no errors de-queue the queue and decrement the no. of
       transfers left, else send the same data again */
    if(event->errors == 0)
    {
        g_BULK_ep[index].bin_consumer++;
    }

    consumer = g_BULK_ep[index].bin_consumer;

    if(consumer != producer)
    {/*if bin is not empty */

        queue = g_BULK_ep[index].queue[consumer%MAX_QUEUE_ELEMS];

        (void)USB_Class_Send_Data(queue.controller_ID, queue.channel,
            queue.app_data.data_ptr, queue.app_data.data_size);
    }
#endif
    if(g_BULK_class_callback != NULL)
    {
        if(event->errors != 0)
        {
            g_BULK_class_callback(event->controller_ID, USB_APP_ERROR,
                (uint_8*)(&(event->errors)));
        }
        else
        {
            g_BULK_class_callback(event->controller_ID, USB_APP_SEND_COMPLETE,
                (void*)&bulk_in_recv);
        }
    }
}

/**************************************************************************//*!
 *
 * @name  USB_Class_BULK_Service_Dic_Bulk_Out
 *
 * @brief The function is callback function of DIC Bulk Out Endpoint
 *
 * @param event : Pointer to USB Event Structure
 *
 * @return None
 *
 ******************************************************************************
 * Called by Lower Layer when Data on DIC RECV Interface is received
 *****************************************************************************/
void USB_Class_BULK_Service_Dic_Bulk_Out (PTR_USB_DEV_EVENT_STRUCT event  /* [IN] Pointer to USB Event Structure */)
{   
	  //APP_DATA_STRUCT bulk_out_recv;

    //bulk_out_recv.data_ptr = event->buffer_ptr;
    //bulk_out_recv.data_size = event->len;

    if(g_BULK_class_callback != NULL)
    {

        if(event->errors != 0)
        {
            g_BULK_class_callback(event->controller_ID, USB_APP_ERROR,(uint_8*)(&(event->errors)));
        }
        else
        {
           // g_BULK_class_callback(event->controller_ID, USB_APP_DATA_RECEIVED,(void*)&bulk_out_recv);
					g_BULK_class_callback(event->controller_ID, USB_APP_DATA_RECEIVED,event);
        }
    }
}


/**************************************************************************//*!
 *
 * @name  USB_Class_BULK_Event
 *
 * @brief The function initializes BULK endpoints
 *
 * @param controller_ID : Controller ID
 * @param event         : Event Type
 * @param val           : Pointer to configuration Value
 *
 * @return None
 *
 ******************************************************************************
 *
 *****************************************************************************/
 void USB_Class_BULK_Event (
    uint_8 controller_ID,   /* [IN] Controller ID */
    uint_8 event,           /* [IN] Event Type */
    void* val               /* [OUT] Pointer to configuration Value */
)
{
    uint_8 index;
    USB_ENDPOINTS *usb_ep_data = (USB_ENDPOINTS *)
        USB_Desc_Get_Endpoints(controller_ID);

    if(event == USB_APP_ENUM_COMPLETE)
    {
        uint_8 count = 0,ep_count = 0;  
        uint_8 index_num = 0;    
                  		    
		ep_count = usb_ep_data->count; 

		for(count=index_num; count<ep_count+index_num; count++) 
		{   
			USB_EP_STRUCT_PTR ep_struct_ptr= 
				(USB_EP_STRUCT_PTR) (&usb_ep_data->ep[count]);
			(void)_usb_device_deinit_endpoint(&controller_ID,
				ep_struct_ptr->ep_num, ep_struct_ptr->direction);
		}
        
        /* intialize all non control endpoints */
        for(count=index_num; count<ep_count+index_num; count++) 
        {
            USB_EP_STRUCT_PTR ep_struct=
                (USB_EP_STRUCT_PTR) (&usb_ep_data->ep[count]);

            (void)_usb_device_init_endpoint(&controller_ID, ep_struct->ep_num,
            		ep_struct->size, ep_struct->direction, ep_struct->type, FALSE);

            /* register callback service for Non Control EndPoints */
            switch(ep_struct->type)
            {
#if !DIC_ISOCHRONOUS_SETTING
                case USB_BULK_PIPE :
#ifdef MULTIPLE_DEVICES
                    if(ep_struct->direction == USB_RECV)
                    {
                        (void)_usb_device_register_service(controller_ID,
                            (uint_8)(USB_SERVICE_EP0+ep_struct->ep_num),
                            USB_Class_BULK_Service_Dic_Bulk_Out);
                    }
                    else
                    {
                        (void)_usb_device_register_service(controller_ID,
                            (uint_8)(USB_SERVICE_EP0+ep_struct->ep_num),
                            USB_Class_BULK_Service_Dic_Bulk_In);
                    }                	
#endif
                    break;
#else
                case USB_ISOCHRONOUS_PIPE :
                    if(ep_struct->direction == USB_RECV)
                    {
                        (void)_usb_device_register_service(controller_ID,
                            (uint_8)(USB_SERVICE_EP0+ep_struct->ep_num),
                            USB_Class_BULK_Service_Dic_Iso_Out);
                    }
                    else
                    {
                        (void)_usb_device_register_service(controller_ID,
                            (uint_8)(USB_SERVICE_EP0+ep_struct->ep_num),
                            USB_Class_BULK_Service_Dic_Iso_In);
                    }
                    break;
#endif
                default: 
                    break;
            }
            /* set the EndPoint Status as Idle in the device layer */
            (void)_usb_device_set_status(&controller_ID,
                (uint_8)(USB_STATUS_ENDPOINT | ep_struct->ep_num |
                (ep_struct->direction << USB_COMPONENT_DIRECTION_SHIFT)),
                (uint_8)USB_STATUS_IDLE);
        }
    }
    else if(event == USB_APP_BUS_RESET)
    {
#if IMPLEMENT_QUEUING
        for(index = 0; index < usb_ep_data->count; index++)
        {
            g_BULK_ep[index].bin_consumer = 0x00;
            g_BULK_ep[index].bin_producer = 0x00;
        }
#endif
    }
    if(g_BULK_class_callback != NULL)
    {
        g_BULK_class_callback(controller_ID, event, val);
    }
}

/**************************************************************************//*!
 *
 * @name  USB_Other_Requests
 *
 * @brief The function provides flexibilty to add class and vendor specific
 *        requests
 *
 * @param controller_ID : Controller ID
 * @param setup_packet  : Setup packet received
 * @param data:         : Data to be send back
 * @param size:         : Size to be returned
 *
 * @return status:
 *                        USB_OK : When Successfull
 *                        Others : When Error
 *
 ******************************************************************************
 * Handles BULK Class requests and forwards vendor specific request to the
 * application
 *****************************************************************************/
static uint_8 USB_Other_Requests
(
    uint_8 controller_ID,           /* [IN] Controller ID */
    USB_SETUP_STRUCT * setup_packet,/* [IN] Pointer to Setup Packet Received */
    uint_8_ptr *data,           /* [OUT] Pointer to Data Buffer to be sent */
    USB_PACKET_SIZE *size           /* [OUT] Size of Data buffer */
)
{
    /* EPB 新增：原为空壳（对任何 class/vendor request 一律返回 INVALID → STALL）。
     * WCID (MS-OS 1.0) 要求设备响应 vendor request 0xEE 返回扩展兼容 ID 描述符，
     * 这里把 class/vendor 请求转发给应用层回调（main.c WcidVendorReq），
     * 该回调由 USB_Class_BULK_Init 第 3 参注册到 g_vendor_req_callback。 */
    if (g_vendor_req_callback != NULL)
    {
        return g_vendor_req_callback(controller_ID, setup_packet, data, size);
    }

    return USBERR_INVALID_REQ_TYPE;
}


/*****************************************************************************
 * Global Functions
 *****************************************************************************/
/**************************************************************************//*!
 *
 * @name  USB_Class_BULK_Init
 *
 * @brief The function initializes the Device and Controller layer
 *
 * @param controller_ID:        Controller ID
 * @param BULK_class_callback:   BULK Class Callback
 * @param vendor_req_callback:  vendor specific class request callback
 * @param param_callback:       PSTN Callback
 *
 * @return status
 *         USB_OK           : When Successfull
 *         Others           : Errors
 ******************************************************************************
 * This function initializes the BULK Class layer and layers it is dependent upon
 *****************************************************************************/
uint_8 USB_Class_BULK_Init (
	uint_8    		   		controller_ID,       /* [IN] Controller ID */
	USB_CLASS_CALLBACK 		BULK_class_callback,  /* [IN] BULK Class Callback */
	USB_REQ_FUNC       		vendor_req_callback, /* [IN] Vendor Request Callback */
	USB_CLASS_CALLBACK 		pstn_callback,       /* [IN] PSTN Callback */
	uint_8            		bVregEn              /* Enables or disables internal regulator */
)
{
    uint_8 index,status = USB_OK;
    USB_ENDPOINTS *usb_ep_data =
        (USB_ENDPOINTS *)USB_Desc_Get_Endpoints(controller_ID);
#ifndef COMPOSITE_DEV        
    /* Initialize the device layer*/
    status = _usb_device_init(controller_ID, NULL,
        (uint_8)(usb_ep_data->count+1), bVregEn);
    /* +1 is for Control Endpoint */

    if(status == USB_OK)
    {
        /* Initialize the generic class functions */
        status = USB_Class_Init(controller_ID,USB_Class_BULK_Event,USB_Other_Requests);
#endif
#if IMPLEMENT_QUEUING
            for(index = 0; index < usb_ep_data->count; index++)
            {
                g_BULK_ep[index].endpoint = usb_ep_data->ep[index].ep_num;
                g_BULK_ep[index].type = usb_ep_data->ep[index].type;
                g_BULK_ep[index].bin_consumer = 0x00;
                g_BULK_ep[index].bin_producer = 0x00;
            }
#endif
        if(status == USB_OK)
        {
           /* save the callback pointer */
           g_BULK_class_callback = BULK_class_callback;

           /* save the callback pointer */
           g_vendor_req_callback = vendor_req_callback;
        }
#ifndef COMPOSITE_DEV                
    }
#endif    
    return status;
}

/**************************************************************************//*!
 *
 * @name  USB_Class_BULK_DeInit
 *
 * @brief   The function de-initializes the Device and Controller layer
 *
 * @param   controller_ID               : Controller ID
 *
 * @return status:
 *                        USB_OK        : When Successfull
 *                        Others        : When Error
 *
 ******************************************************************************
 *This function de-initializes the BULK Class layer
 *****************************************************************************/
uint_8 USB_Class_BULK_DeInit 
(
    uint_8 controller_ID              /* [IN] Controller ID */
) 
{
    uint_8 status = USB_OK;
#ifdef COMPOSITE_DEV  
    UNUSED(controller_ID)
#endif
    /* save the callback pointer */
    g_BULK_class_callback = NULL;
    
    /* free the vendor request callback pointer */
    g_vendor_req_callback = NULL;
    
#ifndef COMPOSITE_DEV    
    /* call common class deinit function */
    status = USB_Class_DeInit(controller_ID);
    
    if(status == USB_OK)
    /* Call device deinit function */
    	status = _usb_device_deinit();
#endif
    return status;
}

/**************************************************************************//*!
 *
 * @name  USB_Class_BULK_Send_Data
 *
 * @brief This function is used to send data from BULK Class over send endpoints
 *
 * @param controller_ID : Controller ID
 * @param ep_num        : Endpoint number
 * @param app_buff      : Buffer to send
 * @param size          : Length of the transfer
 *
 * @return status
 *         USB_OK           : When Successfull
 *         Others           : Errors
 ******************************************************************************
 * Helper function. Sends DATA over CIC and DIC Interfaces to Host
 *****************************************************************************/
uint_8 USB_Class_BULK_Send_Data (
    uint_8 controller_ID,   /* [IN] Controller ID */
    uint_8 ep_num,          /* [IN] Endpoint Number */
    uint_8_ptr app_buff,    /* Pointer to Application Buffer */
    USB_PACKET_SIZE size    /* Size of Application Buffer */
)
{
    uint_8 status = USB_OK;

//#if IMPLEMENT_QUEUING
    uint_8 index;
    uint_8 producer, consumer;

    USB_ENDPOINTS *usb_ep_data = (USB_ENDPOINTS *)
                                    USB_Desc_Get_Endpoints(controller_ID);

    /* map the endpoint num to the index of the endpoint structure */
    for(index = 0; index < usb_ep_data->count; index++)
    {
        if(usb_ep_data->ep[index].ep_num == ep_num)
        {
            break;
        }
    }

    producer = g_BULK_ep[index].bin_producer;
    consumer = g_BULK_ep[index].bin_consumer;

    //if(((uint_8)(producer - consumer)) != (uint_8)(MAX_QUEUE_ELEMS))
		if(((uint_8)(producer - consumer)) == 0)	
    {
        /* the bin is not full*/

        uint_8 queue_num = (uint_8)(producer % MAX_QUEUE_ELEMS);

        /* put all send request parameters in the endpoint data structure */
        g_BULK_ep[index].queue[queue_num].controller_ID = controller_ID;
        g_BULK_ep[index].queue[queue_num].channel = ep_num;
        g_BULK_ep[index].queue[queue_num].app_data.data_ptr = app_buff;
        g_BULK_ep[index].queue[queue_num].app_data.data_size = size;

        /* increment producer bin by 1*/
        g_BULK_ep[index].bin_producer = ++producer;
			
			  status = USB_Class_Send_Data(controller_ID, ep_num, app_buff,size);
			  /* 
        if((ep_num == 0x02) && (uint_8)(producer - consumer) == 1)
        {
//#endif
            status = USB_Class_Send_Data(controller_ID, ep_num, app_buff,size);
//#if IMPLEMENT_QUEUING
        }
				else if((ep_num == 0x04 || ep_num == 0x06) && (uint_8)(producer - consumer) >= 1 && (uint_8)(producer - consumer) < 4)
				{
					  status = USB_Class_Send_Data(controller_ID, ep_num, app_buff,size);
				}*/
    }
    else /* bin is full */
    {
        status = USBERR_DEVICE_BUSY;
    }
//#endif
    return status;
}

/*********************************************************************************************
*������    :
*����      :
*����      :
*����      :
*��ע      :
*����      :
**********************************************************************************************/
uint_8 USB_Class_BULK_FreeEndpIn ( uint_8 controller_ID)
{

    uint_8 index;
	  uint_8 ep = 0;
    uint_8 producer, consumer;
    USB_ENDPOINTS *usb_ep_data = (USB_ENDPOINTS *)USB_Desc_Get_Endpoints(controller_ID);

    /* map the endpoint num to the index of the endpoint structure */
    for(index = 0; index < usb_ep_data->count; index++)
    {
        if(usb_ep_data->ep[index].direction == USB_SEND)
        {
					  producer = g_BULK_ep[index].bin_producer;
            consumer = g_BULK_ep[index].bin_consumer;
					  if(((uint_8)(producer - consumer)) == 0)	
            {
							ep = usb_ep_data->ep[index].ep_num ;
							break;
		        }
        }
    }
		if(ep > 4)
		{
			ep =0;
		}
    return ep;
}

/*********************************************************************************************
*������    :
*����      :
*����      :
*����      :
*��ע      :
*����      :
**********************************************************************************************/
boolean USB_Class_BULK_EndpInIsFree ( uint_8 controller_ID,uint_8 ep)
{

    uint_8 index;
    uint_8 producer, consumer;
    USB_ENDPOINTS *usb_ep_data = (USB_ENDPOINTS *)USB_Desc_Get_Endpoints(controller_ID);

    /* map the endpoint num to the index of the endpoint structure */
    for(index = 0; index < usb_ep_data->count; index++)
    {
			
			  if(ep == usb_ep_data->ep[index].ep_num )
				{
					if(usb_ep_data->ep[index].direction == USB_SEND)
          {
					  producer = g_BULK_ep[index].bin_producer;
            consumer = g_BULK_ep[index].bin_consumer;
					  if(((uint_8)(producer - consumer)) == 0)	
            {
							return TRUE;
		        }
          }
					else
					{
						return FALSE;
					}
				}
			}
    return FALSE;
}

/* EOF */
