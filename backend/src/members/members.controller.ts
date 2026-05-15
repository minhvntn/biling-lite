import { Body, Controller, Get, Param, Patch, Post, Query } from '@nestjs/common';
import { BuyPlaytimeDto } from './dto/buy-playtime.dto';
import { CreateMemberDto } from './dto/create-member.dto';
import { TopupMemberDto } from './dto/topup-member.dto';
import { AdjustBalanceDto } from './dto/adjust-balance.dto';
import { MemberLoginDto } from './dto/member-login.dto';
import { UpdateMemberDto } from './dto/update-member.dto';
import { TransferBalanceDto } from './dto/transfer-balance.dto';
import { WithdrawBalanceDto } from './dto/withdraw-balance.dto';
import { ApproveWithdrawRequestDto } from './dto/approve-withdraw-request.dto';
import { RejectWithdrawRequestDto } from './dto/reject-withdraw-request.dto';
import { RequestTopupDto } from './dto/request-topup.dto';
import { ApproveTopupRequestDto } from './dto/approve-topup-request.dto';
import { RejectTopupRequestDto } from './dto/reject-topup-request.dto';
import { UpdateLoyaltySettingsDto } from './dto/update-loyalty-settings.dto';
import { RecordMemberUsageDto } from './dto/record-member-usage.dto';
import { RedeemLoyaltyPointsDto } from './dto/redeem-loyalty-points.dto';
import { SetMemberPresenceDto } from './dto/set-member-presence.dto';
import { SetAdminPresenceDto } from './dto/set-admin-presence.dto';
import { UpdateLoyaltyRankDto } from './dto/update-loyalty-rank.dto';
import { UpdateSpinPrizeSettingsDto } from './dto/update-spin-prize-settings.dto';
import { PetLoyaltyPointsDto } from './dto/pet-loyalty-points.dto';
import { MembersService } from './members.service';

@Controller('members')
export class MembersController {
  constructor(private readonly membersService: MembersService) {}

  @Get()
  async getMembers(@Query('search') search?: string) {
    return this.membersService.getMembers(search);
  }

  @Post()
  async createMember(@Body() payload: CreateMemberDto) {
    return this.membersService.createMember(payload);
  }

  @Post('login')
  async login(@Body() payload: MemberLoginDto) {
    return this.membersService.login(payload);
  }

  @Post('presence')
  async setMemberPresence(@Body() payload: SetMemberPresenceDto) {
    return this.membersService.setMemberPresence(payload);
  }

  @Post('guest-presence')
  async setGuestPresence(@Body() payload: any) {
    return this.membersService.setGuestPresence(payload);
  }

  @Post('admin-presence')
  async setAdminPresence(@Body() payload: SetAdminPresenceDto) {
    return this.membersService.setAdminPresence(payload);
  }

  @Get('loyalty/settings')
  async getLoyaltySettings() {
    return this.membersService.getLoyaltySettings();
  }

  @Patch('loyalty/settings')
  async updateLoyaltySettings(@Body() payload: UpdateLoyaltySettingsDto) {
    return this.membersService.updateLoyaltySettings(payload);
  }

  @Get('loyalty/ranks')
  async getLoyaltyRanks() {
    return this.membersService.getLoyaltyRanks();
  }

  @Get('loyalty/spin-settings')
  async getLoyaltySpinSettings() {
    return this.membersService.getLoyaltySpinSettings();
  }

  @Get('withdraw-requests/pending')
  async getPendingWithdrawRequests() {
    return this.membersService.getPendingWithdrawRequests();
  }

  @Post('withdraw-requests/:requestId/approve')
  async approveWithdrawRequest(
    @Param('requestId') requestId: string,
    @Body() payload: ApproveWithdrawRequestDto,
  ) {
    return this.membersService.approveWithdrawRequest(requestId, payload);
  }

  @Post('withdraw-requests/:requestId/reject')
  async rejectWithdrawRequest(
    @Param('requestId') requestId: string,
    @Body() payload: RejectWithdrawRequestDto,
  ) {
    return this.membersService.rejectWithdrawRequest(requestId, payload);
  }

  @Get('topup-requests/pending')
  async getPendingTopupRequests() {
    return this.membersService.getPendingTopupRequests();
  }

