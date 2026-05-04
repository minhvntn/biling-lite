import { Logger } from '@nestjs/common';
import { ConfigService } from '@nestjs/config';
import { Cron, CronExpression } from '@nestjs/schedule';
import {
  ConnectedSocket,
  MessageBody,
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
})
export class BillingGateway implements OnGatewayInit {
  private readonly logger = new Logger(BillingGateway.name);

  @WebSocketServer()
  server!: Server;

  constructor(
    private readonly pcsService: PcsService,
    private readonly commandsService: CommandsService,
    private readonly configService: ConfigService,
    private readonly realtime: RealtimeService,
  ) {}

  afterInit(): void {
    this.realtime.setServer(this.server);
    this.logger.log('Billing gateway initialized');
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

    client.emit('agent.hello.ack', {
      ok: true,
      pcId: transition.pc.id,
      status: transition.pc.status,
      hourlyRate,
      isGuestLoginEnabled,
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

    client.emit('agent.heartbeat.ack', {
      ok: true,
      status: transition.pc.status,
      hourlyRate,
      isGuestLoginEnabled,
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

    transitions.forEach((transition) => {
      this.emitStatusTransition(transition, 'presence.timeout');
    });
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
    client.join(`pc:${pcId}`);
    client.join(`agent:${agentId}`);
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
