import {
  BadRequestException,
  Injectable,
  NotFoundException,
} from '@nestjs/common';
import { randomUUID } from 'crypto';
import { EventSource, Pc, PcGroup, PcStatus, Prisma } from '@prisma/client';
import { PrismaService } from '../prisma/prisma.service';
import { RealtimeService } from '../realtime/realtime.service';
import { AgentHeartbeatPayload, AgentHelloPayload } from './types/agent-events.type';
import {
  calculateSessionAmountByPromotions,
  getEffectiveHourlyRateAt,
  getActivePromotionsAt,
} from '../pricing/time-based-billing.util';

type PresencePayload = AgentHelloPayload | AgentHeartbeatPayload;

export type PresenceTransition = {
  pc: Pc & { group?: PcGroup | null };
  previousStatus: PcStatus | null;
};

export type TransferredBillItem = {
  id: string;
  fromPcId: string;
  fromPcName: string;
  fromSessionId: string;
  playAmount: number;
  playDurationSeconds: number;
  playDurationText: string;
  hourlyRate: number;
  serviceAmount: number;
  serviceItemsSummary: string;
  totalAmount: number;
  transferredAt: string;
  note: string;
};

export type PcListItem = {
  id: string;
  agentId: string;
  name: string;
  groupId: string | null;
  groupName: string;
  hourlyRate: number;
  hostname: string | null;
  ipAddress: string | null;
  macAddress: string | null;
  connectedSockets: number;
  isConnected: boolean;
  status: PcStatus;
  lastSeenAt: string | null;
  activeSession: {
    id: string;
    startedAt: string;
    elapsedSeconds: number;
    billableMinutes: number;
    pricePerMinute: number;
    estimatedAmount: number;
  } | null;
  activeMember: {
    memberId: string;
    username: string;
    fullName: string;
    balance: number;
    memberType: string;
  } | null;
  activeGuest: {
    displayName: string;
    prepaidAmount: number;
  } | null;
  activeAdmin: {
    username: string;
    fullName: string;
  } | null;
};

@Injectable()
export class PcsService {
  public static readonly wakingPcIds = new Map<string, number>();
  private readonly macAddressByAgentId = new Map<string, string>();

  constructor(
    private readonly prisma: PrismaService,
    private readonly realtime: RealtimeService,
  ) {}

  async getPcList(): Promise<PcListItem[]> {
    const defaultGroup = await this.ensureDefaultGroup();

    const pcs = await this.prisma.pc.findMany({
      orderBy: [{ name: 'asc' }],
      include: {
        group: true,
        sessions: {
          where: { status: 'ACTIVE' },
          orderBy: { startedAt: 'desc' },
          take: 1,
        },
      },
    });

    const activeUsersByPc = await this.resolveActiveUsersByPcId(
      pcs.map((pc) => pc.id),
    );
    const activeMemberIds = Array.from(
      new Set(
        Array.from(activeUsersByPc.values())
          .filter((x) => x.member !== null)
          .map((x) => x.member!.memberId),
      ),
    );
    const activeMembers = activeMemberIds.length
      ? await this.prisma.member.findMany({
          where: {
            id: { in: activeMemberIds },
          },
          select: {
            id: true,
            balance: true,
            memberType: true,
            playSeconds: true,
            usePromoPlayTime: true,
          },
        })
      : [];
    const activeMemberBalanceById = new Map(
      activeMembers.map((member) => [member.id, Number(member.balance)]),
    );
    const activeMemberPlaySecondsById = new Map(
      activeMembers.map((member) => [member.id, Number(member.playSeconds)]),
    );
    const activeMemberTypeById = new Map(
      activeMembers.map((member) => [member.id, member.memberType]),
    );
    const activeMemberUsePromoPlayTimeById = new Map(
      activeMembers.map((member) => [member.id, Boolean(member.usePromoPlayTime)]),
    );

    const pricingStepSetting = await this.prisma.appSetting.findUnique({
      where: { key: 'PRICING_STEP' },
    });
    const minimumChargeSetting = await this.prisma.appSetting.findUnique({
      where: { key: 'MINIMUM_CHARGE' },
    });
    const pricingStep = pricingStepSetting ? Number(pricingStepSetting.value) : 1000;
    const minimumCharge = minimumChargeSetting ? Number(minimumChargeSetting.value) : 1000;

    const now = Date.now();
    const activePromotions = await this.prisma.timeBasedPromotion.findMany({
      where: { isActive: true },
      select: {
        daysOfWeek: true,
        annualDates: true,
        startTime: true,
        endTime: true,
        discountPercent: true,
        isActive: true,
      },
    });

    const activeSessionIds = pcs.map((pc) => pc.sessions[0]?.id).filter(Boolean) as string[];
    const allServiceOrders = activeSessionIds.length ? await this.prisma.pcServiceOrder.findMany({
      where: { sessionId: { in: activeSessionIds } },
      select: { id: true, pcId: true },
    }) : [];

    const allPaidEvents = pcs.length ? await this.prisma.eventLog.findMany({
      where: { eventType: 'service.order.paid', pcId: { in: pcs.map((p) => p.id) } },
      select: { payload: true },
      orderBy: { createdAt: 'desc' },
      take: 2000,
    }) : [];

    const paidIds = new Set<string>();
    for (const event of allPaidEvents) {
      const payload = event.payload as any;
      if (payload && Array.isArray(payload.orderIds)) {
        for (const id of payload.orderIds) {
          if (typeof id === 'string') {
            paidIds.add(id.trim());
          }
        }
      }
    }

    const unpaidOrdersByPc = new Set<string>();
    for (const order of allServiceOrders) {
      if (!paidIds.has(order.id)) {
        unpaidOrdersByPc.add(order.pcId);
      }
    }

    const items = await Promise.all(
      pcs.map(async (pc) => {
        const activeSession = pc.sessions[0] ?? null;
        const activeUserFromLatest = activeUsersByPc.get(pc.id) ?? null;
        const activeAdmin = activeUserFromLatest?.admin ?? null;
        let activeMemberBase = null;
        let activeGuest = null;

        if (activeSession && activeUserFromLatest) {
          if (activeUserFromLatest.member) {
            activeMemberBase = activeUserFromLatest.member;
          } else if (activeUserFromLatest.guest) {
            activeGuest = activeUserFromLatest.guest;
          }
        }

        const effectiveGroup = pc.group ?? defaultGroup;
        const baseHourlyRate = activeMemberBase 
          ? Number((effectiveGroup as any).memberHourlyRate ?? effectiveGroup.hourlyRate)
          : Number(effectiveGroup.hourlyRate);
        const hourlyRate = getEffectiveHourlyRateAt(
          baseHourlyRate,
          new Date(now),
          activePromotions,
        );

        const activeMember = activeMemberBase
          ? {
              ...activeMemberBase,
              balance: activeMemberBalanceById.get(activeMemberBase.memberId) ?? 0,
              playSeconds: activeMemberPlaySecondsById.get(activeMemberBase.memberId) ?? 0,
              memberType: activeMemberTypeById.get(activeMemberBase.memberId) ?? 'REGULAR',
              usePromoPlayTime: activeMemberUsePromoPlayTimeById.get(activeMemberBase.memberId) ?? false,
            }
          : null;
        const sessionClockAtMs =
          pc.status === PcStatus.OFFLINE
            ? Math.min(
                now,
                pc.lastSeenAt?.getTime() ?? activeSession?.startedAt.getTime() ?? now,
              )
            : now;
        const effectiveStartedAtMs = activeSession
          ? Math.max(
              activeSession.startedAt.getTime(),
              activeSession.createdAt.getTime(),
            )
          : sessionClockAtMs;
        const effectiveStartedAt = new Date(effectiveStartedAtMs);
        const elapsedSeconds = activeSession
          ? Math.max(0, Math.floor((sessionClockAtMs - effectiveStartedAtMs) / 1000))
          : 0;
        const billableMinutes = activeSession
          ? Math.max(1, Math.ceil(elapsedSeconds / 60))
          : 0;

        const isUsingPromoPlayTime = Boolean(
          activeMember &&
            activeMember.memberType === 'COMBO' &&
            Number(activeMember.playSeconds) > 0,
        );

        let estimatedAmount =
          activeSession && !isUsingPromoPlayTime
            ? calculateSessionAmountByPromotions({
                startedAt: effectiveStartedAt,
                endedAt: new Date(sessionClockAtMs),
                baseHourlyRate,
                promotions: activePromotions,
                mode: 'PER_MINUTE_STARTED',
              })
            : 0;

        if (activeSession && !isUsingPromoPlayTime) {
          if (pricingStep > 0) {
            estimatedAmount = Math.ceil(estimatedAmount / pricingStep) * pricingStep;
          }
          if (estimatedAmount < minimumCharge) {
            estimatedAmount = minimumCharge;
          }
        }

        const connectedSockets = this.realtime.countAgentSockets(pc.agentId);
        let status: any = pc.status;
        if (status === PcStatus.OFFLINE) {
          const wakeTime = PcsService.wakingPcIds.get(pc.id);
          if (wakeTime && (now - wakeTime) < 180000) {
            status = 'BOOTING';
          }
        }

        return {
          id: pc.id,
          agentId: pc.agentId,
          name: pc.name,
          groupId: effectiveGroup.id,
          groupName: effectiveGroup.name,
          hourlyRate,
          hostname: pc.hostname,
          ipAddress: pc.ipAddress,
          macAddress:
            this.macAddressByAgentId.get(pc.agentId.toUpperCase()) ?? null,
          connectedSockets,
          isConnected: connectedSockets > 0,
          status,
          lastSeenAt: pc.lastSeenAt?.toISOString() ?? null,
          activeSession: activeSession
            ? {
                id: activeSession.id,
                startedAt: effectiveStartedAt.toISOString(),
                elapsedSeconds,
                billableMinutes,
                pricePerMinute: Number(activeSession.pricePerMinute ?? 0),
                estimatedAmount,
              }
            : null,
          hasUnpaidServices: unpaidOrdersByPc.has(pc.id),
          activeMember,
          activeGuest,
          activeAdmin,
        };
      }),
    );

    return items;
  }

