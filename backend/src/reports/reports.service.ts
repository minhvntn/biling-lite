import { Injectable } from '@nestjs/common';
import { PrismaService } from '../prisma/prisma.service';

@Injectable()
export class ReportsService {
  constructor(private readonly prisma: PrismaService) {}

  async getDailyRevenue(date?: string) {
    const targetDate = this.parseDate(date);
    const dayStart = new Date(
      targetDate.getFullYear(),
      targetDate.getMonth(),
      targetDate.getDate(),
      0,
      0,
      0,
      0,
    );
    const dayEnd = new Date(dayStart);
    dayEnd.setDate(dayEnd.getDate() + 1);

    const sessions = await this.prisma.session.findMany({
      where: {
        status: 'CLOSED',
        endedAt: {
          gte: dayStart,
          lt: dayEnd,
        },
      },
      select: {
        id: true,
        amount: true,
      },
    });

    const totalAmount = sessions.reduce((accumulator, session) => {
      return accumulator + Number(session.amount ?? 0);
    }, 0);

    return {
      date: dayStart.toISOString().slice(0, 10),
      closedSessions: sessions.length,
      totalAmount,
      serverTime: new Date().toISOString(),
    };
  }

  async getSystemEvents(limitRaw?: string) {
    const limit = this.parseLimit(limitRaw);
    const events = await this.prisma.eventLog.findMany({
      orderBy: [{ createdAt: 'desc' }],
      take: limit,
      include: {
        pc: {
          select: {
            id: true,
            name: true,
            agentId: true,
          },
        },
      },
    });

    return {
      items: events.map((item) => ({
        id: item.id,
        source: item.source,
        eventType: item.eventType,
        pcId: item.pcId,
        pcName: item.pc?.name ?? null,
        agentId: item.pc?.agentId ?? null,
        payload: item.payload,
        createdAt: item.createdAt.toISOString(),
      })),
      total: events.length,
      serverTime: new Date().toISOString(),
    };
  }

  private parseDate(rawDate?: string): Date {
    if (!rawDate) {
      return new Date();
    }

    const parsed = new Date(rawDate);
    return Number.isNaN(parsed.getTime()) ? new Date() : parsed;
  }

  private parseLimit(rawLimit?: string): number {
    const parsed = Number(rawLimit ?? '200');
    if (!Number.isFinite(parsed)) {
      return 200;
    }

    return Math.min(500, Math.max(20, Math.floor(parsed)));
  }
}
