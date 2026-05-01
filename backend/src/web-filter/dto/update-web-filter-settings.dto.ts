import { Transform } from 'class-transformer';
import {
  ArrayMaxSize,
  IsArray,
  IsBoolean,
  IsOptional,
  IsString,
  MaxLength,
} from 'class-validator';

export class UpdateWebFilterSettingsDto {
  @IsOptional()
  @IsBoolean()
  enabled?: boolean;

  @IsOptional()
  @IsArray()
  @ArrayMaxSize(1000)
  @IsString({ each: true })
  @MaxLength(120, { each: true })
  blockedDomains?: string[];

  @IsOptional()
  @IsString()
  @MaxLength(100)
  @Transform(({ value }: { value?: string }) => value?.trim() || undefined)
  updatedBy?: string;
}

