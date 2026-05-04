import { IsNumber, IsOptional, IsString, Min } from 'class-validator';

export class UpdateLoyaltyRankDto {
  @IsOptional()
  @IsString()
  rankName?: string;

  @IsOptional()
  @IsNumber()
  @Min(0)
  minTopup?: number;

  @IsOptional()
  @IsNumber()
  @Min(0)
  bonusPercent?: number;

  @IsOptional()
  @IsNumber()
  @Min(1)
  minutesPerPoint?: number;

  @IsOptional()
  @IsString()
  updatedBy?: string;
}
