import { Body, Controller, Get, Param, Post, Query } from '@nestjs/common';
import { BuyPlaytimeDto } from './dto/buy-playtime.dto';
import { CreateMemberDto } from './dto/create-member.dto';
import { TopupMemberDto } from './dto/topup-member.dto';
import { AdjustBalanceDto } from './dto/adjust-balance.dto';
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
}