  @Post('topup-requests/:requestId/approve')
  async approveTopupRequest(
    @Param('requestId') requestId: string,
    @Body() payload: ApproveTopupRequestDto,
  ) {
    return this.membersService.approveTopupRequest(requestId, payload);
  }

  @Post('topup-requests/:requestId/reject')
  async rejectTopupRequest(
    @Param('requestId') requestId: string,
    @Body() payload: RejectTopupRequestDto,
  ) {
    return this.membersService.rejectTopupRequest(requestId, payload);
  }

  @Patch('loyalty/spin-settings')
  async updateLoyaltySpinSettings(
    @Body() payload: UpdateSpinPrizeSettingsDto,
  ) {
    return this.membersService.updateLoyaltySpinSettings(payload);
  }

  @Patch('loyalty/ranks/:rankId')
  async updateLoyaltyRank(
    @Param('rankId') rankId: string,
    @Body() payload: UpdateLoyaltyRankDto,
  ) {
    return this.membersService.updateLoyaltyRank(rankId, payload);
  }

  @Get(':memberId/loyalty')
  async getMemberLoyalty(@Param('memberId') memberId: string) {
    return this.membersService.getMemberLoyalty(memberId);
  }

  @Post(':memberId/usage')
  async recordMemberUsage(
    @Param('memberId') memberId: string,
    @Body() payload: RecordMemberUsageDto,
  ) {
    return this.membersService.recordMemberUsage(memberId, payload);
  }

  @Post(':memberId/loyalty/redeem')
  async redeemLoyaltyPoints(
    @Param('memberId') memberId: string,
    @Body() payload: RedeemLoyaltyPointsDto,
  ) {
    return this.membersService.redeemLoyaltyPoints(memberId, payload);
  }
  
  @Post(':memberId/loyalty/spin')
  async spinLoyaltyPoints(
    @Param('memberId') memberId: string,
    @Body() payload: { createdBy?: string; note?: string },
  ) {
    return this.membersService.spinLoyaltyPoints(memberId, payload);
  }

  @Post(':memberId/loyalty/pet-points')
  async applyPetLoyaltyPoints(
    @Param('memberId') memberId: string,
    @Body() payload: PetLoyaltyPointsDto,
  ) {
    return this.membersService.applyPetLoyaltyPoints(memberId, payload);
  }

  @Get(':memberId/transactions')
  async getMemberTransactions(@Param('memberId') memberId: string) {
    return this.membersService.getMemberTransactions(memberId);
  }

  @Post(':memberId/topups')
  async topupMember(
    @Param('memberId') memberId: string,
    @Body() payload: TopupMemberDto,
  ) {
    return this.membersService.topupMember(memberId, payload);
  }

  @Post(':memberId/buy-hours')
  async buyPlaytime(
    @Param('memberId') memberId: string,
    @Body() payload: BuyPlaytimeDto,
  ) {
    return this.membersService.buyPlaytime(memberId, payload);
  }

  @Post(':memberId/adjust')
  async adjustBalance(
    @Param('memberId') memberId: string,
    @Body() payload: AdjustBalanceDto,
  ) {
    return this.membersService.adjustBalance(memberId, payload);
  }

  @Patch(':memberId')
  async updateMember(
    @Param('memberId') memberId: string,
    @Body() payload: UpdateMemberDto,
  ) {
    return this.membersService.updateMember(memberId, payload);
  }

  @Post(':memberId/transfer')
  async transferBalance(
    @Param('memberId') memberId: string,
    @Body() payload: TransferBalanceDto,
  ) {
    return this.membersService.transferBalance(memberId, payload);
  }

  @Post(':memberId/withdraw')
  async withdrawBalance(
    @Param('memberId') memberId: string,
    @Body() payload: WithdrawBalanceDto,
  ) {
    return this.membersService.withdrawBalance(memberId, payload);
  }

  @Post(':memberId/topup-request')
  async requestTopup(
    @Param('memberId') memberId: string,
    @Body() payload: RequestTopupDto,
  ) {
    return this.membersService.requestTopup(memberId, payload);
  }

  @Post('loyalty/ranks/rebuild')
  async rebuildRanks(@Body() payload: { maxThreshold: number }) {
    return this.membersService.rebuildRanks(payload.maxThreshold);
  }
}
