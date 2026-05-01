import {
  BadRequestException,
  ConflictException,
  Injectable,
  NotFoundException,
} from '@nestjs/common';
import { EventSource, Prisma } from '@prisma/client';
import { PrismaService } from '../prisma/prisma.service';
import { CreatePcServiceOrderDto } from './dto/create-pc-service-order.dto';
import { CreateServiceItemDto } from './dto/create-service-item.dto';
import { UpdateServiceItemDto } from './dto/update-service-item.dto';

type GetServiceItemsOptions = {
  includeInactive?: boolean;
};

@Injectable()
export class ServicesService {
  constructor(private readonly prisma: PrismaService) {}

  async getServiceItems(options?: GetServiceItemsOptions) {
    const includeInactive = options?.includeInactive ?? false;
    const items = await this.prisma.serviceItem.findMany({
      where: includeInactive ? undefined : { isActive: true },
      orderBy: [{ isActive: 'desc' }, { name: 'asc' }],
    });

    return {
      items: items.map((item) => this.toServiceItem(item)),
      total: items.length,
      serverTime: new Date().toISOString(),
    };
  }

  async createServiceItem(payload: CreateServiceItemDto) {
    const name = payload.name.trim();
    if (!name) {
      throw new BadRequestException('Ten dich vu khong hop le');
    }

    try {
      const created = await this.prisma.serviceItem.create({
        data: {
          name,
          category: this.normalizeOptionalText(payload.category),
          unitPrice: this.roundMoney(payload.unitPrice),
          isActive: payload.isActive ?? true,
        },
      });

      return this.toServiceItem(created);
    } catch (error) {
      if (
        error instanceof Prisma.PrismaClientKnownRequestError &&
        error.code === 'P2002'
      ) {
        throw new ConflictException('Ten dich vu da ton tai');
      }

      throw error;
    }
  }

  async updateServiceItem(serviceItemId: string, payload: UpdateServiceItemDto) {
    const existing = await this.prisma.serviceItem.findUnique({
      where: { id: serviceItemId },
    });

    if (!existing) {
      throw new NotFoundException('Khong tim thay dich vu');
    }

    const data: Prisma.ServiceItemUpdateInput = {};
    if (payload.name !== undefined) {
      const name = payload.name.trim();
      if (!name) {
        throw new BadRequestException('Ten dich vu khong hop le');
      }

      data.name = name;
    }

    if (payload.category !== undefined) {
      data.category = this.normalizeOptionalText(payload.category);
    }

    if (payload.unitPrice !== undefined) {
      data.unitPrice = this.roundMoney(payload.unitPrice);
    }

    if (payload.isActive !== undefined) {
      data.isActive = payload.isActive;
    }

    if (Object.keys(data).length === 0) {
      return this.toServiceItem(existing);
    }

    try {
      const updated = await this.prisma.serviceItem.update({
        where: { id: serviceItemId },
        data,
      });

      return this.toServiceItem(updated);
    } catch (error) {
      if (
        error instanceof Prisma.PrismaClientKnownRequestError &&
        error.code === 'P2002'
      ) {
        throw new ConflictException('Ten dich vu da ton tai');
      }

      throw error;
    }
  }