  async getEffectiveHourlyRate(baseRate: number): Promise<number> {
    const promotions = await this.prisma.timeBasedPromotion.findMany({
      where: { isActive: true },
      select: {
        daysOfWeek: true,
        annualDates: true,
        startTime: true,
        endTime: true,
        discountPercent: true,
        isActive: true,
      },
    });

    return getEffectiveHourlyRateAt(
      baseRate,
      new Date(),
      promotions,
    );
  }

  async getActivePromotions(): Promise<{ name: string; discountPercent: number }[]> {
    const promotions = await this.prisma.timeBasedPromotion.findMany({
      where: { isActive: true },
      select: {
        name: true,
        daysOfWeek: true,
        annualDates: true,
        startTime: true,
        endTime: true,
        discountPercent: true,
        isActive: true,
      },
    });

    const activePromos = getActivePromotionsAt(new Date(), promotions);
    return activePromos.map(promo => ({
      name: promo.name ?? '',
      discountPercent: Number(promo.discountPercent ?? 0),
    }));
  }

  async getGuestLoginEnabled(): Promise<boolean> {
    const setting = await this.prisma.appSetting.findUnique({
      where: { key: 'GUEST_LOGIN_ENABLED' },
    });
    if (!setting) {
      return true; // Default to true if not set
    }
    return setting.value === 'true';
  }

