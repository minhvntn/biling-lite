import { Transform } from 'class-transformer';
import {
  IsDateString,
  IsInt,
  IsOptional,
  IsString,
  Max,
  MaxLength,
  Min,
} from 'class-validator';

export class UploadPcLiveFrameDto {
  @IsString()
  @MaxLength(120)
  @Transform(({ value }: { value?: string }) => value?.trim() ?? '')
  agentId!: string;

  @IsOptional()
  @IsString()
  @MaxLength(120)
  @Transform(({ value }: { value?: string }) => value?.trim() ?? undefined)
  requestId?: string;

  @IsString()
  @MaxLength(8000000)
  imageBase64!: string;

  @IsOptional()
  @IsString()
  @MaxLength(80)
  mimeType?: string;

  @IsOptional()
  @IsInt()
  @Min(1)
  @Max(10000)
  width?: number;

  @IsOptional()
  @IsInt()
  @Min(1)
  @Max(10000)
  height?: number;

  @IsOptional()
  @IsDateString()
  capturedAt?: string;
}
