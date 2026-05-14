import { BadRequestException, Injectable, Logger } from '@nestjs/common';
import { Cron, CronExpression } from '@nestjs/schedule';
import { EventSource, Prisma } from '@prisma/client';
import { promises as fs } from 'fs';
import * as path from 'path';
import { PrismaService } from '../prisma/prisma.service';
import { UpdateBackupSettingsDto } from './dto/update-backup-settings.dto';

type BackupScheduleType = 'daily' | 'weekly';

type BackupSettingsSnapshot = {
  enabled: boolean;
  scheduleType: BackupScheduleType;
  time: string;
  weeklyDay: number;
  directory: string;
  retentionDays: number;
  lastRunAt: string;
  lastStatus: string;
  lastError: string;
  lastFileName: string;
  lastFilePath: string;
};

type BackupDataPayload = {
  appSettings: any[];
  pricingConfigs: any[];
  pcGroups: any[];
  pcs: any[];
  members: any[];
  loyaltyRankConfigs: any[];
  serviceItems: any[];
  sessions: any[];
  commands: any[];
  eventLogs: any[];
  memberTransactions: any[];
  pcServiceOrders: any[];
  timeBasedPromotions: any[];
};

type BackupDocument = {
  version: number;
  app: string;
  createdAt: string;
  requestedBy: string;
  trigger: 'manual' | 'auto';
  data: BackupDataPayload;
};

const BACKUP_ENABLED_KEY = 'AUTO_BACKUP_ENABLED';
const BACKUP_SCHEDULE_TYPE_KEY = 'AUTO_BACKUP_SCHEDULE_TYPE';
const BACKUP_TIME_KEY = 'AUTO_BACKUP_TIME';
const BACKUP_WEEKLY_DAY_KEY = 'AUTO_BACKUP_WEEKLY_DAY';
const BACKUP_DIRECTORY_KEY = 'AUTO_BACKUP_DIRECTORY';
const BACKUP_RETENTION_DAYS_KEY = 'AUTO_BACKUP_RETENTION_DAYS';
const BACKUP_LAST_RUN_AT_KEY = 'AUTO_BACKUP_LAST_RUN_AT';
const BACKUP_LAST_STATUS_KEY = 'AUTO_BACKUP_LAST_STATUS';
const BACKUP_LAST_ERROR_KEY = 'AUTO_BACKUP_LAST_ERROR';
const BACKUP_LAST_FILE_NAME_KEY = 'AUTO_BACKUP_LAST_FILE_NAME';
const BACKUP_LAST_FILE_PATH_KEY = 'AUTO_BACKUP_LAST_FILE_PATH';
const BACKUP_LAST_SLOT_KEY = 'AUTO_BACKUP_LAST_SLOT';

const DEFAULT_BACKUP_TIME = '02:00';
const DEFAULT_WEEKLY_DAY = 1;
const DEFAULT_RETENTION_DAYS = 30;

@Injectable()
export class SettingsBackupService {
  private readonly logger = new Logger(SettingsBackupService.name);
  private readonly defaultBackupDirectory = path.resolve(
    process.cwd(),
    'backups',
  );
  private isBackupOrRestoreRunning = false;

  constructor(private readonly prisma: PrismaService) {}

  async getBackupSettings(): Promise<BackupSettingsSnapshot> {
    const settings = await this.loadBackupSettingsSnapshot();
    return settings;
  }

  async updateBackupSettings(
    payload: UpdateBackupSettingsDto,
  ): Promise<BackupSettingsSnapshot> {
    const current = await this.loadBackupSettingsSnapshot();

    const enabled =
      payload.enabled === undefined ? current.enabled : payload.enabled;
    const scheduleType = this.normalizeScheduleType(
      payload.scheduleType ?? current.scheduleType,
    );
    const time = this.normalizeTime(payload.time ?? current.time);
    const weeklyDay = this.normalizeWeeklyDay(payload.weeklyDay ?? current.weeklyDay);
    const directory = this.normalizeDirectory(payload.directory ?? current.directory);
    const retentionDays = this.normalizeRetentionDays(
      payload.retentionDays ?? current.retentionDays,
    );

    await this.upsertSetting(BACKUP_ENABLED_KEY, enabled ? 'true' : 'false');
    await this.upsertSetting(BACKUP_SCHEDULE_TYPE_KEY, scheduleType);
    await this.upsertSetting(BACKUP_TIME_KEY, time);
    await this.upsertSetting(BACKUP_WEEKLY_DAY_KEY, weeklyDay.toString());
    await this.upsertSetting(BACKUP_DIRECTORY_KEY, directory);
    await this.upsertSetting(BACKUP_RETENTION_DAYS_KEY, retentionDays.toString());

    await this.ensureBackupDirectoryExists(directory);
    return this.loadBackupSettingsSnapshot();
  }

