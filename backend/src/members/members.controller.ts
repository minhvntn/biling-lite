import { Body, Controller, Get, Param, Patch, Post, Query } from '@nestjs/common';
import { BuyPlaytimeDto } from './dto/buy-playtime.dto';
import { CreateMemberDto } from './dto/create-member.dto';
import { TopupMemberDto } from './dto/topup-member.dto';
import { AdjustBalanceDto } from './dto/adjust-balance.dto';
import { MemberLoginDto } from './dto/member-login.dto';
import { UpdateMemberDto } from './dto/update-member.dto';
import { TransferBalanceDto } from './dto/transfer-balance.dto';
import { UpdateLoyaltySettingsDto } from './dto/update-loyalty-settings.dto';
import { RecordMemberUsageDto } from './dto/record-member-usage.dto';
import { RedeemLoyaltyPointsDto } from './dto/redeem-loyalty-points.dto';
import { SetMemberPresenceDto } from './dto/set-member-presence.dto';
import { UpdateLoyaltyRankDto } from './dto/update-loyalty-rank.dto';
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
}
