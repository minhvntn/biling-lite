import { BadRequestException, ConflictException, Injectable, NotFoundException } from '@nestjs/common';
import { ConfigService } from '@nestjs/config';
import { Member, MemberTransaction, MemberTransactionType, Prisma } from '@prisma/client';
import { createHash } from 'crypto';
import { PrismaService } from '../prisma/prisma.service';
import { BuyPlaytimeDto } from './dto/buy-playtime.dto';
import { CreateMemberDto } from './dto/create-member.dto';
import { TopupMemberDto } from './dto/topup-member.dto';
import { AdjustBalanceDto } from './dto/adjust-balance.dto';

@Injectable()
export class MembersService {
  constructor(
    private readonly prisma: PrismaService,
    private readonly configService: ConfigService,
  ) {}

  async getMembers(search?: string) {
    const keyword = search?.trim();

    const members = await this.prisma.member.findMany({
      where: keyword
        ? {
            OR: [
              { username: { contains: keyword, mode: 'insensitive' } },
              { fullName: { contains: keyword, mode: 'insensitive' } },
              { phone: { contains: keyword, mode: 'insensitive' } },
            ],
          }
        : undefined,
      orderBy: [{ createdAt: 'desc' }],
      take: 200,
    });

    return {
      items: members.map((member) => this.toMemberItem(member)),
      total: members.length,
      serverTime: new Date().toISOString(),
    };
  }

  async createMember(payload: CreateMemberDto) {
    try {
      const member = await this.prisma.member.create({
        data: {
          username: payload.username,
          fullName: payload.fullName ?? payload.username,
          passwordHash: payload.password
            ? this.hashPassword(payload.password)
            : null,
          phone: payload.phone,
        },
      });

      return this.toMemberItem(member);
    } catch (error) {
      if (error instanceof Prisma.PrismaClientKnownRequestError && error.code === 'P2002') {
        throw new ConflictException('Username da ton tai');
      }

      throw error;
    }
  }

  async topupMember(memberId: string, payload: TopupMemberDto) {
    const amount = this.roundMoney(payload.amount);
    const createdBy = payload.createdBy?.trim() || 'admin.web';

    const result = await this.prisma.$transaction(async (tx) => {
      const member = await tx.member.findUnique({ where: { id: memberId } });
      if (!member) {
        throw new NotFoundException('Khong tim thay hoi vien');
      }

      const updatedMember = await tx.member.update({
        where: { id: member.id },
        data: {
          balance: {
            increment: amount,
          },
        },
      });

      const transaction = await tx.memberTransaction.create({
        data: {
          memberId: member.id,
          type: MemberTransactionType.TOPUP,
          amountDelta: amount,
          playSecondsDelta: 0,
          note: payload.note ?? 'Nap tien',
          createdBy,
        },
      });

      return { updatedMember, transaction };
    });

    return {
      member: this.toMemberItem(result.updatedMember),
      transaction: this.toTransactionItem(result.transaction),
    };
  }

  async buyPlaytime(memberId: string, payload: BuyPlaytimeDto) {
    const ratePerHour = this.roundMoney(
      payload.ratePerHour ?? this.getDefaultRatePerHour(),
    );
    const hours = payload.hours;
    const createdBy = payload.createdBy?.trim() || 'admin.web';
    const playSecondsDelta = Math.max(1, Math.round(hours * 3600));
    const cost = this.roundMoney(ratePerHour * hours);

    const result = await this.prisma.$transaction(async (tx) => {
      const member = await tx.member.findUnique({ where: { id: memberId } });
      if (!member) {
        throw new NotFoundException('Khong tim thay hoi vien');
      }

      const currentBalance = Number(member.balance);
      if (currentBalance < cost) {
        throw new BadRequestException(
          `So du khong du. Can them ${(cost - currentBalance).toLocaleString('vi-VN')} VND`,
        );
      }

      const updatedMember = await tx.member.update({
        where: { id: member.id },
        data: {
          balance: {
            decrement: cost,
          },
          playSeconds: {
            increment: playSecondsDelta,
          },
        },
      });

      const transaction = await tx.memberTransaction.create({
        data: {
          memberId: member.id,
          type: MemberTransactionType.BUY_PLAYTIME,
          amountDelta: -cost,
          playSecondsDelta,
          note:
            payload.note ??
            `Mua ${hours} gio choi (${ratePerHour.toLocaleString('vi-VN')} VND/gio)`,
          createdBy,
        },
      });

      return { updatedMember, transaction, cost, hours, ratePerHour };
    });

    return {
      member: this.toMemberItem(result.updatedMember),
      transaction: this.toTransactionItem(result.transaction),
      purchase: {
        hours: result.hours,
        ratePerHour: result.ratePerHour,
        cost: result.cost,
      },
    };
  }