  async runBackupNow(requestedBy = 'admin.desktop') {
    const settings = await this.loadBackupSettingsSnapshot();
    return this.createBackupAndPersistMetadata({
      requestedBy: this.normalizeRequestedBy(requestedBy),
      trigger: 'manual',
      settings,
    });
  }

  async importBackupFromBuffer(
    originalFileName: string,
    fileBuffer: Buffer,
    requestedBy = 'admin.desktop',
  ) {
    if (!fileBuffer || fileBuffer.length === 0) {
      throw new BadRequestException('File backup rỗng');
    }

    const requestedByNormalized = this.normalizeRequestedBy(requestedBy);
    const fileText = fileBuffer.toString('utf8');
    let parsed: BackupDocument;
    try {
      parsed = JSON.parse(fileText) as BackupDocument;
    } catch {
      throw new BadRequestException('File backup không đúng định dạng JSON');
    }

    const data = this.validateBackupDocument(parsed);

    if (this.isBackupOrRestoreRunning) {
      throw new BadRequestException(
        'Hệ thống đang chạy backup/restore khác. Vui lòng thử lại sau.',
      );
    }

    this.isBackupOrRestoreRunning = true;
    try {
      await this.restoreDataSnapshot(data);

      await this.logEvent('backup.restored', {
        sourceFileName: originalFileName,
        requestedBy: requestedByNormalized,
        restoredAt: new Date().toISOString(),
        counts: this.getDataCounts(data),
      });

      return {
        ok: true,
        restoredAt: new Date().toISOString(),
        sourceFileName: originalFileName,
        requestedBy: requestedByNormalized,
        counts: this.getDataCounts(data),
      };
    } finally {
      this.isBackupOrRestoreRunning = false;
    }
  }

  async listBackupFiles() {
    const settings = await this.loadBackupSettingsSnapshot();
    await this.ensureBackupDirectoryExists(settings.directory);

    const files = await fs.readdir(settings.directory, { withFileTypes: true });
    const jsonFiles = files
      .filter((entry) => entry.isFile())
      .filter((entry) => entry.name.toLowerCase().endsWith('.json'));

    const detailed = await Promise.all(
      jsonFiles.map(async (entry) => {
        const filePath = path.join(settings.directory, entry.name);
        const stat = await fs.stat(filePath);
        return {
          fileName: entry.name,
          filePath,
          sizeBytes: stat.size,
          createdAt: stat.birthtime.toISOString(),
          updatedAt: stat.mtime.toISOString(),
        };
      }),
    );

    detailed.sort((a, b) =>
      b.updatedAt.localeCompare(a.updatedAt, 'en', { sensitivity: 'base' }),
    );

    return {
      directory: settings.directory,
      items: detailed,
    };
  }

  async getBackupFileForDownload(fileName: string): Promise<{
    directory: string;
    fileName: string;
    filePath: string;
  }> {
    const safeFileName = this.ensureSafeBackupFileName(fileName);
    const settings = await this.loadBackupSettingsSnapshot();
    const filePath = path.join(settings.directory, safeFileName);

    try {
      const stat = await fs.stat(filePath);
      if (!stat.isFile()) {
        throw new BadRequestException('Không tìm thấy file backup');
      }
    } catch {
      throw new BadRequestException('Không tìm thấy file backup');
    }

    return {
      directory: settings.directory,
      fileName: safeFileName,
      filePath,
    };
  }

