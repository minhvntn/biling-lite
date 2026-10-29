import { Injectable } from '@nestjs/common';
import { EventSource, Prisma } from '@prisma/client';
import { PrismaService } from '../prisma/prisma.service';
import { RealtimeService } from '../realtime/realtime.service';
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
  '21sextury.com',
  '3hentai.net',
  '4tube.com',
  '7mmtv.tv',
  'admireme.vip',
  'adulttime.com',
  'alohatube.com',
  'anon-v.com',
  'anyporn.com',
  'asmhentai.com',
  'avgle.com',
  'babes.com',
  'badjojo.com',
  'bangbros.com',
  'beeg.com',
  'blacked.com',
  'bongacams.com',
  'bravotube.net',
  'brazzers.com',
  'bunkr.is',
  'bunkr.site',
  'cam4.com',
  'camcaps.to',
  'cammodeldirectory.com',
  'camslib.com',
  'camsoda.com',
  'camwhores.porn',
  'camwhores.tv',
  'chaturbate.com',
  'checkervn.com',
  'cliphunter.com',
  'clips4sale.com',
  'clipsex.com',
  'congdongmassage.com',
  'coomer.party',
  'coomer.su',
  'cumlouder.com',
  'cyberdrop.me',
  'daftsex.com',
  'dammy88.com',
  'danbooru.donmai.us',
  'deeper.com',
  'definethick.com',
  'digitalplayground.com',
  'dirtyship.com',
  'drtuber.com',
  'e-hentai.org',
  'e621.net',
  'empflix.com',
  'eporner.com',
  'erome.com',
  'evilangel.com',
  'exhentai.org',
  'f95zone.to',
  'fakehub.com',
  'fansly.com',
  'fapdu.com',
  'fapellasuite.com',
  'fapello.com',
  'flirt4free.com',
  'freejavbt.com',
  'freeomovie.to',
  'fuq.com',
  'gaigoi.org',
  'gaigoivn.net',
  'gelbooru.com',
  'girlsway.com',
  'gotporn.com',
  'hamtruyen.vn',
  'hanime.tv',
  'hayxx.com',
  'hclips.com',
  'heavy-r.com',
  'hentai2read.com',
  'hentaiera.com',
  'hentaifox.com',
  'hentaihaven.xxx',
  'hentaimama.io',
  'hentaiplay.net',
  'hentaivn.moe',
  'hentaivn.net',
  'hentaivn.tv',
  'hitomi.la',
  'hotmovs.com',
  'hpjav.tv',
  'hqporner.com',
  'hustler.com',
  'imlive.com',
  'iwantclips.com',
  'jable.tv',
  'javbus.com',
  'javcl.com',
  'javdb.com',
  'javfinder.la',
  'javguru.com',
  'javhd.com',
  'javhihi.com',
  'javlibrary.com',
  'javmenu.com',
  'javmost.com',
  'javseen.com',
  'javsub.co',
  'javtiful.com',
  'jerkmate.com',
  'jizzbunker.com',
  'keezmovies.com',
  'kemono.party',
  'kemono.su',
  'kink.com',
  'lauxanh.org',
  'lauxanh.us',
  'livejasmin.com',
  'loyalfans.com',
  'luscious.net',
  'madthumbs.com',
  'manyvids.com',
  'metart.com',
  'missav.ai',
  'missav.com',
  'missav.ws',
  'mofos.com',
  'motherless.com',
  'multporn.net',
  'myfreecams.com',
  'naughtyamerica.com',
  'netflav.com',
  'nhentai.net',
  'nubiles.net',
  'nude.com',
  'nuvid.com',
  'onlyfans.com',
  'paradisehill.cc',
  'penthouse.com',
  'phimhot69.com',
  'phimlon.com',
  'phimsex.com',
  'phimsex.net',
  'phimsex3.com',
  'phimsex99.com',
  'phimsexchina.com',
  'phimsexhay.org',
  'phimsexhd.org',
  'phimsexkorea.com',
  'phimsexloanluan.org',
  'phimsexmoi.net',
  'phimsexmy.com',
  'phimsexnhatban.org',
  'phimsexsub.com',
  'phimsexvn.org',
  'phimxxx.com',
  'playboy.com',
  'porn.com',
  'porndiq.com',
  'porndoe.com',
  'porndude.com',
  'pornez.cam',
  'porngo.com',
  'pornhat.com',
  'pornhd.com',
  'pornhub.com',
  'pornhubselect.com',
  'pornone.com',
  'pornoxo.com',
  'pornpics.com',
  'porntrex.com',
  'porntube.com',
  'private.com',
  'proporn.com',
  'puretaboo.com',
  'pururin.io',
  'realitykings.com',
  'rec-tube.com',
  'redtube.com',
  'rk.com',
  'rule34.xxx',
  'rule34video.com',
  'sex.com',
  'sexchauau.com',
  'sexnhat.me',
  'sexnhat.pro',
  'sexviet.tv',
  'sexvietsub.com',
  'sexvl.com',
  'sexvl.net',
  'simpcity.su',
  'simply-hentai.com',
  'slayed.com',
  'slutload.com',
  'spankbang.com',
  'streamate.com',
  'streamporn.to',
  'stripchat.com',
  'subnhanh.xxx',
  'sunporno.com',
  'supjav.com',
  'sweet-sinner.com',
  'teamskeet.com',
  'theporndude.com',
  'thiendia.com',
  'thienvadia.com',
  'thotsbay.to',
  'thumbzilla.com',
  'tktube.com',
  'tnaflix.com',
  'tokyomotion.net',
  'topvlxx.com',
  'truyentranh18.net',
  'tsumino.com',
  'tube8.com',
  'tubepornclassic.com',
  'tuberel.com',
  'tubev.sex',
  'tushy.com',
  'twistys.com',
  'txxx.com',
  'underhentai.net',
  'upornia.com',
  'viptube.com',
  'vixen.com',
  'vlxx.com',
  'vlxx.moe',
  'vlxx.sex',
  'vlxx.tv',
  'vnbom.com',
  'vrsmash.com',
  'webcamrips.com',
  'wicked.com',
  'xfreehd.com',
  'xhamster.com',
  'xnxx.com',
  'xnxx.tv',
  'xozilla.com',
  'xvideos.com',
  'xvideos2.com',
  'xvideovn.com',
  'xxvn.com',
  'xxvn.net',
  'yespornplease.com',
  'youjizz.com',
  'youporn.com',
];

@Injectable()
export class WebFilterService {
  constructor(
    private readonly prisma: PrismaService,
    private readonly realtime: RealtimeService,
  ) {}

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

    const result = {
      enabled: input.enabled,
      blockedDomains,
      updatedAt: created.createdAt.toISOString(),
      updatedBy: input.updatedBy,
    };

    this.realtime.emitToAll('web.filter.settings.changed', {
      enabled: result.enabled,
      blockedDomains: result.blockedDomains,
      updatedAt: result.updatedAt,
      updatedBy: result.updatedBy,
    });

    return result;
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
