import {
  BadRequestException,
  Body,
  Controller,
  Delete,
  Get,
  Param,
  Patch,
  Post,
  Put,
  Req,
  Res,
  UploadedFile,
  UseInterceptors,
} from '@nestjs/common';
import { FileInterceptor } from '@nestjs/platform-express';
import { Request, Response } from 'express';
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

  @Post('client-settings/lock-screen-media')
  @UseInterceptors(FileInterceptor('file'))
  async uploadLockScreenMedia(
    @UploadedFile() file: any,
    @Body('mode') mode: string,
    @Req() request: Request,
  ) {
    if (!file) {
      throw new BadRequestException('Missing file');
    }

    const baseUrl = `${request.protocol}://${request.get('host')}/api/v1`;
    return this.pricingService.uploadLockScreenMedia(file, mode, baseUrl);
  }

  @Get('client-settings/lock-screen-media/:fileName')
  async getLockScreenMedia(
    @Param('fileName') fileName: string,
    @Res() response: Response,
  ) {
    return this.pricingService.writeLockScreenMediaToResponse(fileName, response);
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

  @Get('promotions')
  async getPromotions() {
    return this.pricingService.getPromotions();
  }

  @Post('promotions')
  async createPromotion(@Body() payload: any) {
    return this.pricingService.createPromotion(payload);
  }

  @Put('promotions/:id')
  async updatePromotion(
    @Param('id') id: string,
    @Body() payload: any,
  ) {
    return this.pricingService.updatePromotion(id, payload);
  }

  @Delete('promotions/:id')
  async deletePromotion(@Param('id') id: string) {
    return this.pricingService.deletePromotion(id);
  }
}