  @Cron(CronExpression.EVERY_MINUTE)
  async runScheduledBackupIfDue() {
    const settings = await this.loadBackupSettingsSnapshot();
    if (!settings.enabled) {
      return;
    }

    const now = new Date();
    if (!this.isScheduledTimeDue(settings, now)) {
      return;
    }

    const slotKey = this.buildBackupSlotKey(settings, now);
    const lastSlot = await this.getSettingValue(BACKUP_LAST_SLOT_KEY);
    if (lastSlot === slotKey) {
      return;
    }

    if (this.isBackupOrRestoreRunning) {
      await this.upsertSetting(BACKUP_LAST_SLOT_KEY, slotKey);
      await this.upsertSetting(
        BACKUP_LAST_STATUS_KEY,
        'skipped-running-another-task',
      );
      return;
    }

    try {
      await this.createBackupAndPersistMetadata({
        requestedBy: 'system.scheduler',
        trigger: 'auto',
        settings,
      });
    } catch (error) {
      const errorMessage =
        error instanceof Error ? error.message : 'Unknown backup error';
      this.logger.error(`Scheduled backup failed: ${errorMessage}`);
      await this.upsertSetting(BACKUP_LAST_STATUS_KEY, 'failed');
      await this.upsertSetting(BACKUP_LAST_ERROR_KEY, errorMessage);
      await this.logEvent('backup.failed', {
        requestedBy: 'system.scheduler',
        trigger: 'auto',
        error: errorMessage,
      });
    } finally {
      await this.upsertSetting(BACKUP_LAST_SLOT_KEY, slotKey);
    }
  }

  private async createBackupAndPersistMetadata(args: {
    requestedBy: string;
    trigger: 'manual' | 'auto';
    settings: BackupSettingsSnapshot;
  }) {
    if (this.isBackupOrRestoreRunning) {
      throw new BadRequestException(
        'Hệ thống đang chạy backup/restore khác. Vui lòng thử lại sau.',
      );
    }

    this.isBackupOrRestoreRunning = true;
    try {
      await this.ensureBackupDirectoryExists(args.settings.directory);
      const createdAt = new Date();
      const fileName = this.buildBackupFileName(createdAt, args.trigger);
      const filePath = path.join(args.settings.directory, fileName);
      const data = await this.collectBackupDataSnapshot();

      const document: BackupDocument = {
        version: 1,
        app: 'servermanagerbilling',
        createdAt: createdAt.toISOString(),
        requestedBy: args.requestedBy,
        trigger: args.trigger,
        data,
      };

      await fs.writeFile(filePath, JSON.stringify(document, null, 2), 'utf8');
      await this.cleanupExpiredBackups(
        args.settings.directory,
        args.settings.retentionDays,
      );

      await this.upsertSetting(
        BACKUP_LAST_RUN_AT_KEY,
        createdAt.toISOString(),
      );
      await this.upsertSetting(BACKUP_LAST_STATUS_KEY, 'success');
      await this.upsertSetting(BACKUP_LAST_ERROR_KEY, '');
      await this.upsertSetting(BACKUP_LAST_FILE_NAME_KEY, fileName);
      await this.upsertSetting(BACKUP_LAST_FILE_PATH_KEY, filePath);

      await this.logEvent('backup.created', {
        requestedBy: args.requestedBy,
        trigger: args.trigger,
        createdAt: createdAt.toISOString(),
        fileName,
        filePath,
        counts: this.getDataCounts(data),
      });

      return {
        ok: true,
        fileName,
        filePath,
        createdAt: createdAt.toISOString(),
        trigger: args.trigger,
        counts: this.getDataCounts(data),
      };
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Unknown error';
      await this.upsertSetting(BACKUP_LAST_STATUS_KEY, 'failed');
      await this.upsertSetting(BACKUP_LAST_ERROR_KEY, message);
      throw error;
    } finally {
      this.isBackupOrRestoreRunning = false;
    }
  }

