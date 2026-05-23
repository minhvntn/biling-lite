import { Injectable } from '@nestjs/common';
import { Cron, CronExpression } from '@nestjs/schedule';
import { PrismaService } from '../prisma/prisma.service';

type RevenuePeriod = 'day' | 'week' | 'month';
type DashboardPeriod = 'week' | 'month' | 'year';
type PcRevenuePeriod = 'day' | 'week' | 'month' | 'year';

type TimeRange = {
  start: Date;
  endExclusive: Date;
};

type PaidServiceOrderRecord = {
  orderId: string;
  paidAt: Date;
  pcId: string;
  pcName: string | null;
  serviceItemId: string;
  serviceItemName: string;
  serviceItemCategory: string | null;
  quantity: number;
  lineTotal: number;
};

type DbStorageHistoryPoint = {
  date: string;
  sizeBytes: number;
};

@Injectable()
export class ReportsService {
  private static readonly WEBSITE_VISIT_EVENT_TYPE = 'website.visit';
  private static readonly WEBSITE_LOG_SETTINGS_EVENT_TYPE = 'website.log.settings';
  private static readonly WEB_FILTER_SETTINGS_EVENT_TYPE = 'web.filter.settings';
  private static readonly SAFE_LOG_RETENTION_DAYS = 30;
  private static readonly SAFE_DELETABLE_SYSTEM_EVENT_TYPES = [
    'admin.notify.sent',
    'backup.created',
    'backup.failed',
    'backup.restored',
    'pc.registered',
    'pc.screenshot.requested',
    'pc.screenshot.captured',
    'pc.wake.sent',
    'session.preserved.offline_guest',
    'session.transferred',
  ] as const;
  private static readonly SAFE_DELETABLE_SYSTEM_EVENT_PREFIXES = ['command.'] as const;
  private static readonly DB_STORAGE_HISTORY_KEY = 'db.storage.history';
  private static readonly DB_STORAGE_HISTORY_MAX_DAYS = 180;

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

  async getPcRevenueStats(periodRaw?: string, dateRaw?: string) {
    const period = this.parsePcRevenuePeriod(periodRaw);
    const anchorDate = this.parseDate(dateRaw);
    const range = this.getPcRevenueRange(period, anchorDate);

    const [paidServiceOrders, pcs] = await Promise.all([
      this.getPaidServiceOrderRecords(range),
      this.prisma.pc.findMany({
        select: {
          id: true,
          name: true,
          sessions: {
            where: {
              status: 'CLOSED',
              endedAt: { gte: range.start, lt: range.endExclusive },
            },
            select: {
              durationSeconds: true,
              amount: true,
            },
          },
        },
      }),
    ]);

    const items = this.buildPcRevenueStats(pcs, paidServiceOrders);
    const totalPlayHours = items.reduce((sum, item) => sum + item.playHours, 0);
    const totalPlaytimeRevenue = items.reduce((sum, item) => sum + item.playtimeRevenue, 0);
    const totalServiceRevenue = items.reduce((sum, item) => sum + item.serviceRevenue, 0);
    const totalRevenue = totalPlaytimeRevenue + totalServiceRevenue;

    return {
      period,
      anchorDate: this.formatDate(anchorDate),
      periodLabel: this.buildPcRevenuePeriodLabel(period, range.start, range.endExclusive),
      rangeStart: range.start.toISOString(),
      rangeEndExclusive: range.endExclusive.toISOString(),
      totalPlayHours: Math.round(totalPlayHours * 10) / 10,
      totalPlaytimeRevenue,
      totalServiceRevenue,
      totalRevenue,
      items,
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
      where: this.buildSafeSystemLogDeleteWhere(),
    });

