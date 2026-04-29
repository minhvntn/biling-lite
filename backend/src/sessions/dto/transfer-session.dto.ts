import { IsString, IsUUID } from 'class-validator';

export class TransferSessionDto {
  @IsUUID()
  targetPcId!: string;

  @IsString()
  requestedBy!: string;
}

