import { Module } from '@nestjs/common';
import { PrismaModule } from '../prisma/prisma.module';
import { MembersController } from './members.controller';
import { MembersService } from './members.service';
import { RealtimeModule } from '../realtime/realtime.module';
import { PushNotificationsModule } from '../push-notifications/push-notifications.module';
import { PcsModule } from '../pcs/pcs.module';

@Module({
  imports: [PrismaModule, RealtimeModule, PushNotificationsModule, PcsModule],
  controllers: [MembersController],
  providers: [MembersService],
  exports: [MembersService],
})
export class MembersModule {}
