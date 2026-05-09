import {
  BadRequestException,
  ConflictException,
  Injectable,
  NotFoundException,
} from '@nestjs/common';
import { Prisma } from '@prisma/client';
import { Response } from 'express';
import { promises as fs } from 'fs';
import * as path from 'path';
import { PrismaService } from '../prisma/prisma.service';
import { AssignPcGroupDto } from './dto/assign-pc-group.dto';
import { CreateGroupRateDto } from './dto/create-group-rate.dto';
import { SetClientRuntimeSettingsDto } from './dto/set-client-runtime-settings.dto';
import { SetDefaultRateDto } from './dto/set-default-rate.dto';
import { UpdateGroupRateDto } from './dto/update-group-rate.dto';

const DEFAULT_GROUP_NAME = 'Mặc định';
const DEFAULT_HOURLY_RATE = 5000;
const CLIENT_READY_AUTO_SHUTDOWN_KEY = '__CLIENT_READY_AUTO_SHUTDOWN_MINUTES__';
const DEFAULT_READY_AUTO_SHUTDOWN_MINUTES = 3;
const CLIENT_LOCK_SCREEN_BACKGROUND_MODE_KEY = '__CLIENT_LOCK_SCREEN_BACKGROUND_MODE__';
const CLIENT_LOCK_SCREEN_BACKGROUND_URL_KEY = '__CLIENT_LOCK_SCREEN_BACKGROUND_URL__';
const DEFAULT_LOCK_SCREEN_BACKGROUND_MODE = 'none';
const LOCK_SCREEN_MEDIA_DIR = path.join(
  process.cwd(),
  'storage',
  'lock-screen-media',
);
const MAX_LOCK_SCREEN_MEDIA_SIZE_BYTES = 100 * 1024 * 1024;

@Injectable()
export class PricingService {
  constructor(private readonly prisma: PrismaService) {}

  async getPricingSettings() {
    const defaultGroup = await this.ensureDefaultGroup();
    const groups = await this.prisma.pcGroup.findMany({
      include: {
        _count: {
          select: { pcs: true },
        },
      },
      orderBy: [{ isDefault: 'desc' }, { name: 'asc' }],
    });

    return {
      defaultRatePerHour: Number(defaultGroup.hourlyRate),
      defaultGroupId: defaultGroup.id,
      groups: groups.map((group) => ({
        id: group.id,
        name: group.name,
        hourlyRate: Number(group.hourlyRate),
        isDefault: group.isDefault,
        machineCount: group._count.pcs,
      })),
      serverTime: new Date().toISOString(),
    };
  }

  async getClientRuntimeSettings() {
    const [config, modeSetting, urlSetting, pricingStepSetting, minimumChargeSetting] = await Promise.all([
      this.ensureClientRuntimeSettings(),
      this.prisma.appSetting.findUnique({
        where: { key: CLIENT_LOCK_SCREEN_BACKGROUND_MODE_KEY },
      }),
      this.prisma.appSetting.findUnique({
        where: { key: CLIENT_LOCK_SCREEN_BACKGROUND_URL_KEY },
      }),
      this.prisma.appSetting.findUnique({
        where: { key: 'PRICING_STEP' },
      }),
      this.prisma.appSetting.findUnique({
        where: { key: 'MINIMUM_CHARGE' },
      }),
    ]);

    return {
      readyAutoShutdownMinutes: Math.max(
        1,
        Math.round(
          Number(config.pricePerMinute) || DEFAULT_READY_AUTO_SHUTDOWN_MINUTES,
        ),
      ),
      lockScreenBackgroundMode: this.normalizeLockScreenBackgroundMode(
        modeSetting?.value,
      ),
      lockScreenBackgroundUrl: (urlSetting?.value ?? '').trim(),
      pricingStep: pricingStepSetting ? Number(pricingStepSetting.value) : 1000,
      minimumCharge: minimumChargeSetting ? Number(minimumChargeSetting.value) : 1000,
      serverTime: new Date().toISOString(),
    };
  }

