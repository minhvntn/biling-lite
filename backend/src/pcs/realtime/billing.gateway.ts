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
import { PrismaService } from '../../prisma/prisma.service';
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
    private readonly prisma: PrismaService,
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
    const activePromos = await this.pcsService.getActivePromotions();

    const activeSession = await this.pcsService.getActiveSessionForPc(transition.pc.id);
    const activePresenceKind = activeSession
      ? await this.pcsService.getActivePresenceKindForPc(transition.pc.id)
      : null;
    const resumeGuestSession =
      !!activeSession && activePresenceKind === 'GUEST';
    const billingInfo = await this.pcsService.getActiveSessionBillingForPc(transition.pc.id);
    const elapsedSeconds = billingInfo?.elapsedSeconds ?? (activeSession
      ? Math.max(
          0,
          Math.floor(
            (Date.now() -
              Math.max(
                activeSession.startedAt.getTime(),
                activeSession.createdAt.getTime(),
              )) /
              1000,
          ),
        )
      : 0);
    const estimatedAmount = billingInfo?.estimatedAmount ?? null;
    const effectiveHourlyRate = billingInfo?.hourlyRate ?? hourlyRate;

    let resumeMemberSession = false;
    let memberInfo = null;

    if (activeSession && activePresenceKind === 'MEMBER') {
      const activeMember = await this.pcsService.getActiveMemberForPc(transition.pc.id);
      if (activeMember && activeMember.memberType === 'VIP') {
        resumeMemberSession = true;
        memberInfo = {
          memberId: activeMember.memberId,
          username: activeMember.username,
          fullName: activeMember.fullName,
          memberType: activeMember.memberType,
          rank: activeMember.rank,
          balance: activeMember.balance,
        };
      }
    }

    client.emit('agent.hello.ack', {
      ok: true,
      pcId: transition.pc.id,
      status: transition.pc.status,
      hourlyRate: effectiveHourlyRate,
      isGuestLoginEnabled,
      resumeGuestSession,
      resumeMemberSession,
      memberInfo,
      elapsedSeconds,
      estimatedAmount,
      activePromotions: activePromos,
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
    const activePromos = await this.pcsService.getActivePromotions();

    const activeSession = await this.pcsService.getActiveSessionForPc(transition.pc.id);
    const billingInfo = await this.pcsService.getActiveSessionBillingForPc(transition.pc.id);
    const elapsedSeconds = billingInfo?.elapsedSeconds ?? (activeSession
      ? Math.max(
          0,
          Math.floor(
            (Date.now() -
              Math.max(
                activeSession.startedAt.getTime(),
                activeSession.createdAt.getTime(),
              )) /
              1000,
          ),
        )
      : 0);
    const estimatedAmount = billingInfo?.estimatedAmount ?? null;
    const effectiveHourlyRate = billingInfo?.hourlyRate ?? hourlyRate;

    client.emit('agent.heartbeat.ack', {
      ok: true,
      status: transition.pc.status,
      hourlyRate: effectiveHourlyRate,
      isGuestLoginEnabled,
      elapsedSeconds,
      estimatedAmount,
      activePromotions: activePromos,
      serverTime: new Date().toISOString(),
    });
  }

  @SubscribeMessage('agent.hardware_info')
  async handleAgentHardwareInfo(
    @ConnectedSocket() client: Socket,
    @MessageBody() payload: any,
  ) {
    if (!payload?.agentId || !payload?.hardware) {
      return;
    }

    const pc = await this.pcsService.getPcByAgentId(payload.agentId);
    if (!pc) {
      return;
    }

    await this.pcsService.updateAutoFetchedHardware(pc.id, payload.hardware);
  }

  @SubscribeMessage('agent.support_request')
  async handleSupportRequest(
    @ConnectedSocket() client: Socket,
    @MessageBody() payload: { agentId: string; message: string; category?: string },
  ) {
    if (!payload?.agentId || !payload?.message?.trim()) {
      return;
    }

    const pc = await this.pcsService.getPcByAgentId(payload.agentId);
    if (!pc) {
      return;
    }

    const saved = await this.pcsService.saveSupportMessage(
      pc.id,
      'CLIENT',
      payload.message.trim(),
      payload.category,
      pc.name,
    );

    // Emit to all admin sockets
    this.realtime.emitToAll('client.support_request', {
      id: saved.id,
      pcId: pc.id,
      pcName: pc.name,
      agentId: pc.agentId,
      message: payload.message.trim(),
      category: payload.category,
      createdAt: saved.createdAt.toISOString(),
    });

    client.emit('agent.support_request.ack', { ok: true });
  }

  @SubscribeMessage('admin.reply_support')
  async handleAdminReplySupport(
    @ConnectedSocket() client: Socket,
    @MessageBody() payload: { pcId: string; message: string; repliedBy?: string },
  ) {
    if (!payload?.pcId || !payload?.message?.trim()) {
      return;
    }

    const pc = await this.prisma.pc.findUnique({ where: { id: payload.pcId } });
    if (!pc) {
      return;
    }

    await this.pcsService.saveSupportMessage(
      pc.id,
      'ADMIN',
      payload.message.trim(),
      undefined,
      payload.repliedBy ?? 'Admin',
    );

    // Send reply to the client agent
    this.realtime.emitToAgent(pc.agentId, 'admin.support_reply', {
      message: payload.message.trim(),
      repliedBy: payload.repliedBy ?? 'Admin',
      sentAt: new Date().toISOString(),
    });

    // Mark client messages as read
    await this.pcsService.markSupportMessagesRead(pc.id);
  }

  @SubscribeMessage('agent.clear_support')
  async handleAgentClearSupport(
    @ConnectedSocket() client: Socket,
    @MessageBody() payload: { agentId: string },
  ) {
    if (!payload?.agentId) return;
    const pc = await this.pcsService.getPcByAgentId(payload.agentId);
    if (!pc) return;
    await this.pcsService.clearSupportMessages(pc.id);
  }

  @SubscribeMessage('admin.clear_support')
  async handleAdminClearSupport(
    @ConnectedSocket() client: Socket,
    @MessageBody() payload: { pcId: string },
  ) {
    if (!payload?.pcId) return;
    await this.pcsService.clearSupportMessages(payload.pcId);
  }

  @SubscribeMessage('agent.app_usage')
  async handleAgentAppUsage(
    @ConnectedSocket() client: Socket,
    @MessageBody() payload: { agentId: string; apps: { name: string; title?: string }[] },
  ) {
    if (!payload?.agentId || !Array.isArray(payload?.apps)) {
      return;
    }

    const pc = await this.pcsService.getPcByAgentId(payload.agentId);
    if (!pc) {
      return;
    }

    await this.pcsService.upsertAppUsage(pc.id, payload.apps);

    // Emit to admin for realtime view
    this.realtime.emitToAll('pc.app_usage_updated', {
      pcId: pc.id,
      pcName: pc.name,
      apps: payload.apps,
      at: new Date().toISOString(),
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