  async startGuestSession(agentId: string) {
    const guestEnabled = await this.getGuestLoginEnabled();
    if (!guestEnabled) {
      throw new BadRequestException(
        'Dang tat dang nhap khach vang lai tren may chu',
      );
    }

    const pc = await this.prisma.pc.findFirst({
      where: { agentId },
      include: { group: true },
    });
    if (!pc) {
      throw new NotFoundException(`Khong tim thay may tram voi agentId=${agentId}`);
    }

    if (pc.status === PcStatus.IN_USE) {
      throw new BadRequestException('May da dang su dung');
    }

    const defaultGroup = await this.ensureDefaultGroup();
    const effectiveGroup = pc.group ?? defaultGroup;
    const baseHourlyRate = Number(effectiveGroup.hourlyRate);
    const hourlyRate = await this.getEffectiveHourlyRate(baseHourlyRate);
    const pricePerMinute = hourlyRate / 60;

    const result = await this.prisma.$transaction(async (tx) => {
      // Reset chat session messages for the new guest session
      await tx.supportMessage.deleteMany({
        where: { pcId: pc.id },
      });

      // Create guest session
      const session = await tx.session.create({
        data: {
          pcId: pc.id,
          status: 'ACTIVE',
          startedAt: new Date(),
          pricePerMinute,
        },
      });

      // Update PC status
      const updatedPc = await tx.pc.update({
        where: { id: pc.id },
        data: { status: PcStatus.IN_USE },
        include: { group: true },
      });

      // Log guest presence event so it shows up in the admin list immediately
      await tx.eventLog.create({
        data: {
          source: EventSource.SERVER,
          eventType: 'guest.pc.presence',
          pcId: pc.id,
          payload: {
            isActive: true,
            displayName: 'Khách vãng lai',
            prepaidAmount: 0,
            at: new Date().toISOString(),
          },
        },
      });

      await tx.eventLog.create({
        data: {
          source: EventSource.SERVER,
          eventType: 'pc.status.changed',
          pcId: pc.id,
          payload: {
            previousStatus: pc.status,
            status: PcStatus.IN_USE,
            sourceEvent: 'guest_login_request',
          },
        },
      });

      return { session, pc: updatedPc };
    });

    this.realtime.emitToAll('pc.status.changed', {
      pcId: pc.id,
      agentId: pc.agentId,
      previousStatus: pc.status,
      status: PcStatus.IN_USE,
      at: new Date().toISOString(),
      sourceEvent: 'guest_login_request',
    });

    this.realtime.emitToAgent(pc.agentId, 'admin.support_cleared', {});
    this.realtime.emitToAll('client.support_cleared', { pcId: pc.id });

    return result;
  }

  async registerPresence(
    payload: PresencePayload,
    fallbackIp?: string,
  ): Promise<PresenceTransition> {
    const defaultGroup = await this.ensureDefaultGroup();
    const agentId = payload.agentId?.trim();
    if (!agentId) {
      throw new Error('agentId is required');
    }

    const seenAt = this.parseSeenAt(payload.at);
    const ipAddress = this.resolveIp(payload.ip, fallbackIp);
    const hostname = this.parseOptional(payload.hostname);
    const macAddress = this.normalizeMacAddress(payload.macAddress);
    if (macAddress) {
      this.macAddressByAgentId.set(agentId.toUpperCase(), macAddress);
    }
    const existing = await this.prisma.pc.findUnique({
      where: { agentId },
      include: { group: true },
    });

    if (!existing) {
      const createdPc = await this.prisma.pc.create({
        data: {
          agentId,
          name: hostname ?? agentId,
          groupId: defaultGroup.id,
          hostname,
          ipAddress,
          status: PcStatus.ONLINE,
          lastSeenAt: seenAt,
        },
        include: { group: true },
      });

      await this.logEvent('pc.registered', createdPc.id, {
        agentId: createdPc.agentId,
        status: createdPc.status,
      });
      return { pc: createdPc, previousStatus: null };
    }

    PcsService.wakingPcIds.delete(existing.id);

    let nextStatus = existing.status;
    if (existing.status === PcStatus.OFFLINE) {
      const activeSession = await this.prisma.session.findFirst({
        where: {
          pcId: existing.id,
          status: 'ACTIVE',
        },
        select: {
          id: true,
          startedAt: true,
        },
      });

      // Preserve billing fairness:
      // when an agent reconnects after being OFFLINE, exclude offline downtime
      // from the active session clock by shifting startedAt forward.
      if (activeSession) {
        const offlineGapMs = Math.max(
          0,
          seenAt.getTime() - (existing.lastSeenAt?.getTime() ?? seenAt.getTime()),
        );
        if (offlineGapMs > 0) {
          await this.prisma.session.update({
            where: { id: activeSession.id },
            data: {
              startedAt: new Date(activeSession.startedAt.getTime() + offlineGapMs),
            },
          });
        }
      }

      nextStatus = activeSession ? PcStatus.IN_USE : PcStatus.ONLINE;
    }

    const updatedPc = await this.prisma.pc.update({
      where: { id: existing.id },
      data: {
        groupId: existing.groupId ?? defaultGroup.id,
        hostname: hostname ?? existing.hostname,
        ipAddress: ipAddress ?? existing.ipAddress,
        status: nextStatus,
        lastSeenAt: seenAt,
      },
      include: { group: true },
    });

    if (existing.status !== updatedPc.status) {
      await this.logEvent('pc.status.changed', updatedPc.id, {
        previousStatus: existing.status,
        status: updatedPc.status,
        sourceEvent: 'presence',
      });
    }
    return { pc: updatedPc, previousStatus: existing.status };
  }

  async markStaleAgentsOffline(
    timeoutSeconds: number,
  ): Promise<PresenceTransition[]> {
    const cutoff = new Date(Date.now() - timeoutSeconds * 1000);
    const staleAgents = await this.prisma.pc.findMany({
      where: {
        status: { not: PcStatus.OFFLINE },
        OR: [{ lastSeenAt: null }, { lastSeenAt: { lt: cutoff } }],
      },
    });

    if (staleAgents.length === 0) {
      return [];
    }

    const staleIds = staleAgents.map((pc) => pc.id);
    await this.prisma.pc.updateMany({
      where: { id: { in: staleIds } },
      data: { status: PcStatus.OFFLINE },
    });

    const updatedAgents = await this.prisma.pc.findMany({
      where: { id: { in: staleIds } },
    });
    const updatedById = new Map(updatedAgents.map((pc) => [pc.id, pc]));

    const transitions = staleAgents
      .map((stalePc) => {
        const updatedPc = updatedById.get(stalePc.id);
        if (!updatedPc) {
          return null;
        }

        return {
          pc: updatedPc,
          previousStatus: stalePc.status,
        };
      })
      .filter((item): item is PresenceTransition => item !== null);

    await Promise.all(
      transitions.map((transition) =>
        this.logEvent('pc.status.changed', transition.pc.id, {
          previousStatus: transition.previousStatus,
          status: transition.pc.status,
          sourceEvent: 'presence.timeout',
        }),
      ),
    );

    return transitions;
  }

  async markAgentOfflineByDisconnect(
    agentId: string,
  ): Promise<PresenceTransition | null> {
    const normalizedAgentId = agentId.trim();
    if (!normalizedAgentId) {
      return null;
    }

    const existing = await this.prisma.pc.findFirst({
      where: {
        agentId: {
          equals: normalizedAgentId,
          mode: 'insensitive',
        },
      },
    });

    if (!existing || existing.status === PcStatus.OFFLINE) {
      return null;
    }

    const updated = await this.prisma.pc.update({
      where: { id: existing.id },
      data: { status: PcStatus.OFFLINE },
    });

    await this.logEvent('pc.status.changed', updated.id, {
      previousStatus: existing.status,
      status: updated.status,
      sourceEvent: 'socket.disconnect',
    });

    return {
      pc: updated,
      previousStatus: existing.status,
    };
  }

