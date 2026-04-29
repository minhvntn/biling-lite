import { Transform } from 'class-transformer';
import { IsNumber, IsOptional, IsString, Max, MaxLength, Min } from 'class-validator';

export class BuyPlaytimeDto {
  @IsNumber({ maxDecimalPlaces: 2 })
  @Min(0.5)
  @Max(24)
  hours!: number;

  @IsOptional()
  @IsNumber({ maxDecimalPlaces: 2 })
  @Min(1000)
  @Max(1000000)
  ratePerHour?: number;

  @IsOptional()
  @IsString()
  @MaxLength(200)
  @Transform(({ value }: { value?: string }) => value?.trim() || undefined)
  note?: string;

  @IsOptional()
  @IsString()
  @MaxLength(100)
  @Transform(({ value }: { value?: string }) => value?.trim() || undefined)
  createdBy?: string;
}
