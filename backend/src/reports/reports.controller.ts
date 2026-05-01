import { Controller, Get, Query } from '@nestjs/common';
import { ReportsService } from './reports.service';

@Controller('reports')
export class ReportsController {
  constructor(private readonly reportsService: ReportsService) {}

  @Get('revenue/summary')
  async getRevenueSummary(
    @Query('period') period?: string,
    @Query('date') date?: string,
  ) {
    return this.reportsService.getRevenueSummary(period, date);
  }

  @Get('revenue/daily')
  async getDailyRevenue(@Query('date') date?: string) {
    return this.reportsService.getDailyRevenue(date);
  }

  @Get('events/system')
  async getSystemEvents(@Query('limit') limit?: string) {
    return this.reportsService.getSystemEvents(limit);
  }
}