  async getPcByAgentId(agentId: string) {
    return this.prisma.pc.findFirst({
      where: {
        agentId: {
          equals: agentId,
          mode: 'insensitive',
        },
      },
    });
  }

  async getActivePresenceKindForPc(
    pcId: string,
  ): Promise<'MEMBER' | 'GUEST' | 'ADMIN' | null> {
    const latestPresence = await this.prisma.eventLog.findFirst({
      where: {
        pcId,
        eventType: {
          in: ['member.pc.presence', 'guest.pc.presence', 'admin.pc.presence'],
        },
      },
      orderBy: {
        createdAt: 'desc',
      },
    });

    if (latestPresence?.eventType === 'member.pc.presence') {
      return this.parseMemberPresencePayload(latestPresence.payload)
        ? 'MEMBER'
        : null;
    }

    if (latestPresence?.eventType === 'guest.pc.presence') {
      return this.parseGuestPresencePayload(latestPresence.payload) ? 'GUEST' : null;
    }

    if (latestPresence?.eventType === 'admin.pc.presence') {
      return this.parseAdminPresencePayload(latestPresence.payload) ? 'ADMIN' : null;
    }

    return null;
  }

  private async resolveActiveUsersByPcId(
    pcIds: string[],
  ): Promise<
    Map<
      string,
      {
        admin: { username: string; fullName: string } | null;
        member: { memberId: string; username: string; fullName: string } | null;
        guest: { displayName: string; prepaidAmount: number } | null;
        latestKind: 'ADMIN' | 'MEMBER' | 'GUEST' | null;
      }
    >
  > {
    const result = new Map<
      string,
      {
        admin: { username: string; fullName: string } | null;
        member: { memberId: string; username: string; fullName: string } | null;
        guest: { displayName: string; prepaidAmount: number } | null;
        latestKind: 'ADMIN' | 'MEMBER' | 'GUEST' | null;
      }
    >();
    if (pcIds.length === 0) {
      return result;
    }

    await Promise.all(
      pcIds.map(async (pcId) => {
        const presences = await this.prisma.eventLog.findMany({
          where: {
            pcId,
            eventType: {
              in: ['member.pc.presence', 'guest.pc.presence', 'admin.pc.presence'],
            },
          },
          orderBy: {
            createdAt: 'desc',
          },
          take: 10,
        });

        let admin = null;
        let member = null;
        let guest = null;
        let latestKind: 'ADMIN' | 'MEMBER' | 'GUEST' | null = null;

        for (const presence of presences) {
          if (presence.eventType === 'admin.pc.presence') {
            const parsed = this.parseAdminPresencePayload(presence.payload);
            if (parsed && !admin) {
              admin = parsed;
              if (!latestKind) latestKind = 'ADMIN';
            }
          } else if (presence.eventType === 'member.pc.presence') {
            const parsed = this.parseMemberPresencePayload(presence.payload);
            if (parsed && !member && !guest) {
              member = parsed;
              if (!latestKind) latestKind = 'MEMBER';
            }
          } else if (presence.eventType === 'guest.pc.presence') {
            const parsed = this.parseGuestPresencePayload(presence.payload);
            if (parsed && !guest && !member) {
              guest = parsed;
              if (!latestKind) latestKind = 'GUEST';
            }
          }
        }

        result.set(pcId, {
          admin,
          member,
          guest,
          latestKind,
        });
      }),
    );

    return result;
  }

  private parseMemberPresencePayload(
    payload?: Prisma.JsonValue | null,
  ): { memberId: string; username: string; fullName: string } | null {
    if (!payload || typeof payload !== 'object' || Array.isArray(payload)) {
      return null;
    }

    const value = payload as Record<string, unknown>;
    if (value.isActive !== true) {
      return null;
    }

    const memberId = this.readString(value.memberId);
    const username = this.readString(value.username);
    const fullName = this.readString(value.fullName) || username;

    if (!memberId || !username) {
      return null;
    }

    return {
      memberId,
      username,
      fullName,
    };
  }

  private parseGuestPresencePayload(
    payload?: Prisma.JsonValue | null,
  ): { displayName: string; prepaidAmount: number } | null {
    if (!payload || typeof payload !== 'object' || Array.isArray(payload)) {
      return null;
    }

    const value = payload as Record<string, unknown>;
    if (value.isActive !== true) {
      return null;
    }

    const displayName = this.readString(value.displayName) || 'Khách vãng lai';
    const prepaidAmount = this.readNumber(value.prepaidAmount);

    return {
      displayName,
      prepaidAmount,
    };
  }

  private parseAdminPresencePayload(
    payload?: Prisma.JsonValue | null,
  ): { username: string; fullName: string } | null {
    if (!payload || typeof payload !== 'object' || Array.isArray(payload)) {
      return null;
    }

    const value = payload as Record<string, unknown>;
    if (value.isActive !== true) {
      return null;
    }

    const username = this.readString(value.username) || 'Admin';
    const fullName = this.readString(value.fullName) || username;
    return { username, fullName };
  }

  private readString(value: unknown): string {
    if (typeof value !== 'string') {
      return '';
    }

    return value.trim();
  }

  private readNumber(value: unknown): number {
    if (typeof value === 'number' && Number.isFinite(value)) {
      return value;
    }

    if (typeof value === 'string') {
      const parsed = Number(value);
      if (Number.isFinite(parsed)) {
        return parsed;
      }
    }

    return 0;
  }

  private parseSeenAt(at?: string): Date {
    if (!at) {
      return new Date();
    }

    const parsed = new Date(at);
    return Number.isNaN(parsed.getTime()) ? new Date() : parsed;
  }

  private resolveIp(rawIp?: string, fallbackIp?: string): string | null {
    return this.parseOptional(rawIp) ?? this.parseOptional(fallbackIp);
  }

  private parseOptional(value?: string): string | null {
    const trimmed = value?.trim();
    return trimmed ? trimmed : null;
  }

