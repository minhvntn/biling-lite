import { BadRequestException, Injectable, NotFoundException } from '@nestjs/common';
import { ConfigService } from '@nestjs/config';
import { Cron, CronExpression } from '@nestjs/schedule';
import { EventSource, Prisma, SessionStatus } from '@prisma/client';
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

      const movedSession = await tx.session.update({
        where: { id: activeFrom.id },
        data: { pcId: targetPcId },
      });

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
      const endedAt = new Date();
      const durationSeconds = Math.max(
        0,
        Math.floor((endedAt.getTime() - session.startedAt.getTime()) / 1000),
      );
      const billableMinutes = Math.max(1, Math.ceil(durationSeconds / 60));
      const pricePerMinute = Number(session.pricePerMinute ?? 0);
      const amount = billableMinutes * pricePerMinute;

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
      });
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
}
