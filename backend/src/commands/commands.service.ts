import { Injectable, NotFoundException } from '@nestjs/common';
import { CommandStatus, CommandType, EventSource, PcStatus, Prisma } from '@prisma/client';
import { Cron, CronExpression } from '@nestjs/schedule';
import { ConfigService } from '@nestjs/config';
import { PrismaService } from '../prisma/prisma.service';
import { RealtimeService } from '../realtime/realtime.service';
import { CommandAckPayload } from './types/command-ack.type';

type CommandRequestedBy = string | undefined;

@Injectable()
export class CommandsService {
  constructor(
    private readonly prisma: PrismaService,
    private readonly realtime: RealtimeService,
    private readonly configService: ConfigService,
  ) {}

  async createOpenCommand(pcId: string, requestedBy?: CommandRequestedBy) {
    return this.createAndDispatch(pcId, CommandType.OPEN, requestedBy);
  }

  async createLockCommand(pcId: string, requestedBy?: CommandRequestedBy) {
    return this.createAndDispatch(pcId, CommandType.LOCK, requestedBy);
  }

  async createRestartCommand(pcId: string, requestedBy?: CommandRequestedBy) {
    return this.createAndDispatch(pcId, CommandType.RESTART, requestedBy);
  }

  async createShutdownCommand(pcId: string, requestedBy?: CommandRequestedBy) {
    return this.createAndDispatch(pcId, CommandType.SHUTDOWN, requestedBy);
  }

  async createCloseAppsCommand(pcId: string, requestedBy?: CommandRequestedBy) {
    return this.createAndDispatch(pcId, CommandType.CLOSE_APPS, requestedBy);
  }

  async createPauseCommand(pcId: string, requestedBy?: CommandRequestedBy) {
    return this.createAndDispatch(pcId, CommandType.PAUSE, requestedBy);
  }

  async createResumeCommand(pcId: string, requestedBy?: CommandRequestedBy) {
    return this.createAndDispatch(pcId, CommandType.RESUME, requestedBy);
  }

  async notifyPc(pcId: string, message: string, requestedBy?: CommandRequestedBy) {
    const pc = await this.prisma.pc.findUnique({ where: { id: pcId } });
    if (!pc) {
      throw new NotFoundException('PC not found');
    }

    const trimmed = message.trim();
    if (!trimmed) {
      return { ok: false, reason: 'EMPTY_MESSAGE' as const };
    }

    const sockets = this.realtime.countAgentSockets(pc.agentId);
    if (sockets <= 0) {
      return { ok: false, reason: 'AGENT_OFFLINE' as const };
    }

    const payload = {
      message: trimmed,
      requestedBy: requestedBy?.trim() || 'admin.desktop',
      sentAt: new Date().toISOString(),
    };

    this.realtime.emitToAgent(pc.agentId, 'admin.notify', payload);
    await this.logEvent(EventSource.ADMIN, 'admin.notify.sent', pc.id, payload);
    return { ok: true };
  }

  async getCommandById(commandId: string) {
    const command = await this.prisma.command.findUnique({
      where: { id: commandId },
      include: {
        pc: true,
      },
    });

    if (!command) {
      throw new NotFoundException('Command not found');
    }

    return command;
  }

