import { Module } from '@nestjs/common';
import { CombosController } from './combos.controller';
import { CombosService } from './combos.service';
import { PrismaModule } from '../prisma/prisma.module';
import { CommandsModule } from '../commands/commands.module';
import { PcsModule } from '../pcs/pcs.module';

@Module({
  imports: [PrismaModule, CommandsModule, PcsModule],
  controllers: [CombosController],
  providers: [CombosService]
})
export class CombosModule {}
