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
  PcStatus,
  Prisma,
} from '@prisma/client';
import * as bcrypt from 'bcrypt';
import { createHash } from 'crypto';
import { PrismaService } from '../prisma/prisma.service';
import { RealtimeService } from '../realtime/realtime.service';
import { BuyPlaytimeDto } from './dto/buy-playtime.dto';
import { CreateMemberDto } from './dto/create-member.dto';
import { TopupMemberDto } from './dto/topup-member.dto';
import { AdjustBalanceDto } from './dto/adjust-balance.dto';
import { MemberLoginDto } from './dto/member-login.dto';
import { UpdateMemberDto } from './dto/update-member.dto';
import { TransferBalanceDto } from './dto/transfer-balance.dto';
import { WithdrawBalanceDto } from './dto/withdraw-balance.dto';
import { RequestTopupDto } from './dto/request-topup.dto';
import { UpdateLoyaltySettingsDto } from './dto/update-loyalty-settings.dto';
import { RecordMemberUsageDto } from './dto/record-member-usage.dto';
import { RedeemLoyaltyPointsDto } from './dto/redeem-loyalty-points.dto';
import { SetMemberPresenceDto } from './dto/set-member-presence.dto';
import { SetAdminPresenceDto } from './dto/set-admin-presence.dto';
import { UpdateLoyaltyRankDto } from './dto/update-loyalty-rank.dto';
import { UpdateSpinPrizeSettingsDto } from './dto/update-spin-prize-settings.dto';
import { PetLoyaltyPointsDto } from './dto/pet-loyalty-points.dto';

const LOYALTY_CONFIG_KEY = '__LOYALTY_MEMBER_POINTS__';
const LOYALTY_MINUTES_PER_POINT = 15;
const LOYALTY_SECONDS_PER_POINT = LOYALTY_MINUTES_PER_POINT * 60;
const LOYALTY_REDEEM_SECONDS_PER_POINT = 60;
const LOYALTY_USAGE_CREATED_BY = 'client.session.loyalty';
const LOYALTY_REDEEM_CREATED_BY = 'client.loyalty';
const LOYALTY_REDEEM_NOTE_PREFIX = 'LOYALTY_REDEEM';
const LOYALTY_PET_REWARD_NOTE_PREFIX = 'PET_REWARD';
const LOYALTY_PET_SPEND_NOTE_PREFIX = `${LOYALTY_REDEEM_NOTE_PREFIX}_PET`;
const LOYALTY_SPIN_CONFIG_KEY = '__LOYALTY_SPIN_CONFIG__';
const CLIENT_MEMBER_WITHDRAW_ENABLED_KEY = '__CLIENT_MEMBER_WITHDRAW_ENABLED__';
const DEFAULT_MEMBER_WITHDRAW_ENABLED = true;
const CLIENT_MEMBER_TOPUP_REQUEST_ENABLED_KEY = '__CLIENT_MEMBER_TOPUP_REQUEST_ENABLED__';
const DEFAULT_MEMBER_TOPUP_REQUEST_ENABLED = true;
const MEMBER_WITHDRAW_REQUEST_EVENT_TYPE = 'member.withdraw.requested';
const MEMBER_TOPUP_REQUEST_EVENT_TYPE = 'member.topup.requested';
const MEMBER_PASSWORD_BCRYPT_ROUNDS = 10;
const ACTIVE_PRESENCE_EVENT_TYPES = [
  'member.pc.presence',
  'guest.pc.presence',
  'admin.pc.presence',
] as const;

type MemberWithdrawRequestStatus = 'PENDING' | 'APPROVED' | 'REJECTED';

type MemberWithdrawRequestPayload = {
  memberId: string;
  username: string;
  fullName: string;
  amount: number;
  note: string | null;
  createdBy: string;
  agentId: string | null;
  pcId: string | null;
  pcName: string | null;
  status: MemberWithdrawRequestStatus;
  requestedAt: string;
  approvedAt?: string;
  approvedBy?: string;
  rejectedAt?: string;
  rejectedBy?: string;
  rejectNote?: string | null;
  processedAmount?: number;
  remainingBalance?: number;
  transactionId?: string;
};

type MemberTopupRequestStatus = 'PENDING' | 'APPROVED' | 'REJECTED';

type MemberTopupRequestPayload = {
  memberId: string;
  username: string;
  fullName: string;
  amount: number;
  note: string | null;
  createdBy: string;
  agentId: string | null;
  pcId: string | null;
  pcName: string | null;
  status: MemberTopupRequestStatus;
  requestedAt: string;
  approvedAt?: string;
  approvedBy?: string;
  rejectedAt?: string;
  rejectedBy?: string;
  rejectNote?: string | null;
  processedAmount?: number;
  balanceAfterTopup?: number;
  transactionId?: string;
};

type SpinPrizeItem = {
  minutes: number;
  chance: number;
  label: string;
};

const DEFAULT_SPIN_PRIZE_TABLE: ReadonlyArray<SpinPrizeItem> = [
  { minutes: 0, chance: 28, label: '0p' },
  { minutes: 1, chance: 18, label: '1p' },
  { minutes: 2, chance: 15, label: '2p' },
  { minutes: 4, chance: 12, label: '4p' },
  { minutes: 6, chance: 10, label: '6p' },
  { minutes: 8, chance: 7, label: '8p' },
  { minutes: 10, chance: 5, label: '10p' },
  { minutes: 15, chance: 3, label: '15p' },
  { minutes: 20, chance: 1.5, label: '20p' },
  { minutes: 30, chance: 0.5, label: '30p' },
];

@Injectable()
export class MembersService {
  constructor(
    private readonly prisma: PrismaService,
    private readonly configService: ConfigService,
    private readonly realtime: RealtimeService,
  ) {}