  private async collectBackupDataSnapshot(): Promise<BackupDataPayload> {
    return this.prisma.$transaction(async (tx) => {
      const [
        appSettings,
        pricingConfigs,
        pcGroups,
        pcs,
        members,
        loyaltyRankConfigs,
        serviceItems,
        sessions,
        commands,
        eventLogs,
        memberTransactions,
        pcServiceOrders,
        timeBasedPromotions,
      ] = await Promise.all([
        tx.appSetting.findMany({ orderBy: { key: 'asc' } }),
        tx.pricingConfig.findMany({ orderBy: { createdAt: 'asc' } }),
        tx.pcGroup.findMany({ orderBy: { createdAt: 'asc' } }),
        tx.pc.findMany({ orderBy: { createdAt: 'asc' } }),
        tx.member.findMany({ orderBy: { createdAt: 'asc' } }),
        tx.loyaltyRankConfig.findMany({ orderBy: { createdAt: 'asc' } }),
        tx.serviceItem.findMany({ orderBy: { createdAt: 'asc' } }),
        tx.session.findMany({ orderBy: { startedAt: 'asc' } }),
        tx.command.findMany({ orderBy: { requestedAt: 'asc' } }),
        tx.eventLog.findMany({ orderBy: { createdAt: 'asc' } }),
        tx.memberTransaction.findMany({ orderBy: { createdAt: 'asc' } }),
        tx.pcServiceOrder.findMany({ orderBy: { createdAt: 'asc' } }),
        tx.timeBasedPromotion.findMany({ orderBy: { createdAt: 'asc' } }),
      ]);

      return {
        appSettings,
        pricingConfigs,
        pcGroups,
        pcs,
        members,
        loyaltyRankConfigs,
        serviceItems,
        sessions,
        commands,
        eventLogs,
        memberTransactions,
        pcServiceOrders,
        timeBasedPromotions,
      };
    });
  }

  private async restoreDataSnapshot(data: BackupDataPayload): Promise<void> {
    await this.prisma.$transaction(async (tx) => {
      await tx.$executeRawUnsafe(`
        TRUNCATE TABLE
          "pc_service_orders",
          "member_transactions",
          "commands",
          "sessions",
          "events_log",
          "members",
          "service_items",
          "pcs",
          "pc_groups",
          "pricing_config",
          "loyalty_rank_configs",
          "time_based_promotions",
          "app_settings"
        RESTART IDENTITY CASCADE;
      `);

      if (data.appSettings.length > 0) {
        await tx.appSetting.createMany({ data: data.appSettings });
      }
      if (data.pricingConfigs.length > 0) {
        await tx.pricingConfig.createMany({ data: data.pricingConfigs });
      }
      if (data.pcGroups.length > 0) {
        await tx.pcGroup.createMany({ data: data.pcGroups });
      }
      if (data.pcs.length > 0) {
        await tx.pc.createMany({ data: data.pcs });
      }
      if (data.members.length > 0) {
        await tx.member.createMany({ data: data.members });
      }
      if (data.loyaltyRankConfigs.length > 0) {
        await tx.loyaltyRankConfig.createMany({ data: data.loyaltyRankConfigs });
      }
      if (data.serviceItems.length > 0) {
        await tx.serviceItem.createMany({ data: data.serviceItems });
      }
      if (data.sessions.length > 0) {
        await tx.session.createMany({ data: data.sessions });
      }
      if (data.commands.length > 0) {
        await tx.command.createMany({ data: data.commands });
      }
      if (data.eventLogs.length > 0) {
        await tx.eventLog.createMany({ data: data.eventLogs });
      }
      if (data.memberTransactions.length > 0) {
        await tx.memberTransaction.createMany({
          data: data.memberTransactions,
        });
      }
      if (data.pcServiceOrders.length > 0) {
        await tx.pcServiceOrder.createMany({ data: data.pcServiceOrders });
      }
      if (data.timeBasedPromotions.length > 0) {
        await tx.timeBasedPromotion.createMany({
          data: data.timeBasedPromotions,
        });
      }
    });
  }

  private validateBackupDocument(payload: BackupDocument): BackupDataPayload {
    if (!payload || typeof payload !== 'object') {
      throw new BadRequestException('Nội dung file backup không hợp lệ');
    }

    if (!payload.data || typeof payload.data !== 'object') {
      throw new BadRequestException('File backup thiếu dữ liệu');
    }

    const requiredKeys: Array<keyof BackupDataPayload> = [
      'appSettings',
      'pricingConfigs',
      'pcGroups',
      'pcs',
      'members',
      'loyaltyRankConfigs',
      'serviceItems',
      'sessions',
      'commands',
      'eventLogs',
      'memberTransactions',
      'pcServiceOrders',
      'timeBasedPromotions',
    ];

    for (const key of requiredKeys) {
      if (!Array.isArray((payload.data as any)[key])) {
        throw new BadRequestException(`File backup thiếu danh sách ${key}`);
      }
    }

    return payload.data as BackupDataPayload;
  }

