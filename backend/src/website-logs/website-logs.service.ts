import { Injectable } from '@nestjs/common';
import { EventSource, Prisma } from '@prisma/client';
import { PrismaService } from '../prisma/prisma.service';
import {
  IngestWebsiteLogsDto,
  WebsiteLogEntryDto,
} from './dto/ingest-website-logs.dto';
import { QueryWebsiteLogsDto } from './dto/query-website-logs.dto';
import { UpdateWebsiteLogSettingsDto } from './dto/update-website-log-settings.dto';

type WebsiteLogSettings = {
  enabled: boolean;
  updatedAt: string;
  updatedBy: string;
};

const WEBSITE_VISIT_EVENT_TYPE = 'website.visit';
const WEBSITE_LOG_SETTINGS_EVENT_TYPE = 'website.log.settings';
const DEFAULT_UPDATED_BY = 'admin.desktop';

@Injectable()
export class WebsiteLogsService {
  constructor(private readonly prisma: PrismaService) {}

  async getSettings(): Promise<WebsiteLogSettings> {
    const latest = await this.prisma.eventLog.findFirst({
      where: {
        eventType: WEBSITE_LOG_SETTINGS_EVENT_TYPE,
      },
      orderBy: {
        createdAt: 'desc',
      },
    });

    if (!latest) {
      return this.persistSettings(false, DEFAULT_UPDATED_BY);
    }

    const payload = this.readPayloadObject(latest.payload);
    const enabled = payload ? this.readBool(payload, 'enabled', false) : false;
    const updatedBy =
      (payload ? this.readString(payload, 'updatedBy') : null) ??
      DEFAULT_UPDATED_BY;

    return {
      enabled,
      updatedAt: latest.createdAt.toISOString(),
      updatedBy,
    };
  }

  async updateSettings(
    payload: UpdateWebsiteLogSettingsDto,
  ): Promise<WebsiteLogSettings> {
    const current = await this.getSettings();
    const enabled = payload.enabled ?? current.enabled;
    const updatedBy = payload.updatedBy?.trim() || DEFAULT_UPDATED_BY;
    return this.persistSettings(enabled, updatedBy);
  }

  async ingestLogs(payload: IngestWebsiteLogsDto) {
    const settings = await this.getSettings();
    if (!settings.enabled) {
      return {
        accepted: 0,
        ignored: true,
        reason: 'WEBSITE_LOG_DISABLED',
        serverTime: new Date().toISOString(),
      };
    }

    const agentId = payload.agentId.trim();
    const pc = await this.prisma.pc.findUnique({
      where: { agentId },
      select: { id: true, name: true, agentId: true },
    });

    if (!pc) {
      return {
        accepted: 0,
        ignored: true,
        reason: 'PC_NOT_FOUND',
        serverTime: new Date().toISOString(),
      };
    }

    const rows = payload.entries
      .map((entry) => this.normalizeEntry(entry))
      .filter((item): item is NonNullable<typeof item> => item !== null)
      .map((entry) => ({
        source: EventSource.CLIENT,
        eventType: WEBSITE_VISIT_EVENT_TYPE,
        pcId: pc.id,
        payload: {
          domain: entry.domain,
          url: entry.url,
          title: entry.title,
          browser: entry.browser,
          visitedAt: entry.visitedAt,
        } as Prisma.InputJsonValue,
      }));

    if (rows.length === 0) {
      return {
        accepted: 0,
        ignored: true,
        reason: 'NO_VALID_ENTRIES',
        serverTime: new Date().toISOString(),
      };
    }

    await this.prisma.eventLog.createMany({
      data: rows,
    });

    return {
      accepted: rows.length,
      ignored: false,
      pcId: pc.id,
      pcName: pc.name,
      agentId: pc.agentId,
      serverTime: new Date().toISOString(),
    };
  }

  async getLogs(query: QueryWebsiteLogsDto) {
    const limit = this.parseLimit(query.limit);
    let targetPcId = query.pcId;
    if (!targetPcId && query.agentId?.trim()) {
      const pc = await this.prisma.pc.findUnique({
        where: { agentId: query.agentId.trim() },
        select: { id: true },
      });
      targetPcId = pc?.id;
    }

    const where: Prisma.EventLogWhereInput = {
      eventType: WEBSITE_VISIT_EVENT_TYPE,
    };
    if (targetPcId) {
      where.pcId = targetPcId;
    }

    if (query.from || query.to) {
      where.createdAt = {
        gte: query.from ? new Date(query.from) : undefined,
        lt: query.to ? new Date(query.to) : undefined,
      };
    }

    const events = await this.prisma.eventLog.findMany({
      where,
      include: {
        pc: {
          select: {
            id: true,
            name: true,
            agentId: true,
          },
        },
      },
      orderBy: {
        createdAt: 'desc',
      },
      take: limit,
    });

    const search = query.search?.trim().toLowerCase();
    const mapped = events
      .map((item) => this.mapLogItem(item))
      .filter((item) => {
        if (!search) {
          return true;
        }

        const haystack = `${item.domain} ${item.url} ${item.title} ${item.browser}`.toLowerCase();
        return haystack.includes(search);
      });

    return {
      items: mapped,
      total: mapped.length,
      serverTime: new Date().toISOString(),
    };
  }

