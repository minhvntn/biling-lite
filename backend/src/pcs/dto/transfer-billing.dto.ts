import { IsNotEmpty, IsOptional, IsString, IsUUID } from 'class-validator';

export class TransferBillingDto {
  @IsNotEmpty()
  @IsUUID()
  targetPcId: string;

  @IsOptional()
  @IsString()
  note?: string;

  @IsOptional()
  @IsString()
  requestedBy?: string;
}
