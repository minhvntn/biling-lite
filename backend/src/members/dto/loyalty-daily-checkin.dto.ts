import { IsOptional, IsString, MaxLength } from 'class-validator';

export class LoyaltyDailyCheckinDto {
  @IsOptional()
  @IsString()
  @MaxLength(100)
  createdBy?: string;

  @IsOptional()
  @IsString()
  @MaxLength(255)
  note?: string;
}