  async clearLogs() {
    const result = await this.prisma.eventLog.deleteMany({
      where: {
        eventType: WEBSITE_VISIT_EVENT_TYPE,
      },
    });

    return {
      deletedCount: result.count,
      serverTime: new Date().toISOString(),
    };
  }

  private async persistSettings(
    enabled: boolean,
    updatedBy: string,
  ): Promise<WebsiteLogSettings> {
    const created = await this.prisma.eventLog.create({
      data: {
        source: EventSource.ADMIN,
        eventType: WEBSITE_LOG_SETTINGS_EVENT_TYPE,
        payload: {
          enabled,
          updatedBy,
          at: new Date().toISOString(),
        },
      },
    });

    return {
      enabled,
      updatedAt: created.createdAt.toISOString(),
      updatedBy,
    };
  }

  private normalizeEntry(entry: WebsiteLogEntryDto) {
    const domain =
      this.normalizeDomain(entry.domain) ??
      this.normalizeDomain(this.extractDomainFromUrl(entry.url));
    if (!domain) {
      return null;
    }

    const url = this.normalizeText(entry.url, 400) ?? `http://${domain}`;
    const title = this.normalizeText(entry.title, 200) ?? null;
    const browser = this.normalizeText(entry.browser, 80) ?? 'unknown';
    const visitedAt = this.normalizeVisitedAt(entry.visitedAt);

    return {
      domain,
      url,
      title,
      browser,
      visitedAt,
    };
  }

  private normalizeVisitedAt(value?: string): string {
    if (!value) {
      return new Date().toISOString();
    }

    const parsed = new Date(value);
    return Number.isNaN(parsed.getTime())
      ? new Date().toISOString()
      : parsed.toISOString();
  }

  private normalizeText(
    value: string | undefined,
    maxLength: number,
  ): string | null {
    if (!value) {
      return null;
    }

    const trimmed = value.trim();
    if (!trimmed) {
      return null;
    }

    if (trimmed.length <= maxLength) {
      return trimmed;
    }

    return trimmed.slice(0, maxLength);
  }

  private extractDomainFromUrl(rawUrl?: string): string | null {
    if (!rawUrl) {
      return null;
    }

    const value = rawUrl.trim();
    if (!value) {
      return null;
    }

    const withProtocol = /^https?:\/\//i.test(value)
      ? value
      : `http://${value}`;
    try {
      const parsed = new URL(withProtocol);
      return parsed.hostname;
    } catch {
      return null;
    }
  }

  private normalizeDomain(raw?: string | null): string | null {
    if (!raw) {
      return null;
    }

    let value = raw.trim().toLowerCase();
    if (!value) {
      return null;
    }

    value = value.replace(/^https?:\/\//, '');
    value = value.replace(/^www\./, '');
    value = value.replace(/^\*\./, '');
    value = value.split(/[/?#:]/)[0] ?? '';
    value = value.replace(/\.+$/, '');

    if (!value || value.length < 3 || value.length > 120) {
      return null;
    }

    if (!/^[a-z0-9.-]+$/.test(value)) {
      return null;
    }

    if (!value.includes('.')) {
      return null;
    }

    if (value.startsWith('-') || value.endsWith('-')) {
      return null;
    }

    return value;
  }

  private readPayloadObject(
    payload: Prisma.JsonValue | null | undefined,
  ): Record<string, unknown> | null {
    if (!payload || typeof payload !== 'object' || Array.isArray(payload)) {
      return null;
    }

    return payload as Record<string, unknown>;
  }

  private readString(
    payload: Record<string, unknown>,
    key: string,
  ): string | null {
    const value = payload[key];
    if (typeof value !== 'string') {
      return null;
    }

    const trimmed = value.trim();
    return trimmed || null;
  }

  private readBool(
    payload: Record<string, unknown>,
    key: string,
    fallback: boolean,
  ): boolean {
    const value = payload[key];
    return typeof value === 'boolean' ? value : fallback;
  }

  private parseLimit(raw: number | undefined): number {
    if (!raw || !Number.isFinite(raw)) {
      return 300;
    }

    return Math.min(1000, Math.max(20, Math.floor(raw)));
  }

  private mapLogItem(item: {
    id: string;
    pcId: string | null;
    payload: Prisma.JsonValue | null;
    createdAt: Date;
    pc: { id: string; name: string; agentId: string } | null;
  }) {
    const payload = this.readPayloadObject(item.payload) ?? {};
    const domain = this.readString(payload, 'domain') ?? '-';
    const url = this.readString(payload, 'url') ?? '-';
    const title = this.readString(payload, 'title') ?? '-';
    const browser = this.readString(payload, 'browser') ?? '-';
    const visitedAt =
      this.readString(payload, 'visitedAt') ?? item.createdAt.toISOString();

    return {
      id: item.id,
      pcId: item.pc?.id ?? item.pcId ?? null,
      pcName: item.pc?.name ?? null,
      agentId: item.pc?.agentId ?? null,
      domain,
      url,
      title,
      browser,
      visitedAt,
      createdAt: item.createdAt.toISOString(),
    };
  }
}
