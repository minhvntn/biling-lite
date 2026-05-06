import {
  BadRequestException,
  ConflictException,
  Injectable,
  NotFoundException,
  UnauthorizedException,
} from '@nestjs/common';
import { ConfigService } from '@nestjs/config';
import {
  EventSource,
  Member,
  MemberTransaction,
  MemberTransactionType,
  Prisma,
} from '@prisma/client';
import { createHash } from 'crypto';
import { PrismaService } from '../prisma/prisma.service';
import { BuyPlaytimeDto } from './dto/buy-playtime.dto';
import { CreateMemberDto } from './dto/create-member.dto';
import { TopupMemberDto } from './dto/topup-member.dto';
import { AdjustBalanceDto } from './dto/adjust-balance.dto';
import { MemberLoginDto } from './dto/member-login.dto';
import { UpdateMemberDto } from './dto/update-member.dto';
import { TransferBalanceDto } from './dto/transfer-balance.dto';
import { UpdateLoyaltySettingsDto } from './dto/update-loyalty-settings.dto';
import { RecordMemberUsageDto } from './dto/record-member-usage.dto';
import { RedeemLoyaltyPointsDto } from './dto/redeem-loyalty-points.dto';
import { SetMemberPresenceDto } from './dto/set-member-presence.dto';
import { UpdateLoyaltyRankDto } from './dto/update-loyalty-rank.dto';

const LOYALTY_CONFIG_KEY = '__LOYALTY_MEMBER_POINTS__';
const LOYALTY_MINUTES_PER_POINT = 15;
const LOYALTY_SECONDS_PER_POINT = LOYALTY_MINUTES_PER_POINT * 60;
const LOYALTY_REDEEM_SECONDS_PER_POINT = 60;
const LOYALTY_USAGE_CREATED_BY = 'client.session.loyalty';
const LOYALTY_REDEEM_CREATED_BY = 'client.loyalty';
const LOYALTY_REDEEM_NOTE_PREFIX = 'LOYALTY_REDEEM';

@Injectable()
export class MembersService {
  constructor(
    private readonly prisma: PrismaService,
    private readonly configService: ConfigService,
  ) {}

  async getMembers(search?: string) {
    const keyword = search?.trim();

    const [members, rankConfigs] = await Promise.all([
      this.prisma.member.findMany({
        where: keyword
          ? {
              OR: [
                { username: { contains: keyword, mode: 'insensitive' } },
                { fullName: { contains: keyword, mode: 'insensitive' } },
                { phone: { contains: keyword, mode: 'insensitive' } },
                { identityNumber: { contains: keyword, mode: 'insensitive' } },
              ],
            }
          : undefined,
        orderBy: [{ createdAt: 'desc' }],
        take: 200,
      }),
      this.prisma.loyaltyRankConfig.findMany({
        orderBy: { minTopup: 'desc' },
      }),
    ]);

    const items = await Promise.all(
      members.map(async (member) => {
        const rank = this.calculateRankName(Number(member.totalTopup), rankConfigs);
        const loyalty = await this.buildLoyaltySnapshot(member.id, this.prisma);
        return this.toMemberItem(member, rank, loyalty.availablePoints);
      }),
    );
    
    return {
      items,
      total: members.length,
      serverTime: new Date().toISOString(),
    };
  }

  async createMember(payload: CreateMemberDto) {
    try {
      const [member, rankConfigs] = await Promise.all([
        this.prisma.member.create({
          data: {
            username: payload.username,
            fullName: payload.fullName ?? payload.username,
            passwordHash: payload.password
              ? this.hashPassword(payload.password)
              : null,
            phone: payload.phone,
            identityNumber: payload.identityNumber,
          },
        }),
        this.prisma.loyaltyRankConfig.findMany({
          orderBy: { minTopup: 'desc' },
        }),
      ]);

      return this.toMemberItem(
        member,
        this.calculateRankName(Number(member.totalTopup), rankConfigs),
      );
    } catch (error) {
      if (error instanceof Prisma.PrismaClientKnownRequestError && error.code === 'P2002') {
        throw new ConflictException('Username da ton tai');
      }

      throw error;
    }
  }

