import { Transform, Type } from 'class-transformer';
import { IsNumber, IsOptional, IsString, Max, MaxLength, Min } from 'class-validator';

export class TransferBalanceDto {
  @IsString()
  @MaxLength(50)
  @Transform(({ value }: { value: string }) => value?.trim())
  targetUsername!: string;

  @Type(() => Number)
  @IsNumber({ maxDecimalPlaces: 2 })
  @Min(1000)
  @Max(100000000)
  amount!: number;

  @IsOptional()
  @IsString()
  @MaxLength(300)
  @Transform(({ value }: { value?: string }) => value?.trim() || undefined)
  note?: string;

  @IsOptional()
  @IsString()
  @MaxLength(100)
  @Transform(({ value }: { value?: string }) => value?.trim() || undefined)
  createdBy?: string;
}

