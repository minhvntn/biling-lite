import { Module } from '@nestjs/common';
import { WebFilterController } from './web-filter.controller';
import { WebFilterService } from './web-filter.service';

@Module({
  controllers: [WebFilterController],
  providers: [WebFilterService],
  exports: [WebFilterService],
})
export class WebFilterModule {}

