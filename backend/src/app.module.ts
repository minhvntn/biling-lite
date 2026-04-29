import { Module } from '@nestjs/common';
import { ConfigModule } from '@nestjs/config';
import { ScheduleModule } from '@nestjs/schedule';
import { AppController } from './app.controller';
import { AppService } from './app.service';
import { PcsModule } from './pcs/pcs.module';
import { SessionsModule } from './sessions/sessions.module';
import { PricingModule } from './pricing/pricing.module';
import { CommandsModule } from './commands/commands.module';
import { AuthAdminModule } from './auth-admin/auth-admin.module';
import { PrismaModule } from './prisma/prisma.module';
import { RealtimeModule } from './realtime/realtime.module';
import { ReportsModule } from './reports/reports.module';
import { MembersModule } from './members/members.module';

@Module({
  imports: [
    ConfigModule.forRoot({ isGlobal: true }),
    ScheduleModule.forRoot(),
    PcsModule,
    SessionsModule,
    PricingModule,
    CommandsModule,
    AuthAdminModule,
    PrismaModule,
    RealtimeModule,
    ReportsModule,
    MembersModule,
  ],
  controllers: [AppController],
  providers: [AppService],
})
export class AppModule {}
