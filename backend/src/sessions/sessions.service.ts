import { BadRequestException, Injectable, NotFoundException } from '@nestjs/common';
import { ConfigService } from '@nestjs/config';
import { Cron, CronExpression } from '@nestjs/schedule';
import {
  EventSource,
  PcStatus,
  Prisma,
  SessionStatus,
} from '@prisma/client';
import { PrismaService } from '../prisma/prisma.service';
import { QuerySessionsDto } from './dto/query-sessions.dto';

@Injectable()
export class SessionsService {
  constructor(
    private readonly prisma: PrismaService,
    private readonly configService: ConfigService,
  ) {}

  async getSessions(query: QuerySessionsDto) {
    const where: Prisma.SessionWhereInput = {};

    if (query.pcId) {
      where.pcId = query.pcId;
    }

    if (query.status) {
      where.status = query.status as SessionStatus;
    }

    if (query.from || query.to) {
      where.startedAt = {
        gte: query.from ? new Date(query.from) : undefined,
        lte: query.to ? new Date(query.to) : undefined,
      };
    }

    if (query.endedFrom || query.endedTo) {
      where.endedAt = {
        gte: query.endedFrom ? new Date(query.endedFrom) : undefined,
        lt: query.endedTo ? new Date(query.endedTo) : undefined,
      };
    }

    const sessions = await this.prisma.session.findMany({
      where,
      include: {
        pc: true,
      },
      orderBy: [{ startedAt: 'desc' }],
      take: 200,
    });

    return {
      items: sessions.map((session) => ({
        id: session.id,
        pcId: session.pcId,
        pcName: session.pc.name,
        agentId: session.pc.agentId,
        status: session.status,
        startedAt: session.startedAt.toISOString(),
        endedAt: session.endedAt?.toISOString() ?? null,
        durationSeconds: session.durationSeconds,
        billableMinutes: session.billableMinutes,
        pricePerMinute: session.pricePerMinute
          ? Number(session.pricePerMinute)
          : null,
        amount: session.amount ? Number(session.amount) : null,
        closedReason: session.closedReason ?? null,
      })),
      total: sessions.length,
      serverTime: new Date().toISOString(),
    };
  }

  async getActiveSessions() {
    return this.getSessions({ status: 'ACTIVE' });
  }

