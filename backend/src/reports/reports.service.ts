import { Injectable } from '@nestjs/common';
import { PrismaService } from '../prisma/prisma.service';

type RevenuePeriod = 'day' | 'week' | 'month';

@Injectable()
export class ReportsService {
  constructor(private readonly prisma: PrismaService) {}

  async getDailyRevenue(date?: string) {
    const summary = await this.getRevenueSummary('day', date);
    return {
      date: summary.anchorDate,
      closedSessions: summary.closedSessions,
      totalAmount: summary.totalAmount,
      serverTime: summary.serverTime,
    };
  }

  async getRevenueSummary(periodRaw?: string, date?: string) {
    const period = this.parsePeriod(periodRaw);
    const anchorDate = this.parseDate(date);
    const range = this.getRange(period, anchorDate);

    const [sessions, serviceOrders] = await Promise.all([
      this.prisma.session.findMany({
        where: {
          status: 'CLOSED',
          endedAt: {
            gte: range.start,
            lt: range.endExclusive,
          },
        },
        select: {
          amount: true,
        },
      }),
      this.prisma.pcServiceOrder.findMany({
        where: {
          createdAt: {
            gte: range.start,
            lt: range.endExclusive,
          },
        },
        select: {
          lineTotal: true,
        },
      }),
    ]);

    const sessionAmount = sessions.reduce((total, item) => {
      return total + Number(item.amount ?? 0);
    }, 0);
    const serviceAmount = serviceOrders.reduce((total, item) => {
      return total + Number(item.lineTotal ?? 0);
    }, 0);
    const totalAmount = sessionAmount + serviceAmount;

    return {
      period,
      anchorDate: this.formatDate(anchorDate),
      periodLabel: this.buildPeriodLabel(period, range.start, range.endExclusive),
      rangeStart: range.start.toISOString(),
      rangeEndExclusive: range.endExclusive.toISOString(),
      closedSessions: sessions.length,
      serviceOrders: serviceOrders.length,
      sessionAmount,
      serviceAmount,
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

  private parsePeriod(rawPeriod?: string): RevenuePeriod {
    const value = rawPeriod?.trim().toLowerCase();
    if (value === 'week' || value === 'month') {
      return value;
    }

    return 'day';
  }

  private parseDate(rawDate?: string): Date {
    if (!rawDate) {
      return new Date();
    }

    const parsed = new Date(rawDate);
    return Number.isNaN(parsed.getTime()) ? new Date() : parsed;
  }

  private getRange(period: RevenuePeriod, anchorDate: Date) {
    const dayStart = new Date(
      anchorDate.getFullYear(),
      anchorDate.getMonth(),
      anchorDate.getDate(),
      0,
      0,
      0,
      0,
    );

    if (period === 'day') {
      const end = new Date(dayStart);
      end.setDate(end.getDate() + 1);
      return {
        start: dayStart,
        endExclusive: end,
      };
    }

    if (period === 'week') {
      const weekStart = new Date(dayStart);
      const dayOfWeek = weekStart.getDay();
      const offset = dayOfWeek === 0 ? -6 : 1 - dayOfWeek; // Monday start.
      weekStart.setDate(weekStart.getDate() + offset);
      const weekEnd = new Date(weekStart);
      weekEnd.setDate(weekEnd.getDate() + 7);
      return {
        start: weekStart,
        endExclusive: weekEnd,
      };
    }

    const monthStart = new Date(
      dayStart.getFullYear(),
      dayStart.getMonth(),
      1,
      0,
      0,
      0,
      0,
    );
    const monthEnd = new Date(
      monthStart.getFullYear(),
      monthStart.getMonth() + 1,
      1,
      0,
      0,
      0,
      0,
    );
    return {
      start: monthStart,
      endExclusive: monthEnd,
    };
  }

  private formatDate(date: Date): string {
    const year = date.getFullYear();
    const month = `${date.getMonth() + 1}`.padStart(2, '0');
    const day = `${date.getDate()}`.padStart(2, '0');
    return `${year}-${month}-${day}`;
  }

  private buildPeriodLabel(
    period: RevenuePeriod,
    start: Date,
    endExclusive: Date,
  ): string {
    const toDisplayDate = (value: Date) => {
      const day = `${value.getDate()}`.padStart(2, '0');
      const month = `${value.getMonth() + 1}`.padStart(2, '0');
      const year = value.getFullYear();
      return `${day}-${month}-${year}`;
    };

    if (period === 'day') {
      return `Ngày ${toDisplayDate(start)}`;
    }

    if (period === 'week') {
      const end = new Date(endExclusive);
      end.setDate(end.getDate() - 1);
      return `Tuần ${toDisplayDate(start)} đến ${toDisplayDate(end)}`;
    }

    const month = `${start.getMonth() + 1}`.padStart(2, '0');
    return `Tháng ${month}-${start.getFullYear()}`;
  }

  private parseLimit(rawLimit?: string): number {
    const parsed = Number(rawLimit ?? '200');
    if (!Number.isFinite(parsed)) {
      return 200;
    }

    return Math.min(500, Math.max(20, Math.floor(parsed)));
  }
}
