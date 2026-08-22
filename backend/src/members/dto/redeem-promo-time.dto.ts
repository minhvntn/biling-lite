import { IsInt, Min, IsOptional, IsString } from 'class-validator';

export class RedeemPromoTimeDto {
  @IsInt()
  @Min(1)
  redeemSeconds!: number;

  @IsOptional()
  @IsString()
  note?: string;

  @IsOptional()
  @IsString()
  createdBy?: string;
}
