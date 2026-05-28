import { Transform } from 'class-transformer';
import { IsBoolean, IsInt, IsNumber, IsOptional, IsString, Max, MaxLength, Min } from 'class-validator';

export class UpdateLoyaltySettingsDto {
  @IsBoolean()
  enabled!: boolean;

  @IsOptional()
  @Transform(({ value }: { value?: unknown }) =>
    value === undefined || value === null || value === '' ? undefined : Number(value),
  )
  @IsInt()
  @Min(1)
  @Max(10000)
  minutesPerPoint?: number;

  @IsOptional()
  @Transform(({ value }: { value?: unknown }) =>
    value === undefined || value === null || value === '' ? undefined : Number(value),
  )
  @IsInt()
  @Min(1)
  @Max(10000)
  lowestRankMinutesPerPoint?: number;

  @IsOptional()
  @Transform(({ value }: { value?: unknown }) =>
    value === undefined || value === null || value === '' ? undefined : Number(value),
  )
  @IsInt()
  @Min(1)
  @Max(10000)
  pointsToMinutes?: number;

  @IsOptional()
  @Transform(({ value }: { value?: unknown }) =>
    value === undefined || value === null || value === '' ? undefined : Number(value),
  )
  @IsNumber()
  @Min(0.1)
  @Max(100)
  weekdayMultiplier?: number;

  @IsOptional()
  @Transform(({ value }: { value?: unknown }) =>
    value === undefined || value === null || value === '' ? undefined : Number(value),
  )
  @IsNumber()
  @Min(0.1)
  @Max(100)
  weekendMultiplier?: number;

  @IsOptional()
  @IsString()
  @MaxLength(100)
  @Transform(({ value }: { value?: string }) => value?.trim() || undefined)
  updatedBy?: string;
}
