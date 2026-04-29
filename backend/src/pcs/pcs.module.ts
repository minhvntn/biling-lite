import { Module } from '@nestjs/common';
import { CommandsModule } from '../commands/commands.module';
import { PcsController } from './pcs.controller';
import { BillingGateway } from './realtime/billing.gateway';
import { PcsService } from './pcs.service';

@Module({
  imports: [CommandsModule],
  controllers: [PcsController],
  providers: [PcsService, BillingGateway],
  exports: [PcsService],
})
export class PcsModule {}