  async setClientRuntimeSettings(payload: SetClientRuntimeSettingsDto) {
    const hasReadyMinutes = payload.readyAutoShutdownMinutes !== undefined;
    const hasLockScreenMode = payload.lockScreenBackgroundMode !== undefined;
    const hasLockScreenUrl = payload.lockScreenBackgroundUrl !== undefined;

    if (!hasReadyMinutes && !hasLockScreenMode && !hasLockScreenUrl) {
      throw new BadRequestException('Khong co du lieu cai dat de cap nhat');
    }

    if (hasReadyMinutes) {
      const minutes = Math.max(
        1,
        Math.round(payload.readyAutoShutdownMinutes as number),
      );
      await this.prisma.pricingConfig.upsert({
        where: { name: CLIENT_READY_AUTO_SHUTDOWN_KEY },
        update: {
          pricePerMinute: minutes,
          isActive: true,
        },
        create: {
          name: CLIENT_READY_AUTO_SHUTDOWN_KEY,
          pricePerMinute: minutes,
          isActive: true,
        },
      });
    }

    if (hasLockScreenMode) {
      const mode = this.normalizeLockScreenBackgroundMode(
        payload.lockScreenBackgroundMode,
      );
      await this.prisma.appSetting.upsert({
        where: { key: CLIENT_LOCK_SCREEN_BACKGROUND_MODE_KEY },
        update: { value: mode },
        create: { key: CLIENT_LOCK_SCREEN_BACKGROUND_MODE_KEY, value: mode },
      });
    }

    if (hasLockScreenUrl) {
      const url = (payload.lockScreenBackgroundUrl ?? '').trim().slice(0, 2048);
      await this.prisma.appSetting.upsert({
        where: { key: CLIENT_LOCK_SCREEN_BACKGROUND_URL_KEY },
        update: { value: url },
        create: { key: CLIENT_LOCK_SCREEN_BACKGROUND_URL_KEY, value: url },
      });
    }

    return this.getClientRuntimeSettings();
  }

  async uploadLockScreenMedia(
    file: any,
    modeRaw: string | undefined,
    apiBaseUrl: string,
  ) {
    const mode = this.normalizeLockScreenBackgroundMode(modeRaw);
    if (mode === 'none') {
      throw new BadRequestException(
        'Mode lock screen khong hop le. Chi chap nhan image/video',
      );
    }

    if (!file?.buffer?.length) {
      throw new BadRequestException('File upload rong');
    }

    if (file.size <= 0 || file.size > MAX_LOCK_SCREEN_MEDIA_SIZE_BYTES) {
      throw new BadRequestException(
        `Kich thuoc file khong hop le (toi da ${Math.floor(MAX_LOCK_SCREEN_MEDIA_SIZE_BYTES / (1024 * 1024))}MB)`,
      );
    }

    const extension = this.resolveLockScreenMediaExtension(
      file.originalname,
      file.mimetype,
      mode,
    );
    if (!extension) {
      throw new BadRequestException(
        'Dinh dang file khong ho tro cho lock screen',
      );
    }

    await fs.mkdir(LOCK_SCREEN_MEDIA_DIR, { recursive: true });

    const suffix = Math.random().toString(36).slice(2, 8);
    const fileName = `${Date.now()}-${suffix}${extension}`;
    const fullPath = path.join(LOCK_SCREEN_MEDIA_DIR, fileName);
    await fs.writeFile(fullPath, file.buffer);

    const mediaUrl = `${apiBaseUrl}/pricing/client-settings/lock-screen-media/${encodeURIComponent(fileName)}`;
    await this.setClientRuntimeSettings({
      lockScreenBackgroundMode: mode,
      lockScreenBackgroundUrl: mediaUrl,
    });

    return this.getClientRuntimeSettings();
  }

  async writeLockScreenMediaToResponse(
    fileNameRaw: string,
    response: Response,
  ) {
    const safeFileName = path.basename(fileNameRaw ?? '').trim();
    if (!safeFileName) {
      throw new NotFoundException('Khong tim thay file');
    }

    const fullPath = path.join(LOCK_SCREEN_MEDIA_DIR, safeFileName);
    try {
      await fs.access(fullPath);
    } catch {
      throw new NotFoundException('Khong tim thay file');
    }

    const extension = path.extname(fullPath).toLowerCase();
    const contentType = this.getLockScreenMediaContentType(extension);
    response.setHeader('Content-Type', contentType);
    response.setHeader('Cache-Control', 'public, max-age=3600');
    return response.sendFile(fullPath);
  }

  async setDefaultRate(payload: SetDefaultRateDto) {
    const hourlyRate = this.roundRate(payload.hourlyRate);
    const defaultGroup = await this.ensureDefaultGroup();

    const updated = await this.prisma.pcGroup.update({
      where: { id: defaultGroup.id },
      data: {
        hourlyRate,
      },
    });

    return {
      id: updated.id,
      name: updated.name,
      hourlyRate: Number(updated.hourlyRate),
      isDefault: updated.isDefault,
    };
  }