  async login(payload: MemberLoginDto) {
    const username = payload.username.trim();
    const password = payload.password;

    const member = await this.prisma.member.findFirst({
      where: {
        username: {
          equals: username,
          mode: 'insensitive',
        },
      },
    });

    if (!member) {
      throw new UnauthorizedException('Sai tài khoản hoặc mật khẩu');
    }

    if (!member.isActive) {
      throw new UnauthorizedException('Tài khoản hội viên đã bị khóa');
    }

    if (!member.passwordHash) {
      throw new UnauthorizedException('Tài khoản hội viên chưa cài mật khẩu');
    }

    if (this.hashPassword(password) !== member.passwordHash) {
      throw new UnauthorizedException('Sai tài khoản hoặc mật khẩu');
    }

    const [rankConfigs] = await Promise.all([
      this.prisma.loyaltyRankConfig.findMany({
        orderBy: { minTopup: 'desc' },
      }),
    ]);

    let hourlyRate = 12000;
    if (payload.agentId) {
      const pc = await this.prisma.pc.findUnique({
        where: { agentId: payload.agentId },
        include: { group: true },
      });
      if (pc) {
        const defaultGroup = await this.prisma.pcGroup.findFirst({
          where: { isDefault: true },
        });
        const baseRate = Number(
          pc.group?.hourlyRate ?? defaultGroup?.hourlyRate ?? 12000,
        );
        // We need to call PcsService or duplicate the logic.
        // For simplicity, I'll duplicate the logic or inject PcsService if possible.
        // Actually, let's inject PcsService or move the logic to a shared utility.
        // But PcsService is already available in the app.
        // Since I cannot easily refactor everything now, I'll use a private method or duplicate.
        hourlyRate = await this.getEffectiveHourlyRate(baseRate);
      }
    }

    return {
      member: this.toMemberItem(
        member,
        this.calculateRankName(Number(member.totalTopup), rankConfigs),
      ),
      authenticatedAt: new Date().toISOString(),
      hourlyRate,
    };
  }

  async setMemberPresence(payload: SetMemberPresenceDto) {
    const agentId = payload.agentId.trim();
    const memberId = payload.memberId.trim();
    const isActive = payload.isActive;

    const [pc, member] = await Promise.all([
      this.prisma.pc.findUnique({
        where: { agentId },
      }),
      this.prisma.member.findUnique({
        where: { id: memberId },
      }),
    ]);

    if (!pc) {
      throw new NotFoundException('Khong tim thay may tram');
    }

    if (!member) {
      throw new NotFoundException('Khong tim thay hoi vien');
    }

    const username = payload.username?.trim() || member.username;
    const fullName = payload.fullName?.trim() || member.fullName;

    await this.prisma.eventLog.create({
      data: {
        source: EventSource.CLIENT,
        eventType: 'member.pc.presence',
        pcId: pc.id,
        payload: {
          memberId: member.id,
          username,
          fullName,
          isActive,
          at: new Date().toISOString(),
        },
      },
    });

    if (isActive) {
      await this.prisma.session.updateMany({
        where: { pcId: pc.id, status: 'ACTIVE' },
        data: {
          endedAt: new Date(),
          status: 'CLOSED',
          closedReason: 'SYSTEM',
        },
      });

      // Upfront 1 minute playSeconds deduction (60 seconds) for member login
      const currentPlaySeconds = Math.max(0, member.playSeconds);
      const consumedSeconds = Math.min(60, currentPlaySeconds);

      if (consumedSeconds > 0) {
        await this.prisma.member.update({
          where: { id: member.id },
          data: {
            playSeconds: {
              decrement: consumedSeconds,
            },
          },
        });

        await this.prisma.memberTransaction.create({
          data: {
            memberId: member.id,
            type: 'ADJUSTMENT',
            amountDelta: 0,
            playSecondsDelta: -consumedSeconds,
            note: 'UPFRONT_LOGIN_CHARGE',
            createdBy: 'client.session',
          },
        });
      }

      const defaultGroup = await this.prisma.pcGroup.findFirst({
        where: { isDefault: true },
      });
      const baseRate = Number(
        pc.groupId
          ? (await this.prisma.pcGroup.findUnique({ where: { id: pc.groupId } }))?.hourlyRate ?? defaultGroup?.hourlyRate ?? 12000
          : defaultGroup?.hourlyRate ?? 12000,
      );
      const hourlyRate = await this.getEffectiveHourlyRate(baseRate);
      const pricePerMinute = Number(hourlyRate) / 60;

      await this.prisma.session.create({
        data: {
          pcId: pc.id,
          status: 'ACTIVE',
          startedAt: new Date(),
          pricePerMinute,
        },
      });
    } else {
      const activeSession = await this.prisma.session.findFirst({
        where: {
          pcId: pc.id,
          status: 'ACTIVE',
        },
        orderBy: { startedAt: 'desc' },
      });

      if (activeSession) {
        const endedAt = new Date();
        const durationSeconds = Math.max(
          0,
          Math.floor((endedAt.getTime() - activeSession.startedAt.getTime()) / 1000),
        );
        const billableMinutes = Math.max(1, Math.ceil(durationSeconds / 60));
        const pricePerMinute = Number(activeSession.pricePerMinute ?? 0);
        const amount = billableMinutes * pricePerMinute;

        await this.prisma.session.update({
          where: { id: activeSession.id },
          data: {
            endedAt,
            durationSeconds,
            billableMinutes,
            amount,
            status: 'CLOSED',
            closedReason: 'ADMIN_LOCK',
          },
        });
      }
    }

    return {
      ok: true,
      agentId,
      pcId: pc.id,
      memberId: member.id,
      username,
      isActive,
      updatedAt: new Date().toISOString(),
    };
  }

