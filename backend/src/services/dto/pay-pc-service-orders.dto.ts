import { IsOptional, IsString, MaxLength } from 'class-validator';

export class PayPcServiceOrdersDto {
  @IsOptional()
  @IsString()
  @MaxLength(100)
  requestedBy?: string;

  @IsOptional()
  @IsString()
  @MaxLength(255)
  note?: string;
}