  private normalizeMacAddress(value?: string): string | null {
    const compact = (value ?? '').replace(/[^0-9a-fA-F]/g, '').toUpperCase();
    if (compact.length !== 12 || !/^[0-9A-F]{12}$/.test(compact)) {
      return null;
    }

    return compact.match(/.{1,2}/g)?.join(':') ?? null;
  }

  private async ensureDefaultGroup() {
    const existingDefault = await this.prisma.pcGroup.findFirst({
      where: { isDefault: true },
      orderBy: { updatedAt: 'desc' },
    });
    if (existingDefault) {
      return existingDefault;
    }

    const fallbackGroup = await this.prisma.pcGroup.findFirst({
      where: { name: 'Mặc định' },
    });
    if (fallbackGroup) {
      return this.prisma.$transaction(async (tx) => {
        await tx.pcGroup.updateMany({
          where: { isDefault: true },
          data: { isDefault: false },
        });

        return tx.pcGroup.update({
          where: { id: fallbackGroup.id },
          data: { isDefault: true },
        });
      });
    }

    return this.prisma.pcGroup.create({
      data: {
        name: 'Mặc định',
        hourlyRate: 5000,
        memberHourlyRate: 5000,
        isDefault: true,
      },
    });
  }

  async getActiveSessionForPc(pcId: string) {
    return this.prisma.session.findFirst({
      where: {
        pcId,
        status: 'ACTIVE',
      },
      orderBy: { startedAt: 'desc' },
    });
  }

  async getActiveMemberForPc(
    pcId: string,
  ): Promise<{ memberId: string; username: string; fullName: string; memberType: string; rank: string; balance: number } | null> {
    const latestPresence = await this.prisma.eventLog.findFirst({
      where: {
        pcId,
        eventType: 'member.pc.presence',
      },
      orderBy: {
        createdAt: 'desc',
      },
    });

    if (!latestPresence) {
      return null;
    }

    const memberPayload = this.parseMemberPresencePayload(latestPresence.payload);
    if (!memberPayload) {
      return null;
    }

    const member = await this.prisma.member.findUnique({
      where: { id: memberPayload.memberId },
      select: {
        id: true,
        username: true,
        fullName: true,
        memberType: true,
        totalTopup: true,
        balance: true,
      },
    });

    if (!member) {
      return null;
    }

    const rankConfigs = await this.prisma.loyaltyRankConfig.findMany({
      orderBy: { minTopup: 'desc' },
    });

    let rank = 'Sắt';
    if (rankConfigs.length > 0) {
      const matched = rankConfigs.find((c) => Number(member.totalTopup) >= Number(c.minTopup));
      if (matched) {
        rank = matched.rankName;
      }
    }

    return {
      memberId: member.id,
      username: member.username,
      fullName: member.fullName,
      memberType: member.memberType,
      balance: Number(member.balance),
      rank,
    };
  }

  async getActiveGuestForPc(
    pcId: string,
  ): Promise<{ displayName: string; prepaidAmount: number } | null> {
    const latestPresence = await this.prisma.eventLog.findFirst({
      where: {
        pcId,
        eventType: 'guest.pc.presence',
      },
      orderBy: {
        createdAt: 'desc',
      },
    });

    if (!latestPresence) {
      return null;
    }

    return this.parseGuestPresencePayload(latestPresence.payload);
  }

  async getActiveSessionBillingForPc(pcId: string): Promise<{
    elapsedSeconds: number;
    billableMinutes: number;
    estimatedAmount: number;
    hourlyRate: number;
  } | null> {
    const pc = await this.prisma.pc.findUnique({
      where: { id: pcId },
      include: {
        group: true,
        sessions: {
          where: { status: 'ACTIVE' },
          orderBy: { startedAt: 'desc' },
          take: 1,
        },
      },
    });

    if (!pc || !pc.sessions[0]) {
      return null;
    }

    const activeSession = pc.sessions[0];
    const now = Date.now();
    const effectiveStartedAtMs = Math.max(
      activeSession.startedAt.getTime(),
      activeSession.createdAt.getTime(),
    );
    const elapsedSeconds = Math.max(0, Math.floor((now - effectiveStartedAtMs) / 1000));
    const billableMinutes = Math.max(1, Math.ceil(elapsedSeconds / 60));

    const activeMember = await this.getActiveMemberForPc(pc.id);
    const isUsingPromoPlayTime = Boolean(
      activeMember &&
        activeMember.memberType === 'COMBO' &&
        ((activeMember as any).playSeconds ?? 0) > 0,
    );

    const activePromotions = await this.prisma.timeBasedPromotion.findMany({
      where: { isActive: true },
      select: {
        daysOfWeek: true,
        annualDates: true,
        startTime: true,
        endTime: true,
        discountPercent: true,
        isActive: true,
      },
    });

    const defaultGroup = await this.prisma.pcGroup.findFirst({
      where: { isDefault: true },
    });
    const effectiveGroup = pc.group ?? defaultGroup;
    const baseHourlyRate = activeMember
      ? Number((effectiveGroup as any)?.memberHourlyRate ?? effectiveGroup?.hourlyRate ?? 12000)
      : Number(effectiveGroup?.hourlyRate ?? 12000);

    const currentHourlyRate = getEffectiveHourlyRateAt(
      baseHourlyRate,
      new Date(now),
      activePromotions,
    );

    const pricingStepSetting = await this.prisma.appSetting.findUnique({
      where: { key: 'PRICING_STEP' },
    });
    const minimumChargeSetting = await this.prisma.appSetting.findUnique({
      where: { key: 'MINIMUM_CHARGE' },
    });
    const pricingStep = pricingStepSetting ? Number(pricingStepSetting.value) : 1000;
    const minimumCharge = minimumChargeSetting ? Number(minimumChargeSetting.value) : 1000;

    let estimatedAmount =
      activeSession && !isUsingPromoPlayTime
        ? calculateSessionAmountByPromotions({
            startedAt: new Date(effectiveStartedAtMs),
            endedAt: new Date(now),
            baseHourlyRate,
            promotions: activePromotions,
            mode: 'PER_MINUTE_STARTED',
          })
        : 0;

    if (activeSession && !isUsingPromoPlayTime) {
      if (pricingStep > 0) {
        estimatedAmount = Math.ceil(estimatedAmount / pricingStep) * pricingStep;
      }
      if (estimatedAmount < minimumCharge) {
        estimatedAmount = minimumCharge;
      }
    }

    return {
      elapsedSeconds,
      billableMinutes,
      estimatedAmount,
      hourlyRate: currentHourlyRate,
    };
  }

