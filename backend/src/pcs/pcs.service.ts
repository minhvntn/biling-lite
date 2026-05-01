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
  groupId: string | null;
  groupName: string;
  hourlyRate: number;
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
  activeMember: {
    memberId: string;
    username: string;
    fullName: string;
  } | null;
  activeGuest: {
    displayName: string;
    prepaidAmount: number;
  } | null;
};

@Injectable()
export class PcsService {
  constructor(private readonly prisma: PrismaService) {}

  async getPcList(): Promise<PcListItem[]> {
    const defaultGroup = await this.ensureDefaultGroup();

    const pcs = await this.prisma.pc.findMany({
      orderBy: [{ name: 'asc' }],
      include: {
        group: true,
        sessions: {
          where: { status: 'ACTIVE' },
          orderBy: { startedAt: 'desc' },
          take: 1,
        },
      },
    });

    const activeUsersByPc = await this.resolveActiveUsersByPcId(
      pcs.map((pc) => pc.id),
    );

    const now = Date.now();
    return pcs.map((pc) => {
      const effectiveGroup = pc.group ?? defaultGroup;
      const activeSession = pc.sessions[0] ?? null;
      const activeUser = pc.status === PcStatus.IN_USE ? activeUsersByPc.get(pc.id) : null;
      const activeMember = activeUser?.kind === 'MEMBER' ? activeUser.member : null;
      const activeGuest = activeUser?.kind === 'GUEST' ? activeUser.guest : null;
      const elapsedSeconds = activeSession
        ? Math.max(0, Math.floor((now - activeSession.startedAt.getTime()) / 1000))
        : 0;
      const billableMinutes = activeSession ? Math.max(1, Math.ceil(elapsedSeconds / 60)) : 0;
      const estimatedAmount =
        billableMinutes * Number(activeSession?.pricePerMinute ?? 0);

      return {
        id: pc.id,
        agentId: pc.agentId,
        name: pc.name,
        groupId: effectiveGroup.id,
        groupName: effectiveGroup.name,
        hourlyRate: Number(effectiveGroup.hourlyRate),
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
        activeMember,
        activeGuest,
      };
    });
  }

  async registerPresence(
    payload: PresencePayload,
    fallbackIp?: string,
  ): Promise<PresenceTransition> {
    const defaultGroup = await this.ensureDefaultGroup();
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
          groupId: defaultGroup.id,
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
        groupId: existing.groupId ?? defaultGroup.id,
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

  private async resolveActiveUsersByPcId(
    pcIds: string[],
  ): Promise<
    Map<
      string,
      | { kind: 'MEMBER'; member: { memberId: string; username: string; fullName: string } }
      | { kind: 'GUEST'; guest: { displayName: string; prepaidAmount: number } }
    >
  > {
    const result = new Map<
      string,
      | { kind: 'MEMBER'; member: { memberId: string; username: string; fullName: string } }
      | { kind: 'GUEST'; guest: { displayName: string; prepaidAmount: number } }
    >();
    if (pcIds.length === 0) {
      return result;
    }

    await Promise.all(
      pcIds.map(async (pcId) => {
        const latestPresence = await this.prisma.eventLog.findFirst({
          where: {
            pcId,
            eventType: {
              in: ['member.pc.presence', 'guest.pc.presence'],
            },
          },
          orderBy: {
            createdAt: 'desc',
          },
        });

        if (latestPresence?.eventType === 'member.pc.presence') {
          const member = this.parseMemberPresencePayload(latestPresence.payload);
          if (member) {
            result.set(pcId, {
              kind: 'MEMBER',
              member,
            });
          }
          return;
        }

        if (latestPresence?.eventType === 'guest.pc.presence') {
          const guest = this.parseGuestPresencePayload(latestPresence.payload);
          if (guest) {
            result.set(pcId, {
              kind: 'GUEST',
              guest,
            });
          }
        }
      }),
    );

    return result;
  }

  private parseMemberPresencePayload(
    payload?: Prisma.JsonValue | null,
  ): { memberId: string; username: string; fullName: string } | null {
    if (!payload || typeof payload !== 'object' || Array.isArray(payload)) {
      return null;
    }

    const value = payload as Record<string, unknown>;
    if (value.isActive !== true) {
      return null;
    }

    const memberId = this.readString(value.memberId);
    const username = this.readString(value.username);
    const fullName = this.readString(value.fullName) || username;

    if (!memberId || !username) {
      return null;
    }

    return {
      memberId,
      username,
      fullName,
    };
  }

  private parseGuestPresencePayload(
    payload?: Prisma.JsonValue | null,
  ): { displayName: string; prepaidAmount: number } | null {
    if (!payload || typeof payload !== 'object' || Array.isArray(payload)) {
      return null;
    }

    const value = payload as Record<string, unknown>;
    if (value.isActive !== true) {
      return null;
    }

    const displayName = this.readString(value.displayName) || 'Khách vãng lai';
    const prepaidAmount = this.readNumber(value.prepaidAmount);

    return {
      displayName,
      prepaidAmount,
    };
  }

  private readString(value: unknown): string {
    if (typeof value !== 'string') {
      return '';
    }

    return value.trim();
  }

  private readNumber(value: unknown): number {
    if (typeof value === 'number' && Number.isFinite(value)) {
      return value;
    }

    if (typeof value === 'string') {
      const parsed = Number(value);
      if (Number.isFinite(parsed)) {
        return parsed;
      }
    }

    return 0;
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

  private async ensureDefaultGroup() {
    const existingDefault = await this.prisma.pcGroup.findFirst({
      where: { isDefault: true },
      orderBy: { updatedAt: 'desc' },
    });
    if (existingDefault) {
      return existingDefault;
    }

    const fallbackGroup = await this.prisma.pcGroup.findFirst({
      where: { name: 'Mặc định' },
    });
    if (fallbackGroup) {
      return this.prisma.$transaction(async (tx) => {
        await tx.pcGroup.updateMany({
          where: { isDefault: true },
          data: { isDefault: false },
        });

        return tx.pcGroup.update({
          where: { id: fallbackGroup.id },
          data: { isDefault: true },
        });
      });
    }

    return this.prisma.pcGroup.create({
      data: {
        name: 'Mặc định',
        hourlyRate: 5000,
        isDefault: true,
      },
    });
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
