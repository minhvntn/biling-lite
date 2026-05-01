import { Body, Controller, Get, Put } from '@nestjs/common';
import { UpdateWebFilterSettingsDto } from './dto/update-web-filter-settings.dto';
import { WebFilterService } from './web-filter.service';

@Controller('web-filter')
export class WebFilterController {
  constructor(private readonly webFilterService: WebFilterService) {}

  @Get('settings')
  async getSettings() {
    return this.webFilterService.getSettings();
  }

  @Put('settings')
  async updateSettings(@Body() payload: UpdateWebFilterSettingsDto) {
    return this.webFilterService.updateSettings(payload);
  }
}

