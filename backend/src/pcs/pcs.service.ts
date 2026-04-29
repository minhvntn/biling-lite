import { Injectable } from '@nestjs/common';
import { EventSource, Pc, PcStatus, Prisma } from '@prisma/client';
import { PrismaService } from '../prisma/prisma.service';
import { AgentHeartbeatPayload, AgentHelloPayload } from './types/agent-events.type';

type PresencePayload = AgentHelloPayload | AgentHeartbeatPayload;

export type PresenceTransition = {
  pc: Pc;
  previousStatus: PcStatus | null;
};

export type PcListItem = {
  id: string;
  agentId: string;
  name: string;
  hostname: string | null;
  ipAddress: string | null;
  status: PcStatus;
  lastSeenAt: string | null;
  activeSession: {
    id: string;
    startedAt: string;
    elapsedSeconds: number;
    billableMinutes: number;
    estimatedAmount: number;
  } | null;
};

@Injectable()
export class PcsService {
  constructor(private readonly prisma: PrismaService) {}

  async getPcList(): Promise<PcListItem[]> {
    const activePricing = await this.prisma.pricingConfig.findFirst({
      where: { isActive: true },
      orderBy: { updatedAt: 'desc' },
    });
    const pricePerMinute = Number(activePricing?.pricePerMinute ?? 0);

    const pcs = await this.prisma.pc.findMany({
      orderBy: [{ name: 'asc' }],
      include: {
        sessions: {
          where: { status: 'ACTIVE' },
          orderBy: { startedAt: 'desc' },
          take: 1,
        },
      },
    });

    const now = Date.now();
    return pcs.map((pc) => {
      const activeSession = pc.sessions[0] ?? null;
      const elapsedSeconds = activeSession
        ? Math.max(0, Math.floor((now - activeSession.startedAt.getTime()) / 1000))
        : 0;
      const billableMinutes = activeSession ? Math.max(1, Math.ceil(elapsedSeconds / 60)) : 0;
      const estimatedAmount = billableMinutes * pricePerMinute;

      return {
        id: pc.id,
        agentId: pc.agentId,
        name: pc.name,
        hostname: pc.hostname,
        ipAddress: pc.ipAddress,
        status: pc.status,
        lastSeenAt: pc.lastSeenAt?.toISOString() ?? null,
        activeSession: activeSession
          ? {
              id: activeSession.id,
              startedAt: activeSession.startedAt.toISOString(),
              elapsedSeconds,
              billableMinutes,
              estimatedAmount,
            }
          : null,
      };
    });
  }

  async registerPresence(
    payload: PresencePayload,
    fallbackIp?: string,
  ): Promise<PresenceTransition> {
    const agentId = payload.agentId?.trim();
    if (!agentId) {
      throw new Error('agentId is required');
    }

    const seenAt = this.parseSeenAt(payload.at);
    const ipAddress = this.resolveIp(payload.ip, fallbackIp);
    const hostname = this.parseOptional(payload.hostname);
    const existing = await this.prisma.pc.findUnique({ where: { agentId } });

    if (!existing) {
      const createdPc = await this.prisma.pc.create({
        data: {
          agentId,
          name: hostname ?? agentId,
          hostname,
          ipAddress,
          status: PcStatus.ONLINE,
          lastSeenAt: seenAt,
        },
      });

      await this.logEvent('pc.registered', createdPc.id, {
        agentId: createdPc.agentId,
        status: createdPc.status,
      });
      return { pc: createdPc, previousStatus: null };
    }

    const nextStatus =
      existing.status === PcStatus.OFFLINE ? PcStatus.ONLINE : existing.status;

    const updatedPc = await this.prisma.pc.update({
      where: { id: existing.id },
      data: {
        hostname: hostname ?? existing.hostname,
        ipAddress: ipAddress ?? existing.ipAddress,
        status: nextStatus,
        lastSeenAt: seenAt,
      },
    });

    if (existing.status !== updatedPc.status) {
      await this.logEvent('pc.status.changed', updatedPc.id, {
        previousStatus: existing.status,
        status: updatedPc.status,
        sourceEvent: 'presence',
      });
    }
    return { pc: updatedPc, previousStatus: existing.status };
  }

  async markStaleAgentsOffline(
    timeoutSeconds: number,
  ): Promise<PresenceTransition[]> {
    const cutoff = new Date(Date.now() - timeoutSeconds * 1000);
    const staleAgents = await this.prisma.pc.findMany({
      where: {
        status: { not: PcStatus.OFFLINE },
        OR: [{ lastSeenAt: null }, { lastSeenAt: { lt: cutoff } }],
      },
    });

    if (staleAgents.length === 0) {
      return [];
    }

    const staleIds = staleAgents.map((pc) => pc.id);
    await this.prisma.pc.updateMany({
      where: { id: { in: staleIds } },
      data: { status: PcStatus.OFFLINE },
    });

    const updatedAgents = await this.prisma.pc.findMany({
      where: { id: { in: staleIds } },
    });
    const updatedById = new Map(updatedAgents.map((pc) => [pc.id, pc]));

    const transitions = staleAgents
      .map((stalePc) => {
        const updatedPc = updatedById.get(stalePc.id);
        if (!updatedPc) {
          return null;
        }

        return {
          pc: updatedPc,
          previousStatus: stalePc.status,
        };
      })
      .filter((item): item is PresenceTransition => item !== null);

    await Promise.all(
      transitions.map((transition) =>
        this.logEvent('pc.status.changed', transition.pc.id, {
          previousStatus: transition.previousStatus,
          status: transition.pc.status,
          sourceEvent: 'presence.timeout',
        }),
      ),
    );

    return transitions;
  }

  private parseSeenAt(at?: string): Date {
    if (!at) {
      return new Date();
    }

    const parsed = new Date(at);
    return Number.isNaN(parsed.getTime()) ? new Date() : parsed;
  }

  private resolveIp(rawIp?: string, fallbackIp?: string): string | null {
    return this.parseOptional(rawIp) ?? this.parseOptional(fallbackIp);
  }

  private parseOptional(value?: string): string | null {
    const trimmed = value?.trim();
    return trimmed ? trimmed : null;
  }

  private async logEvent(
    eventType: string,
    pcId?: string,
    payload?: Prisma.InputJsonValue,
  ): Promise<void> {
    try {
      await this.prisma.eventLog.create({
        data: {
          source: EventSource.SERVER,
          eventType,
          pcId,
          payload,
        },
      });
    } catch {
      // Ignore audit log failures.
    }
  }
}
