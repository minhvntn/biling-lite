import { Logger } from '@nestjs/common';
import { ConfigService } from '@nestjs/config';
import { Cron, CronExpression } from '@nestjs/schedule';
import {
  ConnectedSocket,
  MessageBody,
  OnGatewayDisconnect,
  OnGatewayInit,
  SubscribeMessage,
  WebSocketGateway,
  WebSocketServer,
} from '@nestjs/websockets';
import { PcStatus } from '@prisma/client';
import { Server, Socket } from 'socket.io';
import { CommandsService } from '../../commands/commands.service';
import { CommandAckPayload } from '../../commands/types/command-ack.type';
import { RealtimeService } from '../../realtime/realtime.service';
import { SessionsService } from '../../sessions/sessions.service';
import { PcsService, PresenceTransition } from '../pcs.service';
import {
  AgentHeartbeatPayload,
  AgentHelloPayload,
} from '../types/agent-events.type';

@WebSocketGateway({
  namespace: '/billing',
  cors: {
    origin: '*',
  },
  pingInterval: 5000,
  pingTimeout: 7000,
})
export class BillingGateway implements OnGatewayInit, OnGatewayDisconnect {
  private readonly logger = new Logger(BillingGateway.name);

  @WebSocketServer()
  server!: Server;

  constructor(
    private readonly pcsService: PcsService,
    private readonly commandsService: CommandsService,
    private readonly sessionsService: SessionsService,
    private readonly configService: ConfigService,
    private readonly realtime: RealtimeService,
  ) {}

  afterInit(): void {
    this.realtime.setServer(this.server);
    this.logger.log('Billing gateway initialized');
  }

  async handleDisconnect(client: Socket): Promise<void> {
    const agentId = this.resolveAgentIdFromSocket(client);
    if (!agentId) {
      return;
    }

    // Wait one tick so socket.io room membership reflects this disconnect.
    await new Promise((resolve) => setTimeout(resolve, 150));

    if (this.realtime.countAgentSockets(agentId) > 0) {
      return;
    }

    const transition = await this.pcsService.markAgentOfflineByDisconnect(agentId);
    if (!transition) {
      return;
    }

    this.emitStatusTransition(transition, 'socket.disconnect');
    await this.sessionsService.closeActiveSessionForOfflinePc(
      transition.pc.id,
      'socket.disconnect',
    );
  }

  @SubscribeMessage('agent.hello')
  async handleAgentHello(
    @ConnectedSocket() client: Socket,
    @MessageBody() payload: AgentHelloPayload,
  ) {
    if (!payload?.agentId) {
      client.emit('agent.hello.ack', {
        ok: false,
        error: 'agentId is required',
      });
      return;
    }

    const transition = await this.pcsService.registerPresence(
      payload,
      this.extractClientIp(client),
    );
    this.joinPcRooms(client, transition.pc.id, transition.pc.agentId);
    this.emitStatusTransition(transition, 'agent.hello');

    const baseRate = transition.pc.group?.hourlyRate ? Number(transition.pc.group.hourlyRate) : 12000;
    const hourlyRate = await this.pcsService.getEffectiveHourlyRate(baseRate);
    const isGuestLoginEnabled = await this.pcsService.getGuestLoginEnabled();

    const activeSession = await this.pcsService.getActiveSessionForPc(transition.pc.id);
    const activePresenceKind = activeSession
      ? await this.pcsService.getActivePresenceKindForPc(transition.pc.id)
      : null;
    const resumeGuestSession =
      !!activeSession && activePresenceKind === 'GUEST';
    const elapsedSeconds = activeSession
      ? Math.max(0, Math.floor((Date.now() - activeSession.startedAt.getTime()) / 1000))
      : 0;

    client.emit('agent.hello.ack', {
      ok: true,
      pcId: transition.pc.id,
      status: transition.pc.status,
      hourlyRate,
      isGuestLoginEnabled,
      resumeGuestSession,
      elapsedSeconds,
      serverTime: new Date().toISOString(),
    });
  }