  async createGroup(payload: CreateGroupRateDto) {
    const name = payload.name.trim();
    const hourlyRate = this.roundRate(payload.hourlyRate);
    if (!name) {
      throw new BadRequestException('Tên nhóm không hợp lệ');
    }

    try {
      const created = await this.prisma.pcGroup.create({
        data: {
          name,
          hourlyRate,
          isDefault: false,
        },
      });

      return {
        id: created.id,
        name: created.name,
        hourlyRate: Number(created.hourlyRate),
        isDefault: created.isDefault,
      };
    } catch (error) {
      if (
        error instanceof Prisma.PrismaClientKnownRequestError &&
        error.code === 'P2002'
      ) {
        throw new ConflictException('Tên nhóm máy đã tồn tại');
      }

      throw error;
    }
  }

  async updateGroup(groupId: string, payload: UpdateGroupRateDto) {
    const existing = await this.prisma.pcGroup.findUnique({
      where: { id: groupId },
    });
    if (!existing) {
      throw new NotFoundException('Không tìm thấy nhóm máy');
    }

    const data: Prisma.PcGroupUpdateInput = {};
    if (payload.name !== undefined) {
      const trimmed = payload.name.trim();
      if (!trimmed) {
        throw new BadRequestException('Tên nhóm không hợp lệ');
      }

      data.name = trimmed;
    }

    if (payload.hourlyRate !== undefined) {
      data.hourlyRate = this.roundRate(payload.hourlyRate);
    }

    if (Object.keys(data).length === 0) {
      return {
        id: existing.id,
        name: existing.name,
        hourlyRate: Number(existing.hourlyRate),
        isDefault: existing.isDefault,
      };
    }

    try {
      const updated = await this.prisma.pcGroup.update({
        where: { id: groupId },
        data,
      });

      return {
        id: updated.id,
        name: updated.name,
        hourlyRate: Number(updated.hourlyRate),
        isDefault: updated.isDefault,
      };
    } catch (error) {
      if (
        error instanceof Prisma.PrismaClientKnownRequestError &&
        error.code === 'P2002'
      ) {
        throw new ConflictException('Tên nhóm máy đã tồn tại');
      }

      throw error;
    }
  }

  async assignPcToGroup(pcId: string, payload: AssignPcGroupDto) {
    const [pc, group] = await Promise.all([
      this.prisma.pc.findUnique({ where: { id: pcId } }),
      this.prisma.pcGroup.findUnique({ where: { id: payload.groupId } }),
    ]);

    if (!pc) {
      throw new NotFoundException('Không tìm thấy máy trạm');
    }

    if (!group) {
      throw new NotFoundException('Không tìm thấy nhóm máy');
    }

    const updatedPc = await this.prisma.pc.update({
      where: { id: pc.id },
      data: { groupId: group.id },
      include: { group: true },
    });

    return {
      pcId: updatedPc.id,
      agentId: updatedPc.agentId,
      groupId: updatedPc.groupId,
      groupName: updatedPc.group?.name ?? null,
      hourlyRate: updatedPc.group ? Number(updatedPc.group.hourlyRate) : null,
    };
  }

  private roundRate(value: number): number {
    if (!Number.isFinite(value) || value <= 0) {
      throw new BadRequestException('Giá giờ chơi không hợp lệ');
    }

    return Math.round(value * 100) / 100;
  }

  private normalizeLockScreenBackgroundMode(raw?: string | null): string {
    const normalized = (raw ?? '').trim().toLowerCase();
    if (normalized === 'image' || normalized === 'video') {
      return normalized;
    }

    return DEFAULT_LOCK_SCREEN_BACKGROUND_MODE;
  }

  private resolveLockScreenMediaExtension(
    originalName: string,
    mimeType: string | undefined,
    mode: string,
  ): string | null {
    const extFromName = path.extname(originalName ?? '').toLowerCase();
    const extFromMime = this.mapLockScreenMimeTypeToExtension(mimeType);
    const extension = extFromName || extFromMime;
    if (!extension) {
      return null;
    }

    const imageExts = new Set([
      '.jpg',
      '.jpeg',
      '.png',
      '.bmp',
      '.gif',
      '.webp',
    ]);
    const videoExts = new Set([
      '.mp4',
      '.webm',
      '.avi',
      '.mkv',
      '.mov',
      '.wmv',
      '.m4v',
    ]);

    if (mode === 'image' && !imageExts.has(extension)) {
      return null;
    }

    if (mode === 'video' && !videoExts.has(extension)) {
      return null;
    }

    return extension;
  }

  private mapLockScreenMimeTypeToExtension(
    mimeType: string | undefined,
  ): string {
    const normalized = (mimeType ?? '').trim().toLowerCase();
    return (
      {
        'image/jpeg': '.jpg',
        'image/png': '.png',
        'image/bmp': '.bmp',
        'image/gif': '.gif',
        'image/webp': '.webp',
        'video/mp4': '.mp4',
        'video/webm': '.webm',
        'video/x-msvideo': '.avi',
        'video/quicktime': '.mov',
        'video/x-matroska': '.mkv',
        'video/x-ms-wmv': '.wmv',
      }[normalized] ?? ''
    );
  }

