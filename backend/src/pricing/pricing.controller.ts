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
import { networkInterfaces } from 'os';
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
  async getClientRuntimeSettings(@Req() request: Request) {
    const settings = await this.pricingService.getClientRuntimeSettings();
    return this.rewriteClientRuntimeMediaUrl(settings, request);
  }

  @Patch('client-settings')
  async setClientRuntimeSettings(
    @Body() payload: SetClientRuntimeSettingsDto,
    @Req() request: Request,
  ) {
    const settings = await this.pricingService.setClientRuntimeSettings(payload);
    return this.rewriteClientRuntimeMediaUrl(settings, request);
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

    const baseUrl = this.buildClientFacingApiBaseUrl(request);
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

  private rewriteClientRuntimeMediaUrl<T extends { lockScreenBackgroundUrl?: string }>(
    settings: T,
    request: Request,
  ): T {
    const rawUrl = settings.lockScreenBackgroundUrl?.trim();
    if (!rawUrl) {
      return settings;
    }

    let mediaUrl: URL;
    try {
      mediaUrl = new URL(rawUrl);
    } catch {
      return settings;
    }

    if (!this.isLoopbackHost(mediaUrl.hostname)) {
      return settings;
    }

    const baseUrl = this.buildClientFacingApiBaseUrl(request);
    let apiBase: URL;
    try {
      apiBase = new URL(baseUrl);
    } catch {
      return settings;
    }

    mediaUrl.protocol = apiBase.protocol;
    mediaUrl.hostname = apiBase.hostname;
    mediaUrl.port = apiBase.port;

    return {
      ...settings,
      lockScreenBackgroundUrl: mediaUrl.toString(),
    };
  }

  private buildClientFacingApiBaseUrl(request: Request): string {
    const protocolHeader = request.get('x-forwarded-proto')?.split(',')[0]?.trim();
    const protocol = protocolHeader || request.protocol || 'http';

    const hostHeader = request.get('x-forwarded-host')?.split(',')[0]?.trim()
      || request.get('host')?.trim()
      || '';

    let hostname = request.hostname?.trim() || '';
    let port = '';

    if (hostHeader) {
      try {
        const parsed = new URL(`http://${hostHeader}`);
        hostname = parsed.hostname || hostname;
        port = parsed.port || port;
      } catch {
        // Ignore invalid host header, fallback to request.hostname.
      }
    }

    if (!hostname) {
      hostname = 'localhost';
    }

    if (this.isLoopbackHost(hostname)) {
      hostname = this.resolveBestServerLanIpv4() ?? hostname;
    }

    const authority = port ? `${hostname}:${port}` : hostname;
    return `${protocol}://${authority}/api/v1`;
  }

  private isLoopbackHost(hostname: string): boolean {
    const normalized = hostname.trim().toLowerCase().replace(/^\[(.*)\]$/, '$1');
    return (
      normalized === 'localhost' ||
      normalized === '127.0.0.1' ||
      normalized === '::1' ||
      normalized === '0.0.0.0' ||
      normalized === '::'
    );
  }

  private resolveBestServerLanIpv4(): string | null {
    const interfaces = networkInterfaces();
    const privateAddresses: string[] = [];
    const fallbackAddresses: string[] = [];

    for (const entries of Object.values(interfaces)) {
      if (!entries || entries.length === 0) {
        continue;
      }

      for (const entry of entries) {
        if (!entry || entry.family !== 'IPv4' || entry.internal) {
          continue;
        }

        const address = (entry.address || '').trim();
        if (!address || address.startsWith('169.254.')) {
          continue;
        }

        if (this.isPrivateIpv4(address)) {
          privateAddresses.push(address);
        } else {
          fallbackAddresses.push(address);
        }
      }
    }

    return privateAddresses[0] || fallbackAddresses[0] || null;
  }

  private isPrivateIpv4(address: string): boolean {
    if (address.startsWith('10.') || address.startsWith('192.168.')) {
      return true;
    }

    if (!address.startsWith('172.')) {
      return false;
    }

    const segments = address.split('.');
    if (segments.length < 2) {
      return false;
    }

    const secondOctet = Number(segments[1]);
    return Number.isInteger(secondOctet) && secondOctet >= 16 && secondOctet <= 31;
  }
}