  private async logEvent(
    eventType: string,
    pcId?: string,
    payload?: Prisma.InputJsonValue,
  ): Promise<void> {
    try {
      await this.prisma.eventLog.create({
        data: {
          source: EventSource.SERVER,
          eventType,
          pcId,
          payload,
        },
      });
    } catch {
      // Ignore audit log failures.
    }
  }

  async getAllPcHardware() {
    return this.prisma.pcHardware.findMany();
  }

  async getPcHardware(pcId: string) {
    let hardware = await this.prisma.pcHardware.findUnique({
      where: { pcId },
    });
    if (!hardware) {
      hardware = await this.prisma.pcHardware.create({
        data: { pcId },
      });
    }
    return hardware;
  }

  async updatePcHardware(pcId: string, dto: any) {
    return this.prisma.pcHardware.upsert({
      where: { pcId },
      create: {
        pcId,
        cpu: dto.cpu,
        ram: dto.ram,
        gpu: dto.gpu,
        motherboard: dto.motherboard,
        storage: dto.storage,
        monitor: dto.monitor,
        note: dto.note,
      },
      update: {
        cpu: dto.cpu,
        ram: dto.ram,
        gpu: dto.gpu,
        motherboard: dto.motherboard,
        storage: dto.storage,
        monitor: dto.monitor,
        note: dto.note,
      },
    });
  }

  async updateAutoFetchedHardware(pcId: string, dto: any) {
    const updateData: any = {
      cpu: dto.cpu,
      ram: dto.ram,
      gpu: dto.gpu,
      motherboard: dto.motherboard,
      lan: dto.lan,
      temperature: dto.temperature,
    };
    
    // Only auto-update monitor if it's successfully detected
    if (dto.monitor && dto.monitor !== "Unknown Monitor") {
      updateData.monitor = dto.monitor;
    }

    if (dto.storage) {
      updateData.storage = dto.storage;
    }

    return this.prisma.pcHardware.upsert({
      where: { pcId },
      create: {
        pcId,
        ...updateData
      },
      update: {
        ...updateData
      },
    });
  }

  async getPcMaintenanceLogs(pcId: string) {
    return this.prisma.pcMaintenanceLog.findMany({
      where: { pcId },
      orderBy: { date: 'desc' },
    });
  }

  async addPcMaintenanceLog(pcId: string, dto: any) {
    return this.prisma.pcMaintenanceLog.create({
      data: {
        pcId,
        date: new Date(dto.date),
        description: dto.description,
        cost: dto.cost,
        performedBy: dto.performedBy,
      },
    });
  }

  // ── Support Messages ──

  async saveSupportMessage(pcId: string, direction: string, message: string, category?: string, sentBy?: string) {
    return this.prisma.supportMessage.create({
      data: { pcId, direction, message, category, sentBy },
    });
  }

  async getSupportMessages(pcId: string, limit = 50) {
    return this.prisma.supportMessage.findMany({
      where: { pcId },
      orderBy: { createdAt: 'desc' },
      take: limit,
    });
  }

  async clearSupportMessages(pcId: string) {
    const pc = await this.prisma.pc.findUnique({
      where: { id: pcId },
      select: { id: true, agentId: true },
    });
    const result = await this.prisma.supportMessage.deleteMany({
      where: { pcId },
    });
    if (pc) {
      this.realtime.emitToAgent(pc.agentId, 'admin.support_cleared', {});
      this.realtime.emitToAll('client.support_cleared', { pcId: pc.id });
    }
    return result;
  }

  async getRecentSupportMessages(limit = 100) {
    return this.prisma.supportMessage.findMany({
      orderBy: { createdAt: 'desc' },
      include: { pc: { select: { name: true } } },
      take: limit,
    });
  }

  async replySupportMessage(pcId: string, message: string, repliedBy: string) {
    const pc = await this.prisma.pc.findUnique({ where: { id: pcId } });
    if (!pc) throw new Error('PC not found');

    const msg = await this.prisma.supportMessage.create({
      data: { pcId, direction: 'ADMIN', message, sentBy: repliedBy },
    });

    this.realtime.emitToAgent(pc.agentId, 'admin.reply_support', {
      message,
      repliedBy,
      sentAt: msg.createdAt.toISOString(),
    });

    return msg;
  }

  async markSupportMessagesRead(pcId: string) {
    return this.prisma.supportMessage.updateMany({
      where: { pcId, direction: 'CLIENT', isRead: false },
      data: { isRead: true },
    });
  }

  async getUnreadSupportCount() {
    return this.prisma.supportMessage.count({
      where: { direction: 'CLIENT', isRead: false },
    });
  }

  // ── App Usage Tracking ──

  async upsertAppUsage(pcId: string, apps: { name: string; title?: string }[]) {
    const now = new Date();
    const activeSession = await this.prisma.session.findFirst({
      where: { pcId, status: 'ACTIVE' },
      orderBy: { startedAt: 'desc' },
    });
    const sessionId = activeSession?.id ?? null;

    // Find currently open (not ended) app entries for this PC
    const openEntries = await this.prisma.pcAppUsage.findMany({
      where: { pcId, endedAt: null },
    });

    const currentAppNames = new Set(apps.map(a => a.name.toLowerCase()));
    const openByName = new Map(openEntries.map(e => [e.appName.toLowerCase(), e]));

    // Close apps that are no longer running
    const toClose = openEntries.filter(e => !currentAppNames.has(e.appName.toLowerCase()));
    for (const entry of toClose) {
      const duration = Math.max(0, Math.floor((now.getTime() - entry.startedAt.getTime()) / 1000));
      await this.prisma.pcAppUsage.update({
        where: { id: entry.id },
        data: { endedAt: now, durationSeconds: duration },
      });
    }

    // Create new entries for newly detected apps
    for (const app of apps) {
      if (!openByName.has(app.name.toLowerCase())) {
        await this.prisma.pcAppUsage.create({
          data: {
            pcId,
            sessionId,
            appName: app.name,
            windowTitle: app.title ?? null,
            startedAt: now,
          },
        });
      } else {
        // Update window title if changed
        const existing = openByName.get(app.name.toLowerCase())!;
        if (app.title && app.title !== existing.windowTitle) {
          await this.prisma.pcAppUsage.update({
            where: { id: existing.id },
            data: { windowTitle: app.title },
          });
        }
      }
    }
  }