  private getDataCounts(data: BackupDataPayload) {
    return {
      appSettings: data.appSettings.length,
      pricingConfigs: data.pricingConfigs.length,
      pcGroups: data.pcGroups.length,
      pcs: data.pcs.length,
      members: data.members.length,
      loyaltyRankConfigs: data.loyaltyRankConfigs.length,
      serviceItems: data.serviceItems.length,
      sessions: data.sessions.length,
      commands: data.commands.length,
      eventLogs: data.eventLogs.length,
      memberTransactions: data.memberTransactions.length,
      pcServiceOrders: data.pcServiceOrders.length,
      timeBasedPromotions: data.timeBasedPromotions.length,
    };
  }

  private async cleanupExpiredBackups(
    directory: string,
    retentionDays: number,
  ): Promise<void> {
    const files = await fs.readdir(directory, { withFileTypes: true });
    const cutoff = Date.now() - retentionDays * 24 * 60 * 60 * 1000;

    for (const entry of files) {
      if (!entry.isFile()) {
        continue;
      }

      if (!entry.name.toLowerCase().endsWith('.json')) {
        continue;
      }

      const fullPath = path.join(directory, entry.name);
      try {
        const stat = await fs.stat(fullPath);
        if (stat.mtimeMs < cutoff) {
          await fs.unlink(fullPath);
        }
      } catch {
        // ignore single-file cleanup errors
      }
    }
  }

  private buildBackupFileName(date: Date, trigger: 'manual' | 'auto'): string {
    const yyyy = date.getFullYear();
    const mm = `${date.getMonth() + 1}`.padStart(2, '0');
    const dd = `${date.getDate()}`.padStart(2, '0');
    const hh = `${date.getHours()}`.padStart(2, '0');
    const mi = `${date.getMinutes()}`.padStart(2, '0');
    const ss = `${date.getSeconds()}`.padStart(2, '0');
    return `billing-backup-${trigger}-${yyyy}${mm}${dd}-${hh}${mi}${ss}.json`;
  }

  private isScheduledTimeDue(
    settings: BackupSettingsSnapshot,
    now: Date,
  ): boolean {
    const [hourText, minuteText] = settings.time.split(':');
    const targetHour = Number(hourText);
    const targetMinute = Number(minuteText);

    if (now.getHours() !== targetHour || now.getMinutes() !== targetMinute) {
      return false;
    }

    if (settings.scheduleType === 'weekly') {
      return this.toIsoWeekday(now) === settings.weeklyDay;
    }

    return true;
  }

  private buildBackupSlotKey(
    settings: BackupSettingsSnapshot,
    now: Date,
  ): string {
    const yyyy = now.getFullYear();
    const mm = `${now.getMonth() + 1}`.padStart(2, '0');
    const dd = `${now.getDate()}`.padStart(2, '0');
    if (settings.scheduleType === 'weekly') {
      const weekday = this.toIsoWeekday(now);
      return `weekly:${yyyy}${mm}${dd}:${weekday}:${settings.time}`;
    }

    return `daily:${yyyy}${mm}${dd}:${settings.time}`;
  }

  private toIsoWeekday(date: Date): number {
    const day = date.getDay();
    return day === 0 ? 7 : day;
  }

  private normalizeRequestedBy(value: string): string {
    const trimmed = (value ?? '').trim();
    return trimmed || 'admin.desktop';
  }

  private normalizeScheduleType(value: string): BackupScheduleType {
    return value === 'weekly' ? 'weekly' : 'daily';
  }

  private normalizeTime(value: string): string {
    const raw = (value ?? '').trim();
    if (/^([01]\d|2[0-3]):([0-5]\d)$/.test(raw)) {
      return raw;
    }

    return DEFAULT_BACKUP_TIME;
  }

  private normalizeWeeklyDay(value: number): number {
    if (Number.isInteger(value) && value >= 1 && value <= 7) {
      return value;
    }

    return DEFAULT_WEEKLY_DAY;
  }

  private normalizeRetentionDays(value: number): number {
    if (Number.isInteger(value) && value >= 1 && value <= 3650) {
      return value;
    }

    return DEFAULT_RETENTION_DAYS;
  }

