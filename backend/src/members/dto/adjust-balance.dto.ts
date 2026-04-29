import { IsOptional, IsString, MaxLength, IsNumber } from 'class-validator';

export class AdjustBalanceDto {
  @IsNumber()
  amountDelta!: number;

  @IsOptional()
  @IsString()
  @MaxLength(300)
  note?: string;

  @IsOptional()
  @IsString()
  @MaxLength(100)
  createdBy?: string;
}

