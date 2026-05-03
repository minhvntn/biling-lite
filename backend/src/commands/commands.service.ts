import { BadRequestException, Injectable, NotFoundException } from '@nestjs/common';
import { CommandStatus, CommandType, EventSource, PcStatus, Prisma } from '@prisma/client';
import { Cron, CronExpression } from '@nestjs/schedule';
import { ConfigService } from '@nestjs/config';
import { randomUUID } from 'crypto';
import { PrismaService } from '../prisma/prisma.service';
import { RealtimeService } from '../realtime/realtime.service';
import { CommandAckPayload } from './types/command-ack.type';
import { UploadPcScreenshotDto } from './dto/upload-pc-screenshot.dto';

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

  async createGuestOpenCommand(
    pcId: string,
    amount: number,
    requestedBy?: CommandRequestedBy,
  ) {
    const normalizedAmount = this.roundMoney(amount);
    const command = await this.createAndDispatch(pcId, CommandType.OPEN, requestedBy);

    if (
      command.status !== CommandStatus.PENDING &&
      command.status !== CommandStatus.SENT &&
      command.status !== CommandStatus.ACK_SUCCESS
    ) {
      return command;
    }

    await this.logEvent(EventSource.ADMIN, 'guest.pc.presence', pcId, {
      isActive: true,
      displayName: 'Khách vãng lai',
      prepaidAmount: normalizedAmount,
      requestedBy: requestedBy?.trim() || 'admin.desktop',
      at: new Date().toISOString(),
    });

    return command;
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

  async requestPcScreenshot(pcId: string, requestedBy?: CommandRequestedBy) {
    const pc = await this.prisma.pc.findUnique({ where: { id: pcId } });
    if (!pc) {
      throw new NotFoundException('PC not found');
    }

    const sockets = this.realtime.countAgentSockets(pc.agentId);
    if (sockets <= 0) {
      return { ok: false, reason: 'AGENT_OFFLINE' as const };
    }

    const requestId = randomUUID();
    const payload = {
      pcId: pc.id,
      agentId: pc.agentId,
      requestId,
      requestedBy: requestedBy?.trim() || 'admin.desktop',
      requestedAt: new Date().toISOString(),
    };

    this.realtime.emitToAgent(pc.agentId, 'admin.capture_screenshot', payload);
    await this.logEvent(EventSource.ADMIN, 'pc.screenshot.requested', pc.id, payload);

    return {
      ok: true,
      requestId,
      sentAt: new Date().toISOString(),
    };
  }

  async uploadPcScreenshot(pcId: string, body: UploadPcScreenshotDto) {
    const pc = await this.prisma.pc.findUnique({ where: { id: pcId } });
    if (!pc) {
      throw new NotFoundException('PC not found');
    }

    const normalizedAgentId = body.agentId.trim();
    if (!normalizedAgentId) {
      throw new BadRequestException('agentId is required');
    }

    if (!pc.agentId) {
      throw new BadRequestException('AGENT_MISMATCH');
    }
    if (
      normalizedAgentId.localeCompare(pc.agentId, undefined, {
        sensitivity: 'accent',
      }) !== 0
    ) {
      throw new BadRequestException('AGENT_MISMATCH');
    }

    const imageBase64 = this.normalizeBase64(body.imageBase64);
    if (!imageBase64) {
      throw new BadRequestException('Invalid imageBase64');
    }

    if (imageBase64.length > 6_000_000) {
      throw new BadRequestException('Screenshot too large');
    }

    const payload = {
      requestId: body.requestId?.trim() || null,
      imageBase64,
      mimeType: body.mimeType?.trim() || 'image/jpeg',
      width: body.width ?? null,
      height: body.height ?? null,
      capturedAt: body.capturedAt ?? new Date().toISOString(),
      uploadedAt: new Date().toISOString(),
    };

    const created = await this.prisma.eventLog.create({
      data: {
        source: EventSource.CLIENT,
        eventType: 'pc.screenshot.captured',
        pcId: pc.id,
        payload,
      },
    });

    return {
      ok: true,
      screenshotEventId: created.id,
      capturedAt: payload.capturedAt,
      serverTime: new Date().toISOString(),
    };
  }

  async getLatestPcScreenshot(pcId: string, requestId?: string) {
    const pc = await this.prisma.pc.findUnique({ where: { id: pcId } });
    if (!pc) {
      throw new NotFoundException('PC not found');
    }

    const events = await this.prisma.eventLog.findMany({
      where: {
        pcId: pc.id,
        eventType: 'pc.screenshot.captured',
      },
      orderBy: { createdAt: 'desc' },
      take: 20,
    });

    const normalizedRequestId = requestId?.trim();
    const picked = events.find((item) => {
      if (!normalizedRequestId) {
        return true;
      }

      const payload = this.parseScreenshotPayload(item.payload);
      if (!payload) {
        return false;
      }

      return payload.requestId === normalizedRequestId;
    });

    if (!picked) {
      return {
        ok: false,
        reason: 'NOT_FOUND' as const,
        serverTime: new Date().toISOString(),
      };
    }

    const payload = this.parseScreenshotPayload(picked.payload);
    if (!payload) {
      return {
        ok: false,
        reason: 'INVALID_PAYLOAD' as const,
        serverTime: new Date().toISOString(),
      };
    }

    return {
      ok: true,
      screenshot: {
        eventId: picked.id,
        requestId: payload.requestId,
        imageBase64: payload.imageBase64,
        mimeType: payload.mimeType ?? 'image/jpeg',
        width: payload.width,
        height: payload.height,
        capturedAt: payload.capturedAt ?? picked.createdAt.toISOString(),
        createdAt: picked.createdAt.toISOString(),
      },
      serverTime: new Date().toISOString(),
    };
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
            hourlyRate?: number;
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
            const hourlyRate = await this.resolveHourlyRateForPcTx(tx, command.pcId);
            const pricePerMinute = this.toPricePerMinute(hourlyRate);

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
              hourlyRate,
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

    if (payload.result === 'SUCCESS' && command.type === CommandType.LOCK) {
      await this.logEvent(EventSource.SERVER, 'guest.pc.presence', command.pcId, {
        isActive: false,
        at: new Date().toISOString(),
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
      await this.logEvent(EventSource.SERVER, 'guest.pc.presence', pcId, {
        isActive: false,
        at: new Date().toISOString(),
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
      hourlyRate:
        sentCommand.type === CommandType.OPEN ||
        sentCommand.type === CommandType.RESUME
          ? await this.resolveHourlyRateForPc(pc.id)
          : undefined,
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

  private toPricePerMinute(hourlyRate: number): number {
    return Math.round((hourlyRate / 60) * 100) / 100;
  }

  private roundMoney(value: number): number {
    if (!Number.isFinite(value) || value < 1000) {
      throw new BadRequestException('So tien khach vang lai khong hop le');
    }

    return Math.round(value * 100) / 100;
  }

  private normalizeBase64(raw: string): string | null {
    const trimmed = raw?.trim();
    if (!trimmed) {
      return null;
    }

    const markerIndex = trimmed.indexOf('base64,');
    const content =
      markerIndex >= 0 ? trimmed.slice(markerIndex + 'base64,'.length) : trimmed;
    const compact = content.replace(/\s+/g, '');
    if (!compact) {
      return null;
    }

    return compact;
  }

  private parseScreenshotPayload(
    payload: Prisma.JsonValue | null,
  ):
    | {
        requestId: string | null;
        imageBase64: string;
        mimeType: string | null;
        width: number | null;
        height: number | null;
        capturedAt: string | null;
      }
    | null {
    if (!payload || typeof payload !== 'object' || Array.isArray(payload)) {
      return null;
    }

    const value = payload as Record<string, unknown>;
    const imageBase64 =
      typeof value.imageBase64 === 'string' ? value.imageBase64.trim() : '';
    if (!imageBase64) {
      return null;
    }

    const requestId =
      typeof value.requestId === 'string' && value.requestId.trim()
        ? value.requestId.trim()
        : null;
    const mimeType =
      typeof value.mimeType === 'string' && value.mimeType.trim()
        ? value.mimeType.trim()
        : null;
    const width =
      typeof value.width === 'number' && Number.isFinite(value.width)
        ? Math.round(value.width)
        : null;
    const height =
      typeof value.height === 'number' && Number.isFinite(value.height)
        ? Math.round(value.height)
        : null;
    const capturedAt =
      typeof value.capturedAt === 'string' && value.capturedAt.trim()
        ? value.capturedAt.trim()
        : null;

    return {
      requestId,
      imageBase64,
      mimeType,
      width,
      height,
      capturedAt,
    };
  }

  private async resolveHourlyRateForPc(pcId: string): Promise<number> {
    const pc = await this.prisma.pc.findUnique({
      where: { id: pcId },
      include: { group: true },
    });

    if (!pc) {
      throw new NotFoundException('PC not found');
    }

    if (pc.group) {
      return Number(pc.group.hourlyRate);
    }

    const defaultGroup = await this.ensureDefaultGroup(this.prisma);
    return Number(defaultGroup.hourlyRate);
  }

  private async resolveHourlyRateForPcTx(
    tx: Prisma.TransactionClient,
    pcId: string,
  ): Promise<number> {
    const pc = await tx.pc.findUnique({
      where: { id: pcId },
      include: { group: true },
    });

    if (!pc) {
      throw new NotFoundException('PC not found');
    }

    if (pc.group) {
      return Number(pc.group.hourlyRate);
    }

    const defaultGroup = await this.ensureDefaultGroup(tx);
    return Number(defaultGroup.hourlyRate);
  }

  private async ensureDefaultGroup(client: PrismaService | Prisma.TransactionClient) {
    const existingDefault = await client.pcGroup.findFirst({
      where: { isDefault: true },
      orderBy: { updatedAt: 'desc' },
    });
    if (existingDefault) {
      return existingDefault;
    }

    const fallbackGroup = await client.pcGroup.findFirst({
      where: { name: 'Mặc định' },
    });

    if (fallbackGroup) {
      await client.pcGroup.updateMany({
        where: { isDefault: true },
        data: { isDefault: false },
      });

      return client.pcGroup.update({
        where: { id: fallbackGroup.id },
        data: { isDefault: true },
      });
    }

    return client.pcGroup.create({
      data: {
        name: 'Mặc định',
        hourlyRate: 5000,
        isDefault: true,
      },
    });
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