  async transferActiveSession(
    fromPcId: string,
    targetPcId: string,
    requestedBy: string,
  ) {
    if (fromPcId === targetPcId) {
      throw new BadRequestException('Khong the chuyen sang cung mot may');
    }

    const result = await this.prisma.$transaction(async (tx) => {
      const fromPc = await tx.pc.findUnique({ where: { id: fromPcId } });
      const targetPc = await tx.pc.findUnique({ where: { id: targetPcId } });
      if (!fromPc || !targetPc) {
        throw new NotFoundException('Khong tim thay may');
      }

      if (targetPc.status !== PcStatus.ONLINE) {
        throw new BadRequestException('May dich khong o trang thai san sang');
      }

      const activeFrom = await tx.session.findFirst({
        where: { pcId: fromPcId, status: 'ACTIVE' },
        orderBy: { startedAt: 'desc' },
      });
      if (!activeFrom) {
        throw new BadRequestException('May nguon khong co phien dang choi');
      }

      const activeTarget = await tx.session.findFirst({
        where: { pcId: targetPcId, status: 'ACTIVE' },
      });
      if (activeTarget) {
        throw new BadRequestException('May dich dang co phien dang choi');
      }

      const activePresence = await tx.eventLog.findFirst({
        where: {
          pcId: fromPcId,
          eventType: {
            in: ['member.pc.presence', 'guest.pc.presence', 'admin.pc.presence'],
          },
        },
        orderBy: { createdAt: 'desc' },
        select: {
          eventType: true,
          payload: true,
        },
      });
      const transferablePresence = this.extractTransferablePresence(
        activePresence?.eventType,
        activePresence?.payload,
      );

      const sessionOrders = await tx.pcServiceOrder.findMany({
        where: {
          pcId: fromPcId,
          sessionId: activeFrom.id,
        },
        select: { id: true },
      });
      const sessionOrderIds = sessionOrders.map((item) => item.id);

      const movedSession = await tx.session.update({
        where: { id: activeFrom.id },
        data: { pcId: targetPcId },
      });

      if (sessionOrderIds.length > 0) {
        await tx.pcServiceOrder.updateMany({
          where: {
            pcId: fromPcId,
            sessionId: activeFrom.id,
            id: { in: sessionOrderIds },
          },
          data: { pcId: targetPcId },
        });

        const paidEvents = await tx.eventLog.findMany({
          where: {
            pcId: fromPcId,
            eventType: 'service.order.paid',
          },
          select: {
            id: true,
            payload: true,
          },
          orderBy: [{ createdAt: 'desc' }],
          take: 1000,
        });

        const transferredPaidEventIds = paidEvents
          .filter((event) =>
            this.isServicePaidEventForTransferredOrders(
              event.payload,
              activeFrom.id,
              sessionOrderIds,
            ),
          )
          .map((event) => event.id);

        if (transferredPaidEventIds.length > 0) {
          await tx.eventLog.updateMany({
            where: {
              id: { in: transferredPaidEventIds },
            },
            data: {
              pcId: targetPcId,
            },
          });
        }
      }

      const transferredAt = new Date().toISOString();
      if (transferablePresence) {
        await tx.eventLog.create({
          data: {
            source: EventSource.SERVER,
            eventType: transferablePresence.eventType,
            pcId: fromPcId,
            payload: {
              ...transferablePresence.identityPayload,
              isActive: false,
              at: transferredAt,
              transferredToPcId: targetPcId,
              requestedBy: requestedBy?.trim() || 'admin.desktop',
            },
          },
        });

        await tx.eventLog.create({
          data: {
            source: EventSource.SERVER,
            eventType: transferablePresence.eventType,
            pcId: targetPcId,
            payload: {
              ...transferablePresence.identityPayload,
              isActive: true,
              at: transferredAt,
              transferredFromPcId: fromPcId,
              requestedBy: requestedBy?.trim() || 'admin.desktop',
            },
          },
        });
      }

      await tx.pc.update({
        where: { id: fromPcId },
        data: { status: 'LOCKED' },
      });
      await tx.pc.update({
        where: { id: targetPcId },
        data: { status: 'IN_USE' },
      });

      return { movedSession, fromPc, targetPc };
    });

    await this.logEvent('session.transferred', result.targetPc.id, {
      sessionId: result.movedSession.id,
      fromPcId,
      targetPcId,
      requestedBy: requestedBy?.trim() || 'admin.desktop',
    });

    return {
      ok: true,
      sessionId: result.movedSession.id,
      fromPcId,
      targetPcId,
    };
  }

  async closeActiveSessionForOfflinePc(
    pcId: string,
    sourceEvent: string,
  ): Promise<{ sessionId: string; amount: number; billableMinutes: number } | null> {
    const activeSession = await this.prisma.session.findFirst({
      where: {
        pcId,
        status: 'ACTIVE',
      },
      include: {
        pc: true,
      },
      orderBy: { startedAt: 'desc' },
    });

    if (!activeSession) {
      return null;
    }

    const shouldPreserveGuestSession =
      await this.hasActiveGuestPresenceForPc(pcId);
    if (shouldPreserveGuestSession) {
      await this.logEvent('session.preserved.offline_guest', pcId, {
        sessionId: activeSession.id,
        sourceEvent,
      });
      return null;
    }

    return this.closeSessionAsAutoOffline(activeSession, sourceEvent);
  }

  @Cron(CronExpression.EVERY_MINUTE)
  async closeStaleOfflineSessions(): Promise<void> {
    const timeoutMinutes = this.getOfflineCloseMinutes();
    const cutoff = new Date(Date.now() - timeoutMinutes * 60 * 1000);
    const activeSessions = await this.prisma.session.findMany({
      where: {
        status: 'ACTIVE',
        pc: {
          status: 'OFFLINE',
          lastSeenAt: {
            lt: cutoff,
          },
        },
      },
      include: {
        pc: true,
      },
      take: 200,
    });

    for (const session of activeSessions) {
      const shouldPreserveGuestSession =
        await this.hasActiveGuestPresenceForPc(session.pcId);
      if (shouldPreserveGuestSession) {
        await this.logEvent('session.preserved.offline_guest', session.pcId, {
          sessionId: session.id,
          sourceEvent: 'offline.close_stale',
        });
        continue;
      }

      await this.closeSessionAsAutoOffline(session, 'offline.close_stale');
    }
  }