  async setGuestPresence(payload: {
    agentId: string;
    isActive: boolean;
    displayName?: string;
    prepaidAmount?: number;
  }) {
    const agentId = payload.agentId.trim();
    const pc = await this.prisma.pc.findUnique({
      where: { agentId },
    });

    if (!pc) {
      throw new NotFoundException('Khong tim thay may tram');
    }

    await this.prisma.eventLog.create({
      data: {
        source: EventSource.CLIENT,
        eventType: 'guest.pc.presence',
        pcId: pc.id,
        payload: {
          isActive: payload.isActive,
          displayName: payload.displayName || 'Khách vãng lai',
          prepaidAmount: payload.prepaidAmount || 0,
          at: new Date().toISOString(),
        },
      },
    });

    return { ok: true };
  }

  async getLoyaltySettings() {
    const config = await this.prisma.pricingConfig.findUnique({
      where: { name: LOYALTY_CONFIG_KEY },
    });

    return {
      enabled: config?.isActive ?? true,
      minutesPerPoint: LOYALTY_MINUTES_PER_POINT,
    };
  }

  async getEffectiveHourlyRate(baseRate: number): Promise<number> {
    const now = new Date();
    const day = now.getDay();
    const currentTime =
      now.getHours().toString().padStart(2, '0') +
      ':' +
      now.getMinutes().toString().padStart(2, '0');

    const promotions = await this.prisma.timeBasedPromotion.findMany({
      where: { isActive: true },
    });

    let bestDiscount = 0;
    for (const promo of promotions) {
      if (promo.daysOfWeek.includes(day)) {
        if (currentTime >= promo.startTime && currentTime <= promo.endTime) {
          const discount = Number(promo.discountPercent);
          if (discount > bestDiscount) {
            bestDiscount = discount;
          }
        }
      }
    }

    if (bestDiscount > 0) {
      const discounted = baseRate * (1 - bestDiscount / 100);
      return Math.round(discounted);
    }

    return baseRate;
  }

  async updateLoyaltySettings(payload: UpdateLoyaltySettingsDto) {
    const config = await this.prisma.pricingConfig.upsert({
      where: { name: LOYALTY_CONFIG_KEY },
      update: {
        isActive: payload.enabled,
      },
      create: {
        name: LOYALTY_CONFIG_KEY,
        pricePerMinute: 0,
        isActive: payload.enabled,
      },
    });

    return {
      ...this.toLoyaltySettingsItem(config.isActive, config.updatedAt),
      updatedBy: payload.updatedBy?.trim() || 'admin.desktop',
    };
  }

  async getMemberLoyalty(memberId: string) {
    const member = await this.prisma.member.findUnique({ where: { id: memberId } });
    if (!member) {
      throw new NotFoundException('Khong tim thay hoi vien');
    }

    const enabled = await this.getLoyaltyFeatureEnabled();
    const snapshot = await this.buildLoyaltySnapshot(member.id, this.prisma);

    return {
      enabled,
      config: this.toLoyaltySettingsItem(enabled),
      member: this.toMemberItem(member),
      loyalty: snapshot,
      exchangeRate: {
        pointsToMinutes: 1,
      },
    };
  }

  async recordMemberUsage(memberId: string, payload: RecordMemberUsageDto) {
    const note = payload.note?.trim() || 'SESSION_USAGE';
    const requestedSeconds = payload.usedSeconds;

    return this.prisma.$transaction(async (tx) => {
      const member = await tx.member.findUnique({ where: { id: memberId } });
      if (!member) {
        throw new NotFoundException('Khong tim thay hoi vien');
      }

      const currentPlaySeconds = Math.max(0, member.playSeconds);
      const consumedSeconds = Math.max(0, Math.min(currentPlaySeconds, requestedSeconds));
      let updatedMember = member;
      const enabled = await this.getLoyaltyFeatureEnabled(tx);
      const createdBy = enabled
        ? LOYALTY_USAGE_CREATED_BY
        : payload.createdBy?.trim() || 'client.session';

      if (consumedSeconds > 0) {
        updatedMember = await tx.member.update({
          where: { id: member.id },
          data: {
            playSeconds: {
              decrement: consumedSeconds,
            },
          },
        });

        await tx.memberTransaction.create({
          data: {
            memberId: member.id,
            type: MemberTransactionType.ADJUSTMENT,
            amountDelta: 0,
            playSecondsDelta: -consumedSeconds,
            note,
            createdBy,
          },
        });
      }

      const loyalty = await this.buildLoyaltySnapshot(member.id, tx);

      return {
        member: this.toMemberItem(updatedMember),
        consumedSeconds,
        requestedSeconds,
        enabled,
        loyalty,
        trackedAt: new Date().toISOString(),
      };
    });
  }