  async getAppUsage(pcId: string, from?: Date, to?: Date) {
    const where: any = { pcId };
    if (from || to) {
      where.startedAt = {};
      if (from) where.startedAt.gte = from;
      if (to) where.startedAt.lte = to;
    }
    return this.prisma.pcAppUsage.findMany({
      where,
      orderBy: { startedAt: 'desc' },
      take: 200,
    });
  }

  async getCurrentRunningApps() {
    const results = await this.prisma.pcAppUsage.findMany({
      where: { endedAt: null },
      include: { pc: { select: { id: true, name: true, agentId: true } } },
      orderBy: { startedAt: 'desc' },
    });
    return results;
  }

  private formatPlayDuration(totalSeconds: number): string {
    const hours = Math.floor(totalSeconds / 3600);
    const minutes = Math.floor((totalSeconds % 3600) / 60);
    if (hours > 0) {
      return minutes > 0 ? `${hours} giờ ${minutes} phút` : `${hours} giờ`;
    }
    return `${Math.max(1, minutes)} phút`;
  }

  private getTransferredBillsSettingKey(pcId: string): string {
    return `__TRANSFERRED_BILLS_${pcId}__`;
  }

  async getTransferredBillsList(pcId: string): Promise<TransferredBillItem[]> {
    const key = this.getTransferredBillsSettingKey(pcId);
    const setting = await this.prisma.appSetting.findUnique({
      where: { key },
      select: { value: true },
    });
    if (!setting || !setting.value) {
      return [];
    }
    try {
      const parsed = JSON.parse(setting.value);
      return Array.isArray(parsed) ? parsed : [];
    } catch {
      return [];
    }
  }

  async getTransferredBills(pcId: string) {
    const bills = await this.getTransferredBillsList(pcId);
    const totalTransferredPlayAmount = bills.reduce(
      (sum, b) => sum + (Number(b.playAmount) || 0),
      0,
    );
    const totalTransferredServiceAmount = bills.reduce(
      (sum, b) => sum + (Number(b.serviceAmount) || 0),
      0,
    );
    const totalTransferredAmount =
      totalTransferredPlayAmount + totalTransferredServiceAmount;

    return {
      pcId,
      bills,
      totalTransferredPlayAmount,
      totalTransferredServiceAmount,
      totalTransferredAmount,
    };
  }

  async clearTransferredBills(pcId: string, requestedBy?: string) {
    const bills = await this.getTransferredBillsList(pcId);
    if (bills.length > 0) {
      const key = this.getTransferredBillsSettingKey(pcId);
      await this.prisma.appSetting.deleteMany({
        where: { key },
      });

      try {
        await this.prisma.eventLog.create({
          data: {
            source: EventSource.ADMIN,
            eventType: 'billing.transferred.cleared',
            pcId,
            payload: {
              pcId,
              clearedBills: bills,
              clearedAt: new Date().toISOString(),
              requestedBy: requestedBy || 'admin.desktop',
            },
          },
        });
      } catch {
        // Ignore event log error
      }

      this.realtime.emitToAll('billing.transferred.cleared', { pcId });
    }

    return { success: true, pcId, clearedCount: bills.length };
  }

