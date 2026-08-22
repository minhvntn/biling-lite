import { Type } from 'class-transformer';
import { IsBoolean, IsDateString, IsInt, IsOptional, Max, Min } from 'class-validator';

export class UpdateLuckyEnvelopeSettingsDto {
  @IsBoolean()
  enabled!: boolean;

  @IsOptional()
  @IsDateString()
  startDate?: string;

  @IsOptional()
  @IsDateString()
  endDate?: string;

  @Type(() => Number)
  @IsInt()
  @Min(1)
  @Max(10000)
  pointCost!: number;

  @Type(() => Number)
  @IsInt()
  @Min(1)
  @Max(100000)
  minMinutes!: number;

  @Type(() => Number)
  @IsInt()
  @Min(1)
  @Max(100000)
  maxMinutes!: number;

  @Type(() => Number)
  @IsInt()
  @Min(0)
  @Max(1000)
  maxClaims!: number;
}
