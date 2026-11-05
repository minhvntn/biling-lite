import { Controller, Get, Param, Post, Put, Body, Query, Delete } from '@nestjs/common';
import { PcsService } from './pcs.service';
import { TransferBillingDto } from './dto/transfer-billing.dto';

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

  @Get('hardware/all')
  async getAllHardware() {
    return this.pcsService.getAllPcHardware();
  }

  @Get(':id/hardware')
  async getHardware(@Param('id') pcId: string) {
    return this.pcsService.getPcHardware(pcId);
  }

  @Put(':id/hardware')
  async updateHardware(@Param('id') pcId: string, @Body() dto: any) {
    return this.pcsService.updatePcHardware(pcId, dto);
  }

  @Get(':id/maintenance')
  async getMaintenanceLogs(@Param('id') pcId: string) {
    return this.pcsService.getPcMaintenanceLogs(pcId);
  }

  @Post(':id/maintenance')
  async addMaintenanceLog(@Param('id') pcId: string, @Body() dto: any) {
    return this.pcsService.addPcMaintenanceLog(pcId, dto);
  }

  @Get('support-messages/all')
  async getAllSupportMessages() {
    const msgs = await this.pcsService.getRecentSupportMessages();
    return msgs.map(m => ({
      ...m,
      pcName: (m as any).pc?.name || 'Unknown',
      pcId: m.pcId,
    }));
  }

  @Get('support-messages/unread-count')
  async getUnreadSupportCount() {
    const count = await this.pcsService.getUnreadSupportCount();
    return { count };
  }

  @Get('agent/:agentId/support-messages')
  async getAgentSupportMessages(@Param('agentId') agentId: string) {
    const pc = await this.pcsService.getPcByAgentId(agentId);
    if (!pc) return [];
    return this.pcsService.getSupportMessages(pc.id);
  }

  @Get(':id/support-messages')
  async getSupportMessages(@Param('id') pcId: string) {
    return this.pcsService.getSupportMessages(pcId);
  }

  @Delete('agent/:agentId/support-messages')
  async clearAgentSupportMessages(@Param('agentId') agentId: string) {
    const pc = await this.pcsService.getPcByAgentId(agentId);
    if (!pc) return { ok: false };
    await this.pcsService.clearSupportMessages(pc.id);
    return { ok: true };
  }

  @Delete(':id/support-messages')
  async clearSupportMessages(@Param('id') pcId: string) {
    await this.pcsService.clearSupportMessages(pcId);
    return { ok: true };
  }

  @Post(':id/support-messages/mark-read')
  async markSupportMessagesRead(@Param('id') pcId: string) {
    return this.pcsService.markSupportMessagesRead(pcId);
  }

  @Post(':id/reply-support')
  async replySupport(@Param('id') pcId: string, @Body() dto: { message: string }) {
    return this.pcsService.replySupportMessage(pcId, dto.message, 'Admin');
  }

  @Get('app-usage/current')
  async getCurrentRunningApps() {
    return this.pcsService.getCurrentRunningApps();
  }

  @Get(':id/app-usage')
  async getAppUsage(
    @Param('id') pcId: string,
    @Query('from') from?: string,
    @Query('to') to?: string,
  ) {
    return this.pcsService.getAppUsage(
      pcId,
      from ? new Date(from) : undefined,
      to ? new Date(to) : undefined,
    );
  }

  @Post(':id/transfer-billing')
  async transferBilling(
    @Param('id') fromPcId: string,
    @Body() dto: TransferBillingDto,
  ) {
    return this.pcsService.transferBilling(
      fromPcId,
      dto.targetPcId,
      dto.note,
      dto.requestedBy,
    );
  }

  @Get(':id/transferred-bills')
  async getTransferredBills(@Param('id') pcId: string) {
    return this.pcsService.getTransferredBills(pcId);
  }

  @Post(':id/transferred-bills/clear')
  async clearTransferredBills(
    @Param('id') pcId: string,
    @Body() body: { requestedBy?: string },
  ) {
    return this.pcsService.clearTransferredBills(pcId, body?.requestedBy);
  }
}