  @SubscribeMessage('agent.heartbeat')
  async handleAgentHeartbeat(
    @ConnectedSocket() client: Socket,
    @MessageBody() payload: AgentHeartbeatPayload,
  ) {
    if (!payload?.agentId) {
      client.emit('agent.heartbeat.ack', {
        ok: false,
        error: 'agentId is required',
      });
      return;
    }

    const transition = await this.pcsService.registerPresence(
      payload,
      this.extractClientIp(client),
    );
    this.joinPcRooms(client, transition.pc.id, transition.pc.agentId);
    this.emitStatusTransition(transition, 'agent.heartbeat');

    const baseRate = transition.pc.group?.hourlyRate ? Number(transition.pc.group.hourlyRate) : 12000;
    const hourlyRate = await this.pcsService.getEffectiveHourlyRate(baseRate);
    const isGuestLoginEnabled = await this.pcsService.getGuestLoginEnabled();

    const activeSession = await this.pcsService.getActiveSessionForPc(transition.pc.id);
    const elapsedSeconds = activeSession
      ? Math.max(0, Math.floor((Date.now() - activeSession.startedAt.getTime()) / 1000))
      : 0;

    client.emit('agent.heartbeat.ack', {
      ok: true,
      status: transition.pc.status,
      hourlyRate,
      isGuestLoginEnabled,
      elapsedSeconds,
      serverTime: new Date().toISOString(),
    });
  }

  @SubscribeMessage('command.ack')
  async handleCommandAck(
    @ConnectedSocket() client: Socket,
    @MessageBody() payload: CommandAckPayload,
  ) {
    if (!payload?.commandId || !payload?.agentId || !payload?.result) {
      client.emit('command.ack.response', {
        ok: false,
        reason: 'INVALID_PAYLOAD',
      });
      return;
    }

    const result = await this.commandsService.acknowledgeFromAgent(payload);
    client.emit('command.ack.response', result);
  }

  @Cron(CronExpression.EVERY_10_SECONDS)
  async markOfflineByTimeout(): Promise<void> {
    const timeoutSeconds = this.getOfflineTimeoutSeconds();
    const transitions = await this.pcsService.markStaleAgentsOffline(
      timeoutSeconds,
    );

    for (const transition of transitions) {
      this.emitStatusTransition(transition, 'presence.timeout');
      await this.sessionsService.closeActiveSessionForOfflinePc(
        transition.pc.id,
        'presence.timeout',
      );
    }
  }

  private getOfflineTimeoutSeconds(): number {
    const configured = Number(
      this.configService.get<string>('AGENT_OFFLINE_TIMEOUT_SECONDS') ?? '30',
    );

    if (!Number.isFinite(configured) || configured < 10) {
      return 30;
    }

    return configured;
  }

  private emitStatusTransition(
    transition: PresenceTransition,
    sourceEvent: string,
  ): void {
    const { pc, previousStatus } = transition;
    const hasStatusChange = previousStatus === null || previousStatus !== pc.status;

    if (!hasStatusChange) {
      return;
    }

    this.realtime.emitToAll('pc.status.changed', {
      pcId: pc.id,
      agentId: pc.agentId,
      previousStatus: previousStatus ?? PcStatus.OFFLINE,
      status: pc.status,
      at: new Date().toISOString(),
      sourceEvent,
    });

    this.logger.log(
      `PC ${pc.agentId} status ${previousStatus ?? 'UNKNOWN'} -> ${pc.status} via ${sourceEvent}`,
    );
  }

  private joinPcRooms(client: Socket, pcId: string, agentId: string): void {
    client.data.agentId = agentId;
    client.data.pcId = pcId;
    client.join(`pc:${pcId}`);
    client.join(`agent:${agentId}`);
  }

  private resolveAgentIdFromSocket(client: Socket): string | null {
    const fromData =
      typeof client.data?.agentId === 'string' ? client.data.agentId.trim() : '';
    if (fromData) {
      return fromData;
    }

    for (const room of client.rooms) {
      if (!room.startsWith('agent:')) {
        continue;
      }

      const agentId = room.slice('agent:'.length).trim();
      if (agentId) {
        return agentId;
      }
    }

    return null;
  }

  private extractClientIp(client: Socket): string | undefined {
    const forwardedFor = client.handshake.headers['x-forwarded-for'];
    const firstForwarded =
      typeof forwardedFor === 'string'
        ? forwardedFor.split(',')[0]?.trim()
        : undefined;

    return firstForwarded ?? client.handshake.address ?? undefined;
  }
}
