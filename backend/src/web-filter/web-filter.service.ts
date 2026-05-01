import { Injectable } from '@nestjs/common';
import { EventSource, Prisma } from '@prisma/client';
import { PrismaService } from '../prisma/prisma.service';
import { UpdateWebFilterSettingsDto } from './dto/update-web-filter-settings.dto';

type WebFilterSettings = {
  enabled: boolean;
  blockedDomains: string[];
  updatedAt: string;
  updatedBy: string;
};

const DEFAULT_UPDATED_BY = 'admin.desktop';
const WEB_FILTER_EVENT_TYPE = 'web.filter.settings';
const DEFAULT_BLOCKED_DOMAINS = [
  'xvideos.com',
  'xnxx.com',
  'pornhub.com',
  'xhamster.com',
  'redtube.com',
  'youporn.com',
  'tube8.com',
  'beeg.com',
  'spankbang.com',
  'sex.com',
  'brazzers.com',
  'bangbros.com',
  'youjizz.com',
  'hclips.com',
  'tnaflix.com',
  'sunporno.com',
  'porn.com',
  'pornpics.com',
  'rule34.xxx',
  'hentaihaven.xxx',
  'nhentai.net',
  'f95zone.to',
  'motherless.com',
  'cam4.com',
  'chaturbate.com',
  'bongacams.com',
  'stripchat.com',
  'livejasmin.com',
  'xnxx.tv',
  'xvideos2.com',
];

@Injectable()
export class WebFilterService {
  constructor(private readonly prisma: PrismaService) {}

  async getSettings() {
    const existing = await this.prisma.eventLog.findFirst({
      where: {
        eventType: WEB_FILTER_EVENT_TYPE,
      },
      orderBy: {
        createdAt: 'desc',
      },
    });

    if (!existing) {
      return this.persistSettings({
        enabled: true,
        blockedDomains: DEFAULT_BLOCKED_DOMAINS,
        updatedBy: DEFAULT_UPDATED_BY,
      });
    }

    const parsed = this.parseSettingsPayload(existing.payload);
    if (!parsed) {
      return this.persistSettings({
        enabled: true,
        blockedDomains: DEFAULT_BLOCKED_DOMAINS,
        updatedBy: DEFAULT_UPDATED_BY,
      });
    }

    return {
      enabled: parsed.enabled,
      blockedDomains: parsed.blockedDomains,
      updatedAt: existing.createdAt.toISOString(),
      updatedBy: parsed.updatedBy || DEFAULT_UPDATED_BY,
    };
  }

  async updateSettings(payload: UpdateWebFilterSettingsDto) {
    const current = await this.getSettings();
    const enabled = payload.enabled ?? current.enabled;
    const blockedDomains = payload.blockedDomains
      ? this.normalizeDomains(payload.blockedDomains)
      : current.blockedDomains;
    const updatedBy = payload.updatedBy?.trim() || DEFAULT_UPDATED_BY;

    return this.persistSettings({
      enabled,
      blockedDomains,
      updatedBy,
    });
  }

  private async persistSettings(input: {
    enabled: boolean;
    blockedDomains: string[];
    updatedBy: string;
  }): Promise<WebFilterSettings> {
    const blockedDomains = this.normalizeDomains(input.blockedDomains);
    const created = await this.prisma.eventLog.create({
      data: {
        source: EventSource.ADMIN,
        eventType: WEB_FILTER_EVENT_TYPE,
        payload: {
          enabled: input.enabled,
          blockedDomains,
          updatedBy: input.updatedBy,
          at: new Date().toISOString(),
        },
      },
    });

    return {
      enabled: input.enabled,
      blockedDomains,
      updatedAt: created.createdAt.toISOString(),
      updatedBy: input.updatedBy,
    };
  }

  private parseSettingsPayload(
    payload: Prisma.JsonValue | null | undefined,
  ): { enabled: boolean; blockedDomains: string[]; updatedBy: string } | null {
    if (!payload || typeof payload !== 'object' || Array.isArray(payload)) {
      return null;
    }

    const record = payload as Record<string, unknown>;
    const enabledRaw = record.enabled;
    const blockedRaw = record.blockedDomains;
    const updatedByRaw = record.updatedBy;

    if (typeof enabledRaw !== 'boolean') {
      return null;
    }

    const blockedDomains = Array.isArray(blockedRaw)
      ? blockedRaw.filter((item): item is string => typeof item === 'string')
      : [];

    return {
      enabled: enabledRaw,
      blockedDomains: this.normalizeDomains(blockedDomains),
      updatedBy:
        typeof updatedByRaw === 'string' && updatedByRaw.trim()
          ? updatedByRaw.trim()
          : DEFAULT_UPDATED_BY,
    };
  }

  private normalizeDomains(rawDomains: string[]): string[] {
    const unique = new Set<string>();
    for (const raw of rawDomains) {
      const normalized = this.normalizeDomain(raw);
      if (!normalized) {
        continue;
      }

      unique.add(normalized);
    }

    return Array.from(unique.values());
  }

  private normalizeDomain(raw: string): string | null {
    let value = raw.trim().toLowerCase();
    if (!value) {
      return null;
    }

    value = value.replace(/^https?:\/\//, '');
    value = value.replace(/^www\./, '');
    value = value.replace(/^\*\./, '');
    value = value.split(/[/?#:]/)[0] ?? '';
    value = value.replace(/\.+$/, '');

    if (!value || value.length < 3 || value.length > 90) {
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
}