  private getOfflineCloseMinutes(): number {
    const raw = Number(
      this.configService.get<string>('SESSION_OFFLINE_CLOSE_MINUTES') ?? '3',
    );
    if (!Number.isFinite(raw) || raw < 1) {
      return 3;
    }

    return raw;
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

  private async closeSessionAsAutoOffline(
    session: {
      id: string;
      pcId: string;
      startedAt: Date;
      pricePerMinute: Prisma.Decimal | null;
      pc: { lastSeenAt: Date | null };
    },
    sourceEvent: string,
  ): Promise<{ sessionId: string; amount: number; billableMinutes: number }> {
    const endedAt = new Date();
    const durationSeconds = Math.max(
      0,
      Math.floor((endedAt.getTime() - session.startedAt.getTime()) / 1000),
    );
    const billableMinutes = Math.max(0, Math.ceil(durationSeconds / 60));
    const pricePerMinute = Number(session.pricePerMinute ?? 0);
    const rawAmount = (durationSeconds / 60) * pricePerMinute;
    const amount = Math.round(rawAmount * 100) / 100;

    await this.prisma.session.update({
      where: { id: session.id },
      data: {
        endedAt,
        durationSeconds,
        billableMinutes,
        amount,
        status: 'CLOSED',
        closedReason: 'AUTO_OFFLINE',
      },
    });

    await this.logEvent('session.closed.auto_offline', session.pcId, {
      sessionId: session.id,
      durationSeconds,
      billableMinutes,
      amount,
      lastSeenAt: session.pc.lastSeenAt?.toISOString() ?? null,
      sourceEvent,
    });

    return {
      sessionId: session.id,
      amount,
      billableMinutes,
    };
  }

  private async hasActiveGuestPresenceForPc(
    pcId: string,
  ): Promise<boolean> {
    const latestPresence = await this.prisma.eventLog.findFirst({
      where: {
        pcId,
        eventType: {
          in: ['member.pc.presence', 'guest.pc.presence', 'admin.pc.presence'],
        },
      },
      orderBy: { createdAt: 'desc' },
      select: {
        eventType: true,
        payload: true,
      },
    });

    return (
      !!latestPresence &&
      latestPresence.eventType === 'guest.pc.presence' &&
      this.isActivePresencePayload(latestPresence.payload)
    );
  }

  private isActivePresencePayload(payload?: Prisma.JsonValue | null): boolean {
    if (!payload || typeof payload !== 'object' || Array.isArray(payload)) {
      return false;
    }

    const value = payload as Record<string, unknown>;
    return value.isActive === true;
  }

  private extractTransferablePresence(
    eventType?: string,
    payload?: Prisma.JsonValue | null,
  ): {
    eventType: 'member.pc.presence' | 'guest.pc.presence' | 'admin.pc.presence';
    identityPayload: Record<string, unknown>;
  } | null {
    if (!payload || typeof payload !== 'object' || Array.isArray(payload)) {
      return null;
    }

    const value = payload as Record<string, unknown>;
    if (value.isActive !== true) {
      return null;
    }

    if (eventType === 'member.pc.presence') {
      const memberId = this.readString(value.memberId);
      const username = this.readString(value.username);
      const fullName = this.readString(value.fullName) || username;
      if (!memberId || !username) {
        return null;
      }

      return {
        eventType,
        identityPayload: {
          memberId,
          username,
          fullName,
        },
      };
    }

    if (eventType === 'guest.pc.presence') {
      const displayName = this.readString(value.displayName) || 'Khach vang lai';
      return {
        eventType,
        identityPayload: {
          displayName,
          prepaidAmount: this.readNumber(value.prepaidAmount),
        },
      };
    }

    if (eventType === 'admin.pc.presence') {
      const username = this.readString(value.username) || 'Admin';
      const fullName = this.readString(value.fullName) || username;
      return {
        eventType,
        identityPayload: {
          username,
          fullName,
        },
      };
    }

    return null;
  }

  private isServicePaidEventForTransferredOrders(
    payload: Prisma.JsonValue | null,
    sessionId: string,
    movedOrderIds: string[],
  ): boolean {
    if (!payload || typeof payload !== 'object' || Array.isArray(payload)) {
      return false;
    }

    const value = payload as Record<string, unknown>;
    const payloadSessionId = this.readString(value.sessionId);
    if (payloadSessionId && payloadSessionId !== sessionId) {
      return false;
    }

    const orderIdsRaw = value.orderIds;
    if (!Array.isArray(orderIdsRaw) || orderIdsRaw.length === 0) {
      return false;
    }

    const movedSet = new Set(movedOrderIds);
    for (const orderId of orderIdsRaw) {
      if (typeof orderId !== 'string') {
        continue;
      }

      const normalized = orderId.trim();
      if (!normalized) {
        continue;
      }

      if (movedSet.has(normalized)) {
        return true;
      }
    }

    return false;
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
}