  async acknowledgeFromAgent(payload: CommandAckPayload) {
    const command = await this.prisma.command.findUnique({
      where: { id: payload.commandId },
      include: { pc: true },
    });

    if (!command) {
      await this.logEvent(EventSource.SERVER, 'command.ack.not_found', undefined, payload);
      return { ok: false, reason: 'COMMAND_NOT_FOUND' as const };
    }

    if (command.pc.agentId !== payload.agentId) {
      await this.logEvent(EventSource.SERVER, 'command.ack.agent_mismatch', command.pcId, {
        commandId: command.id,
        expectedAgentId: command.pc.agentId,
        actualAgentId: payload.agentId,
      });
      return { ok: false, reason: 'AGENT_MISMATCH' as const };
    }

    if (
      command.status === CommandStatus.ACK_SUCCESS ||
      command.status === CommandStatus.ACK_FAILED ||
      command.status === CommandStatus.TIMEOUT
    ) {
      return { ok: true, command };
    }

    const nextStatus =
      payload.result === 'SUCCESS'
        ? CommandStatus.ACK_SUCCESS
        : CommandStatus.ACK_FAILED;
    const errorMessage =
      payload.result === 'SUCCESS'
        ? null
        : payload.message?.trim() || 'Client returned failed';

    const result = await this.prisma.$transaction(async (tx) => {
      const updatedCommand = await tx.command.update({
        where: { id: command.id },
        data: {
          status: nextStatus,
          ackAt: new Date(),
          errorMessage,
        },
        include: { pc: true },
      });

      let statusTransition:
        | { pcId: string; agentId: string; previousStatus: PcStatus; status: PcStatus }
        | null = null;
      let sessionEvent:
        | {
            type: 'session.opened' | 'session.closed';
            sessionId: string;
            amount?: number;
            billableMinutes?: number;
          }
        | null = null;

      if (payload.result === 'SUCCESS') {
        const existingPc = await tx.pc.findUnique({ where: { id: command.pcId } });
        const targetStatus =
          command.type === CommandType.OPEN
            ? PcStatus.IN_USE
            : command.type === CommandType.LOCK
              ? PcStatus.LOCKED
              : command.type === CommandType.PAUSE
                ? PcStatus.PAUSED
                : command.type === CommandType.RESUME
                  ? PcStatus.IN_USE
                  : existingPc?.status ?? PcStatus.ONLINE;

        if (existingPc && existingPc.status !== targetStatus) {
          const updatedPc = await tx.pc.update({
            where: { id: existingPc.id },
            data: { status: targetStatus },
          });

          statusTransition = {
            pcId: updatedPc.id,
            agentId: updatedPc.agentId,
            previousStatus: existingPc.status,
            status: updatedPc.status,
          };
        }

        if (command.type === CommandType.OPEN) {
          const activeSession = await tx.session.findFirst({
            where: {
              pcId: command.pcId,
              status: 'ACTIVE',
            },
          });

          if (!activeSession) {
            const activePricing = await tx.pricingConfig.findFirst({
              where: { isActive: true },
              orderBy: { updatedAt: 'desc' },
            });
            const pricePerMinute = activePricing?.pricePerMinute ?? 0;

            const createdSession = await tx.session.create({
              data: {
                pcId: command.pcId,
                startedAt: new Date(),
                status: 'ACTIVE',
                pricePerMinute,
              },
            });
            sessionEvent = {
              type: 'session.opened',
              sessionId: createdSession.id,
            };
          }
        }

        if (command.type === CommandType.LOCK) {
          const activeSession = await tx.session.findFirst({
            where: {
              pcId: command.pcId,
              status: 'ACTIVE',
            },
            orderBy: { startedAt: 'desc' },
          });

          if (activeSession) {
            const endedAt = new Date();
            const durationSeconds = Math.max(
              0,
              Math.floor((endedAt.getTime() - activeSession.startedAt.getTime()) / 1000),
            );
            const billableMinutes = Math.max(1, Math.ceil(durationSeconds / 60));
            const pricePerMinute = Number(activeSession.pricePerMinute ?? 0);
            const amount = billableMinutes * pricePerMinute;

            const closedSession = await tx.session.update({
              where: { id: activeSession.id },
              data: {
                endedAt,
                durationSeconds,
                billableMinutes,
                amount,
                status: 'CLOSED',
                closedReason: 'ADMIN_LOCK',
              },
            });
            sessionEvent = {
              type: 'session.closed',
              sessionId: closedSession.id,
              amount,
              billableMinutes,
            };
          }
        }
      }

      return { updatedCommand, statusTransition, sessionEvent };
    });

    this.broadcastCommandUpdated(result.updatedCommand);
    await this.logEvent(EventSource.SERVER, 'command.ack.received', command.pcId, {
      commandId: command.id,
      result: payload.result,
      nextStatus,
      message: payload.message ?? null,
    });
    if (result.statusTransition) {
      this.realtime.emitToAll('pc.status.changed', {
        ...result.statusTransition,
        at: new Date().toISOString(),
        sourceEvent: 'command.ack',
      });
      await this.logEvent(EventSource.SERVER, 'pc.status.changed', command.pcId, {
        previousStatus: result.statusTransition.previousStatus,
        status: result.statusTransition.status,
        sourceEvent: 'command.ack',
      });
    }
    if (result.sessionEvent) {
      await this.logEvent(EventSource.SERVER, result.sessionEvent.type, command.pcId, {
        sessionId: result.sessionEvent.sessionId,
        amount: result.sessionEvent.amount ?? null,
        billableMinutes: result.sessionEvent.billableMinutes ?? null,
      });
    }

    return { ok: true, command: result.updatedCommand };
  }

  @Cron(CronExpression.EVERY_10_SECONDS)
  async markTimedOutCommands(): Promise<void> {
    const timeoutSeconds = this.getCommandAckTimeoutSeconds();
    const cutoff = new Date(Date.now() - timeoutSeconds * 1000);
    const staleCommands = await this.prisma.command.findMany({
      where: {
        status: CommandStatus.SENT,
        sentAt: { lt: cutoff },
      },
      include: { pc: true },
    });

    if (staleCommands.length === 0) {
      return;
    }

    for (const command of staleCommands) {
      const updated = await this.prisma.command.update({
        where: { id: command.id },
        data: {
          status: CommandStatus.TIMEOUT,
          ackAt: new Date(),
          errorMessage: command.errorMessage ?? 'No client ack received in time',
        },
        include: { pc: true },
      });
      this.broadcastCommandUpdated(updated);
      await this.logEvent(EventSource.SERVER, 'command.timeout.ack_missing', updated.pcId, {
        commandId: updated.id,
      });
    }
  }

