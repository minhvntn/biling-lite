import { Module } from '@nestjs/common';
import { SettingsController } from './settings.controller';
import { PrismaModule } from '../prisma/prisma.module';
import { SettingsBackupService } from './settings-backup.service';

@Module({
  imports: [PrismaModule],
  controllers: [SettingsController],
  providers: [SettingsBackupService],
})
export class SettingsModule {}
