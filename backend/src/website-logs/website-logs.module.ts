import { Module } from '@nestjs/common';
import { WebsiteLogsController } from './website-logs.controller';
import { WebsiteLogsService } from './website-logs.service';

@Module({
  controllers: [WebsiteLogsController],
  providers: [WebsiteLogsService],
  exports: [WebsiteLogsService],
})
export class WebsiteLogsModule {}