  private normalizeDirectory(value: string): string {
    const trimmed = (value ?? '').trim();
    if (!trimmed) {
      return this.defaultBackupDirectory;
    }

    return path.resolve(trimmed);
  }

  private ensureSafeBackupFileName(fileName: string): string {
    const trimmed = (fileName ?? '').trim();
    if (!trimmed) {
      throw new BadRequestException('Tên file backup không hợp lệ');
    }

    if (trimmed.includes('/') || trimmed.includes('\\')) {
      throw new BadRequestException('Tên file backup không hợp lệ');
    }

    if (!trimmed.toLowerCase().endsWith('.json')) {
      throw new BadRequestException('Chỉ hỗ trợ file backup .json');
    }

    return trimmed;
  }

  private async ensureBackupDirectoryExists(directory: string): Promise<void> {
    await fs.mkdir(directory, { recursive: true });
  }

  private async loadBackupSettingsSnapshot(): Promise<BackupSettingsSnapshot> {
    const keys = [
      BACKUP_ENABLED_KEY,
      BACKUP_SCHEDULE_TYPE_KEY,
      BACKUP_TIME_KEY,
      BACKUP_WEEKLY_DAY_KEY,
      BACKUP_DIRECTORY_KEY,
      BACKUP_RETENTION_DAYS_KEY,
      BACKUP_LAST_RUN_AT_KEY,
      BACKUP_LAST_STATUS_KEY,
      BACKUP_LAST_ERROR_KEY,
      BACKUP_LAST_FILE_NAME_KEY,
      BACKUP_LAST_FILE_PATH_KEY,
    ];

    const rows = await this.prisma.appSetting.findMany({
      where: {
        key: {
          in: keys,
        },
      },
    });

    const map = new Map<string, string>();
    for (const row of rows) {
      map.set(row.key, row.value);
    }

    const scheduleType = this.normalizeScheduleType(
      map.get(BACKUP_SCHEDULE_TYPE_KEY) ?? 'daily',
    );
    const time = this.normalizeTime(
      map.get(BACKUP_TIME_KEY) ?? DEFAULT_BACKUP_TIME,
    );
    const weeklyDay = this.normalizeWeeklyDay(
      Number(map.get(BACKUP_WEEKLY_DAY_KEY) ?? DEFAULT_WEEKLY_DAY),
    );
    const retentionDays = this.normalizeRetentionDays(
      Number(map.get(BACKUP_RETENTION_DAYS_KEY) ?? DEFAULT_RETENTION_DAYS),
    );
    const directory = this.normalizeDirectory(
      map.get(BACKUP_DIRECTORY_KEY) ?? this.defaultBackupDirectory,
    );
    const enabled = (map.get(BACKUP_ENABLED_KEY) ?? 'false') === 'true';

    return {
      enabled,
      scheduleType,
      time,
      weeklyDay,
      directory,
      retentionDays,
      lastRunAt: map.get(BACKUP_LAST_RUN_AT_KEY) ?? '',
      lastStatus: map.get(BACKUP_LAST_STATUS_KEY) ?? '',
      lastError: map.get(BACKUP_LAST_ERROR_KEY) ?? '',
      lastFileName: map.get(BACKUP_LAST_FILE_NAME_KEY) ?? '',
      lastFilePath: map.get(BACKUP_LAST_FILE_PATH_KEY) ?? '',
    };
  }

  private async getSettingValue(key: string): Promise<string> {
    const row = await this.prisma.appSetting.findUnique({ where: { key } });
    return row?.value ?? '';
  }

  private async upsertSetting(key: string, value: string): Promise<void> {
    await this.prisma.appSetting.upsert({
      where: { key },
      update: { value },
      create: { key, value },
    });
  }

  private async logEvent(
    eventType: string,
    payload: Prisma.InputJsonValue,
  ): Promise<void> {
    try {
      await this.prisma.eventLog.create({
        data: {
          source: EventSource.SERVER,
          eventType,
          payload,
        },
      });
    } catch (error) {
      const message =
        error instanceof Error ? error.message : 'Unknown event log error';
      this.logger.warn(`Cannot write backup event log: ${message}`);
    }
  }
}