  async getMembers(search?: string) {
    const keyword = search?.trim();
    const where: Prisma.MemberWhereInput | undefined = keyword
      ? {
          OR: [
            { username: { contains: keyword, mode: 'insensitive' } },
            { fullName: { contains: keyword, mode: 'insensitive' } },
            { phone: { contains: keyword, mode: 'insensitive' } },
            { identityNumber: { contains: keyword, mode: 'insensitive' } },
          ],
        }
      : undefined;

    const [members, total, rankConfigs] = await Promise.all([
      this.prisma.member.findMany({
        where,
        orderBy: [{ createdAt: 'desc' }],
        take: 200,
      }),
      this.prisma.member.count({
        where,
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
      total,
      serverTime: new Date().toISOString(),
    };
  }

  async createMember(payload: CreateMemberDto) {
    const normalizedUsername = payload.username?.trim() ?? '';
    if (normalizedUsername.length < 1) {
      throw new BadRequestException('Username phai co it nhat 1 ky tu');
    }

    const existingMember = await this.prisma.member.findFirst({
      where: {
        username: {
          equals: normalizedUsername,
          mode: 'insensitive',
        },
      },
      select: { id: true },
    });
    if (existingMember) {
      throw new ConflictException('Username da ton tai');
    }

    try {
      const passwordHash = payload.password
        ? await this.hashPassword(payload.password)
        : null;
      const [member, rankConfigs] = await Promise.all([
        this.prisma.member.create({
          data: {
            username: normalizedUsername,
            fullName: payload.fullName ?? normalizedUsername,
            passwordHash,
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

    const passwordResult = await this.verifyPassword(password, member.passwordHash);
    if (!passwordResult.ok) {
      throw new UnauthorizedException('Sai tài khoản hoặc mật khẩu');
    }

    if (passwordResult.needsRehash) {
      await this.rehashMemberPassword(member.id, password);
    }

    const normalizedAgentId = payload.agentId?.trim();
    const currentPc = normalizedAgentId
      ? await this.prisma.pc.findFirst({
          where: {
            agentId: {
              equals: normalizedAgentId,
              mode: 'insensitive',
            },
          },
          select: { id: true },
        })
      : null;
    const activeOnOtherPc = await this.findMemberActivePc(
      member.id,
      currentPc?.id,
    );
    if (activeOnOtherPc) {
      throw new ConflictException(
        this.buildMemberAlreadyInUseMessage(
          activeOnOtherPc.pcName,
          activeOnOtherPc.agentId,
        ),
      );
    }

    const isUsageAlreadyActiveOnCurrentPc = currentPc
      ? await this.isMemberUsageActiveOnPc(member.id, currentPc.id)
      : false;

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

    const upfrontLoginCharge = this.computeUpfrontLoginCharge(hourlyRate);
    const currentBalance = Number(member.balance);
    if (
      !isUsageAlreadyActiveOnCurrentPc &&
      upfrontLoginCharge > 0 &&
      currentBalance < upfrontLoginCharge
    ) {
      throw new BadRequestException(
        'Số dư tài khoản không đủ.',
      );
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
      throw new NotFoundException('Không tìm thấy hội viên');
    }

    const username = payload.username?.trim() || member.username;
    const fullName = payload.fullName?.trim() || member.fullName;
    const previousStatus = pc.status;
    const nextStatus: PcStatus = isActive ? PcStatus.IN_USE : PcStatus.ONLINE;

    if (isActive) {
      const activeOnOtherPc = await this.findMemberActivePc(member.id, pc.id);
      if (activeOnOtherPc) {
        throw new ConflictException(
          this.buildMemberAlreadyInUseMessage(
            activeOnOtherPc.pcName,
            activeOnOtherPc.agentId,
          ),
        );
      }
    }

    let pricePerMinute = 0;
    let upfrontLoginCharge = 0;
    if (isActive) {
      const defaultGroup = await this.prisma.pcGroup.findFirst({
        where: { isDefault: true },
      });
      const baseRate = Number(
        pc.groupId
          ? (await this.prisma.pcGroup.findUnique({ where: { id: pc.groupId } }))
              ?.hourlyRate ?? defaultGroup?.hourlyRate ?? 12000
          : defaultGroup?.hourlyRate ?? 12000,
      );
      const hourlyRate = await this.getEffectiveHourlyRate(baseRate);
      pricePerMinute = Number(hourlyRate) / 60;
      upfrontLoginCharge = this.computeUpfrontLoginCharge(hourlyRate);

      const currentBalance = Number(member.balance);
      if (upfrontLoginCharge > 0 && currentBalance < upfrontLoginCharge) {
        throw new BadRequestException(
          'Số dư tài khoản không đủ.',
        );
      }
    }

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

      if (upfrontLoginCharge > 0) {
        await this.prisma.member.update({
          where: { id: member.id },
          data: {
            balance: {
              decrement: upfrontLoginCharge,
            },
          },
        });

        await this.prisma.memberTransaction.create({
          data: {
            memberId: member.id,
            type: 'ADJUSTMENT',
            amountDelta: -upfrontLoginCharge,
            playSecondsDelta: 0,
            note: 'UPFRONT_LOGIN_CHARGE',
            createdBy: 'client.session',
          },
        });
      }

      await this.prisma.session.create({
        data: {
          pcId: pc.id,
          status: 'ACTIVE',
          startedAt: new Date(),
          pricePerMinute,
        },
      });

      // Update PC status to IN_USE
      await this.prisma.pc.update({
        where: { id: pc.id },
        data: { status: nextStatus },
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

      // Update PC status to ONLINE (Sẵn sàng) on logout
      await this.prisma.pc.update({
        where: { id: pc.id },
        data: { status: nextStatus },
      });
    }

    if (previousStatus !== nextStatus) {
      this.realtime.emitToAll('pc.status.changed', {
        pcId: pc.id,
        agentId: pc.agentId,
        previousStatus,
        status: nextStatus,
        at: new Date().toISOString(),
        sourceEvent: isActive
          ? 'member.presence.active'
          : 'member.presence.inactive',
      });
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

  async setAdminPresence(payload: SetAdminPresenceDto) {
    const agentId = payload.agentId.trim();
    const isActive = payload.isActive;
    const pc = await this.prisma.pc.findUnique({
      where: { agentId },
    });

    if (!pc) {
      throw new NotFoundException('Khong tim thay may tram');
    }

    const username = payload.username?.trim() || 'Admin';
    const fullName = payload.fullName?.trim() || username;
    const previousStatus = pc.status;
    const nextStatus: PcStatus = isActive ? PcStatus.IN_USE : PcStatus.ONLINE;

    await this.prisma.eventLog.create({
      data: {
        source: EventSource.CLIENT,
        eventType: 'admin.pc.presence',
        pcId: pc.id,
        payload: {
          isActive,
          username,
          fullName,
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
    }

    if (previousStatus !== nextStatus) {
      await this.prisma.pc.update({
        where: { id: pc.id },
        data: { status: nextStatus },
      });

      this.realtime.emitToAll('pc.status.changed', {
        pcId: pc.id,
        agentId: pc.agentId,
        previousStatus,
        status: nextStatus,
        at: new Date().toISOString(),
        sourceEvent: isActive
          ? 'admin.presence.active'
          : 'admin.presence.inactive',
      });
    }

    return {
      ok: true,
      agentId,
      pcId: pc.id,
      username,
      isActive,
      updatedAt: new Date().toISOString(),
    };
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
      throw new NotFoundException('Không tìm thấy hội viên');
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

      const remainingSeconds = requestedSeconds - consumedSeconds;
      if (remainingSeconds > 0) {
        const pc = await tx.pc.findFirst({
          where: { agentId: createdBy },
        });

        let pricePerMinute = 200;
        if (pc) {
          const activeSession = await tx.session.findFirst({
            where: { pcId: pc.id, status: 'ACTIVE' },
          });
          if (activeSession && activeSession.pricePerMinute) {
            pricePerMinute = Number(activeSession.pricePerMinute);
          } else {
            const defaultGroup = await tx.pcGroup.findFirst({
              where: { isDefault: true },
            });
            const baseRate = Number(
              pc.groupId
                ? (await tx.pcGroup.findUnique({ where: { id: pc.groupId } }))?.hourlyRate ?? defaultGroup?.hourlyRate ?? 12000
                : defaultGroup?.hourlyRate ?? 12000,
            );
            const hourlyRate = await this.getEffectiveHourlyRate(baseRate);
            pricePerMinute = Number(hourlyRate) / 60;
          }
        }

        const pricePerSecond = pricePerMinute / 60;
        const rawCost = remainingSeconds * pricePerSecond;
        const costAmount = Number(rawCost.toFixed(2));

        if (costAmount > 0) {
          updatedMember = await tx.member.update({
            where: { id: member.id },
            data: {
              balance: {
                decrement: costAmount,
              },
            },
          });

          await tx.memberTransaction.create({
            data: {
              memberId: member.id,
              type: MemberTransactionType.ADJUSTMENT,
              amountDelta: -costAmount,
              playSecondsDelta: 0,
              note: `${note}:CASH_CHARGE`,
              createdBy,
            },
          });
        }
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
      const spinPrizeTable = await this.getSpinPrizeTable(tx);
      const totalChance = spinPrizeTable.reduce((sum, item) => sum + item.chance, 0);
      const roll = Math.random() * totalChance;
      let wonMinutes = 0;
      let prizeLabel = '0p';
      let cumulative = 0;
      let matched = false;
      for (const row of spinPrizeTable) {
        cumulative += row.chance;
        if (roll < cumulative) {
          wonMinutes = row.minutes;
          prizeLabel = row.label;
          matched = true;
          break;
        }
      }
      if (!matched && spinPrizeTable.length > 0) {
        const fallback = spinPrizeTable[spinPrizeTable.length - 1];
        wonMinutes = fallback.minutes;
        prizeLabel = fallback.label;
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

  async applyPetLoyaltyPoints(memberId: string, payload: PetLoyaltyPointsDto) {
    const action = payload.action;
    const points = Math.trunc(payload.points);
    const requestedBy = payload.createdBy?.trim() || 'client.virtual_pet';

    if (action === 'REWARD' && points > 12) {
      throw new BadRequestException('Moi lan thu ao chi thuong toi da 12 diem');
    }

    const enabled = await this.getLoyaltyFeatureEnabled();
    if (!enabled) {
      throw new BadRequestException('Tinh nang diem tich luy dang tat');
    }

    return this.prisma.$transaction(async (tx) => {
      const member = await tx.member.findUnique({ where: { id: memberId } });
      if (!member) {
        throw new NotFoundException('Khong tim thay hoi vien');
      }

      const before = await this.buildLoyaltySnapshot(memberId, tx);
      if (action === 'SPEND' && before.availablePoints < points) {
        throw new BadRequestException(
          `Khong du diem. Hien chi con ${before.availablePoints} diem`,
        );
      }

      if (action === 'SPEND') {
        const secondsToSpend = points * LOYALTY_REDEEM_SECONDS_PER_POINT;
        await tx.memberTransaction.create({
          data: {
            memberId,
            type: MemberTransactionType.ADJUSTMENT,
            amountDelta: 0,
            playSecondsDelta: secondsToSpend,
            note:
              payload.note?.trim() ||
              `${LOYALTY_PET_SPEND_NOTE_PREFIX}: spend ${points} points`,
            createdBy: LOYALTY_REDEEM_CREATED_BY,
          },
        });
      } else {
        const secondsToReward = points * LOYALTY_SECONDS_PER_POINT;
        await tx.memberTransaction.create({
          data: {
            memberId,
            type: MemberTransactionType.ADJUSTMENT,
            amountDelta: 0,
            playSecondsDelta: -secondsToReward,
            note:
              payload.note?.trim() ||
              `${LOYALTY_PET_REWARD_NOTE_PREFIX}: reward ${points} points`,
            createdBy: LOYALTY_USAGE_CREATED_BY,
          },
        });
      }

      await tx.eventLog.create({
        data: {
          source: EventSource.CLIENT,
          eventType: 'member.loyalty.pet_points',
          pcId: null,
          payload: {
            memberId,
            action,
            points,
            requestedBy,
            at: new Date().toISOString(),
          },
        },
      });

      const [updatedMember, rankConfigs, loyalty] = await Promise.all([
        tx.member.findUnique({ where: { id: memberId } }),
        tx.loyaltyRankConfig.findMany({ orderBy: { minTopup: 'desc' } }),
        this.buildLoyaltySnapshot(memberId, tx),
      ]);

      return {
        member: this.toMemberItem(
          updatedMember!,
          this.calculateRankName(Number(updatedMember!.totalTopup), rankConfigs),
          loyalty.availablePoints,
        ),
        loyalty,
        action,
        points,
        updatedAt: new Date().toISOString(),
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

    const member = this.toMemberItem(
      result.updatedMember,
      this.calculateRankName(Number(result.updatedMember.totalTopup), rankConfigs),
    );
    await this.emitMemberAccountChanged(member, 'TOPUP', createdBy);

    return {
      member,
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

    const member = this.toMemberItem(
      result.updatedMember,
      this.calculateRankName(Number(result.updatedMember.totalTopup), rankConfigs),
    );
    await this.emitMemberAccountChanged(member, 'BUY_PLAYTIME', createdBy);

    return {
      member,
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

    const member = this.toMemberItem(
      result.updatedMember,
      this.calculateRankName(Number(result.updatedMember.totalTopup), rankConfigs),
    );
    await this.emitMemberAccountChanged(member, 'ADJUST_BALANCE', createdBy);

    return {
      member,
      transaction: this.toTransactionItem(result.transaction),
    };
  }

  async updateMember(memberId: string, payload: UpdateMemberDto) {
    const result = await this.prisma.$transaction(async (tx) => {
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

        data.passwordHash = await this.hashPassword(payload.password);
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

    await this.emitMemberAccountChanged(
      result.member,
      'UPDATE_MEMBER',
      payload.updatedBy?.trim() || 'admin.desktop',
    );

    return result;
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
    await this.emitMemberAccountChanged(result.sourceMember, 'TRANSFER_OUT', createdBy);
    await this.emitMemberAccountChanged(result.targetMember, 'TRANSFER_IN', createdBy);
    return result;
  }

  async requestTopup(memberId: string, payload: RequestTopupDto) {
    const topupRequestEnabled = await this.getMemberTopupRequestEnabled();
    if (!topupRequestEnabled) {
      throw new BadRequestException('Tinh nang nap tien nhanh hoi vien dang tam tat');
    }

    const amount = this.roundMoney(payload.amount);
    const createdBy = payload.createdBy?.trim() || 'client.member.topup.request';
    const note = payload.note?.trim() || null;
    const normalizedAgentId = payload.agentId?.trim() || null;

    const [member, pc] = await Promise.all([
      this.prisma.member.findUnique({ where: { id: memberId } }),
      normalizedAgentId
        ? this.prisma.pc.findFirst({
            where: {
              agentId: {
                equals: normalizedAgentId,
                mode: 'insensitive',
              },
            },
            select: {
              id: true,
              name: true,
            },
          })
        : Promise.resolve(null),
    ]);

    if (!member) {
      throw new NotFoundException('Không tìm thấy hội viên');
    }

    if (!member.isActive) {
      throw new BadRequestException('Tài khoản hội viên không hoạt động');
    }

    const requestPayload: MemberTopupRequestPayload = {
      memberId: member.id,
      username: member.username,
      fullName: member.fullName,
      amount,
      note,
      createdBy,
      agentId: normalizedAgentId,
      pcId: pc?.id ?? null,
      pcName: pc?.name ?? null,
      status: 'PENDING',
      requestedAt: new Date().toISOString(),
    };

    const requestEvent = await this.prisma.eventLog.create({
      data: {
        source: createdBy.startsWith('client.') ? EventSource.CLIENT : EventSource.ADMIN,
        eventType: MEMBER_TOPUP_REQUEST_EVENT_TYPE,
        pcId: pc?.id,
        payload: requestPayload,
      },
    });

    const requestItem = this.toTopupRequestItem(requestEvent.id, requestPayload);
    this.realtime.emitToAll('member.topup.requested', requestItem);

    return {
      request: requestItem,
      status: 'PENDING',
      message: 'Yêu cầu nạp tiền đã được gửi, chờ admin xác nhận',
    };
  }

  async getPendingTopupRequests() {
    const events = await this.prisma.eventLog.findMany({
      where: {
        eventType: MEMBER_TOPUP_REQUEST_EVENT_TYPE,
      },
      orderBy: {
        createdAt: 'desc',
      },
      take: 200,
    });

    const items: Array<ReturnType<MembersService['toTopupRequestItem']>> = [];
    for (const event of events) {
      const payload = this.parseMemberTopupRequestPayload(event.payload);
      if (!payload || payload.status !== 'PENDING') {
        continue;
      }

      items.push(this.toTopupRequestItem(event.id, payload));
    }

    return {
      items,
      total: items.length,
      serverTime: new Date().toISOString(),
    };
  }

  async approveTopupRequest(
    requestId: string,
    payload: { approvedBy?: string },
  ) {
    const approvedBy = payload.approvedBy?.trim() || 'admin.desktop';

    const result = await this.prisma.$transaction(async (tx) => {
      const requestEvent = await tx.eventLog.findUnique({
        where: { id: requestId },
      });

      if (!requestEvent || requestEvent.eventType !== MEMBER_TOPUP_REQUEST_EVENT_TYPE) {
        throw new NotFoundException('Không tìm thấy yêu cầu nạp tiền');
      }

      const requestPayload = this.parseMemberTopupRequestPayload(requestEvent.payload);
      if (!requestPayload) {
        throw new BadRequestException('Nội dung yêu cầu nạp tiền không hợp lệ');
      }

      if (requestPayload.status !== 'PENDING') {
        throw new ConflictException('Yêu cầu nạp tiền đã được xử lý');
      }

      const member = await tx.member.findUnique({
        where: { id: requestPayload.memberId },
      });
      if (!member) {
        throw new NotFoundException('Không tìm thấy hội viên của yêu cầu');
      }

      if (!member.isActive) {
        throw new BadRequestException('Tài khoản hội viên không hoạt động');
      }

      const updatedMember = await tx.member.update({
        where: { id: member.id },
        data: {
          balance: {
            increment: requestPayload.amount,
          },
          totalTopup: {
            increment: requestPayload.amount,
          },
        },
      });

      const transaction = await tx.memberTransaction.create({
        data: {
          memberId: member.id,
          type: MemberTransactionType.TOPUP,
          amountDelta: requestPayload.amount,
          playSecondsDelta: 0,
          note: requestPayload.note || 'Hội viên yêu cầu nạp tiền (được duyệt)',
          createdBy: approvedBy,
        },
      });

      const resolvedPayload: MemberTopupRequestPayload = {
        ...requestPayload,
        status: 'APPROVED',
        approvedAt: new Date().toISOString(),
        approvedBy,
        processedAmount: requestPayload.amount,
        balanceAfterTopup: Number(updatedMember.balance),
        transactionId: transaction.id,
      };

      await tx.eventLog.update({
        where: { id: requestId },
        data: {
          payload: resolvedPayload,
        },
      });

      const rankConfigs = await tx.loyaltyRankConfig.findMany({
        orderBy: { minTopup: 'desc' },
      });

      const memberItem = this.toMemberItem(
        updatedMember,
        this.calculateRankName(Number(updatedMember.totalTopup), rankConfigs),
      );

      return {
        request: this.toTopupRequestItem(requestId, resolvedPayload),
        member: memberItem,
        transaction: this.toTransactionItem(transaction),
      };
    });

    await this.emitMemberAccountChanged(result.member, 'TOPUP_APPROVED', approvedBy);
    this.realtime.emitToAll('member.topup.resolved', {
      ...result.request,
      status: 'APPROVED',
    });

    return {
      ...result.request,
      member: result.member,
      transaction: result.transaction,
    };
  }

  async rejectTopupRequest(
    requestId: string,
    payload: { rejectedBy?: string; note?: string },
  ) {
    const rejectedBy = payload.rejectedBy?.trim() || 'admin.desktop';
    const rejectNote = payload.note?.trim() || null;

    const requestEvent = await this.prisma.eventLog.findUnique({
      where: { id: requestId },
    });

    if (!requestEvent || requestEvent.eventType !== MEMBER_TOPUP_REQUEST_EVENT_TYPE) {
      throw new NotFoundException('Không tìm thấy yêu cầu nạp tiền');
    }

    const requestPayload = this.parseMemberTopupRequestPayload(requestEvent.payload);
    if (!requestPayload) {
      throw new BadRequestException('Nội dung yêu cầu nạp tiền không hợp lệ');
    }

    if (requestPayload.status !== 'PENDING') {
      throw new ConflictException('Yêu cầu nạp tiền đã được xử lý');
    }

    const resolvedPayload: MemberTopupRequestPayload = {
      ...requestPayload,
      status: 'REJECTED',
      rejectedAt: new Date().toISOString(),
      rejectedBy,
      rejectNote,
    };

    await this.prisma.eventLog.update({
      where: { id: requestId },
      data: {
        payload: resolvedPayload,
      },
    });

    const request = this.toTopupRequestItem(requestId, resolvedPayload);
    this.realtime.emitToAll('member.topup.resolved', {
      ...request,
      status: 'REJECTED',
    });

    return request;
  }

  async withdrawBalance(memberId: string, payload: WithdrawBalanceDto) {
    const withdrawEnabled = await this.getMemberWithdrawEnabled();
    if (!withdrawEnabled) {
      throw new BadRequestException('Tính năng rút tiền hội viên đang tạm tắt');
    }

    const amount = this.roundMoney(payload.amount);
    const createdBy = payload.createdBy?.trim() || 'client.member.withdraw';
    const note = payload.note?.trim() || null;
    const normalizedAgentId = payload.agentId?.trim() || null;

    const [member, pc] = await Promise.all([
      this.prisma.member.findUnique({ where: { id: memberId } }),
      normalizedAgentId
        ? this.prisma.pc.findFirst({
            where: {
              agentId: {
                equals: normalizedAgentId,
                mode: 'insensitive',
              },
            },
            select: {
              id: true,
              name: true,
            },
          })
        : Promise.resolve(null),
    ]);

    if (!member) {
      throw new NotFoundException('Không tìm thấy hội viên');
    }

    if (!member.isActive) {
      throw new BadRequestException('Tài khoản hội viên không hoạt động');
    }

    const currentBalance = Number(member.balance);
    if (currentBalance < amount) {
      throw new BadRequestException('Số dư không đủ để rút tiền');
    }

    const requestPayload: MemberWithdrawRequestPayload = {
      memberId: member.id,
      username: member.username,
      fullName: member.fullName,
      amount,
      note,
      createdBy,
      agentId: normalizedAgentId,
      pcId: pc?.id ?? null,
      pcName: pc?.name ?? null,
      status: 'PENDING',
      requestedAt: new Date().toISOString(),
    };

    const requestEvent = await this.prisma.eventLog.create({
      data: {
        source: createdBy.startsWith('client.') ? EventSource.CLIENT : EventSource.ADMIN,
        eventType: MEMBER_WITHDRAW_REQUEST_EVENT_TYPE,
        pcId: pc?.id,
        payload: requestPayload,
      },
    });

    const requestItem = this.toWithdrawRequestItem(requestEvent.id, requestPayload);
    this.realtime.emitToAll('member.withdraw.requested', requestItem);

    return {
      request: requestItem,
      status: 'PENDING',
      message: 'Yêu cầu rút tiền đã được gửi, chờ admin xác nhận',
    };
  }

  async getPendingWithdrawRequests() {
    const events = await this.prisma.eventLog.findMany({
      where: {
        eventType: MEMBER_WITHDRAW_REQUEST_EVENT_TYPE,
      },
      orderBy: {
        createdAt: 'desc',
      },
      take: 200,
    });

    const items: Array<ReturnType<MembersService['toWithdrawRequestItem']>> = [];
    for (const event of events) {
      const payload = this.parseMemberWithdrawRequestPayload(event.payload);
      if (!payload || payload.status !== 'PENDING') {
        continue;
      }

      items.push(this.toWithdrawRequestItem(event.id, payload));
    }

    return {
      items,
      total: items.length,
      serverTime: new Date().toISOString(),
    };
  }

  async approveWithdrawRequest(
    requestId: string,
    payload: { approvedBy?: string },
  ) {
    const approvedBy = payload.approvedBy?.trim() || 'admin.desktop';

    const result = await this.prisma.$transaction(async (tx) => {
      const requestEvent = await tx.eventLog.findUnique({
        where: { id: requestId },
      });

      if (!requestEvent || requestEvent.eventType !== MEMBER_WITHDRAW_REQUEST_EVENT_TYPE) {
        throw new NotFoundException('Không tìm thấy yêu cầu rút tiền');
      }

      const requestPayload = this.parseMemberWithdrawRequestPayload(requestEvent.payload);
      if (!requestPayload) {
        throw new BadRequestException('Nội dung yêu cầu rút tiền không hợp lệ');
      }

      if (requestPayload.status !== 'PENDING') {
        throw new ConflictException('Yêu cầu rút tiền đã được xử lý');
      }

      const member = await tx.member.findUnique({
        where: { id: requestPayload.memberId },
      });
      if (!member) {
        throw new NotFoundException('Không tìm thấy hội viên của yêu cầu');
      }

      if (!member.isActive) {
        throw new BadRequestException('Tài khoản hội viên không hoạt động');
      }

      const currentBalance = Number(member.balance);
      if (currentBalance < requestPayload.amount) {
        throw new BadRequestException('Số dư không đủ để rút tiền');
      }

      const updatedMember = await tx.member.update({
        where: { id: member.id },
        data: {
          balance: {
            decrement: requestPayload.amount,
          },
        },
      });

      const transaction = await tx.memberTransaction.create({
        data: {
          memberId: member.id,
          type: MemberTransactionType.ADJUSTMENT,
          amountDelta: -requestPayload.amount,
          playSecondsDelta: 0,
          note: requestPayload.note || 'Hội viên rút tiền (được duyệt)',
          createdBy: approvedBy,
        },
      });

      const resolvedPayload: MemberWithdrawRequestPayload = {
        ...requestPayload,
        status: 'APPROVED',
        approvedAt: new Date().toISOString(),
        approvedBy,
        processedAmount: requestPayload.amount,
        remainingBalance: Number(updatedMember.balance),
        transactionId: transaction.id,
      };

      await tx.eventLog.update({
        where: { id: requestId },
        data: {
          payload: resolvedPayload,
        },
      });

      const rankConfigs = await tx.loyaltyRankConfig.findMany({
        orderBy: { minTopup: 'desc' },
      });

      const memberItem = this.toMemberItem(
        updatedMember,
        this.calculateRankName(Number(updatedMember.totalTopup), rankConfigs),
      );

      return {
        request: this.toWithdrawRequestItem(requestId, resolvedPayload),
        member: memberItem,
        transaction: this.toTransactionItem(transaction),
      };
    });

    await this.emitMemberAccountChanged(result.member, 'WITHDRAW_APPROVED', approvedBy);
    this.realtime.emitToAll('member.withdraw.resolved', {
      ...result.request,
      status: 'APPROVED',
    });

    return {
      ...result.request,
      member: result.member,
      transaction: result.transaction,
    };
  }

  async rejectWithdrawRequest(
    requestId: string,
    payload: { rejectedBy?: string; note?: string },
  ) {
    const rejectedBy = payload.rejectedBy?.trim() || 'admin.desktop';
    const rejectNote = payload.note?.trim() || null;

    const requestEvent = await this.prisma.eventLog.findUnique({
      where: { id: requestId },
    });

    if (!requestEvent || requestEvent.eventType !== MEMBER_WITHDRAW_REQUEST_EVENT_TYPE) {
      throw new NotFoundException('Không tìm thấy yêu cầu rút tiền');
    }

    const requestPayload = this.parseMemberWithdrawRequestPayload(requestEvent.payload);
    if (!requestPayload) {
      throw new BadRequestException('Nội dung yêu cầu rút tiền không hợp lệ');
    }

    if (requestPayload.status !== 'PENDING') {
      throw new ConflictException('Yêu cầu rút tiền đã được xử lý');
    }

    const resolvedPayload: MemberWithdrawRequestPayload = {
      ...requestPayload,
      status: 'REJECTED',
      rejectedAt: new Date().toISOString(),
      rejectedBy,
      rejectNote,
    };

    await this.prisma.eventLog.update({
      where: { id: requestId },
      data: {
        payload: resolvedPayload,
      },
    });

    const request = this.toWithdrawRequestItem(requestId, resolvedPayload);
    this.realtime.emitToAll('member.withdraw.resolved', {
      ...request,
      status: 'REJECTED',
    });

    return request;
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

  async getLoyaltySpinSettings() {
    const setting = await this.prisma.appSetting.findUnique({
      where: { key: LOYALTY_SPIN_CONFIG_KEY },
    });

    let prizeTable: SpinPrizeItem[];
    if (!setting?.value) {
      prizeTable = DEFAULT_SPIN_PRIZE_TABLE.map((item) => ({ ...item }));
    } else {
      try {
        prizeTable = this.normalizeSpinPrizeTable(JSON.parse(setting.value));
      } catch {
        prizeTable = DEFAULT_SPIN_PRIZE_TABLE.map((item) => ({ ...item }));
      }
    }

    const totalChance = prizeTable.reduce((sum, item) => sum + item.chance, 0);

    return {
      items: prizeTable.map((item) => ({
        minutes: item.minutes,
        chance: item.chance,
        label: item.label,
      })),
      totalChance: Number(totalChance.toFixed(4)),
      updatedAt: (setting?.updatedAt ?? new Date()).toISOString(),
    };
  }

  async updateLoyaltySpinSettings(payload: UpdateSpinPrizeSettingsDto) {
    const normalized = this.normalizeSpinPrizeTable(payload.items);
    const totalChance = normalized.reduce((sum, item) => sum + item.chance, 0);

    if (Math.abs(totalChance - 100) > 0.0001) {
      throw new BadRequestException(
        `Tong ty le phai bang 100%. Hien tai: ${Number(totalChance.toFixed(4))}%`,
      );
    }

    const updatedBy = payload.updatedBy?.trim() || 'admin.desktop';
    const updated = await this.prisma.appSetting.upsert({
      where: { key: LOYALTY_SPIN_CONFIG_KEY },
      update: {
        value: JSON.stringify(normalized.map((item) => ({
          minutes: item.minutes,
          chance: item.chance,
        }))),
      },
      create: {
        key: LOYALTY_SPIN_CONFIG_KEY,
        value: JSON.stringify(normalized.map((item) => ({
          minutes: item.minutes,
          chance: item.chance,
        }))),
      },
    });

    return {
      items: normalized,
      totalChance: Number(totalChance.toFixed(4)),
      updatedAt: updated.updatedAt.toISOString(),
      updatedBy,
    };
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
      'Tinh Anh', 'Kim Cương', 'Cao Thủ', 'Đại Cao Thủ', 'Thách Đấu'
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

  private async getSpinPrizeTable(
    tx?: Prisma.TransactionClient,
  ): Promise<SpinPrizeItem[]> {
    const prisma = tx ?? this.prisma;
    const setting = await prisma.appSetting.findUnique({
      where: { key: LOYALTY_SPIN_CONFIG_KEY },
    });

    if (!setting?.value) {
      return DEFAULT_SPIN_PRIZE_TABLE.map((item) => ({ ...item }));
    }

    try {
      const raw = JSON.parse(setting.value) as unknown;
      return this.normalizeSpinPrizeTable(raw);
    } catch {
      return DEFAULT_SPIN_PRIZE_TABLE.map((item) => ({ ...item }));
    }
  }

  private normalizeSpinPrizeTable(raw: unknown): SpinPrizeItem[] {
    if (!Array.isArray(raw)) {
      throw new BadRequestException('Cau hinh vong quay khong hop le');
    }

    const incomingMap = new Map<number, number>();
    for (const entry of raw) {
      if (!entry || typeof entry !== 'object') {
        throw new BadRequestException('Cau hinh vong quay khong hop le');
      }

      const minutes = Number((entry as { minutes?: unknown }).minutes);
      const chance = Number((entry as { chance?: unknown }).chance);

      if (!Number.isFinite(minutes) || !Number.isInteger(minutes) || minutes < 0) {
        throw new BadRequestException('Gia tri minutes khong hop le');
      }
      if (!Number.isFinite(chance) || chance < 0 || chance > 100) {
        throw new BadRequestException('Gia tri chance khong hop le');
      }
      if (incomingMap.has(minutes)) {
        throw new BadRequestException(`Moc ${minutes}p bi trung lap`);
      }

      incomingMap.set(minutes, Number(chance.toFixed(4)));
    }

    const expectedMinutes = DEFAULT_SPIN_PRIZE_TABLE.map((item) => item.minutes);
    for (const minutes of expectedMinutes) {
      if (!incomingMap.has(minutes)) {
        throw new BadRequestException(
          `Thieu moc ${minutes}p. Cac moc bat buoc: ${expectedMinutes.join(', ')}`,
        );
      }
    }
    if (incomingMap.size !== expectedMinutes.length) {
      throw new BadRequestException(
        `Chi ho tro dung ${expectedMinutes.length} moc: ${expectedMinutes.join(', ')}`,
      );
    }

    return DEFAULT_SPIN_PRIZE_TABLE.map((base) => ({
      minutes: base.minutes,
      chance: incomingMap.get(base.minutes) ?? base.chance,
      label: `${base.minutes}p`,
    }));
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

  private async hashPassword(rawPassword: string): Promise<string> {
    return bcrypt.hash(rawPassword, MEMBER_PASSWORD_BCRYPT_ROUNDS);
  }

  private async verifyPassword(
    rawPassword: string,
    storedHash: string,
  ): Promise<{ ok: boolean; needsRehash: boolean }> {
    if (this.isBcryptHash(storedHash)) {
      return {
        ok: await bcrypt.compare(rawPassword, storedHash),
        needsRehash: false,
      };
    }

    if (this.hashLegacyPassword(rawPassword) === storedHash) {
      return { ok: true, needsRehash: true };
    }

    return { ok: false, needsRehash: false };
  }

  private async rehashMemberPassword(
    memberId: string,
    rawPassword: string,
  ): Promise<void> {
    try {
      await this.prisma.member.update({
        where: { id: memberId },
        data: { passwordHash: await this.hashPassword(rawPassword) },
      });
    } catch {
      // Login already succeeded. A best-effort rehash failure should not block use.
    }
  }

  private isBcryptHash(value: string): boolean {
    return /^\$2[aby]\$\d{2}\$/.test(value);
  }

  private hashLegacyPassword(rawPassword: string): string {
    const salt =
      this.configService.get<string>('MEMBER_PASSWORD_SALT') ??
      'servermanagerbilling-default-salt';

    return createHash('sha256').update(`${salt}:${rawPassword}`).digest('hex');
  }

  private async findMemberActivePc(
    memberId: string,
    excludePcId?: string,
  ): Promise<{ pcId: string; pcName: string; agentId: string } | null> {
    const activePcs = await this.prisma.pc.findMany({
      where: excludePcId
        ? {
            status: PcStatus.IN_USE,
            id: {
              not: excludePcId,
            },
          }
        : {
            status: PcStatus.IN_USE,
          },
      select: {
        id: true,
        name: true,
        agentId: true,
        eventsLog: {
          where: {
            eventType: {
              in: [...ACTIVE_PRESENCE_EVENT_TYPES],
            },
          },
          orderBy: {
            createdAt: 'desc',
          },
          take: 1,
          select: {
            eventType: true,
            payload: true,
          },
        },
      },
    });

    for (const pc of activePcs) {
      const latestPresence = pc.eventsLog[0];
      if (!latestPresence || latestPresence.eventType !== 'member.pc.presence') {
        continue;
      }

      const activeMemberId = this.extractActiveMemberId(latestPresence.payload);
      if (activeMemberId === memberId) {
        return {
          pcId: pc.id,
          pcName: pc.name,
          agentId: pc.agentId,
        };
      }
    }

    return null;
  }

  private extractActiveMemberId(
    payload?: Prisma.JsonValue | null,
  ): string | null {
    if (!payload || typeof payload !== 'object' || Array.isArray(payload)) {
      return null;
    }

    const value = payload as Record<string, unknown>;
    if (value.isActive !== true) {
      return null;
    }

    if (typeof value.memberId !== 'string') {
      return null;
    }

    const memberId = value.memberId.trim();
    return memberId.length > 0 ? memberId : null;
  }

  private async isMemberUsageActiveOnPc(
    memberId: string,
    pcId: string,
  ): Promise<boolean> {
    const [activeSession, latestPresence] = await Promise.all([
      this.prisma.session.findFirst({
        where: {
          pcId,
          status: 'ACTIVE',
        },
        select: {
          id: true,
        },
      }),
      this.prisma.eventLog.findFirst({
        where: {
          pcId,
          eventType: 'member.pc.presence',
        },
        orderBy: {
          createdAt: 'desc',
        },
        select: {
          payload: true,
        },
      }),
    ]);

    if (!activeSession) {
      return false;
    }

    const activeMemberId = this.extractActiveMemberId(latestPresence?.payload);
    return activeMemberId === memberId;
  }

  private computeUpfrontLoginCharge(hourlyRate: number): number {
    if (!Number.isFinite(hourlyRate) || hourlyRate <= 0) {
      return 0;
    }

    return Number((hourlyRate / 60).toFixed(2));
  }

  private buildMemberAlreadyInUseMessage(pcName: string, agentId: string): string {
    return `Tai khoan dang duoc su dung tren may ${pcName} (${agentId}). Vui long dang xuat o may do truoc.`;
  }

  private async emitMemberAccountChanged(
    member: ReturnType<MembersService['toMemberItem']>,
    reason: string,
    changedBy: string,
  ): Promise<void> {
    const activePc = await this.findMemberActivePc(member.id);
    if (!activePc) {
      return;
    }

    this.realtime.emitToAgent(activePc.agentId, 'member.account.changed', {
      memberId: member.id,
      reason,
      changedBy,
      at: new Date().toISOString(),
      member: {
        id: member.id,
        username: member.username,
        balance: member.balance,
        playSeconds: member.playSeconds,
        rank: member.rank,
      },
    });
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

  private parseMemberTopupRequestPayload(
    payload?: Prisma.JsonValue | null,
  ): MemberTopupRequestPayload | null {
    if (!payload || typeof payload !== 'object' || Array.isArray(payload)) {
      return null;
    }

    const value = payload as Record<string, unknown>;
    const memberId = typeof value.memberId === 'string' ? value.memberId.trim() : '';
    const username = typeof value.username === 'string' ? value.username.trim() : '';
    const fullName = typeof value.fullName === 'string' ? value.fullName.trim() : '';
    const requestedAt = typeof value.requestedAt === 'string' ? value.requestedAt.trim() : '';
    const createdBy = typeof value.createdBy === 'string' ? value.createdBy.trim() : '';
    const amount = Number(value.amount);
    const statusRaw = typeof value.status === 'string' ? value.status.trim().toUpperCase() : '';
    const processedAmountRaw =
      value.processedAmount !== undefined ? Number(value.processedAmount) : undefined;
    const balanceAfterTopupRaw =
      value.balanceAfterTopup !== undefined ? Number(value.balanceAfterTopup) : undefined;

    if (
      !memberId ||
      !username ||
      !fullName ||
      !requestedAt ||
      !createdBy ||
      !Number.isFinite(amount) ||
      amount <= 0
    ) {
      return null;
    }

    const status: MemberTopupRequestStatus =
      statusRaw === 'APPROVED' || statusRaw === 'REJECTED' || statusRaw === 'PENDING'
        ? statusRaw
        : 'PENDING';

    return {
      memberId,
      username,
      fullName,
      amount: this.roundMoneyAllowZero(amount),
      note: typeof value.note === 'string' ? value.note : null,
      createdBy,
      agentId: typeof value.agentId === 'string' ? value.agentId : null,
      pcId: typeof value.pcId === 'string' ? value.pcId : null,
      pcName: typeof value.pcName === 'string' ? value.pcName : null,
      status,
      requestedAt,
      approvedAt: typeof value.approvedAt === 'string' ? value.approvedAt : undefined,
      approvedBy: typeof value.approvedBy === 'string' ? value.approvedBy : undefined,
      rejectedAt: typeof value.rejectedAt === 'string' ? value.rejectedAt : undefined,
      rejectedBy: typeof value.rejectedBy === 'string' ? value.rejectedBy : undefined,
      rejectNote: typeof value.rejectNote === 'string' ? value.rejectNote : null,
      processedAmount: Number.isFinite(processedAmountRaw ?? NaN)
        ? processedAmountRaw
        : undefined,
      balanceAfterTopup: Number.isFinite(balanceAfterTopupRaw ?? NaN)
        ? balanceAfterTopupRaw
        : undefined,
      transactionId: typeof value.transactionId === 'string' ? value.transactionId : undefined,
    };
  }

  private toTopupRequestItem(
    requestId: string,
    payload: MemberTopupRequestPayload,
  ) {
    return {
      requestId,
      memberId: payload.memberId,
      username: payload.username,
      fullName: payload.fullName,
      amount: payload.amount,
      note: payload.note,
      createdBy: payload.createdBy,
      agentId: payload.agentId,
      pcId: payload.pcId,
      pcName: payload.pcName,
      status: payload.status,
      requestedAt: payload.requestedAt,
      approvedAt: payload.approvedAt ?? null,
      approvedBy: payload.approvedBy ?? null,
      rejectedAt: payload.rejectedAt ?? null,
      rejectedBy: payload.rejectedBy ?? null,
      rejectNote: payload.rejectNote ?? null,
      processedAmount: payload.processedAmount ?? null,
      balanceAfterTopup: payload.balanceAfterTopup ?? null,
      transactionId: payload.transactionId ?? null,
    };
  }

  private parseMemberWithdrawRequestPayload(
    payload?: Prisma.JsonValue | null,
  ): MemberWithdrawRequestPayload | null {
    if (!payload || typeof payload !== 'object' || Array.isArray(payload)) {
      return null;
    }

    const value = payload as Record<string, unknown>;
    const memberId = typeof value.memberId === 'string' ? value.memberId.trim() : '';
    const username = typeof value.username === 'string' ? value.username.trim() : '';
    const fullName = typeof value.fullName === 'string' ? value.fullName.trim() : '';
    const requestedAt = typeof value.requestedAt === 'string' ? value.requestedAt.trim() : '';
    const createdBy = typeof value.createdBy === 'string' ? value.createdBy.trim() : '';
    const amount = Number(value.amount);
    const statusRaw = typeof value.status === 'string' ? value.status.trim().toUpperCase() : '';
    const processedAmountRaw =
      value.processedAmount !== undefined ? Number(value.processedAmount) : undefined;
    const remainingBalanceRaw =
      value.remainingBalance !== undefined ? Number(value.remainingBalance) : undefined;

    if (
      !memberId ||
      !username ||
      !fullName ||
      !requestedAt ||
      !createdBy ||
      !Number.isFinite(amount) ||
      amount <= 0
    ) {
      return null;
    }

    const status: MemberWithdrawRequestStatus =
      statusRaw === 'APPROVED' || statusRaw === 'REJECTED' || statusRaw === 'PENDING'
        ? statusRaw
        : 'PENDING';

    return {
      memberId,
      username,
      fullName,
      amount: this.roundMoneyAllowZero(amount),
      note: typeof value.note === 'string' ? value.note : null,
      createdBy,
      agentId: typeof value.agentId === 'string' ? value.agentId : null,
      pcId: typeof value.pcId === 'string' ? value.pcId : null,
      pcName: typeof value.pcName === 'string' ? value.pcName : null,
      status,
      requestedAt,
      approvedAt: typeof value.approvedAt === 'string' ? value.approvedAt : undefined,
      approvedBy: typeof value.approvedBy === 'string' ? value.approvedBy : undefined,
      rejectedAt: typeof value.rejectedAt === 'string' ? value.rejectedAt : undefined,
      rejectedBy: typeof value.rejectedBy === 'string' ? value.rejectedBy : undefined,
      rejectNote: typeof value.rejectNote === 'string' ? value.rejectNote : null,
      processedAmount: Number.isFinite(processedAmountRaw ?? NaN)
        ? processedAmountRaw
        : undefined,
      remainingBalance: Number.isFinite(remainingBalanceRaw ?? NaN)
        ? remainingBalanceRaw
        : undefined,
      transactionId: typeof value.transactionId === 'string' ? value.transactionId : undefined,
    };
  }

  private toWithdrawRequestItem(
    requestId: string,
    payload: MemberWithdrawRequestPayload,
  ) {
    return {
      requestId,
      memberId: payload.memberId,
      username: payload.username,
      fullName: payload.fullName,
      amount: payload.amount,
      note: payload.note,
      createdBy: payload.createdBy,
      agentId: payload.agentId,
      pcId: payload.pcId,
      pcName: payload.pcName,
      status: payload.status,
      requestedAt: payload.requestedAt,
      approvedAt: payload.approvedAt ?? null,
      approvedBy: payload.approvedBy ?? null,
      rejectedAt: payload.rejectedAt ?? null,
      rejectedBy: payload.rejectedBy ?? null,
      rejectNote: payload.rejectNote ?? null,
      processedAmount: payload.processedAmount ?? null,
      remainingBalance: payload.remainingBalance ?? null,
      transactionId: payload.transactionId ?? null,
    };
  }

  private async getMemberWithdrawEnabled(): Promise<boolean> {
    const setting = await this.prisma.appSetting.findUnique({
      where: { key: CLIENT_MEMBER_WITHDRAW_ENABLED_KEY },
      select: { value: true },
    });

    if (!setting?.value) {
      return DEFAULT_MEMBER_WITHDRAW_ENABLED;
    }

    const normalized = setting.value.trim().toLowerCase();
    if (normalized === 'true') {
      return true;
    }

    if (normalized === 'false') {
      return false;
    }

    return DEFAULT_MEMBER_WITHDRAW_ENABLED;
  }

  private async getMemberTopupRequestEnabled(): Promise<boolean> {
    const setting = await this.prisma.appSetting.findUnique({
      where: { key: CLIENT_MEMBER_TOPUP_REQUEST_ENABLED_KEY },
      select: { value: true },
    });

    if (!setting?.value) {
      return DEFAULT_MEMBER_TOPUP_REQUEST_ENABLED;
    }

    const normalized = setting.value.trim().toLowerCase();
    if (normalized === 'true') {
      return true;
    }

    if (normalized === 'false') {
      return false;
    }

    return DEFAULT_MEMBER_TOPUP_REQUEST_ENABLED;
  }
}