  async redeemLoyaltyPoints(memberId: string, payload: RedeemLoyaltyPointsDto) {
    const requestedPoints = Math.max(1, Math.floor(payload.points));
    const createdBy = payload.createdBy?.trim() || LOYALTY_REDEEM_CREATED_BY;
    const note =
      payload.note?.trim() || `${LOYALTY_REDEEM_NOTE_PREFIX}: doi ${requestedPoints} diem`;

    return this.prisma.$transaction(async (tx) => {
      const member = await tx.member.findUnique({ where: { id: memberId } });
      if (!member) {
        throw new NotFoundException('Khong tim thay hoi vien');
      }

      const enabled = await this.getLoyaltyFeatureEnabled(tx);
      if (!enabled) {
        throw new BadRequestException('Tinh nang diem tich luy dang tat');
      }

      const before = await this.buildLoyaltySnapshot(member.id, tx);
      if (before.availablePoints < requestedPoints) {
        throw new BadRequestException(
          `Khong du diem. Hien chi con ${before.availablePoints} diem`,
        );
      }

      const playSecondsDelta = requestedPoints * LOYALTY_REDEEM_SECONDS_PER_POINT;
      const updatedMember = await tx.member.update({
        where: { id: member.id },
        data: {
          playSeconds: {
            increment: playSecondsDelta,
          },
        },
      });

      await tx.memberTransaction.create({
        data: {
          memberId: member.id,
          type: MemberTransactionType.ADJUSTMENT,
          amountDelta: 0,
          playSecondsDelta,
          note,
          createdBy,
        },
      });

      const after = await this.buildLoyaltySnapshot(member.id, tx);

      return {
        member: this.toMemberItem(updatedMember),
        redeemedPoints: requestedPoints,
        grantedSeconds: playSecondsDelta,
        grantedMinutes: requestedPoints,
        loyalty: after,
        redeemedAt: new Date().toISOString(),
      };
    });
  }

  async spinLoyaltyPoints(memberId: string, payload: { createdBy?: string; note?: string }) {
    const costPoints = 5;
    const costSeconds = costPoints * LOYALTY_REDEEM_SECONDS_PER_POINT; // 300 seconds
    const createdBy = payload.createdBy?.trim() || 'client.loyalty.spin';
    const spinNotePrefix = 'LOYALTY_SPIN';

    return this.prisma.$transaction(async (tx) => {
      const member = await tx.member.findUnique({ where: { id: memberId } });
      if (!member) {
        throw new NotFoundException('Khong tim thay hoi vien');
      }

      const enabled = await this.getLoyaltyFeatureEnabled(tx);
      if (!enabled) {
        throw new BadRequestException('Tinh nang diem tich luy dang tat');
      }

      const before = await this.buildLoyaltySnapshot(member.id, tx);
      if (before.availablePoints < costPoints) {
        throw new BadRequestException(
          `Khong du diem. Can ${costPoints} diem, ban hien co ${before.availablePoints} diem`,
        );
      }

      // Roll for prize
      const roll = Math.random() * 100;
      let wonMinutes = 0;
      let prizeLabel = 'Hen gap lai lan sau';

      if (roll < 5) {
        wonMinutes = 30;
        prizeLabel = "GIẢI ĐẶC BIỆT 30p";
      } else if (roll < 15) {
        wonMinutes = 20;
        prizeLabel = "GIẢI NHẤT 20p";
      } else if (roll < 30) {
        wonMinutes = 10;
        prizeLabel = "GIẢI NHÌ 10p";
      } else if (roll < 50) {
        wonMinutes = 5;
        prizeLabel = "GIẢI BA 5p";
      } else if (roll < 75) {
        wonMinutes = 2;
        prizeLabel = "KHUYẾN KHÍCH 2p";
      }

      // 1. Spend points by creating a "Redeem" transaction (cost 5 pts)
      // This will be counted in redeemedPoints because it uses LOYALTY_REDEEM_CREATED_BY
      await tx.memberTransaction.create({
        data: {
          memberId: member.id,
          type: MemberTransactionType.ADJUSTMENT,
          amountDelta: 0,
          playSecondsDelta: costSeconds,
          note: `${LOYALTY_REDEEM_NOTE_PREFIX}_SPIN: Cost ${costPoints} points`,
          createdBy: LOYALTY_REDEEM_CREATED_BY,
        },
      });

      // 2. Adjust playtime based on win (subtracting the cost already added)
      const adjustmentSeconds = (wonMinutes * 60) - costSeconds;
      const updatedMember = await tx.member.update({
        where: { id: member.id },
        data: {
          playSeconds: {
            increment: (wonMinutes * 60), // Net gain is wonMinutes
          },
        },
      });

      await tx.memberTransaction.create({
        data: {
          memberId: member.id,
          type: MemberTransactionType.ADJUSTMENT,
          amountDelta: 0,
          playSecondsDelta: adjustmentSeconds,
          note: `${spinNotePrefix}: Won ${wonMinutes} mins (${prizeLabel})`,
          createdBy,
        },
      });

      const after = await this.buildLoyaltySnapshot(member.id, tx);

      return {
        member: this.toMemberItem(updatedMember),
        wonMinutes,
        prizeLabel,
        costPoints,
        loyalty: after,
        spunAt: new Date().toISOString(),
      };
    });
  }