  private getLockScreenMediaContentType(extension: string): string {
    return (
      {
        '.jpg': 'image/jpeg',
        '.jpeg': 'image/jpeg',
        '.png': 'image/png',
        '.bmp': 'image/bmp',
        '.gif': 'image/gif',
        '.webp': 'image/webp',
        '.mp4': 'video/mp4',
        '.webm': 'video/webm',
        '.avi': 'video/x-msvideo',
        '.mkv': 'video/x-matroska',
        '.mov': 'video/quicktime',
        '.wmv': 'video/x-ms-wmv',
        '.m4v': 'video/mp4',
      }[extension] ?? 'application/octet-stream'
    );
  }

  private async ensureClientRuntimeSettings() {
    return this.prisma.pricingConfig.upsert({
      where: { name: CLIENT_READY_AUTO_SHUTDOWN_KEY },
      update: {},
      create: {
        name: CLIENT_READY_AUTO_SHUTDOWN_KEY,
        pricePerMinute: DEFAULT_READY_AUTO_SHUTDOWN_MINUTES,
        isActive: true,
      },
    });
  }

  private async ensureDefaultGroup() {
    const existingDefault = await this.prisma.pcGroup.findFirst({
      where: { isDefault: true },
      orderBy: { updatedAt: 'desc' },
    });
    if (existingDefault) {
      return existingDefault;
    }

    const sameNameGroup = await this.prisma.pcGroup.findFirst({
      where: { name: DEFAULT_GROUP_NAME },
    });

    if (sameNameGroup) {
      return this.prisma.$transaction(async (tx) => {
        await tx.pcGroup.updateMany({
          where: { isDefault: true },
          data: { isDefault: false },
        });

        return tx.pcGroup.update({
          where: { id: sameNameGroup.id },
          data: { isDefault: true },
        });
      });
    }

    return this.prisma.pcGroup.create({
      data: {
        name: DEFAULT_GROUP_NAME,
        hourlyRate: DEFAULT_HOURLY_RATE,
        isDefault: true,
      },
    });
  }

  async getPromotions() {
    const list = await this.prisma.timeBasedPromotion.findMany({
      orderBy: { createdAt: 'asc' },
    });

    if (list.length === 0) {
      const seeded = await this.prisma.timeBasedPromotion.create({
        data: {
          name: 'Khuyến mãi ngày thường (Giảm 10% T2 - T6)',
          daysOfWeek: [1, 2, 3, 4, 5],
          startTime: '08:00',
          endTime: '16:00',
          discountPercent: 10,
          isActive: true,
        },
      });
      return [seeded];
    }

    return list;
  }

  async createPromotion(payload: any) {
    const name = (payload.name ?? '').trim();
    if (!name) {
      throw new BadRequestException('Tên chương trình khuyến mãi không hợp lệ');
    }

    return this.prisma.timeBasedPromotion.create({
      data: {
        name,
        daysOfWeek: Array.isArray(payload.daysOfWeek) ? payload.daysOfWeek : [1, 2, 3, 4, 5],
        startTime: payload.startTime || '08:00',
        endTime: payload.endTime || '16:00',
        discountPercent: Number(payload.discountPercent) || 0,
        isActive: payload.isActive !== undefined ? Boolean(payload.isActive) : true,
      },
    });
  }

  async updatePromotion(id: string, payload: any) {
    const existing = await this.prisma.timeBasedPromotion.findUnique({
      where: { id },
    });
    if (!existing) {
      throw new NotFoundException('Không tìm thấy chương trình khuyến mãi');
    }

    const data: any = {};
    if (payload.name !== undefined) data.name = payload.name.trim();
    if (payload.daysOfWeek !== undefined) data.daysOfWeek = payload.daysOfWeek;
    if (payload.startTime !== undefined) data.startTime = payload.startTime;
    if (payload.endTime !== undefined) data.endTime = payload.endTime;
    if (payload.discountPercent !== undefined) data.discountPercent = Number(payload.discountPercent);
    if (payload.isActive !== undefined) data.isActive = Boolean(payload.isActive);

    return this.prisma.timeBasedPromotion.update({
      where: { id },
      data,
    });
  }

  async deletePromotion(id: string) {
    const existing = await this.prisma.timeBasedPromotion.findUnique({
      where: { id },
    });
    if (!existing) {
      throw new NotFoundException('Không tìm thấy chương trình khuyến mãi');
    }

    await this.prisma.timeBasedPromotion.delete({
      where: { id },
    });

    return { success: true };
  }
}
