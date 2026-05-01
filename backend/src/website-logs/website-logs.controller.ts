import { Body, Controller, Get, Post, Put, Query } from '@nestjs/common';
import { IngestWebsiteLogsDto } from './dto/ingest-website-logs.dto';
import { QueryWebsiteLogsDto } from './dto/query-website-logs.dto';
import { UpdateWebsiteLogSettingsDto } from './dto/update-website-log-settings.dto';
import { WebsiteLogsService } from './website-logs.service';

@Controller('website-logs')
export class WebsiteLogsController {
  constructor(private readonly websiteLogsService: WebsiteLogsService) {}

  @Get('settings')
  async getSettings() {
    return this.websiteLogsService.getSettings();
  }

  @Put('settings')
  async updateSettings(@Body() payload: UpdateWebsiteLogSettingsDto) {
    return this.websiteLogsService.updateSettings(payload);
  }

  @Post('ingest')
  async ingest(@Body() payload: IngestWebsiteLogsDto) {
    return this.websiteLogsService.ingestLogs(payload);
  }

  @Get()
  async queryLogs(@Query() query: QueryWebsiteLogsDto) {
    return this.websiteLogsService.getLogs(query);
  }
}

