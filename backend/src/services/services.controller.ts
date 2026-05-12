import { Body, Controller, Get, Param, Patch, Post, Query } from '@nestjs/common';
import { CancelPcServiceOrdersDto } from './dto/cancel-pc-service-orders.dto';
import { CreatePcServiceOrderDto } from './dto/create-pc-service-order.dto';
import { CreateServiceItemDto } from './dto/create-service-item.dto';
import { PayPcServiceOrdersDto } from './dto/pay-pc-service-orders.dto';
import { UpdateServiceItemDto } from './dto/update-service-item.dto';
import { ServicesService } from './services.service';

@Controller('services')
export class ServicesController {
  constructor(private readonly servicesService: ServicesService) {}

  @Get('items')
  async getServiceItems(@Query('includeInactive') includeInactive?: string) {
    return this.servicesService.getServiceItems({
      includeInactive: includeInactive === 'true',
    });
  }

  @Post('items')
  async createServiceItem(@Body() payload: CreateServiceItemDto) {
    return this.servicesService.createServiceItem(payload);
  }

  @Patch('items/:serviceItemId')
  async updateServiceItem(
    @Param('serviceItemId') serviceItemId: string,
    @Body() payload: UpdateServiceItemDto,
  ) {
    return this.servicesService.updateServiceItem(serviceItemId, payload);
  }

  @Get('pcs/:pcId/orders')
  async getPcServiceOrders(
    @Param('pcId') pcId: string,
    @Query('limit') limit?: string,
  ) {
    const numericLimit = Number(limit);
    return this.servicesService.getPcServiceOrders(pcId, numericLimit);
  }

  @Post('pcs/:pcId/orders')
  async createPcServiceOrder(
    @Param('pcId') pcId: string,
    @Body() payload: CreatePcServiceOrderDto,
  ) {
    return this.servicesService.createPcServiceOrder(pcId, payload);
  }

  @Post('pcs/:pcId/orders/cancel')
  async cancelPcServiceOrders(
    @Param('pcId') pcId: string,
    @Body() payload: CancelPcServiceOrdersDto,
  ) {
    return this.servicesService.cancelPcServiceOrders(pcId, payload);
  }

  @Post('pcs/:pcId/orders/pay')
  async payPcServiceOrders(
    @Param('pcId') pcId: string,
    @Body() payload: PayPcServiceOrdersDto,
  ) {
    return this.servicesService.payPcServiceOrders(pcId, payload);
  }
}