    return {
      deletedCount: result.count,
      serverTime: new Date().toISOString(),
    };
  }

  async getDatabaseStorage(daysRaw?: string) {
    const days = this.parseHistoryDays(daysRaw);
    const [databaseRows, tableRows] = await Promise.all([
      this.prisma.$queryRawUnsafe<
        Array<{
          db_name: string;
          size_bytes: bigint | number | string;
          size_pretty: string;
        }>
      >(`
        SELECT
          current_database() AS db_name,
          pg_database_size(current_database()) AS size_bytes,
          pg_size_pretty(pg_database_size(current_database())) AS size_pretty;
      `),
      this.prisma.$queryRawUnsafe<
        Array<{
          table_name: string;
          total_bytes: bigint | number | string;
          total_pretty: string;
          table_bytes: bigint | number | string;
          table_pretty: string;
          index_toast_bytes: bigint | number | string;
          index_toast_pretty: string;
        }>
      >(`
        SELECT
          relname AS table_name,
          pg_total_relation_size((quote_ident(schemaname)||'.'||quote_ident(relname))::regclass) AS total_bytes,
          pg_size_pretty(pg_total_relation_size((quote_ident(schemaname)||'.'||quote_ident(relname))::regclass)) AS total_pretty,
          pg_relation_size((quote_ident(schemaname)||'.'||quote_ident(relname))::regclass) AS table_bytes,
          pg_size_pretty(pg_relation_size((quote_ident(schemaname)||'.'||quote_ident(relname))::regclass)) AS table_pretty,
          pg_total_relation_size((quote_ident(schemaname)||'.'||quote_ident(relname))::regclass)
            - pg_relation_size((quote_ident(schemaname)||'.'||quote_ident(relname))::regclass) AS index_toast_bytes,
          pg_size_pretty(
            pg_total_relation_size((quote_ident(schemaname)||'.'||quote_ident(relname))::regclass)
            - pg_relation_size((quote_ident(schemaname)||'.'||quote_ident(relname))::regclass)
          ) AS index_toast_pretty
        FROM pg_stat_user_tables
        WHERE schemaname = 'public'
        ORDER BY total_bytes DESC
        LIMIT 12;
      `),
    ]);

    const databaseRow = databaseRows[0];
    const sizeBytes = this.numericToNumber(databaseRow?.size_bytes);
    const normalizedTables = tableRows.map((item) => {
      const totalBytes = this.numericToNumber(item.total_bytes);
      const tableBytes = this.numericToNumber(item.table_bytes);
      const indexToastBytes = this.numericToNumber(item.index_toast_bytes);
      return {
        tableName: item.table_name,
        totalBytes,
        totalPretty: item.total_pretty,
        tableBytes,
        tablePretty: item.table_pretty,
        indexToastBytes,
        indexToastPretty: item.index_toast_pretty,
      };
    });

    const history = await this.captureAndReadDbStorageHistory(sizeBytes, days);
    const dayDelta = this.buildDbStorageDayDelta(history);

    return {
      databaseName: databaseRow?.db_name ?? 'unknown',
      sizeBytes,
      sizePretty: databaseRow?.size_pretty ?? '0 bytes',
      dayDeltaBytes: dayDelta.deltaBytes,
      dayDeltaPercent: dayDelta.deltaPercent,
      topTables: normalizedTables,
      history,
      serverTime: new Date().toISOString(),
    };
  }

  @Cron(CronExpression.EVERY_DAY_AT_1AM)
  async snapshotDatabaseStorageDaily() {
    try {
      const rows = await this.prisma.$queryRawUnsafe<
        Array<{ size_bytes: bigint | number | string }>
      >(
        `SELECT pg_database_size(current_database()) AS size_bytes;`,
      );
      const sizeBytes = this.numericToNumber(rows[0]?.size_bytes);
      await this.captureAndReadDbStorageHistory(sizeBytes, ReportsService.DB_STORAGE_HISTORY_MAX_DAYS);
    } catch {
      // Ignore scheduler errors to avoid crashing the process.
    }
  }

  @Cron(CronExpression.EVERY_DAY_AT_2AM)
  async cleanupSafeSystemLogsDaily() {
    try {
      const cutoff = new Date(
        Date.now() - ReportsService.SAFE_LOG_RETENTION_DAYS * 24 * 60 * 60 * 1000,
      );
      await this.prisma.eventLog.deleteMany({
        where: {
          AND: [
            this.buildSafeSystemLogDeleteWhere(),
            {
              createdAt: {
                lt: cutoff,
              },
            },
          ],
        },
      });
    } catch {
      // Ignore scheduler errors to avoid crashing the process.
    }
  }

  private parsePeriod(rawPeriod?: string): RevenuePeriod {
    const value = rawPeriod?.trim().toLowerCase();
    if (value === 'week' || value === 'month') {
      return value;
    }

    return 'day';
  }

  private parsePcRevenuePeriod(rawPeriod?: string): PcRevenuePeriod {
    const value = rawPeriod?.trim().toLowerCase();
    if (value === 'day' || value === 'week' || value === 'month' || value === 'year') {
      return value;
    }

    return 'week';
  }

  private parseHistoryDays(raw?: string): number {
    const parsed = Number(raw ?? '14');
    if (!Number.isFinite(parsed)) {
      return 14;
    }
    return Math.min(90, Math.max(7, Math.floor(parsed)));
  }

  private numericToNumber(value: bigint | number | string | null | undefined): number {
    if (typeof value === 'number') {
      return Number.isFinite(value) ? value : 0;
    }
    if (typeof value === 'bigint') {
      return Number(value);
    }
    if (typeof value === 'string') {
      const parsed = Number(value);
      return Number.isFinite(parsed) ? parsed : 0;
    }
    return 0;
  }

  private async captureAndReadDbStorageHistory(
    currentSizeBytes: number,
    takeDays: number,
  ): Promise<DbStorageHistoryPoint[]> {
    const today = this.formatDate(new Date());
    const setting = await this.prisma.appSetting.findUnique({
      where: { key: ReportsService.DB_STORAGE_HISTORY_KEY },
    });

    const existing = this.parseDbStorageHistory(setting?.value);
    const normalized = existing
      .filter((item) => item.date && Number.isFinite(item.sizeBytes))
      .sort((a, b) => a.date.localeCompare(b.date));

    const todayIndex = normalized.findIndex((item) => item.date === today);
    let hasChanged = false;
    if (todayIndex >= 0) {
      if (normalized[todayIndex].sizeBytes !== currentSizeBytes) {
        normalized[todayIndex].sizeBytes = currentSizeBytes;
        hasChanged = true;
      }
    } else {
      normalized.push({ date: today, sizeBytes: currentSizeBytes });
      hasChanged = true;
    }

    while (normalized.length > ReportsService.DB_STORAGE_HISTORY_MAX_DAYS) {
      normalized.shift();
      hasChanged = true;
    }

    if (hasChanged) {
      await this.prisma.appSetting.upsert({
        where: { key: ReportsService.DB_STORAGE_HISTORY_KEY },
        create: {
          key: ReportsService.DB_STORAGE_HISTORY_KEY,
          value: JSON.stringify(normalized),
        },
        update: {
          value: JSON.stringify(normalized),
        },
      });
    }

    return normalized.slice(-takeDays);
  }

  private parseDbStorageHistory(rawValue?: string): DbStorageHistoryPoint[] {
    if (!rawValue) {
      return [];
    }

    try {
      const parsed = JSON.parse(rawValue) as Array<{ date?: unknown; sizeBytes?: unknown }>;
      if (!Array.isArray(parsed)) {
        return [];
      }

      return parsed
        .map((item) => {
          const date = typeof item.date === 'string' ? item.date.trim() : '';
          const sizeBytes = this.numericToNumber(
            typeof item.sizeBytes === 'string' ||
              typeof item.sizeBytes === 'number' ||
              typeof item.sizeBytes === 'bigint'
              ? (item.sizeBytes as string | number | bigint)
              : 0,
          );
          return { date, sizeBytes };
        })
        .filter((item) => !!item.date);
    } catch {
      return [];
    }
  }

  private buildDbStorageDayDelta(history: DbStorageHistoryPoint[]) {
    if (history.length < 2) {
      return {
        deltaBytes: 0,
        deltaPercent: 0,
      };
    }

    const latest = history[history.length - 1].sizeBytes;
    const previous = history[history.length - 2].sizeBytes;
    const deltaBytes = latest - previous;
    const deltaPercent =
      previous <= 0 ? 0 : Number((((latest - previous) / previous) * 100).toFixed(2));

    return {
      deltaBytes,
      deltaPercent,
    };
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

  private getPcRevenueRange(period: PcRevenuePeriod, anchorDate: Date): TimeRange {
    if (period === 'year') {
      const yearStart = new Date(anchorDate.getFullYear(), 0, 1, 0, 0, 0, 0);
      const yearEnd = new Date(anchorDate.getFullYear() + 1, 0, 1, 0, 0, 0, 0);
      return {
        start: yearStart,
        endExclusive: yearEnd,
      };
    }

    return this.getRange(period, anchorDate);
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

  private buildPcRevenuePeriodLabel(
    period: PcRevenuePeriod,
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

    if (period === 'month') {
      const month = `${start.getMonth() + 1}`.padStart(2, '0');
      return `Tháng ${month}-${start.getFullYear()}`;
    }

    return `Năm ${start.getFullYear()}`;
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
              amount: true,
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
    const pcRevenueStats = this.buildPcRevenueStats(pcs, currentPaidServiceOrders);

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
      pcRevenueStats,
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
      durationSeconds: number | null;
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
        playHours: 0,
      }));

      for (const session of sessions) {
        const at = session.endedAt ?? session.startedAt;
        const monthIndex = at.getMonth();
        if (monthIndex >= 0 && monthIndex < 12) {
          buckets[monthIndex].playtimeRevenue += Number(session.amount ?? 0);
          buckets[monthIndex].playHours += Math.max(0, session.durationSeconds ?? 0) / 3600;
        }
      }

      for (const order of paidServiceOrders) {
        const monthIndex = order.paidAt.getMonth();
        if (monthIndex >= 0 && monthIndex < 12) {
          buckets[monthIndex].serviceRevenue += order.lineTotal;
        }
      }

      return buckets.map((bucket) => ({
        ...bucket,
        playHours: Math.round(bucket.playHours * 10) / 10,
      }));
    }

    if (period === 'month') {
      const bucketCount = 5;
      const buckets = Array.from({ length: bucketCount }, (_, index) => ({
        label: `Tuan ${index + 1}`,
        playtimeRevenue: 0,
        serviceRevenue: 0,
        playHours: 0,
      }));

      for (const session of sessions) {
        const at = session.endedAt ?? session.startedAt;
        const weekIndex = Math.min(
          bucketCount - 1,
          Math.max(0, Math.floor((at.getDate() - 1) / 7)),
        );
        buckets[weekIndex].playtimeRevenue += Number(session.amount ?? 0);
        buckets[weekIndex].playHours += Math.max(0, session.durationSeconds ?? 0) / 3600;
      }

      for (const order of paidServiceOrders) {
        const weekIndex = Math.min(
          bucketCount - 1,
          Math.max(0, Math.floor((order.paidAt.getDate() - 1) / 7)),
        );
        buckets[weekIndex].serviceRevenue += order.lineTotal;
      }

      return buckets.map((bucket) => ({
        ...bucket,
        playHours: Math.round(bucket.playHours * 10) / 10,
      }));
    }

    const labels = ['T2', 'T3', 'T4', 'T5', 'T6', 'T7', 'CN'];
    const buckets = labels.map((label) => ({
      label,
      playtimeRevenue: 0,
      serviceRevenue: 0,
      playHours: 0,
    }));

    for (const session of sessions) {
      const at = session.endedAt ?? session.startedAt;
      const dayIndex = (at.getDay() + 6) % 7;
      buckets[dayIndex].playtimeRevenue += Number(session.amount ?? 0);
      buckets[dayIndex].playHours += Math.max(0, session.durationSeconds ?? 0) / 3600;
    }

    for (const order of paidServiceOrders) {
      const dayIndex = (order.paidAt.getDay() + 6) % 7;
      buckets[dayIndex].serviceRevenue += order.lineTotal;
    }

    return buckets.map((bucket) => ({
      ...bucket,
      playHours: Math.round(bucket.playHours * 10) / 10,
    }));
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
      playHours: Math.round((secondsByDay[dayIndex] / 3600) * 10) / 10,
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
      { label: 'Sáng (8h-12h)', seconds: 0 },
      { label: 'Trưa (12h-14h)', seconds: 0 },
      { label: 'Chiều (14h-18h)', seconds: 0 },
      { label: 'Tối (18h-22h)', seconds: 0 },
      { label: 'Đêm (22h-8h)', seconds: 0 },
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
      playHours: Math.round((item.seconds / 3600) * 10) / 10,
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
    return [];
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

    if (orderPaidAtMap.size > 0) {
      const orderIds = Array.from(orderPaidAtMap.keys());
      const paidOrders = await this.prisma.pcServiceOrder.findMany({
        where: {
          id: {
            in: orderIds,
          },
        },
        select: {
          id: true,
          pcId: true,
          serviceItemId: true,
          quantity: true,
          lineTotal: true,
          pc: {
            select: {
              name: true,
            },
          },
          serviceItem: {
            select: {
              name: true,
              category: true,
            },
          },
        },
      });

      return paidOrders
        .map((order) => {
          const paidAt = orderPaidAtMap.get(order.id);
          if (!paidAt) {
            return null;
          }

          return {
            orderId: order.id,
            paidAt,
            pcId: order.pcId,
            pcName: order.pc?.name ?? null,
            serviceItemId: order.serviceItemId,
            serviceItemName: order.serviceItem.name,
            serviceItemCategory: order.serviceItem.category,
            quantity: Math.max(0, order.quantity),
            lineTotal: Number(order.lineTotal ?? 0),
          };
        })
        .filter(
          (item): item is PaidServiceOrderRecord =>
            !!item && item.quantity > 0 && item.lineTotal > 0,
        );
    }

    // Fallback for old deployments/data where service.order.paid was not logged.
    const fallbackOrders = await this.prisma.pcServiceOrder.findMany({
      where: {
        createdAt: {
          gte: range.start,
          lt: range.endExclusive,
        },
      },
      select: {
        id: true,
        createdAt: true,
        pcId: true,
        serviceItemId: true,
        quantity: true,
        lineTotal: true,
        pc: {
          select: {
            name: true,
          },
        },
        serviceItem: {
          select: {
            name: true,
            category: true,
          },
        },
      },
    });

    return fallbackOrders
      .map((order) => ({
        orderId: order.id,
        paidAt: order.createdAt,
        pcId: order.pcId,
        pcName: order.pc?.name ?? null,
        serviceItemId: order.serviceItemId,
        serviceItemName: order.serviceItem.name,
        serviceItemCategory: order.serviceItem.category,
        quantity: Math.max(0, order.quantity),
        lineTotal: Number(order.lineTotal ?? 0),
      }))
      .filter((item) => item.quantity > 0 && item.lineTotal > 0);
  }

  private buildPcRevenueStats(
    pcs: Array<{
      id: string;
      name: string;
      sessions: Array<{
        durationSeconds: number | null;
        amount: unknown;
      }>;
    }>,
    paidServiceOrders: PaidServiceOrderRecord[],
  ) {
    const serviceRevenueByPcId = new Map<string, number>();
    for (const order of paidServiceOrders) {
      if (!order.pcId) {
        continue;
      }
      const current = serviceRevenueByPcId.get(order.pcId) ?? 0;
      serviceRevenueByPcId.set(order.pcId, current + Math.max(0, order.lineTotal));
    }

    const stats = pcs.map((pc) => {
      const totalSeconds = pc.sessions.reduce(
        (sum, session) => sum + Math.max(0, session.durationSeconds ?? 0),
        0,
      );
      const playHours = Math.round((totalSeconds / 3600) * 10) / 10;
      const playtimeRevenue = pc.sessions.reduce(
        (sum, session) => sum + Math.max(0, Number(session.amount ?? 0)),
        0,
      );
      const serviceRevenue = serviceRevenueByPcId.get(pc.id) ?? 0;
      const totalRevenue = playtimeRevenue + serviceRevenue;

      return {
        pcId: pc.id,
        pcName: pc.name,
        playHours,
        playtimeRevenue,
        serviceRevenue,
        totalRevenue,
      };
    });

    const maxTotalRevenue = Math.max(
      1,
      ...stats.map((item) => Math.max(0, item.totalRevenue)),
    );
    const maxPlayHours = Math.max(1, ...stats.map((item) => Math.max(0, item.playHours)));

    return stats
      .sort((a, b) => {
        if (b.totalRevenue !== a.totalRevenue) {
          return b.totalRevenue - a.totalRevenue;
        }
        if (b.playHours !== a.playHours) {
          return b.playHours - a.playHours;
        }
        return a.pcName.localeCompare(b.pcName);
      })
      .map((item) => ({
        ...item,
        revenueProgress:
          item.totalRevenue <= 0 ? 0 : Math.round((item.totalRevenue / maxTotalRevenue) * 100),
        playHoursProgress:
          item.playHours <= 0 ? 0 : Math.round((item.playHours / maxPlayHours) * 100),
      }));
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

  private buildSafeSystemLogDeleteWhere() {
    const safeTypeConditions = [
      ...ReportsService.SAFE_DELETABLE_SYSTEM_EVENT_TYPES.map((eventType) => ({
        eventType,
      })),
      ...ReportsService.SAFE_DELETABLE_SYSTEM_EVENT_PREFIXES.map((prefix) => ({
        eventType: {
          startsWith: prefix,
        },
      })),
    ];

    return {
      AND: [
        {
          eventType: {
            notIn: [
              ReportsService.WEBSITE_VISIT_EVENT_TYPE,
              ReportsService.WEBSITE_LOG_SETTINGS_EVENT_TYPE,
              ReportsService.WEB_FILTER_SETTINGS_EVENT_TYPE,
            ],
          },
        },
        {
          OR: safeTypeConditions,
        },
      ],
    };
  }

  private parseLimit(rawLimit?: string): number {
    const parsed = Number(rawLimit ?? '200');
    if (!Number.isFinite(parsed)) {
      return 200;
    }

    return Math.min(500, Math.max(20, Math.floor(parsed)));
  }
}