  async createPcServiceOrder(pcId: string, payload: CreatePcServiceOrderDto) {
    const quantity = payload.quantity ?? 1;
    const note = this.normalizeOptionalText(payload.note);
    const createdBy = payload.requestedBy?.trim() || 'admin.desktop';

    return this.prisma.$transaction(async (tx) => {
      const [pc, serviceItem, activeSession] = await Promise.all([
        tx.pc.findUnique({ where: { id: pcId } }),
        tx.serviceItem.findUnique({ where: { id: payload.serviceItemId } }),
        tx.session.findFirst({
          where: { pcId, status: 'ACTIVE' },
          orderBy: { startedAt: 'desc' },
        }),
      ]);

      if (!pc) {
        throw new NotFoundException('Khong tim thay may tram');
      }

      if (!serviceItem) {
        throw new NotFoundException('Khong tim thay dich vu');
      }

      if (!serviceItem.isActive) {
        throw new BadRequestException('Dich vu dang tam ngung');
      }

      const unitPrice = this.roundMoney(Number(serviceItem.unitPrice));
      const lineTotal = this.roundMoney(unitPrice * quantity);

      const order = await tx.pcServiceOrder.create({
        data: {
          pcId: pc.id,
          sessionId: activeSession?.id ?? null,
          serviceItemId: serviceItem.id,
          quantity,
          unitPrice,
          lineTotal,
          note,
          createdBy,
        },
        include: {
          serviceItem: true,
        },
      });

      try {
        await tx.eventLog.create({
          data: {
            source: EventSource.ADMIN,
            eventType: 'service.order.created',
            pcId: pc.id,
            payload: {
              orderId: order.id,
              serviceItemId: serviceItem.id,
              serviceName: serviceItem.name,
              quantity,
              lineTotal,
              createdBy,
            },
          },
        });
      } catch {
        // Ignore audit logging failures.
      }

      return this.toPcServiceOrder(order);
    });
  }

  async getPcServiceOrders(pcId: string, rawLimit?: number) {
    const limit = this.normalizeLimit(rawLimit);
    const pc = await this.prisma.pc.findUnique({ where: { id: pcId } });
    if (!pc) {
      throw new NotFoundException('Khong tim thay may tram');
    }

    const orders = await this.prisma.pcServiceOrder.findMany({
      where: { pcId },
      include: {
        serviceItem: true,
      },
      orderBy: [{ createdAt: 'desc' }],
      take: limit,
    });

    return {
      pcId,
      items: orders.map((item) => this.toPcServiceOrder(item)),
      total: orders.length,
      serverTime: new Date().toISOString(),
    };
  }

  private toServiceItem(item: {
    id: string;
    name: string;
    category: string | null;
    unitPrice: Prisma.Decimal;
    isActive: boolean;
    createdAt: Date;
    updatedAt: Date;
  }) {
    return {
      id: item.id,
      name: item.name,
      category: item.category,
      unitPrice: Number(item.unitPrice),
      isActive: item.isActive,
      createdAt: item.createdAt.toISOString(),
      updatedAt: item.updatedAt.toISOString(),
    };
  }

  private toPcServiceOrder(item: {
    id: string;
    pcId: string;
    sessionId: string | null;
    serviceItemId: string;
    quantity: number;
    unitPrice: Prisma.Decimal;
    lineTotal: Prisma.Decimal;
    note: string | null;
    createdBy: string;
    createdAt: Date;
    serviceItem: {
      id: string;
      name: string;
      category: string | null;
      unitPrice: Prisma.Decimal;
      isActive: boolean;
      createdAt: Date;
      updatedAt: Date;
    };
  }) {
    return {
      id: item.id,
      pcId: item.pcId,
      sessionId: item.sessionId,
      serviceItemId: item.serviceItemId,
      quantity: item.quantity,
      unitPrice: Number(item.unitPrice),
      lineTotal: Number(item.lineTotal),
      note: item.note,
      createdBy: item.createdBy,
      createdAt: item.createdAt.toISOString(),
      serviceItem: this.toServiceItem(item.serviceItem),
    };
  }

  private roundMoney(value: number): number {
    if (!Number.isFinite(value) || value <= 0) {
      throw new BadRequestException('Gia tri tien khong hop le');
    }

    return Math.round(value * 100) / 100;
  }

  private normalizeOptionalText(value?: string | null): string | null {
    const trimmed = value?.trim();
    return trimmed ? trimmed : null;
  }

  private normalizeLimit(rawLimit?: number): number {
    if (!Number.isFinite(rawLimit ?? NaN)) {
      return 50;
    }

    return Math.min(200, Math.max(1, Math.floor(rawLimit!)));
  }
}

