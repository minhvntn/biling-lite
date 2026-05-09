import { Injectable } from '@nestjs/common';
import { Server } from 'socket.io';

@Injectable()
export class RealtimeService {
  private server: Server | null = null;

  setServer(server: Server): void {
    this.server = server;
  }

  hasServer(): boolean {
    return this.server !== null;
  }

  emitToAll(event: string, payload: unknown): void {
    this.server?.emit(event, payload);
  }

  emitToAgent(agentId: string, event: string, payload: unknown): void {
    this.server?.to(`agent:${agentId}`).emit(event, payload);
  }

  countAgentSockets(agentId: string): number {
    if (!this.server) {
      return 0;
    }

    const adapter = (this.server as any).adapter || (this.server as any).sockets?.adapter;
    const room = adapter?.rooms?.get(`agent:${agentId}`);
    return room?.size ?? 0;
  }
}