  async getMemberTransactions(memberId: string) {
    const member = await this.prisma.member.findUnique({ where: { id: memberId } });
    if (!member) {
      throw new NotFoundException('Khong tim thay hoi vien');
    }

    const transactions = await this.prisma.memberTransaction.findMany({
      where: { memberId },
      orderBy: [{ createdAt: 'desc' }],
      take: 200,
    });

    return {
      member: this.toMemberItem(member),
      items: transactions.map((item) => this.toTransactionItem(item)),
      total: transactions.length,
      serverTime: new Date().toISOString(),
    };
  }

  async adjustBalance(memberId: string, payload: AdjustBalanceDto) {
    const amountDelta = this.roundMoneyAllowNegative(payload.amountDelta);
    const createdBy = payload.createdBy?.trim() || 'admin.desktop';

    const result = await this.prisma.$transaction(async (tx) => {
      const member = await tx.member.findUnique({ where: { id: memberId } });
      if (!member) {
        throw new NotFoundException('Khong tim thay hoi vien');
      }

      const currentBalance = Number(member.balance);
      const nextBalance = currentBalance + amountDelta;
      if (nextBalance < 0) {
        throw new BadRequestException('So du sau dieu chinh khong duong');
      }

      const updatedMember = await tx.member.update({
        where: { id: member.id },
        data: {
          balance: {
            increment: amountDelta,
          },
        },
      });

      const transaction = await tx.memberTransaction.create({
        data: {
          memberId: member.id,
          type: MemberTransactionType.ADJUSTMENT,
          amountDelta,
          playSecondsDelta: 0,
          note: payload.note ?? 'Dieu chinh so du',
          createdBy,
        },
      });

      return { updatedMember, transaction };
    });

    return {
      member: this.toMemberItem(result.updatedMember),
      transaction: this.toTransactionItem(result.transaction),
    };
  }

  private toMemberItem(member: Member) {
    return {
      id: member.id,
      username: member.username,
      fullName: member.fullName,
      phone: member.phone,
      hasPassword: Boolean(member.passwordHash),
      balance: Number(member.balance),
      playSeconds: member.playSeconds,
      playHours: Number((member.playSeconds / 3600).toFixed(2)),
      isActive: member.isActive,
      createdAt: member.createdAt.toISOString(),
      updatedAt: member.updatedAt.toISOString(),
    };
  }

  private toTransactionItem(item: MemberTransaction) {
    return {
      id: item.id,
      memberId: item.memberId,
      type: item.type,
      amountDelta: Number(item.amountDelta),
      playSecondsDelta: item.playSecondsDelta,
      createdBy: item.createdBy,
      note: item.note,
      createdAt: item.createdAt.toISOString(),
    };
  }

  private roundMoney(value: number): number {
    if (!Number.isFinite(value) || value <= 0) {
      throw new BadRequestException('Gia tri tien khong hop le');
    }

    return Math.round(value * 100) / 100;
  }

  private roundMoneyAllowNegative(value: number): number {
    if (!Number.isFinite(value) || value === 0) {
      throw new BadRequestException('Gia tri tien khong hop le');
    }

    return Math.round(value * 100) / 100;
  }

  private getDefaultRatePerHour(): number {
    const raw = Number(this.configService.get<string>('MEMBER_PLAY_HOUR_PRICE') ?? '15000');
    if (!Number.isFinite(raw) || raw <= 0) {
      return 15000;
    }

    return raw;
  }

  private hashPassword(rawPassword: string): string {
    const salt =
      this.configService.get<string>('MEMBER_PASSWORD_SALT') ??
      'servermanagerbilling-default-salt';

    return createHash('sha256').update(`${salt}:${rawPassword}`).digest('hex');
  }
}
