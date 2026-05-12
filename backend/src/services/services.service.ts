import {
  BadRequestException,
  ConflictException,
  Injectable,
  NotFoundException,
} from '@nestjs/common';
import { EventSource, Prisma } from '@prisma/client';
import { PrismaService } from '../prisma/prisma.service';
import { CancelPcServiceOrdersDto } from './dto/cancel-pc-service-orders.dto';
import { CreatePcServiceOrderDto } from './dto/create-pc-service-order.dto';
import { CreateServiceItemDto } from './dto/create-service-item.dto';
import { PayPcServiceOrdersDto } from './dto/pay-pc-service-orders.dto';
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
    const paidOrderIds = await this.getPaidOrderIdsForPc(pcId);

    return {
      pcId,
      items: orders.map((item) =>
        this.toPcServiceOrder(item, paidOrderIds.has(item.id)),
      ),
      total: orders.length,
      serverTime: new Date().toISOString(),
    };
  }

  async cancelPcServiceOrders(pcId: string, payload: CancelPcServiceOrdersDto) {
    const requestedBy = payload.requestedBy?.trim() || 'admin.desktop';
    const note = this.normalizeOptionalText(payload.note);
    const isAdminRequester = this.isAdminServiceRequester(requestedBy);
    const cancelByOrderIds = (payload.orderIds ?? []).length > 0;
    const cancelByServiceQuantity =
      !!payload.serviceItemId && Number.isInteger(payload.quantity);

    if (!cancelByOrderIds && !cancelByServiceQuantity) {
      throw new BadRequestException(
        'Can cung cap orderIds hoac serviceItemId + quantity de huy',
      );
    }

    if (
      !cancelByOrderIds &&
      (!!payload.orderIds?.length ||
        payload.serviceItemId === undefined ||
        payload.quantity === undefined)
    ) {
      throw new BadRequestException('Yeu cau huy dich vu khong hop le');
    }

    return this.prisma.$transaction(async (tx) => {
      const [pc, orders, paidEvents] = await Promise.all([
        tx.pc.findUnique({ where: { id: pcId } }),
        tx.pcServiceOrder.findMany({
          where: {
            pcId,
            sessionId: payload.sessionId ?? undefined,
          },
          orderBy: [{ createdAt: 'desc' }],
        }),
        tx.eventLog.findMany({
          where: { pcId, eventType: 'service.order.paid' },
          select: { payload: true },
          orderBy: [{ createdAt: 'desc' }],
          take: 500,
        }),
      ]);

      if (!pc) {
        throw new NotFoundException('Khong tim thay may tram');
      }

      const paidOrderIds = this.extractPaidOrderIdsFromEvents(paidEvents);
      const unpaidOrders = orders.filter((item) => !paidOrderIds.has(item.id));
      const cancelableOrders = isAdminRequester
        ? unpaidOrders
        : unpaidOrders.filter((item) =>
            this.isServiceOrderOwnedByRequester(item.createdBy, requestedBy),
          );

      const canceledOrderIds: string[] = [];
      const updatedOrders: Array<{
        orderId: string;
        previousQuantity: number;
        nextQuantity: number;
        canceledQuantity: number;
        canceledAmount: number;
      }> = [];
      let canceledQuantity = 0;
      let canceledAmount = 0;

      if (cancelByOrderIds) {
        const orderIds = payload.orderIds ?? [];
        const requestedOrderIdSet = new Set(orderIds);
        const targetOrders = cancelableOrders.filter((item) =>
          requestedOrderIdSet.has(item.id),
        );

        if (targetOrders.length !== requestedOrderIdSet.size) {
          throw new BadRequestException(
            isAdminRequester
              ? 'Mot so don da thanh toan hoac khong ton tai'
              : 'Chi duoc huy dich vu do chinh may tram da goi',
          );
        }

        for (const order of targetOrders) {
          await tx.pcServiceOrder.delete({
            where: { id: order.id },
          });

          canceledOrderIds.push(order.id);
          canceledQuantity += Math.max(0, order.quantity);
          canceledAmount += Math.max(0, Number(order.lineTotal ?? 0));
        }
      } else {
        const serviceItemId = payload.serviceItemId!;
        let remainingToCancel = payload.quantity!;

        const sameServiceOrders = cancelableOrders.filter(
          (item) => item.serviceItemId === serviceItemId,
        );
        if (sameServiceOrders.length === 0) {
          throw new BadRequestException(
            isAdminRequester
              ? 'Khong co dich vu chua thanh toan de huy'
              : 'Khong co dich vu do may tram tu goi de huy',
          );
        }

        for (const order of sameServiceOrders) {
          if (remainingToCancel <= 0) {
            break;
          }

          const orderQuantity = Math.max(0, order.quantity);
          if (orderQuantity <= 0) {
            continue;
          }

          if (orderQuantity <= remainingToCancel) {
            await tx.pcServiceOrder.delete({
              where: { id: order.id },
            });

            canceledOrderIds.push(order.id);
            canceledQuantity += orderQuantity;
            canceledAmount += Math.max(0, Number(order.lineTotal ?? 0));
            remainingToCancel -= orderQuantity;
            continue;
          }

          const canceledFromThisOrder = remainingToCancel;
          const nextQuantity = orderQuantity - canceledFromThisOrder;
          const unitPrice = Number(order.unitPrice ?? 0);
          const nextLineTotal = this.roundMoney(unitPrice * nextQuantity);
          const canceledLineTotal = this.roundMoney(
            unitPrice * canceledFromThisOrder,
          );

          await tx.pcServiceOrder.update({
            where: { id: order.id },
            data: {
              quantity: nextQuantity,
              lineTotal: nextLineTotal,
            },
          });

          updatedOrders.push({
            orderId: order.id,
            previousQuantity: orderQuantity,
            nextQuantity,
            canceledQuantity: canceledFromThisOrder,
            canceledAmount: canceledLineTotal,
          });

          canceledQuantity += canceledFromThisOrder;
          canceledAmount += canceledLineTotal;
          remainingToCancel = 0;
        }

        if (remainingToCancel > 0) {
          throw new BadRequestException(
            isAdminRequester
              ? 'So luong huy vuot qua so luong da goi chua thanh toan'
              : 'So luong huy vuot qua so luong may tram da tu goi',
          );
        }
      }

      canceledAmount = Math.round(canceledAmount * 100) / 100;
      const remainingOrders = await tx.pcServiceOrder.findMany({
        where: {
          pcId,
          sessionId: payload.sessionId ?? undefined,
        },
        select: {
          id: true,
          lineTotal: true,
        },
      });
      const unpaidAmount = remainingOrders.reduce((sum, item) => {
        if (paidOrderIds.has(item.id)) {
          return sum;
        }

        return sum + Math.max(0, Number(item.lineTotal ?? 0));
      }, 0);

      try {
        await tx.eventLog.create({
          data: {
            source: EventSource.ADMIN,
            eventType: 'service.order.canceled',
            pcId: pc.id,
            payload: {
              sessionId: payload.sessionId ?? null,
              canceledOrderIds,
              updatedOrders,
              canceledQuantity,
              canceledAmount,
              requestedBy,
              note,
            },
          },
        });
      } catch {
        // Ignore audit logging failures.
      }

      return {
        pcId,
        sessionId: payload.sessionId ?? null,
        canceledOrderCount: canceledOrderIds.length,
        canceledQuantity,
        canceledAmount,
        unpaidAmount: Math.round(unpaidAmount * 100) / 100,
        serverTime: new Date().toISOString(),
      };
    });
  }

  async payPcServiceOrders(pcId: string, payload: PayPcServiceOrdersDto) {
    const requestedBy = payload.requestedBy?.trim() || 'admin.desktop';
    const note = this.normalizeOptionalText(payload.note);

    return this.prisma.$transaction(async (tx) => {
      const [pc, activeSession] = await Promise.all([
        tx.pc.findUnique({ where: { id: pcId } }),
        tx.session.findFirst({
          where: { pcId, status: 'ACTIVE' },
          orderBy: { startedAt: 'desc' },
        }),
      ]);

      if (!pc) {
        throw new NotFoundException('Khong tim thay may tram');
      }

      if (!activeSession) {
        throw new BadRequestException(
          'May chua co phien dang su dung de thanh toan dich vu',
        );
      }

      const [orders, paidEvents] = await Promise.all([
        tx.pcServiceOrder.findMany({
          where: { pcId, sessionId: activeSession.id },
          orderBy: [{ createdAt: 'asc' }],
        }),
        tx.eventLog.findMany({
          where: { pcId, eventType: 'service.order.paid' },
          select: { payload: true },
          orderBy: [{ createdAt: 'desc' }],
          take: 500,
        }),
      ]);

      if (orders.length === 0) {
        return {
          pcId,
          sessionId: activeSession.id,
          paidOrderCount: 0,
          paidAmount: 0,
          unpaidAmount: 0,
          serverTime: new Date().toISOString(),
        };
      }

      const paidOrderIds = this.extractPaidOrderIdsFromEvents(paidEvents);
      const unpaidOrders = orders.filter((item) => !paidOrderIds.has(item.id));
      const totalUnpaidAmount = unpaidOrders.reduce(
        (sum, item) => sum + Number(item.lineTotal ?? 0),
        0,
      );

      const selectedOrderIds = payload.orderIds ?? [];
      const shouldPaySelectedOnly = selectedOrderIds.length > 0;
      const selectedOrderIdSet = new Set(selectedOrderIds);
      const targetOrders = shouldPaySelectedOnly
        ? unpaidOrders.filter((item) => selectedOrderIdSet.has(item.id))
        : unpaidOrders;
      const paidAmount = targetOrders.reduce(
        (sum, item) => sum + Number(item.lineTotal ?? 0),
        0,
      );
      const remainingUnpaidAmount = Math.max(0, totalUnpaidAmount - paidAmount);

      if (targetOrders.length === 0 || paidAmount <= 0) {
        return {
          pcId,
          sessionId: activeSession.id,
          paidOrderCount: 0,
          paidAmount: 0,
          unpaidAmount: totalUnpaidAmount,
          serverTime: new Date().toISOString(),
        };
      }

      await tx.eventLog.create({
        data: {
          source: EventSource.ADMIN,
          eventType: 'service.order.paid',
          pcId,
          payload: {
            sessionId: activeSession.id,
            orderIds: targetOrders.map((x) => x.id),
            paidOrderCount: targetOrders.length,
            paidAmount,
            requestedBy,
            note,
          },
        },
      });

      return {
        pcId,
        sessionId: activeSession.id,
        paidOrderCount: targetOrders.length,
        paidAmount,
        unpaidAmount: remainingUnpaidAmount,
        serverTime: new Date().toISOString(),
      };
    });
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
  }, isPaid = false) {
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
      isPaid,
      serviceItem: this.toServiceItem(item.serviceItem),
    };
  }

  private async getPaidOrderIdsForPc(pcId: string): Promise<Set<string>> {
    const events = await this.prisma.eventLog.findMany({
      where: { pcId, eventType: 'service.order.paid' },
      select: { payload: true },
      orderBy: [{ createdAt: 'desc' }],
      take: 500,
    });
    return this.extractPaidOrderIdsFromEvents(events);
  }

  private extractPaidOrderIdsFromEvents(
    events: Array<{ payload: Prisma.JsonValue | null }>,
  ): Set<string> {
    const paidIds = new Set<string>();
    for (const event of events) {
      const payload = this.readPayloadObject(event.payload);
      if (!payload) {
        continue;
      }

      const orderIdsRaw = payload.orderIds;
      if (!Array.isArray(orderIdsRaw)) {
        continue;
      }

      for (const orderId of orderIdsRaw) {
        if (typeof orderId !== 'string') {
          continue;
        }

        const normalized = orderId.trim();
        if (!normalized) {
          continue;
        }

        paidIds.add(normalized);
      }
    }

    return paidIds;
  }

  private readPayloadObject(
    payload: Prisma.JsonValue | null | undefined,
  ): Record<string, unknown> | null {
    if (!payload || typeof payload !== 'object' || Array.isArray(payload)) {
      return null;
    }

    return payload as Record<string, unknown>;
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

  private isAdminServiceRequester(requestedBy: string): boolean {
    const normalized = requestedBy.trim().toLowerCase();
    return normalized === 'admin' || normalized.startsWith('admin.');
  }

  private isServiceOrderOwnedByRequester(
    createdBy: string | null | undefined,
    requestedBy: string,
  ): boolean {
    const normalizedCreatedBy = createdBy?.trim().toLowerCase();
    const normalizedRequestedBy = requestedBy.trim().toLowerCase();

    if (!normalizedCreatedBy || !normalizedRequestedBy) {
      return false;
    }

    return normalizedCreatedBy === normalizedRequestedBy;
  }
}
