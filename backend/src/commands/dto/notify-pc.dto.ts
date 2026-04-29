import { IsOptional, IsString, MaxLength } from 'class-validator';

export class NotifyPcDto {
  @IsString()
  @MaxLength(500)
  message!: string;

  @IsOptional()
  @IsString()
  @MaxLength(100)
  requestedBy?: string;
}

