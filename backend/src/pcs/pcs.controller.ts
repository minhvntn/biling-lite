import { Controller, Get, Param, Post } from '@nestjs/common';
import { PcsService } from './pcs.service';

@Controller('pcs')
export class PcsController {
  constructor(private readonly pcsService: PcsService) {}

  @Get()
  async getPcs() {
    const items = await this.pcsService.getPcList();
    return {
      items,
      total: items.length,
      serverTime: new Date().toISOString(),
    };
  }

  @Post(':agentId/guest-login')
  async guestLogin(@Param('agentId') agentId: string) {
    return this.pcsService.startGuestSession(agentId);
  }
}
