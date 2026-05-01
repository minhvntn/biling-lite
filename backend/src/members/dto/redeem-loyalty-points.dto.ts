import { Transform, Type } from 'class-transformer';
import { IsInt, IsOptional, IsString, Max, MaxLength, Min } from 'class-validator';

export class RedeemLoyaltyPointsDto {
  @Type(() => Number)
  @IsInt()
  @Min(1)
  @Max(100000)
  points!: number;

  @IsOptional()
  @IsString()
  @MaxLength(100)
  @Transform(({ value }: { value?: string }) => value?.trim() || undefined)
  createdBy?: string;

  @IsOptional()
  @IsString()
  @MaxLength(255)
  @Transform(({ value }: { value?: string }) => value?.trim() || undefined)
  note?: string;
}
