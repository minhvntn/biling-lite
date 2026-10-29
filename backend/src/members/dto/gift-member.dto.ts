import { Transform } from 'class-transformer';
import { IsNumber, IsOptional, IsString, Max, MaxLength, Min } from 'class-validator';

export class GiftMemberDto {
  @IsNumber({ maxDecimalPlaces: 2 })
  @Min(1)
  @Max(100000000)
  amount!: number;

  @IsOptional()
  @IsNumber()
  @Min(0)
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
