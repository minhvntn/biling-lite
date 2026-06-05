import { Injectable, NotFoundException, Logger } from '@nestjs/common';
import { PrismaService } from '../prisma/prisma.service';
import { CreateComboDto } from './dto/create-combo.dto';
import { GenerateComboDto } from './dto/generate-combo.dto';
import { CommandsService } from '../commands/commands.service';
import { PcsService } from '../pcs/pcs.service';
import { Cron, CronExpression } from '@nestjs/schedule';
import { CommandType } from '@prisma/client';
import * as crypto from 'crypto';
import * as bcrypt from 'bcrypt';

@Injectable()
export class CombosService {
  private readonly logger = new Logger(CombosService.name);

  constructor(
    private prisma: PrismaService,
    private commandsService: CommandsService,
    private pcsService: PcsService
  ) {}

  async findAll() {
    return this.prisma.comboPackage.findMany({
      orderBy: { createdAt: 'desc' },
    });
  }

  async create(data: CreateComboDto) {
    return this.prisma.comboPackage.create({ data });
  }

  async update(id: string, data: CreateComboDto) {
    const combo = await this.prisma.comboPackage.findUnique({ where: { id } });
    if (!combo) throw new NotFoundException('Combo not found');
    return this.prisma.comboPackage.update({
      where: { id },
      data,
    });
  }

  async remove(id: string) {
    return this.prisma.comboPackage.delete({ where: { id } });
  }

  async generateCards(data: GenerateComboDto) {
    const combo = await this.prisma.comboPackage.findUnique({
      where: { id: data.comboId },
    });
    if (!combo) throw new NotFoundException('Combo package not found');

    const today = new Date();
    today.setHours(0, 0, 0, 0);
    const generatedTodayCount = await this.prisma.member.count({
      where: {
        memberType: 'COMBO',
        fullName: `Combo ${combo.name}`,
        createdAt: { gte: today }
      }
    });

    const quantityToGenerate = Math.max(0, data.quantity - generatedTodayCount);
    const generated = [];
    if (quantityToGenerate === 0) {
      return generated;
    }

    let currentIdx = await this.prisma.member.count({ where: { memberType: 'COMBO' } }) + 1;

    for (let i = 0; i < quantityToGenerate; i++) {
      let username = `combo${currentIdx}`;
      let exists = await this.prisma.member.findUnique({ where: { username } });
      while (exists) {
        currentIdx++;
        username = `combo${currentIdx}`;
        exists = await this.prisma.member.findUnique({ where: { username } });
      }

      // Generate random 4 digit password
      const password = Math.floor(1000 + Math.random() * 9000).toString();
      
      const salt = await bcrypt.genSalt(10);
      const passwordHash = await bcrypt.hash(password, salt);

      // Validity logic: expires after X days
      const expiresAt = new Date();
      expiresAt.setDate(expiresAt.getDate() + combo.validityDays);
      
      const [endHour, endMin] = combo.endTime.split(':').map(Number);
      expiresAt.setHours(endHour, endMin, 0, 0);

      const isLimitedCombo = Boolean(combo.durationHours && combo.durationHours > 0);
      const initialPlaySeconds = isLimitedCombo ? combo.durationHours * 3600 : 0;

      const member = await this.prisma.member.create({
        data: {
          username,
          fullName: `Combo ${combo.name}`,
          passwordHash,
          memberType: 'COMBO',
          comboExpiresAt: expiresAt,
          isLimitedCombo,
          playSeconds: initialPlaySeconds,
          balance: 0,
        },
      });

      generated.push({ 
        username, 
        password, 
        comboName: combo.name, 
        expiresAt,
        price: Number(combo.price),
        startTime: combo.startTime,
        endTime: combo.endTime,
        durationHours: combo.durationHours,
        validityDays: combo.validityDays
      });
      currentIdx++;
    }

    return generated;
  }

  @Cron(CronExpression.EVERY_MINUTE)
  async scanAndLockExpiredCombos() {
    const now = new Date();
    
    // Lock PCs that are currently used by expired COMBO members
    const inUsePcs = await this.prisma.pc.findMany({ where: { status: 'IN_USE' } });
    for (const pc of inUsePcs) {
      const activeMember = await this.pcsService.getActiveMemberForPc(pc.id);
      if (activeMember && activeMember.memberType === 'COMBO') {
        const memberEntity = await this.prisma.member.findUnique({ where: { id: activeMember.memberId } });
        if (memberEntity && memberEntity.comboExpiresAt && memberEntity.comboExpiresAt < now) {
          this.logger.log(`Combo expired for member ${activeMember.username} on PC ${pc.name}. Locking PC...`);
          await this.commandsService.createLockCommand(pc.id, 'SYSTEM');
        }
      }
    }

    // Delete COMBO accounts that have been expired for more than 5 minutes 
    const cutoff = new Date(now.getTime() - 5 * 60000);
    const expiredMembers = await this.prisma.member.findMany({
      where: {
        memberType: 'COMBO',
        comboExpiresAt: {
          lt: cutoff
        }
      }
    });

    if (expiredMembers.length > 0) {
      const expiredIds = expiredMembers.map(m => m.id);
      
      await this.prisma.memberTransaction.deleteMany({
        where: { memberId: { in: expiredIds } }
      });
      await this.prisma.memberDailyCheckin.deleteMany({
        where: { memberId: { in: expiredIds } }
      });
      
      // Update session's closedReason if not already CLOSED?
      // Actually we just delete the Member entity
      await this.prisma.member.deleteMany({
        where: { id: { in: expiredIds } }
      });

      this.logger.log(`Cleaned up ${expiredMembers.length} expired COMBO accounts.`);
    }
  }
}
