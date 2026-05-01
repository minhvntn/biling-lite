import {
  BadRequestException,
  ConflictException,
  Injectable,
  NotFoundException,
} from '@nestjs/common';
import { Prisma } from '@prisma/client';
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
    const config = await this.ensureClientRuntimeSettings();
    return {
      readyAutoShutdownMinutes: Math.max(
        1,
        Math.round(
          Number(config.pricePerMinute) || DEFAULT_READY_AUTO_SHUTDOWN_MINUTES,
        ),
      ),
      serverTime: new Date().toISOString(),
    };
  }

  async setClientRuntimeSettings(payload: SetClientRuntimeSettingsDto) {
    const minutes = Math.max(1, Math.round(payload.readyAutoShutdownMinutes));
    const config = await this.prisma.pricingConfig.upsert({
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

    return {
      readyAutoShutdownMinutes: Math.max(
        1,
        Math.round(
          Number(config.pricePerMinute) || DEFAULT_READY_AUTO_SHUTDOWN_MINUTES,
        ),
      ),
      serverTime: new Date().toISOString(),
    };
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
}
