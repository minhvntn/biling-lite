import { Body, Controller, Get, Param, Post, Query } from '@nestjs/common';
import { QuerySessionsDto } from './dto/query-sessions.dto';
import { SessionsService } from './sessions.service';
import { TransferSessionDto } from './dto/transfer-session.dto';

@Controller('sessions')
export class SessionsController {
  constructor(private readonly sessionsService: SessionsService) {}

  @Get()
  async getSessions(@Query() query: QuerySessionsDto) {
    return this.sessionsService.getSessions(query);
  }

  @Get('active')
  async getActiveSessions() {
    return this.sessionsService.getActiveSessions();
  }

  @Post('transfer/:fromPcId')
  async transferSession(
    @Param('fromPcId') fromPcId: string,
    @Body() payload: TransferSessionDto,
  ) {
    return this.sessionsService.transferActiveSession(
      fromPcId,
      payload.targetPcId,
      payload.requestedBy,
    );
  }
}
