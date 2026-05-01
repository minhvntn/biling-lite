import { Body, Controller, Get, Param, Patch, Post, Put } from '@nestjs/common';
import { AssignPcGroupDto } from './dto/assign-pc-group.dto';
import { CreateGroupRateDto } from './dto/create-group-rate.dto';
import { SetClientRuntimeSettingsDto } from './dto/set-client-runtime-settings.dto';
import { SetDefaultRateDto } from './dto/set-default-rate.dto';
import { UpdateGroupRateDto } from './dto/update-group-rate.dto';
import { PricingService } from './pricing.service';

@Controller('pricing')
export class PricingController {
  constructor(private readonly pricingService: PricingService) {}

  @Get()
  async getPricingSettings() {
    return this.pricingService.getPricingSettings();
  }

  @Get('client-settings')
  async getClientRuntimeSettings() {
    return this.pricingService.getClientRuntimeSettings();
  }

  @Patch('client-settings')
  async setClientRuntimeSettings(@Body() payload: SetClientRuntimeSettingsDto) {
    return this.pricingService.setClientRuntimeSettings(payload);
  }

  @Put('default-rate')
  async setDefaultRate(@Body() payload: SetDefaultRateDto) {
    return this.pricingService.setDefaultRate(payload);
  }

  @Post('groups')
  async createGroup(@Body() payload: CreateGroupRateDto) {
    return this.pricingService.createGroup(payload);
  }

  @Patch('groups/:groupId')
  async updateGroup(
    @Param('groupId') groupId: string,
    @Body() payload: UpdateGroupRateDto,
  ) {
    return this.pricingService.updateGroup(groupId, payload);
  }

  @Post('pcs/:pcId/group')
  async assignPcToGroup(
    @Param('pcId') pcId: string,
    @Body() payload: AssignPcGroupDto,
  ) {
    return this.pricingService.assignPcToGroup(pcId, payload);
  }
}
