import { Injectable } from '@nestjs/common';
import { PrismaService } from '../prisma/prisma.service';

type RevenuePeriod = 'day' | 'week' | 'month';
type DashboardPeriod = 'week' | 'month' | 'year';

type TimeRange = {
  start: Date;
  endExclusive: Date;
};

type PaidServiceOrderRecord = {
  orderId: string;
  paidAt: Date;
  serviceItemId: string;
  serviceItemName: string;
  serviceItemCategory: string | null;
  quantity: number;
  lineTotal: number;
};

@Injectable()
export class ReportsService {
  private static readonly WEBSITE_VISIT_EVENT_TYPE = 'website.visit';
  private static readonly WEBSITE_LOG_SETTINGS_EVENT_TYPE = 'website.log.settings';

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

    const [sessions, paidServiceRecords] = await Promise.all([
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
      this.getPaidServiceOrderRecords(range),
    ]);

    const sessionAmount = sessions.reduce((total, item) => {
      return total + Number(item.amount ?? 0);
    }, 0);
    const serviceAmount = paidServiceRecords.reduce((total, item) => {
      return total + item.lineTotal;
    }, 0);
    const totalAmount = sessionAmount + serviceAmount;

    return {
      period,
      anchorDate: this.formatDate(anchorDate),
      periodLabel: this.buildPeriodLabel(period, range.start, range.endExclusive),
      rangeStart: range.start.toISOString(),
      rangeEndExclusive: range.endExclusive.toISOString(),
      closedSessions: sessions.length,
      serviceOrders: paidServiceRecords.length,
      sessionAmount,
      serviceAmount,
      totalAmount,
      serverTime: new Date().toISOString(),
    };
  }

  async getSystemEvents(limitRaw?: string) {
    const limit = this.parseLimit(limitRaw);
    const events = await this.prisma.eventLog.findMany({
      where: {
        eventType: {
          notIn: [
            ReportsService.WEBSITE_VISIT_EVENT_TYPE,
            ReportsService.WEBSITE_LOG_SETTINGS_EVENT_TYPE,
          ],
        },
      },
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

  async clearSystemEvents() {
    const result = await this.prisma.eventLog.deleteMany({
      where: {
        eventType: {
          notIn: [
            ReportsService.WEBSITE_VISIT_EVENT_TYPE,
            ReportsService.WEBSITE_LOG_SETTINGS_EVENT_TYPE,
          ],
        },
      },
    });

    return {
      deletedCount: result.count,
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
      return `Ngay ${toDisplayDate(start)}`;
    }

    if (period === 'week') {
      const end = new Date(endExclusive);
      end.setDate(end.getDate() - 1);
      return `Tuan ${toDisplayDate(start)} den ${toDisplayDate(end)}`;
    }

    const month = `${start.getMonth() + 1}`.padStart(2, '0');
    return `Thang ${month}-${start.getFullYear()}`;
  }

  async getDashboardStats(periodRaw?: string) {
    const period = this.parseDashboardPeriod(periodRaw);
    const now = new Date();
    const currentRange = this.getCurrentDashboardRange(period, now);
    const previousRange = this.getPreviousComparableRange(currentRange);

    const [
      currentSessions,
      previousSessions,
      currentPaidServiceOrders,
      previousPaidServiceOrders,
      topMemberUsageRows,
      pcs,
    ] = await Promise.all([
      this.prisma.session.findMany({
        where: {
          status: 'CLOSED',
          endedAt: { gte: currentRange.start, lt: currentRange.endExclusive },
        },
        select: {
          amount: true,
          durationSeconds: true,
          startedAt: true,
          endedAt: true,
        },
      }),
      this.prisma.session.findMany({
        where: {
          status: 'CLOSED',
          endedAt: { gte: previousRange.start, lt: previousRange.endExclusive },
        },
        select: {
          amount: true,
          durationSeconds: true,
          startedAt: true,
          endedAt: true,
        },
      }),
      this.getPaidServiceOrderRecords(currentRange),
      this.getPaidServiceOrderRecords(previousRange),
      this.getTopMemberUsageInRange(currentRange),
      this.prisma.pc.findMany({
        select: {
          id: true,
          name: true,
          sessions: {
            where: {
              status: 'CLOSED',
              endedAt: { gte: currentRange.start, lt: currentRange.endExclusive },
            },
            select: {
              durationSeconds: true,
            },
          },
        },
      }),
    ]);

    const playtimeRevenue = currentSessions.reduce(
      (sum, session) => sum + Number(session.amount ?? 0),
      0,
    );
    const previousPlaytimeRevenue = previousSessions.reduce(
      (sum, session) => sum + Number(session.amount ?? 0),
      0,
    );
    const serviceRevenue = currentPaidServiceOrders.reduce(
      (sum, record) => sum + record.lineTotal,
      0,
    );
    const previousServiceRevenue = previousPaidServiceOrders.reduce(
      (sum, record) => sum + record.lineTotal,
      0,
    );
    const totalRevenue = playtimeRevenue + serviceRevenue;
    const previousTotalRevenue = previousPlaytimeRevenue + previousServiceRevenue;

    const totalPlaySeconds = currentSessions.reduce(
      (sum, session) => sum + Math.max(0, session.durationSeconds ?? 0),
      0,
    );
    const previousPlaySeconds = previousSessions.reduce(
      (sum, session) => sum + Math.max(0, session.durationSeconds ?? 0),
      0,
    );
    const totalPlayHours = Math.round(totalPlaySeconds / 3600);

    const maxTopMemberConsumedSeconds = Math.max(
      1,
      topMemberUsageRows[0]?.consumedSeconds ?? 0,
    );

    const topMembers = topMemberUsageRows.map((member, index) => {
      const playHours = Math.round(member.consumedSeconds / 3600);
      return {
        username: member.username,
        rank: `${index + 1}`,
        playHours,
        progress:
          member.consumedSeconds <= 0
            ? 0
            : Math.max(
                0,
                Math.min(
                  100,
                  Math.round((member.consumedSeconds / maxTopMemberConsumedSeconds) * 100),
                ),
              ),
      };
    });

    const pcPlaytimes = pcs.map((pc) => {
      const seconds = pc.sessions.reduce(
        (sum, session) => sum + Math.max(0, session.durationSeconds ?? 0),
        0,
      );
      return {
        name: pc.name,
        playHours: Math.round(seconds / 3600),
      };
    });

    const sortedMost = [...pcPlaytimes].sort((a, b) => b.playHours - a.playHours);
    const sortedLeast = [...pcPlaytimes].sort((a, b) => a.playHours - b.playHours);
    const maxHours = Math.max(1, sortedMost[0]?.playHours ?? 0);

    const topPcs = sortedMost.slice(0, 5).map((item) => ({
      name: item.name,
      playHours: item.playHours,
      progress: item.playHours <= 0 ? 0 : Math.round((item.playHours / maxHours) * 100),
    }));

    const leastPcs = sortedLeast.slice(0, 5).map((item) => ({
      name: item.name,
      playHours: item.playHours,
      progress: item.playHours <= 0 ? 0 : Math.round((item.playHours / maxHours) * 100),
    }));

    const topServiceItems = this.buildTopServiceItems(currentPaidServiceOrders);

    return {
      period,
      playtimeRevenue,
      serviceRevenue,
      totalRevenue,
      totalPlayHours,
      playtimeGrowth: this.formatGrowth(playtimeRevenue, previousPlaytimeRevenue),
      serviceGrowth: this.formatGrowth(serviceRevenue, previousServiceRevenue),
      totalGrowth: this.formatGrowth(totalRevenue, previousTotalRevenue),
      playhoursGrowth: this.formatGrowth(totalPlaySeconds, previousPlaySeconds),
      dailyData: this.buildRevenueChartData(period, currentSessions, currentPaidServiceOrders),
      topMembers,
      topPcs,
      leastPcs,
      topServiceItems,
      weeklyDistribution: this.buildWeeklyDistribution(currentSessions),
      hourlyDistribution: this.buildHourlyDistribution(currentSessions),
      serverTime: new Date().toISOString(),
    };
  }

  private parseDashboardPeriod(rawPeriod?: string): DashboardPeriod {
    const normalized = rawPeriod?.trim().toLowerCase();
    if (normalized === 'month' || normalized === 'year') {
      return normalized;
    }
    return 'week';
  }

  private getCurrentDashboardRange(period: DashboardPeriod, now: Date): TimeRange {
    if (period === 'year') {
      return {
        start: new Date(now.getFullYear(), 0, 1, 0, 0, 0, 0),
        endExclusive: now,
      };
    }

    if (period === 'month') {
      return {
        start: new Date(now.getFullYear(), now.getMonth(), 1, 0, 0, 0, 0),
        endExclusive: now,
      };
    }

    const dayOfWeek = now.getDay();
    const offset = dayOfWeek === 0 ? -6 : 1 - dayOfWeek;
    return {
      start: new Date(now.getFullYear(), now.getMonth(), now.getDate() + offset, 0, 0, 0, 0),
      endExclusive: now,
    };
  }

  private getPreviousComparableRange(current: TimeRange): TimeRange {
    const durationMs = Math.max(
      1,
      current.endExclusive.getTime() - current.start.getTime(),
    );
    const previousEnd = current.start;
    const previousStart = new Date(previousEnd.getTime() - durationMs);
    return {
      start: previousStart,
      endExclusive: previousEnd,
    };
  }

  private formatGrowth(current: number, previous: number): string {
    if (!Number.isFinite(current) || current <= 0) {
      return '0.0%';
    }

    if (!Number.isFinite(previous) || previous <= 0) {
      return '▲ +100.0%';
    }

    const percent = ((current - previous) / previous) * 100;
    const abs = Math.abs(percent).toFixed(1);
    return percent >= 0 ? `▲ +${abs}%` : `▼ -${abs}%`;
  }

  private buildRevenueChartData(
    period: DashboardPeriod,
    sessions: Array<{
      amount: unknown;
      endedAt: Date | null;
      startedAt: Date;
    }>,
    paidServiceOrders: PaidServiceOrderRecord[],
  ) {
    if (period === 'year') {
      const labels = [
        'T1', 'T2', 'T3', 'T4', 'T5', 'T6',
        'T7', 'T8', 'T9', 'T10', 'T11', 'T12',
      ];
      const buckets = labels.map((label) => ({
        label,
        playtimeRevenue: 0,
        serviceRevenue: 0,
      }));

      for (const session of sessions) {
        const at = session.endedAt ?? session.startedAt;
        const monthIndex = at.getMonth();
        if (monthIndex >= 0 && monthIndex < 12) {
          buckets[monthIndex].playtimeRevenue += Number(session.amount ?? 0);
        }
      }

      for (const order of paidServiceOrders) {
        const monthIndex = order.paidAt.getMonth();
        if (monthIndex >= 0 && monthIndex < 12) {
          buckets[monthIndex].serviceRevenue += order.lineTotal;
        }
      }

      return buckets;
    }

    if (period === 'month') {
      const bucketCount = 5;
      const buckets = Array.from({ length: bucketCount }, (_, index) => ({
        label: `Tuan ${index + 1}`,
        playtimeRevenue: 0,
        serviceRevenue: 0,
      }));

      for (const session of sessions) {
        const at = session.endedAt ?? session.startedAt;
        const weekIndex = Math.min(
          bucketCount - 1,
          Math.max(0, Math.floor((at.getDate() - 1) / 7)),
        );
        buckets[weekIndex].playtimeRevenue += Number(session.amount ?? 0);
      }

      for (const order of paidServiceOrders) {
        const weekIndex = Math.min(
          bucketCount - 1,
          Math.max(0, Math.floor((order.paidAt.getDate() - 1) / 7)),
        );
        buckets[weekIndex].serviceRevenue += order.lineTotal;
      }

      return buckets;
    }

    const labels = ['T2', 'T3', 'T4', 'T5', 'T6', 'T7', 'CN'];
    const buckets = labels.map((label) => ({
      label,
      playtimeRevenue: 0,
      serviceRevenue: 0,
    }));

    for (const session of sessions) {
      const at = session.endedAt ?? session.startedAt;
      const dayIndex = (at.getDay() + 6) % 7;
      buckets[dayIndex].playtimeRevenue += Number(session.amount ?? 0);
    }

    for (const order of paidServiceOrders) {
      const dayIndex = (order.paidAt.getDay() + 6) % 7;
      buckets[dayIndex].serviceRevenue += order.lineTotal;
    }

    return buckets;
  }

  private buildWeeklyDistribution(
    sessions: Array<{
      durationSeconds: number | null;
      endedAt: Date | null;
      startedAt: Date;
    }>,
  ) {
    const labels = ['T2', 'T3', 'T4', 'T5', 'T6', 'T7', 'CN'];
    const secondsByDay = Array.from({ length: 7 }, () => 0);

    for (const session of sessions) {
      const at = session.endedAt ?? session.startedAt;
      const dayIndex = (at.getDay() + 6) % 7;
      secondsByDay[dayIndex] += Math.max(0, session.durationSeconds ?? 0);
    }

    return labels.map((label, dayIndex) => ({
      label,
      playHours: Math.round(secondsByDay[dayIndex] / 3600),
      isWeekend: dayIndex >= 5,
    }));
  }

  private buildHourlyDistribution(
    sessions: Array<{
      durationSeconds: number | null;
      startedAt: Date;
    }>,
  ) {
    const buckets = [
      { label: 'Sang (8h-12h)', seconds: 0 },
      { label: 'Trua (12h-14h)', seconds: 0 },
      { label: 'Chieu (14h-18h)', seconds: 0 },
      { label: 'Toi (18h-22h)', seconds: 0 },
      { label: 'Dem (22h-8h)', seconds: 0 },
    ];

    for (const session of sessions) {
      const hour = session.startedAt.getHours();
      const seconds = Math.max(0, session.durationSeconds ?? 0);
      if (hour >= 8 && hour < 12) {
        buckets[0].seconds += seconds;
      } else if (hour >= 12 && hour < 14) {
        buckets[1].seconds += seconds;
      } else if (hour >= 14 && hour < 18) {
        buckets[2].seconds += seconds;
      } else if (hour >= 18 && hour < 22) {
        buckets[3].seconds += seconds;
      } else {
        buckets[4].seconds += seconds;
      }
    }

    return buckets.map((item) => ({
      label: item.label,
      playHours: Math.round(item.seconds / 3600),
    }));
  }

  private buildTopServiceItems(paidServiceOrders: PaidServiceOrderRecord[]) {
    const aggregates = new Map<
      string,
      {
        name: string;
        category: string | null;
        quantity: number;
        revenue: number;
        orderCount: number;
      }
    >();

    for (const order of paidServiceOrders) {
      const existing = aggregates.get(order.serviceItemId);
      if (!existing) {
        aggregates.set(order.serviceItemId, {
          name: order.serviceItemName,
          category: order.serviceItemCategory,
          quantity: Math.max(0, order.quantity),
          revenue: order.lineTotal,
          orderCount: 1,
        });
        continue;
      }

      existing.quantity += Math.max(0, order.quantity);
      existing.revenue += order.lineTotal;
      existing.orderCount += 1;
    }

    return Array.from(aggregates.values())
      .sort((a, b) => {
        if (b.quantity !== a.quantity) {
          return b.quantity - a.quantity;
        }
        return b.revenue - a.revenue;
      })
      .slice(0, 10)
      .map((item) => ({
        name: item.name,
        category: item.category,
        quantity: item.quantity,
        revenue: item.revenue,
        orderCount: item.orderCount,
      }));
  }

  private async getTopMemberUsageInRange(range: TimeRange) {
    const groupedUsages = await this.prisma.memberTransaction.groupBy({
      by: ['memberId'],
      where: {
        type: 'ADJUSTMENT',
        createdAt: {
          gte: range.start,
          lt: range.endExclusive,
        },
        playSecondsDelta: {
          lt: 0,
        },
        note: {
          startsWith: 'SESSION_USAGE',
        },
      },
      _sum: {
        playSecondsDelta: true,
      },
      orderBy: {
        _sum: {
          playSecondsDelta: 'asc',
        },
      },
      take: 5,
    });

    if (groupedUsages.length === 0) {
      return [];
    }

    const memberIds = groupedUsages.map((item) => item.memberId);
    const members = await this.prisma.member.findMany({
      where: {
        id: {
          in: memberIds,
        },
      },
      select: {
        id: true,
        username: true,
      },
    });

    const usernameById = new Map(members.map((item) => [item.id, item.username]));

    return groupedUsages.map((item) => ({
      username: usernameById.get(item.memberId) ?? 'unknown',
      consumedSeconds: Math.abs(item._sum.playSecondsDelta ?? 0),
    }));
  }

  private async getPaidServiceOrderRecords(range: TimeRange): Promise<PaidServiceOrderRecord[]> {
    const paidEvents = await this.prisma.eventLog.findMany({
      where: {
        eventType: 'service.order.paid',
        createdAt: {
          gte: range.start,
          lt: range.endExclusive,
        },
      },
      select: {
        createdAt: true,
        payload: true,
      },
      orderBy: [{ createdAt: 'asc' }],
    });

    const orderPaidAtMap = new Map<string, Date>();
    for (const event of paidEvents) {
      const orderIds = this.extractOrderIdsFromPayload(event.payload);
      for (const orderId of orderIds) {
        if (!orderPaidAtMap.has(orderId)) {
          orderPaidAtMap.set(orderId, event.createdAt);
        }
      }
    }

    if (orderPaidAtMap.size === 0) {
      return [];
    }

    const orderIds = Array.from(orderPaidAtMap.keys());
    const orders = await this.prisma.pcServiceOrder.findMany({
      where: {
        id: {
          in: orderIds,
        },
      },
      select: {
        id: true,
        serviceItemId: true,
        quantity: true,
        lineTotal: true,
        serviceItem: {
          select: {
            name: true,
            category: true,
          },
        },
      },
    });

    return orders
      .map((order) => {
        const paidAt = orderPaidAtMap.get(order.id);
        if (!paidAt) {
          return null;
        }

        return {
          orderId: order.id,
          paidAt,
          serviceItemId: order.serviceItemId,
          serviceItemName: order.serviceItem.name,
          serviceItemCategory: order.serviceItem.category,
          quantity: Math.max(0, order.quantity),
          lineTotal: Number(order.lineTotal ?? 0),
        };
      })
      .filter((item): item is PaidServiceOrderRecord => !!item);
  }

  private extractOrderIdsFromPayload(payload: unknown): string[] {
    if (!payload || typeof payload !== 'object' || Array.isArray(payload)) {
      return [];
    }

    const record = payload as Record<string, unknown>;
    const rawOrderIds = record.orderIds;
    if (!Array.isArray(rawOrderIds)) {
      return [];
    }

    const normalized = rawOrderIds
      .map((value) => (typeof value === 'string' ? value.trim() : ''))
      .filter((value) => !!value);

    return Array.from(new Set(normalized));
  }
  private parseLimit(rawLimit?: string): number {
    const parsed = Number(rawLimit ?? '200');
    if (!Number.isFinite(parsed)) {
      return 200;
    }

    return Math.min(500, Math.max(20, Math.floor(parsed)));
  }
}