  private async createAndDispatch(
    pcId: string,
    type: CommandType,
    requestedBy?: CommandRequestedBy,
  ) {
    const pc = await this.prisma.pc.findUnique({ where: { id: pcId } });
    if (!pc) {
      throw new NotFoundException('PC not found');
    }

    const inflightCommand = await this.prisma.command.findFirst({
      where: {
        pcId,
        type,
        status: {
          in: [CommandStatus.PENDING, CommandStatus.SENT],
        },
      },
      orderBy: { requestedAt: 'desc' },
      include: { pc: true },
    });
    if (inflightCommand) {
      await this.logEvent(EventSource.SERVER, 'command.idempotent.inflight', pcId, {
        commandId: inflightCommand.id,
        type,
      });
      return inflightCommand;
    }

    const activeSession = await this.prisma.session.findFirst({
      where: {
        pcId,
        status: 'ACTIVE',
      },
    });

    if (type === CommandType.OPEN && activeSession) {
      const rejected = await this.prisma.command.create({
        data: {
          pcId,
          type,
          status: CommandStatus.ACK_FAILED,
          requestedBy: requestedBy?.trim() || 'admin.local',
          ackAt: new Date(),
          errorMessage: 'PC already has an active session',
        },
        include: { pc: true },
      });
      this.broadcastCommandUpdated(rejected);
      await this.logEvent(EventSource.SERVER, 'command.rejected.already_active', pcId, {
        commandId: rejected.id,
      });
      return rejected;
    }

    if (type === CommandType.LOCK && !activeSession) {
      const noOp = await this.prisma.$transaction(async (tx) => {
        const command = await tx.command.create({
          data: {
            pcId,
            type,
            status: CommandStatus.ACK_SUCCESS,
            requestedBy: requestedBy?.trim() || 'admin.local',
            ackAt: new Date(),
          },
          include: { pc: true },
        });

        if (pc.status !== PcStatus.LOCKED) {
          await tx.pc.update({
            where: { id: pc.id },
            data: { status: PcStatus.LOCKED },
          });
        }

        return command;
      });

      this.realtime.emitToAll('pc.status.changed', {
        pcId: pc.id,
        agentId: pc.agentId,
        previousStatus: pc.status,
        status: PcStatus.LOCKED,
        at: new Date().toISOString(),
        sourceEvent: 'command.lock.noop',
      });
      this.broadcastCommandUpdated(noOp);
      await this.logEvent(EventSource.SERVER, 'command.noop.lock_without_session', pcId, {
        commandId: noOp.id,
      });
      return noOp;
    }

    const command = await this.prisma.command.create({
      data: {
        pcId,
        type,
        status: CommandStatus.PENDING,
        requestedBy: requestedBy?.trim() || 'admin.local',
      },
      include: { pc: true },
    });
    this.broadcastCommandUpdated(command);
    await this.logEvent(EventSource.SERVER, 'command.created', pcId, {
      commandId: command.id,
      type: command.type,
    });

    const connectedSockets = this.realtime.countAgentSockets(pc.agentId);
    if (connectedSockets <= 0) {
      const timeoutCommand = await this.prisma.command.update({
        where: { id: command.id },
        data: {
          status: CommandStatus.TIMEOUT,
          ackAt: new Date(),
          errorMessage: 'Agent is not connected',
        },
        include: { pc: true },
      });

      this.broadcastCommandUpdated(timeoutCommand);
      await this.logEvent(EventSource.SERVER, 'command.timeout.agent_offline', pcId, {
        commandId: timeoutCommand.id,
      });
      return timeoutCommand;
    }

    const sentCommand = await this.prisma.command.update({
      where: { id: command.id },
      data: {
        status: CommandStatus.SENT,
        sentAt: new Date(),
      },
      include: { pc: true },
    });

    this.realtime.emitToAgent(pc.agentId, 'command.execute', {
      commandId: sentCommand.id,
      type: sentCommand.type,
      issuedAt: new Date().toISOString(),
    });
    this.broadcastCommandUpdated(sentCommand);
    await this.logEvent(EventSource.SERVER, 'command.dispatched', pcId, {
      commandId: sentCommand.id,
      type: sentCommand.type,
      sockets: connectedSockets,
    });

    return sentCommand;
  }

  private broadcastCommandUpdated(command: {
    id: string;
    pcId: string;
    status: CommandStatus;
    type: CommandType;
    errorMessage: string | null;
  }): void {
    this.realtime.emitToAll('command.updated', {
      commandId: command.id,
      pcId: command.pcId,
      status: command.status,
      type: command.type,
      errorMessage: command.errorMessage,
      at: new Date().toISOString(),
    });
  }

  private getCommandAckTimeoutSeconds(): number {
    const raw = Number(
      this.configService.get<string>('COMMAND_ACK_TIMEOUT_SECONDS') ?? '20',
    );
    if (!Number.isFinite(raw) || raw < 5) {
      return 20;
    }

    return raw;
  }

  private async logEvent(
    source: EventSource,
    eventType: string,
    pcId?: string,
    payload?: Prisma.InputJsonValue,
  ): Promise<void> {
    try {
      await this.prisma.eventLog.create({
        data: {
          source,
          eventType,
          pcId,
          payload,
        },
      });
    } catch {
      // Audit logging should not break the command flow.
    }
  }
}