  async transferBilling(
    fromPcId: string,
    targetPcId: string,
    note?: string,
    requestedBy?: string,
  ) {
    if (fromPcId === targetPcId) {
      throw new BadRequestException('Không thể chuyển thanh toán cho chính máy này');
    }

    const [fromPc, targetPc] = await Promise.all([
      this.prisma.pc.findUnique({
        where: { id: fromPcId },
        include: {
          sessions: {
            where: { status: 'ACTIVE' },
            orderBy: { startedAt: 'desc' },
            take: 1,
          },
        },
      }),
      this.prisma.pc.findUnique({
        where: { id: targetPcId },
        include: {
          sessions: {
            where: { status: 'ACTIVE' },
            orderBy: { startedAt: 'desc' },
            take: 1,
          },
        },
      }),
    ]);

    if (!fromPc) {
      throw new NotFoundException('Không tìm thấy máy chuyển');
    }
    if (!targetPc) {
      throw new NotFoundException('Không tìm thấy máy đích');
    }

    const targetSession = targetPc.sessions[0];
    if (!targetSession) {
      throw new BadRequestException(
        `Máy đích (${targetPc.name || targetPc.agentId}) không có phiên đang chơi để nhận cộng dồn thanh toán`,
      );
    }

    const fromSession = fromPc.sessions[0];
    if (!fromSession) {
      throw new BadRequestException(
        `Máy chuyển (${fromPc.name || fromPc.agentId}) không có phiên đang hoạt động`,
      );
    }

    // Validate session user types: Only Guest (Khách vãng lai) and VIP members are eligible
    const activeUsers = await this.resolveActiveUsersByPcId([fromPcId, targetPcId]);
    const fromUser = activeUsers.get(fromPcId);
    const targetUser = activeUsers.get(targetPcId);

    let fromMemberType: string | null = null;
    if (fromUser?.member?.memberId) {
      const m = await this.prisma.member.findUnique({
        where: { id: fromUser.member.memberId },
        select: { memberType: true },
      });
      fromMemberType = m?.memberType ?? null;
    } else {
      const m = await this.getActiveMemberForPc(fromPcId);
      if (m) fromMemberType = m.memberType;
    }

    if (fromUser?.admin) {
      throw new BadRequestException('Không thể chuyển thanh toán cho phiên Admin');
    }
    if (fromMemberType && fromMemberType !== 'VIP') {
      throw new BadRequestException(
        'Tính năng chuyển thanh toán chỉ áp dụng cho khách vãng lai và hội viên VIP',
      );
    }

    let targetMemberType: string | null = null;
    if (targetUser?.member?.memberId) {
      const m = await this.prisma.member.findUnique({
        where: { id: targetUser.member.memberId },
        select: { memberType: true },
      });
      targetMemberType = m?.memberType ?? null;
    } else {
      const m = await this.getActiveMemberForPc(targetPcId);
      if (m) targetMemberType = m.memberType;
    }

    if (targetUser?.admin) {
      throw new BadRequestException('Không thể chuyển thanh toán tới máy Admin');
    }
    if (targetMemberType && targetMemberType !== 'VIP') {
      throw new BadRequestException(
        'Máy đích nhận cộng dồn thanh toán phải là khách vãng lai hoặc hội viên VIP',
      );
    }

    const fromBilling = await this.getActiveSessionBillingForPc(fromPcId);
    if (!fromBilling) {
      throw new BadRequestException('Không thể tính tiền cho máy chuyển');
    }

    // 1. Fetch unpaid service orders on fromPc
    const [orders, paidEvents] = await Promise.all([
      this.prisma.pcServiceOrder.findMany({
        where: { pcId: fromPcId, sessionId: fromSession.id },
        include: { serviceItem: true },
        orderBy: [{ createdAt: 'asc' }],
      }),
      this.prisma.eventLog.findMany({
        where: { pcId: fromPcId, eventType: 'service.order.paid' },
        select: { payload: true },
        orderBy: [{ createdAt: 'desc' }],
        take: 500,
      }),
    ]);

    const paidOrderIds = new Set<string>();
    for (const event of paidEvents) {
      const payload = event.payload as any;
      if (payload?.orderIds && Array.isArray(payload.orderIds)) {
        payload.orderIds.forEach((id: string) => paidOrderIds.add(id));
      }
    }

    const unpaidOrders = orders.filter((o) => !paidOrderIds.has(o.id));
    const unpaidServicesTotal = unpaidOrders.reduce(
      (sum, o) => sum + Number(o.lineTotal ?? 0),
      0,
    );

    // 2. Move unpaid orders to targetPc and targetSession
    const fromLabel = fromPc.name || fromPc.agentId;
    for (const order of unpaidOrders) {
      const prefix = `[Gộp từ ${fromLabel}]`;
      const updatedNote = order.note ? `${prefix} ${order.note}` : prefix;
      await this.prisma.pcServiceOrder.update({
        where: { id: order.id },
        data: {
          pcId: targetPcId,
          sessionId: targetSession.id,
          note: updatedNote,
        },
      });
    }

    // 3. Collect prior transferred bills if fromPc already held some
    const fromPriorBills = await this.getTransferredBillsList(fromPcId);

    // 4. Build new TransferredBillItem
    const serviceSummary =
      unpaidOrders
        .map((o) => `${o.quantity}x ${o.serviceItem?.name || 'Món'}`)
        .join(', ') || 'Không có';

    const newBillItem: TransferredBillItem = {
      id: randomUUID(),
      fromPcId,
      fromPcName: fromLabel,
      fromSessionId: fromSession.id,
      playAmount: fromBilling.estimatedAmount,
      playDurationSeconds: fromBilling.elapsedSeconds,
      playDurationText: this.formatPlayDuration(fromBilling.elapsedSeconds),
      hourlyRate: fromBilling.hourlyRate,
      serviceAmount: unpaidServicesTotal,
      serviceItemsSummary: serviceSummary,
      totalAmount: fromBilling.estimatedAmount + unpaidServicesTotal,
      transferredAt: new Date().toISOString(),
      note: note || `Cộng dồn vào ${targetPc.name || targetPc.agentId}`,
    };

    // 5. Append to targetPc's transferred bills
    const targetExistingBills = await this.getTransferredBillsList(targetPcId);
    const updatedTargetBills = [
      ...targetExistingBills,
      ...fromPriorBills,
      newBillItem,
    ];

    const targetKey = this.getTransferredBillsSettingKey(targetPcId);
    await this.prisma.appSetting.upsert({
      where: { key: targetKey },
      update: { value: JSON.stringify(updatedTargetBills) },
      create: { key: targetKey, value: JSON.stringify(updatedTargetBills) },
    });

    // Clear fromPc's transferred bills key
    const fromKey = this.getTransferredBillsSettingKey(fromPcId);
    await this.prisma.appSetting.deleteMany({
      where: { key: fromKey },
    });

    // 6. Close fromSession & Lock fromPc
    await this.prisma.session.update({
      where: { id: fromSession.id },
      data: {
        status: 'CLOSED',
        endedAt: new Date(),
        amount: fromBilling.estimatedAmount,
        durationSeconds: fromBilling.elapsedSeconds,
        billableMinutes: fromBilling.billableMinutes,
        closedReason: 'ADMIN_LOCK',
      },
    });

    await this.prisma.pc.update({
      where: { id: fromPcId },
      data: { status: PcStatus.ONLINE },
    });

    // 7. Emit lock command and realtime events
    this.realtime.emitToAgent(fromPc.agentId, 'command.dispatch', {
      type: 'LOCK',
      pcId: fromPcId,
    });

    this.realtime.emitToAll('pc.status.changed', {
      pcId: fromPcId,
      agentId: fromPc.agentId,
      previousStatus: fromPc.status,
      status: PcStatus.ONLINE,
    });

    this.realtime.emitToAll('session.closed', {
      pcId: fromPcId,
      sessionId: fromSession.id,
      amount: fromBilling.estimatedAmount,
      billableMinutes: fromBilling.billableMinutes,
    });

    this.realtime.emitToAll('billing.transferred', {
      fromPcId,
      fromPcName: fromLabel,
      targetPcId,
      targetPcName: targetPc.name || targetPc.agentId,
      transferredBill: newBillItem,
      allTargetBills: updatedTargetBills,
    });

    this.realtime.emitToAll('service.orders.changed', { pcId: targetPcId });
    this.realtime.emitToAll('service.orders.changed', { pcId: fromPcId });

    // 8. Log audit event
    try {
      await this.prisma.eventLog.create({
        data: {
          source: EventSource.ADMIN,
          eventType: 'billing.transferred',
          pcId: fromPcId,
          payload: {
            fromPcId,
            fromPcName: fromLabel,
            targetPcId,
            targetPcName: targetPc.name || targetPc.agentId,
            bill: newBillItem,
            allTargetBills: updatedTargetBills,
            requestedBy: requestedBy || 'admin.desktop',
          },
        },
      });
    } catch {
      // Ignore event log error
    }

    const totalTransferredToTarget = updatedTargetBills.reduce(
      (sum, b) => sum + (Number(b.totalAmount) || 0),
      0,
    );

    return {
      success: true,
      fromPcName: fromLabel,
      targetPcName: targetPc.name || targetPc.agentId,
      transferredBill: newBillItem,
      allTargetBills: updatedTargetBills,
      totalTransferredToTarget,
    };
  }
}
