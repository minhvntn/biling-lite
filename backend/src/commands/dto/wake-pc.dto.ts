import { Transform } from 'class-transformer';
import { IsOptional, IsString, MaxLength } from 'class-validator';

export class WakePcDto {
  @IsString()
  @MaxLength(32)
  @Transform(({ value }: { value?: string }) => value?.trim() || '')
  macAddress!: string;

  @IsOptional()
  @IsString()
  @MaxLength(45)
  @Transform(({ value }: { value?: string }) => value?.trim() || undefined)
  broadcastAddress?: string;

  @IsOptional()
  @IsString()
  @MaxLength(100)
  @Transform(({ value }: { value?: string }) => value?.trim() || undefined)
  requestedBy?: string;
}