  async topupMember(memberId: string, payload: TopupMemberDto) {
    const amount = this.roundMoney(payload.amount);
    const createdBy = payload.createdBy?.trim() || 'admin.web';

    const [result, rankConfigs] = await this.prisma.$transaction(async (tx) => {
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
          totalTopup: {
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

      const configs = await tx.loyaltyRankConfig.findMany({
        orderBy: { minTopup: 'desc' },
      });

      return [{ updatedMember, transaction }, configs] as const;
    });

    return {
      member: this.toMemberItem(
        result.updatedMember,
        this.calculateRankName(Number(result.updatedMember.totalTopup), rankConfigs),
      ),
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

    const [result, rankConfigs] = await this.prisma.$transaction(async (tx) => {
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

      const configs = await tx.loyaltyRankConfig.findMany({
        orderBy: { minTopup: 'desc' },
      });

      return [{ updatedMember, transaction, cost, hours, ratePerHour }, configs] as const;
    });

    return {
      member: this.toMemberItem(
        result.updatedMember,
        this.calculateRankName(Number(result.updatedMember.totalTopup), rankConfigs),
      ),
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

    const [result, rankConfigs] = await this.prisma.$transaction(async (tx) => {
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

      const configs = await tx.loyaltyRankConfig.findMany({
        orderBy: { minTopup: 'desc' },
      });

      return [{ updatedMember, transaction }, configs] as const;
    });

    return {
      member: this.toMemberItem(
        result.updatedMember,
        this.calculateRankName(Number(result.updatedMember.totalTopup), rankConfigs),
      ),
      transaction: this.toTransactionItem(result.transaction),
    };
  }

  async updateMember(memberId: string, payload: UpdateMemberDto) {
    return this.prisma.$transaction(async (tx) => {
      const existing = await tx.member.findUnique({ where: { id: memberId } });
      if (!existing) {
        throw new NotFoundException('Khong tim thay hoi vien');
      }

      const data: Prisma.MemberUpdateInput = {};
      if (payload.fullName !== undefined) {
        data.fullName = payload.fullName;
      }

      if (payload.phone !== undefined) {
        data.phone = payload.phone || null;
      }

      if (payload.identityNumber !== undefined) {
        data.identityNumber = payload.identityNumber || null;
      }

      if (payload.password !== undefined) {
        if (!payload.password.trim()) {
          throw new BadRequestException('Mat khau khong hop le');
        }

        data.passwordHash = this.hashPassword(payload.password);
      }

      if (payload.isActive !== undefined) {
        data.isActive = payload.isActive;
      }

      const balanceValue =
        payload.balance !== undefined ? this.roundMoneyZeroAllowed(payload.balance) : null;
      const playSecondsValue =
        payload.playHours !== undefined
          ? Math.max(0, Math.round(payload.playHours * 3600))
          : null;

      if (balanceValue !== null) {
        data.balance = balanceValue;
      }

      if (playSecondsValue !== null) {
        data.playSeconds = playSecondsValue;
      }

      const hasProfileChanges = Object.keys(data).length > 0;
      let updated = existing;
      if (hasProfileChanges) {
        updated = await tx.member.update({
          where: { id: memberId },
          data,
        });
      }

      const oldBalance = Number(existing.balance);
      const newBalance = Number(updated.balance);
      const amountDelta = this.roundMoneyAllowZero(newBalance - oldBalance);
      const playSecondsDelta = updated.playSeconds - existing.playSeconds;

      if (amountDelta !== 0 || playSecondsDelta !== 0) {
        await tx.memberTransaction.create({
          data: {
            memberId: existing.id,
            type: MemberTransactionType.ADJUSTMENT,
            amountDelta,
            playSecondsDelta,
            note: payload.note ?? 'Cap nhat thong tin hoi vien',
            createdBy: payload.updatedBy?.trim() || 'admin.desktop',
          },
        });
      }

      const configs = await tx.loyaltyRankConfig.findMany({
        orderBy: { minTopup: 'desc' },
      });

      // Handle availablePoints adjustment
      if (payload.availablePoints !== undefined) {
        const loyalty = await this.buildLoyaltySnapshot(memberId, tx);
        const pointsDiff = payload.availablePoints - loyalty.availablePoints;
        if (pointsDiff !== 0) {
          if (pointsDiff > 0) {
            // Add points by adding a "pseudo-usage" transaction (earning)
            // 1 point = LOYALTY_SECONDS_PER_POINT
            const secondsToAdd = pointsDiff * LOYALTY_SECONDS_PER_POINT;
            await tx.memberTransaction.create({
              data: {
                memberId,
                type: MemberTransactionType.ADJUSTMENT,
                amountDelta: 0,
                playSecondsDelta: -secondsToAdd,
                note: `Admin điều chỉnh tăng ${pointsDiff} điểm`,
                createdBy: LOYALTY_USAGE_CREATED_BY,
              },
            });
          } else {
            // Subtract points by adding a "pseudo-redemption" transaction
            const pointsToSubtract = Math.abs(pointsDiff);
            const secondsToRedeem = pointsToSubtract * LOYALTY_REDEEM_SECONDS_PER_POINT;
            await tx.memberTransaction.create({
              data: {
                memberId,
                type: MemberTransactionType.ADJUSTMENT,
                amountDelta: 0,
                playSecondsDelta: secondsToRedeem,
                note: `${LOYALTY_REDEEM_NOTE_PREFIX}: Admin điều chỉnh giảm ${pointsToSubtract} điểm`,
                createdBy: LOYALTY_REDEEM_CREATED_BY,
              },
            });
          }
        }
      }

      // Re-fetch member if points were adjusted or profile changed
      const finalMember = await tx.member.findUnique({ where: { id: memberId } });
      const finalLoyalty = await this.buildLoyaltySnapshot(memberId, tx);

      return {
        member: this.toMemberItem(
          finalMember!,
          this.calculateRankName(Number(finalMember!.totalTopup), configs),
          finalLoyalty.availablePoints,
        ),
      };
    });
  }

  async transferBalance(memberId: string, payload: TransferBalanceDto) {
    const amount = this.roundMoney(payload.amount);
    const createdBy = payload.createdBy?.trim() || 'admin.desktop';
    const targetUsername = payload.targetUsername.trim();
    const [result, rankConfigs] = await this.prisma.$transaction(async (tx) => {
      const source = await tx.member.findUnique({ where: { id: memberId } });
      if (!source) {
        throw new NotFoundException('Khong tim thay hoi vien chuyen tien');
      }

      const target = await tx.member.findFirst({
        where: {
          username: {
            equals: targetUsername,
            mode: 'insensitive',
          },
        },
      });

      if (!target) {
        throw new NotFoundException('Khong tim thay hoi vien nhan tien');
      }

      if (source.id === target.id) {
        throw new BadRequestException('Khong the chuyen tien cho chinh minh');
      }

      if (!source.isActive || !target.isActive) {
        throw new BadRequestException('Tai khoan hoi vien khong hoat dong');
      }

      const sourceBalance = Number(source.balance);
      if (sourceBalance < amount) {
        throw new BadRequestException('So du khong du de chuyen tien');
      }

      const [updatedSource, updatedTarget] = await Promise.all([
        tx.member.update({
          where: { id: source.id },
          data: {
            balance: {
              decrement: amount,
            },
          },
        }),
        tx.member.update({
          where: { id: target.id },
          data: {
            balance: {
              increment: amount,
            },
          },
        }),
      ]);

      const sourceNote =
        payload.note?.trim() || `Chuyen tien cho ${updatedTarget.username}`;
      const targetNote =
        payload.note?.trim() || `Nhan tien tu ${updatedSource.username}`;

      await tx.memberTransaction.createMany({
        data: [
          {
            memberId: updatedSource.id,
            type: MemberTransactionType.ADJUSTMENT,
            amountDelta: -amount,
            playSecondsDelta: 0,
            note: sourceNote,
            createdBy,
          },
          {
            memberId: updatedTarget.id,
            type: MemberTransactionType.ADJUSTMENT,
            amountDelta: amount,
            playSecondsDelta: 0,
            note: targetNote,
            createdBy,
          },
        ],
      });

      const configs = await tx.loyaltyRankConfig.findMany({
        orderBy: { minTopup: 'desc' },
      });

      return [
        {
          sourceMember: this.toMemberItem(updatedSource, this.calculateRankName(Number(updatedSource.totalTopup), configs)),
          targetMember: this.toMemberItem(updatedTarget, this.calculateRankName(Number(updatedTarget.totalTopup), configs)),
          amount,
          transferredAt: new Date().toISOString(),
        },
        configs,
      ] as const;
    });

    await this.logMemberTransferEvent(result, payload, createdBy);
    return result;
  }

  private toMemberItem(member: Member, rankName?: string, availablePoints?: number) {
    return {
      id: member.id,
      username: member.username,
      fullName: member.fullName,
      phone: member.phone,
      identityNumber: member.identityNumber,
      hasPassword: Boolean(member.passwordHash),
      balance: Number(member.balance),
      playSeconds: member.playSeconds,
      playHours: Number((member.playSeconds / 3600).toFixed(2)),
      totalTopup: Number(member.totalTopup),
      rank: rankName || 'N/A',
      availablePoints: availablePoints ?? 0,
      isActive: member.isActive,
      createdAt: member.createdAt.toISOString(),
      updatedAt: member.updatedAt.toISOString(),
    };
  }

  private calculateRankName(
    totalTopup: number,
    configs: { rankName: string; minTopup: Prisma.Decimal }[],
  ): string {
    if (configs.length === 0) return 'S\u1eaft';
    const matched = configs.find((c) => totalTopup >= Number(c.minTopup));
    return matched?.rankName || 'S\u1eaft';
  }

  async getLoyaltyRanks() {
    return this.prisma.loyaltyRankConfig.findMany({
      orderBy: { minTopup: 'asc' },
    });
  }

  async updateLoyaltyRank(rankId: string, payload: UpdateLoyaltyRankDto) {
    const updated = await this.prisma.loyaltyRankConfig.update({
      where: { id: rankId },
      data: {
        rankName: payload.rankName,
        minTopup: payload.minTopup,
        bonusPercent: payload.bonusPercent,
        minutesPerPoint: payload.minutesPerPoint,
      },
    });

    return updated;
  }

  async rebuildRanks(maxThreshold: number) {
    if (!Number.isFinite(maxThreshold) || maxThreshold <= 0) {
      throw new BadRequestException('Ngưỡng tối đa không hợp lệ');
    }

    const categories = [
      'Sắt', 'Đồng', 'Bạc', 'Vàng', 'Bạch Kim', 
      'Lục Bảo', 'Kim Cương', 'Cao Thủ', 'Đại Cao Thủ', 'Thách Đấu'
    ];
    const tiersPerCategory = 10;
    const totalRanks = categories.length * tiersPerCategory;

    const startMinutes = 150;
    const endMinutes = 15;
    const startBonus = 0;
    const endBonus = 100;

    return this.prisma.$transaction(async (tx) => {
      await tx.loyaltyRankConfig.deleteMany({});

      for (let i = 0; i < totalRanks; i++) {
        const catIndex = Math.floor(i / tiersPerCategory);
        const tier = (i % tiersPerCategory) + 1;
        const rankName = `${categories[catIndex]} ${tier}`;
        const factor = i / (totalRanks - 1);

        const minTopup = Math.round(maxThreshold * factor);
        const minutesPerPoint = Math.round(startMinutes - (startMinutes - endMinutes) * factor);
        const bonusPercent = Math.round(startBonus + (endBonus - startBonus) * factor);

        await tx.loyaltyRankConfig.create({
          data: {
            rankName,
            minTopup,
            bonusPercent,
            minutesPerPoint
          }
        });
      }

      return { success: true, count: totalRanks, maxThreshold };
    });
  }

  private toLoyaltySettingsItem(enabled: boolean, updatedAt?: Date) {
    return {
      enabled,
      minutesPerPoint: LOYALTY_MINUTES_PER_POINT,
      pointsToMinutes: 1,
      updatedAt: (updatedAt ?? new Date()).toISOString(),
    };
  }

  private async getLoyaltyFeatureEnabled(
    tx?: Prisma.TransactionClient,
  ): Promise<boolean> {
    const prisma = tx ?? this.prisma;
    const config = await prisma.pricingConfig.findUnique({
      where: { name: LOYALTY_CONFIG_KEY },
    });

    return config?.isActive ?? false;
  }

  private async buildLoyaltySnapshot(
    memberId: string,
    tx: Prisma.TransactionClient | PrismaService,
  ) {
    const usageAggregate = await tx.memberTransaction.aggregate({
      _sum: {
        playSecondsDelta: true,
      },
      where: {
        memberId,
        createdBy: LOYALTY_USAGE_CREATED_BY,
        playSecondsDelta: {
          lt: 0,
        },
      },
    });

    const redeemAggregate = await tx.memberTransaction.aggregate({
      _sum: {
        playSecondsDelta: true,
      },
      where: {
        memberId,
        createdBy: LOYALTY_REDEEM_CREATED_BY,
        note: {
          startsWith: LOYALTY_REDEEM_NOTE_PREFIX,
        },
        playSecondsDelta: {
          gt: 0,
        },
      },
    });

    const member = await tx.member.findUnique({ where: { id: memberId } });
    const rankConfigs = await tx.loyaltyRankConfig.findMany({
      orderBy: { minTopup: 'desc' },
    });

    const memberRank =
      rankConfigs.find((c) => Number(member!.totalTopup) >= Number(c.minTopup)) ||
      rankConfigs[rankConfigs.length - 1];

    const currentMinutesPerPoint = memberRank?.minutesPerPoint ?? LOYALTY_MINUTES_PER_POINT;
    const currentSecondsPerPoint = currentMinutesPerPoint * 60;

    const consumedSeconds = Math.abs(usageAggregate._sum.playSecondsDelta ?? 0);
    const redeemedSeconds = Math.max(0, redeemAggregate._sum.playSecondsDelta ?? 0);
    const earnedPoints = Math.floor(consumedSeconds / currentSecondsPerPoint);
    const redeemedPoints = Math.floor(redeemedSeconds / LOYALTY_REDEEM_SECONDS_PER_POINT);
    const availablePoints = Math.max(0, earnedPoints - redeemedPoints);
    const progressSeconds = consumedSeconds % currentSecondsPerPoint;

    return {
      availablePoints,
      earnedPoints,
      redeemedPoints,
      consumedSeconds,
      progressSeconds,
      progressMinutes: Number((progressSeconds / 60).toFixed(2)),
      minutesPerPoint: currentMinutesPerPoint, // Include current rate in snapshot
      nextPointInSeconds:
        progressSeconds === 0
          ? currentSecondsPerPoint
          : currentSecondsPerPoint - progressSeconds,
      nextPointInMinutes:
        progressSeconds === 0
          ? currentMinutesPerPoint
          : Number(((currentSecondsPerPoint - progressSeconds) / 60).toFixed(2)),
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

  private roundMoneyZeroAllowed(value: number): number {
    if (!Number.isFinite(value) || value < 0) {
      throw new BadRequestException('Gia tri tien khong hop le');
    }

    return Math.round(value * 100) / 100;
  }

  private roundMoneyAllowZero(value: number): number {
    if (!Number.isFinite(value)) {
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

  private async logMemberTransferEvent(
    result: {
      sourceMember: ReturnType<MembersService['toMemberItem']>;
      targetMember: ReturnType<MembersService['toMemberItem']>;
      amount: number;
      transferredAt: string;
    },
    payload: TransferBalanceDto,
    createdBy: string,
  ): Promise<void> {
    const normalizedAgentId = payload.agentId?.trim() || null;
    const eventSource = createdBy.startsWith('client.')
      ? EventSource.CLIENT
      : EventSource.ADMIN;

    let pcId: string | undefined;
    if (normalizedAgentId) {
      const pc = await this.prisma.pc.findFirst({
        where: {
          agentId: {
            equals: normalizedAgentId,
            mode: 'insensitive',
          },
        },
        select: {
          id: true,
        },
      });

      pcId = pc?.id;
    }

    try {
      await this.prisma.eventLog.create({
        data: {
          source: eventSource,
          eventType: 'member.balance.transferred',
          pcId,
          payload: {
            sourceMemberId: result.sourceMember.id,
            sourceUsername: result.sourceMember.username,
            targetMemberId: result.targetMember.id,
            targetUsername: result.targetMember.username,
            amount: result.amount,
            note: payload.note?.trim() || null,
            createdBy,
            agentId: normalizedAgentId,
            transferredAt: result.transferredAt,
          },
        },
      });
    } catch {
      // Transfer already completed, skip logging failure.
    }
  }
}
